using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public partial class AnalysisWorkerService : BackgroundService
{
    private const string SessionWatermarkKey = "stage5:session_watermark_ms";
    private const string SessionSeedWatermarkKey = "stage5:session_seed_message_watermark";
    private const string SessionChunkCheckpointPrefix = "stage5:session_chunk_checkpoint";
    private const string SessionSkipCounterPrefix = "stage5:skip:msg";
    private const int SessionSkipQuarantineThreshold = 3;
    private const string SessionSkipQuarantineReason = "session_limit_skipped_more_than_3_times";
    private const string CheapPromptId = "stage5_cheap_extract_v10";
    private const string ExpensivePromptId = "stage5_expensive_reason_v5";
    private const int MaxCheapLlmBatchSize = 10;
    private static readonly Regex ServicePlaceholderRegex = new(@"^\[[A-Z_]{2,32}\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan OpenRouterRecoveryProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OpenRouterRecoveryPollInterval = TimeSpan.FromSeconds(15);

    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageExtractionRepository _extractionRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IPromptTemplateRepository _promptRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly ExpensivePassResolver _expensivePassResolver;
    private readonly ExtractionApplier _extractionApplier;
    private readonly MessageContentBuilder _messageContentBuilder;
    private readonly AnalysisContextBuilder _contextBuilder;
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly ILogger<AnalysisWorkerService> _logger;

    public AnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IMessageExtractionRepository extractionRepository,
        IExtractionErrorRepository extractionErrorRepository,
        IAnalysisStateRepository stateRepository,
        IPromptTemplateRepository promptRepository,
        IChatSessionRepository chatSessionRepository,
        OpenRouterAnalysisService analysisService,
        ExpensivePassResolver expensivePassResolver,
        ExtractionApplier extractionApplier,
        MessageContentBuilder messageContentBuilder,
        AnalysisContextBuilder contextBuilder,
        SummaryHistoricalRetrievalService historicalRetrievalService,
        ILogger<AnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _extractionRepository = extractionRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _stateRepository = stateRepository;
        _promptRepository = promptRepository;
        _chatSessionRepository = chatSessionRepository;
        _analysisService = analysisService;
        _expensivePassResolver = expensivePassResolver;
        _extractionApplier = extractionApplier;
        _messageContentBuilder = messageContentBuilder;
        _contextBuilder = contextBuilder;
        _historicalRetrievalService = historicalRetrievalService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Stage5 analysis is disabled");
            return;
        }

        await EnsureDefaultPromptsAsync(stoppingToken);
        _logger.LogInformation(
            "Stage5 analysis worker started. cheap_model={Cheap}, expensive_model={Expensive}, cheap_parallelism={CheapParallelism}, cheap_batch_workers={CheapBatchWorkers}, session_chunk_size={SessionChunkSize}",
            _settings.CheapModel,
            _settings.ExpensiveModel,
            GetCheapLlmParallelism(),
            GetCheapBatchWorkers(),
            Math.Clamp(_settings.SessionChunkSize, 10, 100));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expensiveResolved = await _expensivePassResolver.ProcessExpensiveBacklogAsync(
                    async ct => (await GetPromptAsync(ExpensivePromptId, DefaultExpensivePrompt, ct)).SystemPrompt,
                    stoppingToken);
                if (expensiveResolved > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                }

                var reanalysis = await _messageRepository.GetNeedsReanalysisAsync(GetFetchLimit(), stoppingToken);
                if (reanalysis.Count > 0)
                {
                    var reanalysisResult = await ProcessCheapBatchesAsync(reanalysis, stoppingToken);
                    var succeededReanalysis = reanalysis
                        .Select(x => x.Id)
                        .Where(id => !reanalysisResult.FailedMessageIds.Contains(id));
                    await _messageRepository.MarkNeedsReanalysisDoneAsync(succeededReanalysis, stoppingToken);

                    if (reanalysisResult.BalanceIssueDetected)
                    {
                        await WaitForOpenRouterRecoveryAsync(stoppingToken);
                    }

                    _logger.LogInformation("Stage5 reanalysis pass done: processed={Count}", reanalysis.Count);
                    await DelayBetweenBatchesAsync(stoppingToken);
                    continue;
                }

                var processedSessions = await ProcessSessionFirstPassAsync(stoppingToken);
                if (processedSessions > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                    continue;
                }

                var seededMessages = await SeedSessionsFromProcessedBacklogAsync(stoppingToken);
                if (seededMessages > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                    continue;
                }

                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage5 analysis loop failed");
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessSessionFirstPassAsync(CancellationToken ct)
    {
        var staleBefore = DateTime.UtcNow.AddMinutes(-Math.Max(1, _settings.SessionAnalysisMinIdleMinutes));
        var sessions = await _chatSessionRepository.GetPendingAnalysisSessionsAsync(
            staleBefore,
            Math.Max(1, _settings.SessionAnalysisBatchSize),
            ct);
        if (sessions.Count == 0)
        {
            return 0;
        }

        var analyzedSessionIds = new List<Guid>(sessions.Count);
        var maxAnalyzedSessionEndMs = 0L;
        foreach (var session in sessions)
        {
            var maxSessionMessages = Math.Max(500, Math.Max(_settings.SummarySessionMaxMessages * 10, _settings.SessionChunkSize * 10));
            var sessionMessages = await _messageRepository.GetByChatAndPeriodAsync(
                session.ChatId,
                session.StartDate,
                session.EndDate,
                maxSessionMessages,
                ct);
            sessionMessages = sessionMessages
                .Where(x => x.Timestamp >= session.StartDate && x.Timestamp <= session.EndDate)
                .OrderBy(x => x.Timestamp)
                .ThenBy(x => x.Id)
                .ToList();

            if (sessionMessages.Count == 0)
            {
                analyzedSessionIds.Add(session.Id);
                maxAnalyzedSessionEndMs = Math.Max(maxAnalyzedSessionEndMs, new DateTimeOffset(session.EndDate).ToUnixTimeMilliseconds());
                continue;
            }

            var chunkResult = await ProcessSessionInChunksAsync(session, sessionMessages, ct);
            if (chunkResult.BalanceIssueDetected)
            {
                await WaitForOpenRouterRecoveryAsync(ct);
                break;
            }

            if (chunkResult.FailedMessageIds.Count > 0)
            {
                _logger.LogWarning(
                    "Stage5 session-first analysis incomplete. session_id={SessionId}, chat_id={ChatId}, session_index={SessionIndex}, failed_messages={Failed}",
                    session.Id,
                    session.ChatId,
                    session.SessionIndex,
                    chunkResult.FailedMessageIds.Count);
                continue;
            }

            analyzedSessionIds.Add(session.Id);
            maxAnalyzedSessionEndMs = Math.Max(maxAnalyzedSessionEndMs, new DateTimeOffset(session.EndDate).ToUnixTimeMilliseconds());
        }

        if (analyzedSessionIds.Count > 0)
        {
            await _chatSessionRepository.MarkAnalyzedAsync(analyzedSessionIds, ct);
            await _stateRepository.SetWatermarkAsync(SessionWatermarkKey, maxAnalyzedSessionEndMs, ct);
            _logger.LogInformation(
                "Stage5 session-first pass done: analyzed_sessions={Count}, session_watermark_ms={WatermarkMs}",
                analyzedSessionIds.Count,
                maxAnalyzedSessionEndMs);
        }

        return analyzedSessionIds.Count;
    }

    private async Task<int> SeedSessionsFromProcessedBacklogAsync(CancellationToken ct)
    {
        var watermark = await _stateRepository.GetWatermarkAsync(SessionSeedWatermarkKey, ct);
        var seedBatchSize = Math.Max(GetFetchLimit() * 5, 100);
        var messages = await _messageRepository.GetProcessedAfterIdAsync(watermark, seedBatchSize, ct);
        if (messages.Count == 0)
        {
            return 0;
        }

        await EnsureSessionSlicesForMessagesAsync(messages, ct);
        await _stateRepository.SetWatermarkAsync(SessionSeedWatermarkKey, messages.Max(x => x.Id), ct);
        _logger.LogInformation(
            "Stage5 session seed pass done: processed_messages={Count}, seed_watermark={Watermark}",
            messages.Count,
            messages.Max(x => x.Id));
        return messages.Count;
    }

    private async Task<CheapPassResult> ProcessSessionInChunksAsync(ChatSession session, List<Message> sessionMessages, CancellationToken ct)
    {
        var failed = new HashSet<long>();
        var balanceIssue = false;
        var chunks = BuildAdaptiveSessionChunks(sessionMessages);
        var checkpointKey = BuildSessionChunkCheckpointKey(session);
        var savedIndex = (int)Math.Max(0, await _stateRepository.GetWatermarkAsync(checkpointKey, ct));
        var startIndex = Math.Clamp(savedIndex, 0, chunks.Count);
        string? chunkSummaryPrev = null;
        if (startIndex > 0 && chunks.Count > 0)
        {
            chunkSummaryPrev = BuildChunkSummary(chunks[startIndex - 1], []);
            _logger.LogInformation(
                "Stage5 session chunk resume: session_id={SessionId}, chat_id={ChatId}, session_index={SessionIndex}, resume_chunk={ResumeChunk}, total_chunks={TotalChunks}",
                session.Id,
                session.ChatId,
                session.SessionIndex,
                startIndex,
                chunks.Count);
        }
        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([session.ChatId], ct);
        var chatSessions = sessionsByChat.GetValueOrDefault(session.ChatId) ?? [];

        for (var i = startIndex; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var replySliceContext = await BuildReplySliceContextAsync(session, chunk, chatSessions, ct);
            var ragContext = await BuildRagContextAsync(session, chunk, chunkSummaryPrev, replySliceContext, ct);
            var result = await ProcessCheapBatchesAsync(chunk, ct, chunkSummaryPrev, replySliceContext, ragContext);
            failed.UnionWith(result.FailedMessageIds);
            balanceIssue = balanceIssue || result.BalanceIssueDetected;
            if (result.FailedMessageIds.Count > 0)
            {
                _logger.LogWarning(
                    "Stage5 session chunk failed and checkpoint kept: session_id={SessionId}, chunk_index={ChunkIndex}, failed_messages={FailedMessages}",
                    session.Id,
                    i,
                    result.FailedMessageIds.Count);
                break;
            }

            await _stateRepository.SetWatermarkAsync(checkpointKey, i + 1, ct);
            if (balanceIssue)
            {
                break;
            }

            chunkSummaryPrev = BuildChunkSummary(chunk, result.FailedMessageIds);
        }

        if (!balanceIssue && failed.Count == 0)
        {
            await _stateRepository.SetWatermarkAsync(checkpointKey, 0, ct);
        }

        _logger.LogInformation(
            "Stage5 session chunk pass done: session_id={SessionId}, chat_id={ChatId}, session_index={SessionIndex}, messages={MessageCount}, chunks={ChunkCount}, failed={FailedCount}",
            session.Id,
            session.ChatId,
            session.SessionIndex,
            sessionMessages.Count,
            chunks.Count,
            failed.Count);

        return new CheapPassResult(failed, balanceIssue);
    }

    private async Task<CheapPassResult> ProcessCheapBatchesAsync(
        List<Message> messages,
        CancellationToken ct,
        string? chunkSummaryPrev = null,
        string? replySliceContext = null,
        string? ragContext = null)
    {
        if (messages.Count == 0)
        {
            return new CheapPassResult(new HashSet<long>(), false);
        }

        var quarantinedMessageIds = await _extractionRepository.GetQuarantinedMessageIdsAsync(
            messages.Select(x => x.Id).ToArray(),
            ct);
        if (quarantinedMessageIds.Count > 0)
        {
            _logger.LogInformation(
                "Stage5 skipped already quarantined messages: count={Count}",
                quarantinedMessageIds.Count);
            messages = messages
                .Where(x => !quarantinedMessageIds.Contains(x.Id))
                .ToList();
        }

        if (messages.Count == 0)
        {
            return new CheapPassResult(new HashSet<long>(), false);
        }

        await EnsureSessionSlicesForMessagesAsync(messages, ct);
        var episodicGuard = await ApplyEpisodicMemoryGuardAsync(messages, ct);
        var skippedBySessionLimit = episodicGuard.SkippedBySessionLimitMessageIds.ToHashSet();
        var skipResetMessageIds = messages
            .Select(x => x.Id)
            .Where(id => !skippedBySessionLimit.Contains(id))
            .ToArray();
        await ResetSessionSkipCountersAsync(skipResetMessageIds, ct);
        var newlyQuarantined = await IncrementSkipCountersAndQuarantineAsync(
            episodicGuard.SkippedBySessionLimitMessageIds,
            ct);

        if (episodicGuard.BlockedPendingSummaryMessageIds.Count > 0)
        {
            await _messageRepository.MarkNeedsReanalysisAsync(episodicGuard.BlockedPendingSummaryMessageIds, ct);
            _logger.LogInformation(
                "Stage5 episodic guard deferred messages pending session summaries: count={Count}",
                episodicGuard.BlockedPendingSummaryMessageIds.Count);
        }

        if (episodicGuard.SkippedBySessionLimitMessageIds.Count > 0)
        {
            _logger.LogInformation(
                "Stage5 episodic guard skipped messages beyond session limit: count={Count}, max_sessions_per_chat={MaxSessions}, newly_quarantined={QuarantinedCount}",
                episodicGuard.SkippedBySessionLimitMessageIds.Count,
                GetEpisodicSessionLimit(),
                newlyQuarantined.Count);
        }

        if (episodicGuard.SkippedNoSessionAssignmentMessageIds.Count > 0)
        {
            _logger.LogWarning(
                "Stage5 session gate skipped messages without session assignment: count={Count}",
                episodicGuard.SkippedNoSessionAssignmentMessageIds.Count);
        }

        messages = episodicGuard.AllowedMessages;
        if (messages.Count == 0)
        {
            return new CheapPassResult(
                episodicGuard.BlockedPendingSummaryMessageIds.ToHashSet(),
                false);
        }

        var failedMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        foreach (var blockedId in episodicGuard.BlockedPendingSummaryMessageIds)
        {
            failedMessageIds.TryAdd(blockedId, 0);
        }

        var batchSize = Math.Max(1, _settings.BatchSize);
        var batches = messages
            .Chunk(batchSize)
            .Select(chunk => chunk.ToList())
            .ToList();

        if (batches.Count <= 1 || GetCheapBatchWorkers() <= 1)
        {
            foreach (var batch in batches)
            {
                try
                {
                    var batchResult = await ProcessCheapBatchAsync(batch, ct, chunkSummaryPrev, replySliceContext, ragContext);
                    foreach (var id in batchResult.FailedMessageIds)
                    {
                        failedMessageIds.TryAdd(id, 0);
                    }

                    if (batchResult.BalanceIssueDetected)
                    {
                        Interlocked.Exchange(ref balanceIssueDetected, 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Stage5 cheap worker batch failed: batch_size={BatchSize}",
                        batch.Count);

                    foreach (var message in batch)
                    {
                        failedMessageIds.TryAdd(message.Id, 0);
                    }

                    await _messageRepository.MarkNeedsReanalysisAsync(batch.Select(x => x.Id), ct);
                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_cheap_worker_batch",
                        reason: ex.Message,
                        payload: $"batch_size={batch.Count}",
                        ct: ct);
                }
            }

            return new CheapPassResult(
                failedMessageIds.Keys.ToHashSet(),
                balanceIssueDetected == 1);
        }

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = GetCheapBatchWorkers(),
                CancellationToken = ct
            },
            async (batch, token) =>
            {
                try
                {
                    var batchResult = await ProcessCheapBatchAsync(batch, token, chunkSummaryPrev, replySliceContext, ragContext);
                    foreach (var id in batchResult.FailedMessageIds)
                    {
                        failedMessageIds.TryAdd(id, 0);
                    }

                    if (batchResult.BalanceIssueDetected)
                    {
                        Interlocked.Exchange(ref balanceIssueDetected, 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Stage5 cheap worker batch failed: batch_size={BatchSize}",
                        batch.Count);

                    foreach (var message in batch)
                    {
                        failedMessageIds.TryAdd(message.Id, 0);
                    }

                    await _messageRepository.MarkNeedsReanalysisAsync(batch.Select(x => x.Id), token);
                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_cheap_worker_batch",
                        reason: ex.Message,
                        payload: $"batch_size={batch.Count}",
                        ct: token);
                }
            });

        return new CheapPassResult(
            failedMessageIds.Keys.ToHashSet(),
            balanceIssueDetected == 1);
    }

    private async Task<CheapPassResult> ProcessCheapBatchAsync(
        List<Message> messages,
        CancellationToken ct,
        string? chunkSummaryPrev = null,
        string? replySliceContext = null,
        string? ragContext = null)
    {
        var analyzableMessages = new List<Message>(messages.Count);
        var skippedByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in messages)
        {
            if (TryGetCheapSkipReason(message, out var reason))
            {
                skippedByReason[reason] = skippedByReason.GetValueOrDefault(reason) + 1;
                continue;
            }

            analyzableMessages.Add(message);
        }

        if (skippedByReason.Count > 0)
        {
            _logger.LogInformation(
                "Stage5 cheap prefilter skipped={Skipped} of {Total}: {Reasons}",
                messages.Count - analyzableMessages.Count,
                messages.Count,
                string.Join(", ", skippedByReason.Select(x => $"{x.Key}={x.Value}")));
        }

        var modelByMessageId = BuildCheapModelMap(messages);
        var byId = new ConcurrentDictionary<long, ExtractionItem>();
        var failedChunkMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        if (analyzableMessages.Count > 0)
        {
            var replyContext = await _messageContentBuilder.LoadReplyContextAsync(analyzableMessages, ct);
            var contexts = await _contextBuilder.BuildBatchContextsAsync(analyzableMessages, ct);
            if (!string.IsNullOrWhiteSpace(replySliceContext))
            {
                foreach (var context in contexts.Values)
                {
                    context.ExternalReplyContext.Clear();
                }
            }
            var batch = analyzableMessages.Select(m => new AnalysisInputMessage
            {
                MessageId = m.Id,
                SenderName = m.SenderName,
                Timestamp = m.Timestamp,
                Text = MessageContentBuilder.BuildMessageText(
                    m,
                    replyContext.GetValueOrDefault(m.Id),
                    contexts.GetValueOrDefault(m.Id))
            }).ToList();

            var cheapPrompt = await GetPromptAsync(CheapPromptId, DefaultCheapPrompt, ct);
            var cheapChunks = batch
                .GroupBy(x => modelByMessageId.GetValueOrDefault(x.MessageId, _settings.CheapModel))
                .SelectMany(group => group
                    .Chunk(MaxCheapLlmBatchSize)
                    .Select(chunk => new CheapChunkRequest(group.Key, chunk.ToList())))
                .ToList();

            await Parallel.ForEachAsync(
                cheapChunks,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = GetCheapLlmParallelism(),
                    CancellationToken = ct
                },
                async (request, token) =>
                {
                    try
                    {
                        var cheapResult = await _analysisService.ExtractCheapAsync(
                            request.Model,
                            cheapPrompt.SystemPrompt,
                            request.Messages,
                            token,
                            chunkSummaryPrev,
                            replySliceContext,
                            ragContext);

                        foreach (var item in cheapResult.Items.Where(x => x.MessageId > 0))
                        {
                            byId[item.MessageId] = item;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is OpenRouterBalanceException)
                        {
                            Interlocked.Exchange(ref balanceIssueDetected, 1);
                        }

                        _logger.LogWarning(
                            ex,
                            "Stage5 cheap batch failed for model={Model}, count={Count}",
                            request.Model,
                            request.Messages.Count);

                        await _extractionErrorRepository.LogAsync(
                            stage: "stage5_cheap_batch_model",
                            reason: ex.Message,
                            payload: $"model={request.Model};count={request.Messages.Count}",
                            ct: token);

                        if (ex is not OpenRouterBalanceException)
                        {
                            var fallbackModel = ResolveSingleFallbackModel(request.Model, ex);
                            var singleFallbackFailed = await TryProcessChunkOneByOneAsync(
                                request,
                                cheapPrompt.SystemPrompt,
                                fallbackModel,
                                byId,
                                chunkSummaryPrev,
                                replySliceContext,
                                ragContext,
                                token);

                            foreach (var failedId in singleFallbackFailed)
                            {
                                failedChunkMessageIds.TryAdd(failedId, 0);
                            }
                        }
                        else
                        {
                            foreach (var failed in request.Messages)
                            {
                                failedChunkMessageIds.TryAdd(failed.MessageId, 0);
                            }
                        }
                    }
                });
        }

        if (!failedChunkMessageIds.IsEmpty)
        {
            await _messageRepository.MarkNeedsReanalysisAsync(failedChunkMessageIds.Keys, ct);
            _logger.LogWarning(
                "Stage5 tagged failed cheap chunk messages for reanalysis: count={Count}, balance_issue={BalanceIssue}",
                failedChunkMessageIds.Count,
                balanceIssueDetected == 1);
        }

        foreach (var message in messages)
        {
            if (failedChunkMessageIds.ContainsKey(message.Id))
            {
                continue;
            }

            try
            {
                byId.TryGetValue(message.Id, out var extracted);
                extracted ??= new ExtractionItem { MessageId = message.Id };
                extracted = ExtractionRefiner.NormalizeExtractionForMessage(extracted, message);
                extracted = ExtractionRefiner.SanitizeExtraction(extracted);
                extracted = ExtractionRefiner.RefineExtractionForMessage(extracted, message, _settings);
                extracted.MessageId = message.Id;

                if (!ExtractionValidator.ValidateExtractionForMessage(extracted, message, out var validationError))
                {
                    _logger.LogWarning(
                        "Stage5 extraction validation rejected message_id={MessageId}: {Reason}",
                        message.Id,
                        validationError);

                    await _extractionErrorRepository.LogAsync(
                        stage: "stage5_validation",
                        reason: validationError ?? "invalid_extraction",
                        messageId: message.Id,
                        payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)};json={JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase)}",
                        ct: ct);

                    continue;
                }

                var needsExpensive = IsExpensivePassEnabled() &&
                                     ExtractionRefiner.ShouldRunExpensivePass(message, extracted, _settings);

                await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase), needsExpensive, ct);

                if (!needsExpensive)
                {
                    await _extractionApplier.ApplyExtractionAsync(message.Id, extracted, message, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stage5 cheap item failed for message_id={MessageId}", message.Id);
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cheap_item",
                    reason: ex.Message,
                    messageId: message.Id,
                    payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)};exception={ex.GetType().Name}",
                    ct: ct);
            }
        }

        return new CheapPassResult(failedChunkMessageIds.Keys.ToHashSet(), balanceIssueDetected == 1);
    }

    private Dictionary<long, string> BuildCheapModelMap(List<Message> messages)
    {
        var map = new Dictionary<long, string>(messages.Count);
        if (!_settings.CheapModelAbEnabled)
        {
            foreach (var message in messages)
            {
                map[message.Id] = _settings.CheapModel;
            }

            return map;
        }

        var baseline = string.IsNullOrWhiteSpace(_settings.CheapBaselineModel)
            ? "openai/gpt-4o-mini"
            : _settings.CheapBaselineModel.Trim();
        var candidate = string.IsNullOrWhiteSpace(_settings.CheapCandidateModel)
            ? _settings.CheapModel
            : _settings.CheapCandidateModel.Trim();
        var candidatePercent = Math.Clamp(_settings.CheapAbCandidatePercent, 0, 100);

        foreach (var message in messages)
        {
            var bucket = (int)(Math.Abs(message.Id % 100));
            map[message.Id] = bucket < candidatePercent ? candidate : baseline;
        }

        return map;
    }

    private async Task EnsureDefaultPromptsAsync(CancellationToken ct)
    {
        await EnsurePromptAsync(CheapPromptId, "Stage5 Cheap Extraction v10", DefaultCheapPrompt, ct);
        await EnsurePromptAsync(ExpensivePromptId, "Stage5 Expensive Reasoning v5", DefaultExpensivePrompt, ct);
    }

    private async Task EnsurePromptAsync(string id, string name, string systemPrompt, CancellationToken ct)
    {
        var existing = await _promptRepository.GetByIdAsync(id, ct);
        if (existing != null)
        {
            return;
        }

        await _promptRepository.UpsertAsync(new PromptTemplate
        {
            Id = id,
            Name = name,
            SystemPrompt = systemPrompt
        }, ct);
    }

    private async Task<PromptTemplate> GetPromptAsync(string id, string fallback, CancellationToken ct)
    {
        var prompt = await _promptRepository.GetByIdAsync(id, ct);
        return prompt ?? new PromptTemplate { Id = id, Name = id, SystemPrompt = fallback };
    }

    private static Task DelayBetweenBatchesAsync(CancellationToken ct)
    {
        return Task.Delay(BatchThrottleDelay, ct);
    }

    private async Task WaitForOpenRouterRecoveryAsync(CancellationToken ct)
    {
        _logger.LogWarning(
            "Stage5 paused due to OpenRouter balance/quota issue. Poll interval={PollSeconds}s, probe timeout={ProbeSeconds}s",
            OpenRouterRecoveryPollInterval.TotalSeconds,
            OpenRouterRecoveryProbeTimeout.TotalSeconds);

        var attempts = 0;
        while (!ct.IsCancellationRequested)
        {
            attempts++;
            try
            {
                using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                probeCts.CancelAfter(OpenRouterRecoveryProbeTimeout);
                var recovered = await _analysisService.ProbeCheapAvailabilityAsync(probeCts.Token);
                if (recovered)
                {
                    _logger.LogInformation("OpenRouter recovery probe succeeded after attempts={Attempts}. Resuming Stage5.", attempts);
                    return;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Probe timeout; keep waiting.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter recovery probe failed on attempt={Attempt}", attempts);
            }

            await Task.Delay(OpenRouterRecoveryPollInterval, ct);
        }
    }

    private int GetCheapLlmParallelism()
    {
        return Math.Clamp(_settings.CheapLlmParallelism, 1, 16);
    }

    private async Task<EpisodicGuardResult> ApplyEpisodicMemoryGuardAsync(List<Message> messages, CancellationToken ct)
    {
        var limit = GetEpisodicSessionLimit();
        if (limit <= 0 || messages.Count == 0)
        {
            return new EpisodicGuardResult(messages, [], [], []);
        }

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync(messages.Select(x => x.ChatId).Distinct().ToArray(), ct);
        var allowed = new List<Message>(messages.Count);
        var blockedPendingSummary = new List<long>();
        var skippedBySessionLimit = new List<long>();
        var skippedNoSessionAssignment = new List<long>();

        foreach (var message in messages)
        {
            var sessions = sessionsByChat.GetValueOrDefault(message.ChatId) ?? [];
            if (sessions.Count == 0)
            {
                skippedNoSessionAssignment.Add(message.Id);
                continue;
            }

            var targetSessionIndex = ResolveTargetSessionIndex(sessions, message.Timestamp);
            var assignedSession = sessions.FirstOrDefault(session => session.SessionIndex == targetSessionIndex);
            if (assignedSession == null)
            {
                skippedNoSessionAssignment.Add(message.Id);
                continue;
            }

            if (targetSessionIndex >= limit)
            {
                skippedBySessionLimit.Add(message.Id);
                continue;
            }

            if (targetSessionIndex == 0)
            {
                allowed.Add(message);
                continue;
            }

            var previousSessionIndex = targetSessionIndex - 1;
            var previousSession = sessions.FirstOrDefault(session => session.SessionIndex == previousSessionIndex);
            if (previousSession == null)
            {
                skippedNoSessionAssignment.Add(message.Id);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(previousSession.Summary))
            {
                allowed.Add(message);
                continue;
            }

            blockedPendingSummary.Add(message.Id);
        }

        return new EpisodicGuardResult(allowed, blockedPendingSummary, skippedBySessionLimit, skippedNoSessionAssignment);
    }

    private static int ResolveTargetSessionIndex(List<ChatSession> sessions, DateTime messageTimestamp)
    {
        if (sessions.Count == 0)
        {
            return 0;
        }

        var matchingSession = sessions.FirstOrDefault(session =>
            session.StartDate <= messageTimestamp && messageTimestamp <= session.EndDate);
        if (matchingSession != null)
        {
            return matchingSession.SessionIndex;
        }

        var previousSession = sessions
            .Where(session => session.EndDate < messageTimestamp)
            .OrderByDescending(session => session.SessionIndex)
            .FirstOrDefault();
        if (previousSession != null)
        {
            return previousSession.SessionIndex + 1;
        }

        return 0;
    }

    private int GetEpisodicSessionLimit() => Math.Max(0, _settings.TestModeMaxSessionsPerChat);

    private async Task ResetSessionSkipCountersAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct)
    {
        var keys = messageIds
            .Where(x => x > 0)
            .Distinct()
            .Select(BuildSessionSkipCounterKey)
            .ToArray();
        await _stateRepository.ResetWatermarksIfExistAsync(keys, ct);
    }

    private async Task<HashSet<long>> IncrementSkipCountersAndQuarantineAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct)
    {
        var quarantineIds = new HashSet<long>();
        foreach (var messageId in messageIds.Where(x => x > 0).Distinct())
        {
            var key = BuildSessionSkipCounterKey(messageId);
            var next = await _stateRepository.GetWatermarkAsync(key, ct) + 1;
            await _stateRepository.SetWatermarkAsync(key, next, ct);
            if (next > SessionSkipQuarantineThreshold)
            {
                quarantineIds.Add(messageId);
            }
        }

        if (quarantineIds.Count > 0)
        {
            await _extractionRepository.QuarantineMessagesAsync(quarantineIds.ToArray(), SessionSkipQuarantineReason, ct);
        }

        return quarantineIds;
    }

    private static string BuildSessionSkipCounterKey(long messageId)
    {
        return $"{SessionSkipCounterPrefix}:{messageId}";
    }

    private async Task EnsureSessionSlicesForMessagesAsync(IReadOnlyCollection<Message> messages, CancellationToken ct)
    {
        var sessionLimit = GetEpisodicSessionLimit();
        if (messages.Count == 0 || sessionLimit <= 0)
        {
            return;
        }

        var chatIds = messages.Select(x => x.ChatId).Distinct().ToArray();
        var existingByChat = await _chatSessionRepository.GetByChatsAsync(chatIds, ct);
        var fetchLimit = Math.Max(
            500,
            Math.Max(GetFetchLimit(), Math.Max(1, _settings.SummaryDayMaxMessages) * sessionLimit));
        var gap = TimeSpan.FromMinutes(Math.Max(1, _settings.EpisodicSessionGapMinutes));

        foreach (var chatId in chatIds)
        {
            var chatMessages = await _messageRepository.GetProcessedByChatAsync(chatId, fetchLimit, ct);
            if (chatMessages.Count == 0)
            {
                continue;
            }

            var sessions = SplitByGap(chatMessages, gap)
                .Take(sessionLimit)
                .ToList();
            var existingByIndex = existingByChat.GetValueOrDefault(chatId)?
                .ToDictionary(x => x.SessionIndex) ?? new Dictionary<int, ChatSession>();

            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.Count == 0)
                {
                    continue;
                }

                var sessionStart = session.First().Timestamp;
                var sessionEnd = session.Last().Timestamp;
                var existing = existingByIndex.GetValueOrDefault(i);
                var summary = existing?.Summary ?? string.Empty;
                var isFinalized = existing?.IsFinalized ?? false;
                if (existing != null
                    && existing.StartDate == sessionStart
                    && existing.EndDate == sessionEnd
                    && existing.LastMessageAt == sessionEnd
                    && string.Equals(existing.Summary ?? string.Empty, summary, StringComparison.Ordinal)
                    && existing.IsFinalized == isFinalized)
                {
                    continue;
                }

                await _chatSessionRepository.UpsertAsync(new ChatSession
                {
                    Id = existing?.Id ?? Guid.Empty,
                    ChatId = chatId,
                    SessionIndex = i,
                    StartDate = sessionStart,
                    EndDate = sessionEnd,
                    LastMessageAt = sessionEnd,
                    Summary = summary,
                    IsFinalized = isFinalized
                }, ct);
            }
        }
    }

    private static List<List<Message>> SplitByGap(List<Message> messages, TimeSpan gap)
    {
        var ordered = messages.OrderBy(x => x.Timestamp).ThenBy(x => x.Id).ToList();
        var result = new List<List<Message>>();
        var current = new List<Message>();

        foreach (var message in ordered)
        {
            if (current.Count == 0)
            {
                current.Add(message);
                continue;
            }

            var delta = message.Timestamp - current[^1].Timestamp;
            if (delta > gap)
            {
                result.Add(current);
                current = new List<Message> { message };
                continue;
            }

            current.Add(message);
        }

        if (current.Count > 0)
        {
            result.Add(current);
        }

        return result;
    }

    private async Task<HashSet<long>> TryProcessChunkOneByOneAsync(
        CheapChunkRequest request,
        string cheapPrompt,
        string model,
        ConcurrentDictionary<long, ExtractionItem> byId,
        string? chunkSummaryPrev,
        string? replySliceContext,
        string? ragContext,
        CancellationToken ct)
    {
        var failed = new HashSet<long>();
        var perMessageTimeoutSeconds = Math.Clamp(_settings.HttpTimeoutSeconds / 3, 20, 60);
        if (!string.Equals(model, request.Model, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Stage5 cheap single fallback model override: from={SourceModel}, to={FallbackModel}, count={Count}",
                request.Model,
                model,
                request.Messages.Count);
        }

        foreach (var message in request.Messages)
        {
            try
            {
                using var messageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                messageCts.CancelAfter(TimeSpan.FromSeconds(perMessageTimeoutSeconds));
                var single = await _analysisService.ExtractCheapAsync(
                    model,
                    cheapPrompt,
                    [message],
                    messageCts.Token,
                    chunkSummaryPrev,
                    replySliceContext,
                    ragContext);

                var item = single.Items.FirstOrDefault(x => x.MessageId == message.MessageId);
                if (item != null)
                {
                    byId[message.MessageId] = item;
                }
            }
            catch (Exception ex)
            {
                failed.Add(message.MessageId);
                _logger.LogWarning(
                    ex,
                    "Stage5 cheap single fallback failed for model={Model}, message_id={MessageId}, timeout_s={TimeoutSeconds}",
                    model,
                    message.MessageId,
                    perMessageTimeoutSeconds);

                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_cheap_single_fallback",
                    reason: ex.Message,
                    messageId: message.MessageId,
                    payload: $"model={model};timeout_s={perMessageTimeoutSeconds}",
                    ct: ct);
            }
        }

        if (failed.Count < request.Messages.Count)
        {
            _logger.LogInformation(
                "Stage5 cheap single fallback recovered={Recovered} of {Total} for model={Model}",
                request.Messages.Count - failed.Count,
                request.Messages.Count,
                model);
        }

        return failed;
    }

    private string ResolveSingleFallbackModel(string sourceModel, Exception ex)
    {
        if (ex is TaskCanceledException or TimeoutException)
        {
            var baseline = _settings.CheapBaselineModel?.Trim();
            if (!string.IsNullOrWhiteSpace(baseline) &&
                !string.Equals(sourceModel, baseline, StringComparison.OrdinalIgnoreCase))
            {
                return baseline;
            }
        }

        return sourceModel;
    }

    private int GetCheapBatchWorkers()
    {
        return Math.Clamp(_settings.CheapBatchWorkers, 1, 12);
    }

    private bool IsExpensivePassEnabled()
    {
        return _settings.MaxExpensivePerBatch > 0;
    }

    private int GetFetchLimit()
    {
        var batchSize = Math.Max(1, _settings.BatchSize);
        return batchSize * GetCheapBatchWorkers();
    }

    private static bool TryGetCheapSkipReason(Message message, out string reason)
    {
        var semanticContent = MessageContentBuilder.BuildSemanticContent(message);
        if (string.IsNullOrWhiteSpace(semanticContent))
        {
            reason = "empty_semantic_content";
            return true;
        }

        var text = message.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text) &&
            (text.Equals("[DELETED]", StringComparison.OrdinalIgnoreCase)
             || ServicePlaceholderRegex.IsMatch(text)))
        {
            reason = "service_placeholder";
            return true;
        }

        if (MessageContentBuilder.IsServiceOrTechnicalNoise(message))
        {
            reason = "technical_noise";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private async Task<string?> BuildReplySliceContextAsync(
        ChatSession currentSession,
        List<Message> chunk,
        IReadOnlyCollection<ChatSession> chatSessions,
        CancellationToken ct)
    {
        var replyMap = await _messageContentBuilder.LoadReplyContextAsync(chunk, ct);
        if (replyMap.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var kvp in replyMap.OrderBy(x => x.Key))
        {
            var sourceMessageId = kvp.Key;
            var reply = kvp.Value;
            if (reply.ChatId != currentSession.ChatId)
            {
                continue;
            }

            if (reply.Timestamp >= currentSession.StartDate && reply.Timestamp <= currentSession.EndDate)
            {
                continue;
            }

            parts.Add($"[reply_link] source_message_id={sourceMessageId} replied_message_id={reply.Id} replied_ts={reply.Timestamp:O}");
            var replySession = chatSessions
                .FirstOrDefault(x => x.StartDate <= reply.Timestamp && reply.Timestamp <= x.EndDate);
            if (replySession != null && !string.IsNullOrWhiteSpace(replySession.Summary))
            {
                parts.Add($"[reply_slice_summary] {MessageContentBuilder.TruncateForContext(MessageContentBuilder.CollapseWhitespace(replySession.Summary), 420)}");
            }

            var around = await _messageRepository.GetChatWindowAroundAsync(reply.ChatId, reply.Id, 3, 3, ct);
            if (around.Count > 0)
            {
                parts.Add("[reply_slice_neighbors]");
                foreach (var message in around.OrderBy(x => x.Timestamp).ThenBy(x => x.Id))
                {
                    var sender = string.IsNullOrWhiteSpace(message.SenderName)
                        ? $"user:{message.SenderId}"
                        : message.SenderName.Trim();
                    var text = MessageContentBuilder.TruncateForContext(
                        MessageContentBuilder.CollapseWhitespace(MessageContentBuilder.BuildSemanticContent(message)),
                        140);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    parts.Add($"- [{message.Timestamp:MM-dd HH:mm}] {sender}: {text}");
                }
                parts.Add("[/reply_slice_neighbors]");
            }

            if (parts.Count >= 18)
            {
                break;
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join("\n", parts);
    }

    private sealed record CheapPassResult(
        HashSet<long> FailedMessageIds,
        bool BalanceIssueDetected);
    private sealed record CheapChunkRequest(string Model, List<AnalysisInputMessage> Messages);
    private sealed record EpisodicGuardResult(
        List<Message> AllowedMessages,
        List<long> BlockedPendingSummaryMessageIds,
        List<long> SkippedBySessionLimitMessageIds,
        List<long> SkippedNoSessionAssignmentMessageIds);

    private static string BuildChunkSummary(List<Message> chunk, IReadOnlyCollection<long> failedMessageIds)
    {
        var lines = chunk
            .Where(x => !failedMessageIds.Contains(x.Id))
            .OrderBy(x => x.Timestamp)
            .Take(8)
            .Select(message =>
            {
                var sender = string.IsNullOrWhiteSpace(message.SenderName)
                    ? $"user:{message.SenderId}"
                    : message.SenderName.Trim();
                var text = MessageContentBuilder.TruncateForContext(
                    MessageContentBuilder.CollapseWhitespace(MessageContentBuilder.BuildSemanticContent(message)),
                    120);
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : $"[{message.Timestamp:MM-dd HH:mm}] {sender}: {text}";
            })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return lines.Count == 0
            ? "Контекст предыдущего чанка: существенных реплик не найдено."
            : "Краткая выжимка предыдущего чанка:\n" + string.Join("\n", lines);
    }

    private List<List<Message>> BuildAdaptiveSessionChunks(List<Message> sessionMessages)
    {
        var ordered = sessionMessages
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.Id)
            .ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var targetChars = Math.Max(1200, _settings.SessionChunkTargetChars);
        var maxChars = Math.Max(targetChars, _settings.SessionChunkMaxChars);
        var minMessages = Math.Max(2, _settings.SessionChunkMinMessages);
        var hardMaxMessages = Math.Max(minMessages, _settings.SessionChunkHardMaxMessages);
        var pauseGap = TimeSpan.FromMinutes(Math.Max(1, _settings.SessionChunkPauseGapMinutes));

        var chunks = new List<List<Message>>();
        var current = new List<Message>();
        var currentCost = 0;

        foreach (var message in ordered)
        {
            if (current.Count == 0)
            {
                current.Add(message);
                currentCost = EstimateChunkCostChars(message);
                continue;
            }

            var last = current[^1];
            var delta = message.Timestamp - last.Timestamp;
            var messageCost = EstimateChunkCostChars(message);
            var nextCost = currentCost + messageCost;

            var splitByPause = delta > pauseGap && current.Count >= minMessages;
            var splitByBudget = nextCost > targetChars && current.Count >= minMessages;
            var splitByHardLimits = current.Count >= hardMaxMessages || nextCost > maxChars;
            if (splitByPause || splitByBudget || splitByHardLimits)
            {
                chunks.Add(current);
                current = new List<Message> { message };
                currentCost = messageCost;
                continue;
            }

            current.Add(message);
            currentCost = nextCost;
        }

        if (current.Count > 0)
        {
            chunks.Add(current);
        }

        if (chunks.Count >= 2 && chunks[^1].Count < minMessages)
        {
            var penultimate = chunks[^2];
            var last = chunks[^1];
            var combinedCost = penultimate.Sum(EstimateChunkCostChars) + last.Sum(EstimateChunkCostChars);
            if (penultimate.Count + last.Count <= hardMaxMessages && combinedCost <= maxChars)
            {
                penultimate.AddRange(last);
                chunks.RemoveAt(chunks.Count - 1);
            }
        }

        return chunks;
    }

    private static int EstimateChunkCostChars(Message message)
    {
        var semantic = MessageContentBuilder.BuildSemanticContent(message);
        var textLen = MessageContentBuilder.CollapseWhitespace(semantic).Length;
        var boundedText = Math.Clamp(textLen, 40, 1600);
        const int metadataOverhead = 180;
        return metadataOverhead + boundedText;
    }

    private static string BuildSessionChunkCheckpointKey(ChatSession session)
    {
        return $"{SessionChunkCheckpointPrefix}:{session.Id:N}";
    }

    private async Task<string?> BuildRagContextAsync(
        ChatSession currentSession,
        List<Message> chunk,
        string? chunkSummaryPrev,
        string? replySliceContext,
        CancellationToken ct)
    {
        if (!ShouldTriggerRagFallback(chunk, chunkSummaryPrev, replySliceContext))
        {
            return null;
        }

        var hints = await _historicalRetrievalService.GetHintsAsync(
            currentSession.ChatId,
            currentSession.SessionIndex,
            chunk,
            ct);
        if (hints.Count == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            $"[rag_meta] chat_id={currentSession.ChatId} session_index={currentSession.SessionIndex} hints={hints.Count}"
        };
        foreach (var hint in hints)
        {
            lines.Add($"- session_index={hint.SessionIndex} similarity={hint.Similarity:0.00} summary=\"{MessageContentBuilder.TruncateForContext(hint.Summary, 220)}\"");
        }

        return string.Join("\n", lines);
    }

    private static bool ShouldTriggerRagFallback(List<Message> chunk, string? chunkSummaryPrev, string? replySliceContext)
    {
        if (!string.IsNullOrWhiteSpace(replySliceContext))
        {
            return false;
        }

        var merged = string.Join(" ", chunk
            .Select(MessageContentBuilder.BuildSemanticContent)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(8));
        if (string.IsNullOrWhiteSpace(merged))
        {
            return false;
        }

        var text = MessageContentBuilder.CollapseWhitespace(merged).ToLowerInvariant();
        var hasAnaphora = text.Contains(" его ")
                          || text.Contains(" её ")
                          || text.Contains(" ее ")
                          || text.Contains(" это ")
                          || text.Contains(" тот ")
                          || text.Contains(" та ");
        var hasActionWithoutObject = text.Contains("купил")
                                     || text.Contains("сделал")
                                     || text.Contains("забрал")
                                     || text.Contains("договорился")
                                     || text.Contains("оплатил");
        var hasExplicitObject = text.Contains("ром")
                                || text.Contains("квартира")
                                || text.Contains("машин")
                                || text.Contains("договор")
                                || text.Contains("заказ");
        if (hasAnaphora)
        {
            return true;
        }

        if (hasActionWithoutObject && !hasExplicitObject && !string.IsNullOrWhiteSpace(chunkSummaryPrev))
        {
            return true;
        }

        return false;
    }
}
