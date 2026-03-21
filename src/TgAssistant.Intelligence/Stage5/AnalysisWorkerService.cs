using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
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
    private const string SessionSummaryCheckpointPrefix = "stage5:summary:session";
    private const string SessionSkipCounterPrefix = "stage5:skip:msg";
    private const int SessionSkipQuarantineThreshold = 3;
    private const string SessionSkipQuarantineReason = "session_limit_skipped_more_than_3_times";
    private const int UncappedSessionSliceFetchWindowSessions = 200;
    private const string CheapPromptId = "stage5_cheap_extract_v10";
    private const string ExpensivePromptId = "stage5_expensive_reason_v5";
    private const int MaxCheapLlmBatchSize = 100;
    private static readonly Regex ServicePlaceholderRegex = new(@"^\[[A-Z_]{2,32}\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CyrillicRegex = new(@"[\p{IsCyrillic}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan OpenRouterRecoveryProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OpenRouterRecoveryPollInterval = TimeSpan.FromSeconds(15);

    private readonly AnalysisSettings _settings;
    private readonly EmbeddingSettings _embeddingSettings;
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
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<AnalysisWorkerService> _logger;
    private readonly DateTime? _archiveCutoffUtc;

    public AnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IOptions<EmbeddingSettings> embeddingSettings,
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
        SummaryHistoricalRetrievalService historicalRetrievalService,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<AnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _embeddingSettings = embeddingSettings.Value;
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
        _historicalRetrievalService = historicalRetrievalService;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
        _archiveCutoffUtc = ParseArchiveCutoffUtc(_settings.ArchiveCutoffUtc);
        if (!string.IsNullOrWhiteSpace(_settings.ArchiveCutoffUtc) && !_archiveCutoffUtc.HasValue)
        {
            _logger.LogWarning(
                "Stage5 archive cutoff parse failed. value={ArchiveCutoffUtc}. Expected ISO-8601 UTC format, example: 2026-03-06T23:59:59Z",
                _settings.ArchiveCutoffUtc);
        }
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
            "Stage5 analysis worker started. cheap_model={Cheap}, expensive_model={Expensive}, cheap_parallelism={CheapParallelism}, cheap_batch_workers={CheapBatchWorkers}, session_chunk_size={SessionChunkSize}, session_chunk_parallelism={SessionChunkParallelism}, cheap_chunk_target_chars={CheapChunkTargetChars}, cheap_chunk_max_chars={CheapChunkMaxChars}, cheap_chunk_min_messages={CheapChunkMinMessages}, cheap_chunk_pause_gap_min={CheapChunkPauseGapMinutes}, archive_only_mode={ArchiveOnlyMode}, archive_cutoff_utc={ArchiveCutoffUtc}",
            _settings.CheapModel,
            _settings.ExpensiveModel,
            GetCheapLlmParallelism(),
            GetCheapBatchWorkers(),
            Math.Clamp(_settings.SessionChunkSize, 10, 100),
            GetSessionChunkParallelism(),
            Math.Max(4000, _settings.CheapChunkTargetChars),
            Math.Max(Math.Max(4000, _settings.CheapChunkTargetChars), _settings.CheapChunkMaxChars),
            Math.Max(2, _settings.CheapChunkMinMessages),
            Math.Max(1, _settings.CheapChunkPauseGapMinutes),
            _settings.ArchiveOnlyMode,
            _archiveCutoffUtc);
        _logger.LogInformation(
            "Stage5 operational paths: expensive_pass_enabled={ExpensiveEnabled}, expensive_batch_limit={ExpensiveBatchLimit}, expensive_daily_budget_usd={ExpensiveDailyBudget:0.000000}, summary_inline_enabled={SummaryInlineEnabled}, summary_llm_enabled={SummaryLlmEnabled}, summary_worker_enabled={SummaryWorkerEnabled}, summary_historical_hints_enabled={SummaryHistoricalHintsEnabled}, summary_historical_embedding_model={SummaryHistoricalEmbeddingModel}, edit_diff_enabled={EditDiffEnabled}",
            IsExpensivePassEnabled(),
            Math.Max(0, _settings.MaxExpensivePerBatch),
            _settings.ExpensiveDailyBudgetUsd,
            true,
            _settings.SummaryEnabled,
            _settings.SummaryWorkerEnabled,
            _settings.SummaryHistoricalHintsEnabled,
            ResolveSummaryHistoricalEmbeddingModel(),
            _settings.EditDiffEnabled);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cheapPathDecision = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
                {
                    PathKey = "stage5_cheap",
                    Modality = BudgetModalities.TextAnalysis,
                    IsImportScope = false,
                    IsOptionalPath = false
                }, stoppingToken);
                if (cheapPathDecision.ShouldPausePath)
                {
                    _logger.LogWarning(
                        "Stage5 cheap path paused by budget guardrail. state={State}, reason={Reason}",
                        cheapPathDecision.State,
                        cheapPathDecision.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
                    continue;
                }

                var expensiveResolved = await _expensivePassResolver.ProcessExpensiveBacklogAsync(
                    async ct => (await GetPromptAsync(ExpensivePromptId, DefaultExpensivePrompt, ct)).SystemPrompt,
                    stoppingToken);
                if (expensiveResolved > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                }

                var reanalysis = ApplyArchiveScope(
                    await _messageRepository.GetNeedsReanalysisAsync(GetFetchLimit(), stoppingToken),
                    "reanalysis");
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

        var analyzedSessionsCount = 0;
        var maxAnalyzedSessionEndMs = 0L;
        foreach (var session in sessions)
        {
            if (!await CanProcessSessionSequentiallyAsync(session, ct))
            {
                _logger.LogInformation(
                    "Stage5 session-first paused to preserve strict order: chat_id={ChatId}, session_index={SessionIndex}",
                    session.ChatId,
                    session.SessionIndex);
                break;
            }

            var maxSessionMessages = Math.Max(500, Math.Max(_settings.SummarySessionMaxMessages * 10, _settings.SessionChunkSize * 10));
            var sessionMessages = await _messageRepository.GetByChatAndPeriodAsync(
                session.ChatId,
                session.StartDate,
                session.EndDate,
                maxSessionMessages,
                ct);
            sessionMessages = ApplyArchiveScope(sessionMessages, "session_first")
                .Where(x => x.Timestamp >= session.StartDate && x.Timestamp <= session.EndDate)
                .OrderBy(x => x.Timestamp)
                .ThenBy(x => x.Id)
                .ToList();

            if (sessionMessages.Count == 0)
            {
                _logger.LogInformation(
                    "Stage5 session-first skipped by archive scope: session_id={SessionId}, chat_id={ChatId}, session_index={SessionIndex}",
                    session.Id,
                    session.ChatId,
                    session.SessionIndex);
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
                break;
            }

            await EnsureSessionSummaryAfterSliceAsync(session, sessionMessages, ct);
            maxAnalyzedSessionEndMs = await MarkSessionAnalyzedAndAdvanceWatermarkAsync(session, maxAnalyzedSessionEndMs, ct);
            analyzedSessionsCount++;
        }

        if (analyzedSessionsCount > 0)
        {
            _logger.LogInformation(
                "Stage5 session-first pass done: analyzed_sessions={Count}, session_watermark_ms={WatermarkMs}",
                analyzedSessionsCount,
                maxAnalyzedSessionEndMs);
        }

        return analyzedSessionsCount;
    }

    private async Task<long> MarkSessionAnalyzedAndAdvanceWatermarkAsync(ChatSession session, long currentWatermarkMs, CancellationToken ct)
    {
        var nextWatermarkMs = Math.Max(currentWatermarkMs, new DateTimeOffset(session.EndDate).ToUnixTimeMilliseconds());
        await _chatSessionRepository.MarkAnalyzedAsync([session.Id], ct);
        await _stateRepository.SetWatermarkAsync(SessionWatermarkKey, nextWatermarkMs, ct);
        return nextWatermarkMs;
    }

    private async Task<bool> CanProcessSessionSequentiallyAsync(ChatSession session, CancellationToken ct)
    {
        if (session.SessionIndex <= 0)
        {
            return true;
        }

        var byChat = await _chatSessionRepository.GetByChatsAsync([session.ChatId], ct);
        var chatSessions = byChat.GetValueOrDefault(session.ChatId) ?? [];
        var previous = chatSessions.FirstOrDefault(x => x.SessionIndex == session.SessionIndex - 1);
        if (previous == null)
        {
            _logger.LogWarning(
                "Stage5 sequential gate blocked: previous session is missing. chat_id={ChatId}, session_index={SessionIndex}",
                session.ChatId,
                session.SessionIndex);
            return false;
        }

        if (!previous.IsAnalyzed)
        {
            _logger.LogInformation(
                "Stage5 sequential gate blocked: previous session is not analyzed yet. chat_id={ChatId}, prev_session_index={PrevSessionIndex}, session_index={SessionIndex}",
                session.ChatId,
                previous.SessionIndex,
                session.SessionIndex);
            return false;
        }

        if (string.IsNullOrWhiteSpace(previous.Summary))
        {
            _logger.LogInformation(
                "Stage5 sequential gate blocked: previous session summary is empty. chat_id={ChatId}, prev_session_index={PrevSessionIndex}, session_index={SessionIndex}",
                session.ChatId,
                previous.SessionIndex,
                session.SessionIndex);
            return false;
        }

        return true;
    }

    private async Task<int> SeedSessionsFromProcessedBacklogAsync(CancellationToken ct)
    {
        var watermark = await _stateRepository.GetWatermarkAsync(SessionSeedWatermarkKey, ct);
        var seedBatchSize = Math.Max(GetFetchLimit() * 5, 100);
        var fetchedMessages = await _messageRepository.GetProcessedAfterIdAsync(watermark, seedBatchSize, ct);
        if (fetchedMessages.Count == 0)
        {
            return 0;
        }

        var messages = ApplyArchiveScope(fetchedMessages, "session_seed");
        if (messages.Count == 0)
        {
            await _stateRepository.SetWatermarkAsync(SessionSeedWatermarkKey, fetchedMessages.Max(x => x.Id), ct);
            return 0;
        }

        await EnsureSessionSlicesForMessagesAsync(messages, ct);
        await _stateRepository.SetWatermarkAsync(SessionSeedWatermarkKey, fetchedMessages.Max(x => x.Id), ct);
        _logger.LogInformation(
            "Stage5 session seed pass done: processed_messages={Count}, seed_watermark={Watermark}",
            messages.Count,
            fetchedMessages.Max(x => x.Id));
        return messages.Count;
    }

    private async Task<CheapPassResult> ProcessSessionInChunksAsync(ChatSession session, List<Message> sessionMessages, CancellationToken ct)
    {
        var failedChunkMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        var chunks = BuildAdaptiveSessionChunks(sessionMessages);
        var checkpointKey = BuildSessionChunkCheckpointKey(session);
        var savedIndex = (int)Math.Max(0, await _stateRepository.GetWatermarkAsync(checkpointKey, ct));
        var startIndex = Math.Clamp(savedIndex, 0, chunks.Count);
        if (startIndex > 0 && chunks.Count > 0)
        {
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
        var previousSliceSummary = chatSessions
            .FirstOrDefault(x => x.SessionIndex == session.SessionIndex - 1)
            ?.Summary;
        var sessionChunkParallelism = GetSessionChunkParallelism();

        if (sessionChunkParallelism <= 1 || (chunks.Count - startIndex) <= 1)
        {
            for (var i = startIndex; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var replySliceContext = await BuildReplySliceContextAsync(session, chunk, chatSessions, ct);
                var effectiveChunkSummaryPrev = BuildPreviousSlicePrompt(
                    session,
                    i,
                    previousSliceSummary,
                    replySliceContext);

                var ragContext = await BuildRagContextAsync(session, chunk, effectiveChunkSummaryPrev, replySliceContext, ct);
                var result = await ProcessCheapBatchesAsync(
                    chunk,
                    ct,
                    effectiveChunkSummaryPrev,
                    replySliceContext,
                    ragContext,
                    skipSessionPreparationAndGuard: true);
                foreach (var failedId in result.FailedMessageIds)
                {
                    failedChunkMessageIds.TryAdd(failedId, 0);
                }

                if (result.BalanceIssueDetected)
                {
                    Interlocked.Exchange(ref balanceIssueDetected, 1);
                }

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
                if (balanceIssueDetected == 1)
                {
                    break;
                }
            }
        }
        else
        {
            _logger.LogInformation(
                "Stage5 session chunk parallel mode: session_id={SessionId}, chat_id={ChatId}, session_index={SessionIndex}, start_chunk={StartChunk}, total_chunks={TotalChunks}, parallelism={Parallelism}",
                session.Id,
                session.ChatId,
                session.SessionIndex,
                startIndex,
                chunks.Count,
                sessionChunkParallelism);

            var succeeded = new bool[chunks.Count];
            var firstFailedChunkIndex = int.MaxValue;
            var indices = Enumerable.Range(startIndex, chunks.Count - startIndex).ToArray();
            await Parallel.ForEachAsync(
                indices,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = sessionChunkParallelism,
                    CancellationToken = ct
                },
                async (i, token) =>
                {
                    if (Volatile.Read(ref balanceIssueDetected) == 1)
                    {
                        return;
                    }

                    var chunk = chunks[i];
                    var replySliceContext = await BuildReplySliceContextAsync(session, chunk, chatSessions, token);
                    var effectiveChunkSummaryPrev = BuildPreviousSlicePrompt(
                        session,
                        i,
                        previousSliceSummary,
                        replySliceContext);
                    var ragContext = await BuildRagContextAsync(session, chunk, effectiveChunkSummaryPrev, replySliceContext, token);
                    var result = await ProcessCheapBatchesAsync(
                        chunk,
                        token,
                        effectiveChunkSummaryPrev,
                        replySliceContext,
                        ragContext,
                        skipSessionPreparationAndGuard: true);

                    if (result.BalanceIssueDetected)
                    {
                        Interlocked.Exchange(ref balanceIssueDetected, 1);
                    }

                    if (result.FailedMessageIds.Count > 0)
                    {
                        foreach (var failedId in result.FailedMessageIds)
                        {
                            failedChunkMessageIds.TryAdd(failedId, 0);
                        }

                        var currentFirst = Volatile.Read(ref firstFailedChunkIndex);
                        while (i < currentFirst)
                        {
                            var observed = Interlocked.CompareExchange(ref firstFailedChunkIndex, i, currentFirst);
                            if (observed == currentFirst)
                            {
                                break;
                            }

                            currentFirst = observed;
                        }

                        _logger.LogWarning(
                            "Stage5 session chunk failed in parallel mode: session_id={SessionId}, chunk_index={ChunkIndex}, failed_messages={FailedMessages}",
                            session.Id,
                            i,
                            result.FailedMessageIds.Count);
                        return;
                    }

                    succeeded[i] = true;
                });

            var resumeCheckpoint = startIndex;
            for (var i = startIndex; i < chunks.Count; i++)
            {
                if (!succeeded[i])
                {
                    break;
                }

                resumeCheckpoint = i + 1;
            }

            await _stateRepository.SetWatermarkAsync(checkpointKey, resumeCheckpoint, ct);
            if (failedChunkMessageIds.Count > 0 || balanceIssueDetected == 1)
            {
                _logger.LogWarning(
                    "Stage5 session parallel checkpoint updated: session_id={SessionId}, resume_chunk={ResumeChunk}, first_failed_chunk={FirstFailedChunk}, failed_messages={FailedMessages}, balance_issue={BalanceIssue}",
                    session.Id,
                    resumeCheckpoint,
                    firstFailedChunkIndex == int.MaxValue ? -1 : firstFailedChunkIndex,
                    failedChunkMessageIds.Count,
                    balanceIssueDetected == 1);
            }
        }

        if (balanceIssueDetected == 0 && failedChunkMessageIds.Count == 0)
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
            failedChunkMessageIds.Count);

        return new CheapPassResult(failedChunkMessageIds.Keys.ToHashSet(), balanceIssueDetected == 1);
    }

    private string BuildPreviousSlicePrompt(
        ChatSession session,
        int chunkIndex,
        string? previousSliceSummary,
        string? preDialogContext)
    {
        if (session.SessionIndex <= 0)
        {
            return BuildBootstrapChunkContext(
                "dialog_start",
                session,
                chunkIndex,
                preDialogContext);
        }

        if (!string.IsNullOrWhiteSpace(previousSliceSummary))
        {
            return "Краткая выжимка предыдущего слайса:\n" +
                   MessageContentBuilder.TruncateForContext(
                       MessageContentBuilder.CollapseWhitespace(previousSliceSummary),
                       900);
        }

        _logger.LogInformation(
            "Stage5 previous-slice summary is missing, using bootstrap context: chat_id={ChatId}, session_index={SessionIndex}, chunk_index={ChunkIndex}",
            session.ChatId,
            session.SessionIndex,
            chunkIndex);
        return BuildBootstrapChunkContext(
            "missing_previous_summary",
            session,
            chunkIndex,
            preDialogContext);
    }

    private static string BuildBootstrapChunkContext(
        string reason,
        ChatSession session,
        int chunkIndex,
        string? preDialogContext)
    {
        var lines = new List<string>
        {
            "DIALOG_BOOTSTRAP_MARKER: session_boundary_start",
            $"reason={reason};chat_id={session.ChatId};session_index={session.SessionIndex};chunk_index={chunkIndex}"
        };

        if (!string.IsNullOrWhiteSpace(preDialogContext))
        {
            lines.Add("[PRE_DIALOG_CONTEXT]");
            lines.Add(MessageContentBuilder.TruncateForContext(preDialogContext, 700));
            lines.Add("[/PRE_DIALOG_CONTEXT]");
        }

        return string.Join("\n", lines);
    }

    private async Task<CheapPassResult> ProcessCheapBatchesAsync(
        List<Message> messages,
        CancellationToken ct,
        string? chunkSummaryPrev = null,
        string? replySliceContext = null,
        string? ragContext = null,
        bool skipSessionPreparationAndGuard = false)
    {
        var timer = Stopwatch.StartNew();
        var initialCount = messages.Count;
        var batchScope = ResolveCheapBatchScope(chunkSummaryPrev, replySliceContext, ragContext);
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

        var episodicGuard = new EpisodicGuardResult(messages, [], [], []);
        if (!skipSessionPreparationAndGuard)
        {
            await EnsureSessionSlicesForMessagesAsync(messages, ct);
            episodicGuard = await ApplyEpisodicMemoryGuardAsync(messages, ct);
            var (sessionLimit, sessionLimitMode) = GetEpisodicSessionLimitInfo();
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
                await _messageRepository.MarkNeedsReanalysisAsync(
                    episodicGuard.BlockedPendingSummaryMessageIds,
                    "stage5_blocked_pending_summary",
                    ct);
                _logger.LogInformation(
                    "Stage5 episodic guard deferred messages pending session summaries: count={Count}",
                    episodicGuard.BlockedPendingSummaryMessageIds.Count);
            }

            if (episodicGuard.SkippedBySessionLimitMessageIds.Count > 0)
            {
                _logger.LogInformation(
                    "Stage5 episodic guard skipped messages beyond session limit: count={Count}, max_sessions_per_chat={MaxSessions}, limit_mode={LimitMode}, newly_quarantined={QuarantinedCount}, quarantine_reason={QuarantineReason}",
                    episodicGuard.SkippedBySessionLimitMessageIds.Count,
                    sessionLimit,
                    sessionLimitMode,
                    newlyQuarantined.Count,
                    SessionSkipQuarantineReason);
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
        }

        var failedMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        foreach (var blockedId in episodicGuard.BlockedPendingSummaryMessageIds)
        {
            failedMessageIds.TryAdd(blockedId, 0);
        }

        // Adaptive chunker below is now the primary request shaper.
        // Keep a single pre-batch to avoid redundant fixed slicing before payload-aware chunking.
        _logger.LogInformation(
            "Stage5 slicing: session_chunk_messages={MessageCount}, pre_batch_count=1, cheap_chunk_target_chars={TargetChars}, cheap_chunk_max_chars={MaxChars}",
            messages.Count,
            Math.Max(4000, _settings.CheapChunkTargetChars),
            Math.Max(Math.Max(4000, _settings.CheapChunkTargetChars), _settings.CheapChunkMaxChars));

        try
        {
            var batchResult = await ProcessCheapBatchAsync(messages, ct, chunkSummaryPrev, replySliceContext, ragContext);
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
                messages.Count);

            foreach (var message in messages)
            {
                failedMessageIds.TryAdd(message.Id, 0);
            }

            await _messageRepository.MarkNeedsReanalysisAsync(
                messages.Select(x => x.Id),
                "stage5_cheap_batch_exception",
                ct);
            await _extractionErrorRepository.LogAsync(
                stage: "stage5_cheap_worker_batch",
                reason: ex.Message,
                payload: $"batch_size={messages.Count}",
                ct: ct);
        }

        var result = new CheapPassResult(
            failedMessageIds.Keys.ToHashSet(),
            balanceIssueDetected == 1);
        timer.Stop();
        LogCheapWorkerBatchSummary(
            batchScope,
            initialCount,
            messages.Count,
            result,
            episodicGuard,
            timer.ElapsedMilliseconds);
        return result;
    }

    private async Task<CheapPassResult> ProcessCheapBatchAsync(
        List<Message> messages,
        CancellationToken ct,
        string? chunkSummaryPrev = null,
        string? replySliceContext = null,
        string? ragContext = null)
    {
        var timer = Stopwatch.StartNew();
        var analyzableMessages = FilterAnalyzableMessages(messages, out var skippedByReason);
        LogCheapPrefilterSkips(messages.Count, analyzableMessages.Count, skippedByReason);

        var modelByMessageId = BuildCheapModelMap(messages);
        var byId = new ConcurrentDictionary<long, ExtractionItem>();
        var failedChunkMessageIds = new ConcurrentDictionary<long, byte>();
        var balanceIssueDetected = 0;
        var modelGroups = 0;
        var cheapChunkRequests = 0;
        var validationRejected = 0;
        var needsExpensiveMarked = 0;
        if (analyzableMessages.Count > 0)
        {
            var replyContext = await _messageContentBuilder.LoadReplyContextAsync(analyzableMessages, ct);
            var batch = analyzableMessages.Select(m => new AnalysisInputMessage
            {
                MessageId = m.Id,
                SenderName = m.SenderName,
                Timestamp = m.Timestamp,
                Text = MessageContentBuilder.BuildCheapChunkMessageText(
                    m,
                    replyContext.GetValueOrDefault(m.Id))
            }).ToList();

            var cheapPrompt = await GetPromptAsync(CheapPromptId, DefaultCheapPrompt, ct);
            var (computedModelGroups, cheapChunks) = BuildCheapChunkRequests(batch, modelByMessageId);
            modelGroups = computedModelGroups;
            cheapChunkRequests = cheapChunks.Count;
            _logger.LogInformation(
                "Stage5 cheap chunking: incoming={Incoming}, analyzable={Analyzable}, model_groups={Groups}, cheap_chunk_requests={CheapChunks}",
                messages.Count,
                analyzableMessages.Count,
                modelGroups,
                cheapChunks.Count);

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

        await MarkFailedCheapChunksForReanalysisAsync(failedChunkMessageIds, balanceIssueDetected == 1, ct);
        await MarkProcessedMessagesExceptFailedAsync(messages, failedChunkMessageIds, ct);

        foreach (var message in messages)
        {
            if (failedChunkMessageIds.ContainsKey(message.Id))
            {
                continue;
            }

            var itemResult = await ProcessCheapMessageAsync(message, byId, modelByMessageId, ct);
            if (itemResult.ValidationRejected)
            {
                validationRejected++;
            }

            if (itemResult.NeedsExpensiveMarked)
            {
                needsExpensiveMarked++;
            }
        }

        var result = new CheapPassResult(failedChunkMessageIds.Keys.ToHashSet(), balanceIssueDetected == 1);
        timer.Stop();
        LogCheapBatchSummary(
            messages.Count,
            analyzableMessages.Count,
            modelGroups,
            cheapChunkRequests,
            result,
            validationRejected,
            needsExpensiveMarked,
            timer.ElapsedMilliseconds);
        return result;
    }

    private static List<Message> FilterAnalyzableMessages(
        List<Message> messages,
        out Dictionary<string, int> skippedByReason)
    {
        var analyzableMessages = new List<Message>(messages.Count);
        skippedByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in messages)
        {
            if (TryGetCheapSkipReason(message, out var reason))
            {
                skippedByReason[reason] = skippedByReason.GetValueOrDefault(reason) + 1;
                continue;
            }

            analyzableMessages.Add(message);
        }

        return analyzableMessages;
    }

    private void LogCheapPrefilterSkips(int totalCount, int analyzableCount, Dictionary<string, int> skippedByReason)
    {
        if (skippedByReason.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Stage5 cheap prefilter skipped={Skipped} of {Total}: {Reasons}",
            totalCount - analyzableCount,
            totalCount,
            string.Join(", ", skippedByReason.Select(x => $"{x.Key}={x.Value}")));
    }

    private void LogCheapBatchSummary(
        int incomingCount,
        int analyzableCount,
        int modelGroups,
        int cheapChunkRequests,
        CheapPassResult result,
        int validationRejected,
        int needsExpensiveMarked,
        long durationMs)
    {
        _logger.LogInformation(
            "Stage5 cheap batch summary: incoming={Incoming}, analyzable={Analyzable}, skipped_prefilter={SkippedPrefilter}, model_groups={ModelGroups}, cheap_chunk_requests={CheapChunks}, failed_chunk_messages={FailedChunkMessages}, validation_rejected={ValidationRejected}, marked_expensive={MarkedExpensive}, balance_issue={BalanceIssue}, duration_ms={DurationMs}",
            incomingCount,
            analyzableCount,
            incomingCount - analyzableCount,
            modelGroups,
            cheapChunkRequests,
            result.FailedMessageIds.Count,
            validationRejected,
            needsExpensiveMarked,
            result.BalanceIssueDetected,
            durationMs);
    }

    private (int ModelGroups, List<CheapChunkRequest> Requests) BuildCheapChunkRequests(
        List<AnalysisInputMessage> batch,
        Dictionary<long, string> modelByMessageId)
    {
        var groups = batch
            .GroupBy(x => modelByMessageId.GetValueOrDefault(x.MessageId, _settings.CheapModel))
            .ToList();
        var requests = groups
            .SelectMany(group => BuildAdaptiveCheapChunks(group.ToList())
                .Select(chunk => new CheapChunkRequest(group.Key, chunk)))
            .ToList();
        return (groups.Count, requests);
    }

    private async Task MarkFailedCheapChunksForReanalysisAsync(
        ConcurrentDictionary<long, byte> failedChunkMessageIds,
        bool balanceIssueDetected,
        CancellationToken ct)
    {
        if (failedChunkMessageIds.IsEmpty)
        {
            return;
        }

        await _messageRepository.MarkNeedsReanalysisAsync(
            failedChunkMessageIds.Keys,
            "stage5_cheap_chunk_failed",
            ct);
        _logger.LogWarning(
            "Stage5 tagged failed cheap chunk messages for reanalysis: count={Count}, balance_issue={BalanceIssue}",
            failedChunkMessageIds.Count,
            balanceIssueDetected);
    }

    private async Task MarkProcessedMessagesExceptFailedAsync(
        List<Message> messages,
        ConcurrentDictionary<long, byte> failedChunkMessageIds,
        CancellationToken ct)
    {
        var processedMessageIds = messages
            .Select(x => x.Id)
            .Where(id => !failedChunkMessageIds.ContainsKey(id))
            .ToArray();
        if (processedMessageIds.Length == 0)
        {
            return;
        }

        await _messageRepository.MarkProcessedAsync(processedMessageIds, ct);
    }

    private async Task<CheapItemResult> ProcessCheapMessageAsync(
        Message message,
        ConcurrentDictionary<long, ExtractionItem> byId,
        Dictionary<long, string> modelByMessageId,
        CancellationToken ct)
    {
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

                return new CheapItemResult(ValidationRejected: true, NeedsExpensiveMarked: false);
            }

            var needsExpensive = IsExpensivePassEnabled() &&
                                 ExtractionRefiner.ShouldRunExpensivePass(message, extracted, _settings);
            await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase), needsExpensive, ct);

            if (!needsExpensive)
            {
                await _extractionApplier.ApplyExtractionAsync(message.Id, extracted, message, ct);
            }

            return new CheapItemResult(ValidationRejected: false, NeedsExpensiveMarked: needsExpensive);
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
            return new CheapItemResult(ValidationRejected: false, NeedsExpensiveMarked: false);
        }
    }

    private async Task EnsureSessionSummaryAfterSliceAsync(ChatSession session, List<Message> sessionMessages, CancellationToken ct)
    {
        var byChat = await _chatSessionRepository.GetByChatsAsync([session.ChatId], ct);
        var existing = byChat.GetValueOrDefault(session.ChatId)?
            .FirstOrDefault(x => x.SessionIndex == session.SessionIndex);
        var sessionInput = BuildSessionSummaryInput(sessionMessages);

        var summary = string.Empty;
        if (_settings.SummaryEnabled && sessionInput.Count >= _settings.SummaryMinMessages)
        {
            var historicalHints = await _historicalRetrievalService.GetHintsAsync(
                session.ChatId,
                session.SessionIndex,
                sessionInput,
                ct);
            summary = await GenerateSessionSummaryAsync(
                session.ChatId,
                session.StartDate,
                session.EndDate,
                sessionInput,
                historicalHints,
                ct);
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = BuildFastSessionSummary(sessionInput.Count > 0 ? sessionInput : sessionMessages);
        }

        var previousSummary = MessageContentBuilder.CollapseWhitespace(existing?.Summary ?? string.Empty);
        var normalizedSummary = MessageContentBuilder.CollapseWhitespace(summary);
        if (string.Equals(previousSummary, normalizedSummary, StringComparison.Ordinal))
        {
            await SetSessionSummaryCheckpointAsync(session, ct);
            return;
        }

        await _chatSessionRepository.UpsertAsync(new ChatSession
        {
            Id = session.Id,
            ChatId = session.ChatId,
            SessionIndex = session.SessionIndex,
            StartDate = session.StartDate,
            EndDate = session.EndDate,
            LastMessageAt = session.EndDate,
            Summary = summary,
            IsFinalized = existing?.IsFinalized ?? session.IsFinalized,
            IsAnalyzed = false
        }, ct);

        await _historicalRetrievalService.UpsertSessionSummaryEmbeddingAsync(
            session.ChatId,
            session.SessionIndex,
            summary,
            ct);
        await SetSessionSummaryCheckpointAsync(session, ct);
    }

    private List<Message> BuildSessionSummaryInput(List<Message> sessionMessages)
    {
        var sessionInput = FilterSummarizableMessages(sessionMessages)
            .Take(Math.Max(1, _settings.SummarySessionMaxMessages))
            .ToList();
        if (sessionInput.Count >= _settings.SummaryMinMessages)
        {
            return sessionInput;
        }

        return sessionMessages
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.Id)
            .Take(Math.Max(1, _settings.SummarySessionMaxMessages))
            .ToList();
    }

    private async Task SetSessionSummaryCheckpointAsync(ChatSession session, CancellationToken ct)
    {
        var summaryCheckpointKey = BuildSessionSummaryCheckpointKey(session.ChatId, session.SessionIndex);
        var sessionEndMs = new DateTimeOffset(session.EndDate).ToUnixTimeMilliseconds();
        await _stateRepository.SetWatermarkAsync(summaryCheckpointKey, sessionEndMs, ct);
    }

    private static string BuildFastSessionSummary(List<Message> messages)
    {
        var lines = messages
            .Where(message => !MessageContentBuilder.IsServiceOrTechnicalNoise(message))
            .OrderBy(x => x.Timestamp)
            .Take(6)
            .Select(message =>
            {
                var sender = string.IsNullOrWhiteSpace(message.SenderName)
                    ? $"user:{message.SenderId}"
                    : message.SenderName.Trim();
                var text = MessageContentBuilder.TruncateForContext(
                    MessageContentBuilder.BuildSemanticContent(message),
                    120);
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : $"[{message.Timestamp:MM-dd HH:mm}] {sender}: {text}";
            })
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        return lines.Count == 0
            ? "Сводка сессии недоступна."
            : string.Join(" | ", lines);
    }

    private async Task<string> GenerateSessionSummaryAsync(
        long chatId,
        DateTime periodStart,
        DateTime periodEnd,
        List<Message> messages,
        IReadOnlyCollection<SummaryHistoricalHint>? historicalHints,
        CancellationToken ct)
    {
        var cheapJsonByMessageId = await _extractionRepository.GetCheapJsonByMessageIdsAsync(
            messages.Select(x => x.Id).ToArray(),
            ct);

        try
        {
            var response = await _analysisService.SummarizeDialogAsync(
                string.IsNullOrWhiteSpace(_settings.SummaryModel) ? _settings.ExpensiveModel : _settings.SummaryModel,
                SummaryPrompt,
                chatId,
                "episodic_slice",
                periodStart,
                periodEnd,
                messages,
                historicalHints,
                cheapJsonByMessageId,
                ct);
            var summary = ExtractSummary(response);
            if (IsLikelyRussianSession(messages) && !ContainsCyrillic(summary))
            {
                _logger.LogWarning(
                    "Stage5 summary response has no Cyrillic symbols for likely Russian chat. chat_id={ChatId}, period_start={PeriodStart}, period_end={PeriodEnd}",
                    chatId,
                    periodStart,
                    periodEnd);
            }

            return summary;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stage5 summary generation failed; will fallback. chat_id={ChatId}, period_start={PeriodStart}, period_end={PeriodEnd}, message_count={MessageCount}",
                chatId,
                periodStart,
                periodEnd,
                messages.Count);
            return string.Empty;
        }
    }

    private static List<Message> FilterSummarizableMessages(List<Message> messages)
    {
        return messages
            .Where(message => !MessageContentBuilder.IsServiceOrTechnicalNoise(message))
            .ToList();
    }

    private static string ExtractSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("summary", out var summaryElement))
            {
                var summary = summaryElement.GetString();
                return string.IsNullOrWhiteSpace(summary)
                    ? string.Empty
                    : MessageContentBuilder.CollapseWhitespace(summary);
            }
        }
        catch (JsonException)
        {
            // Ignore and fallback to plain-text interpretation.
        }

        return MessageContentBuilder.CollapseWhitespace(raw);
    }

    private static bool ContainsCyrillic(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && CyrillicRegex.IsMatch(value);
    }

    private static bool IsLikelyRussianSession(IReadOnlyCollection<Message> messages)
    {
        return messages.Any(message => ContainsCyrillic(MessageContentBuilder.BuildSemanticContent(message)));
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
        var pausedSinceUtc = DateTime.UtcNow;
        var lastProbeErrorType = "none";
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
                    var pausedForSeconds = Math.Max(0, (DateTime.UtcNow - pausedSinceUtc).TotalSeconds);
                    _logger.LogInformation(
                        "OpenRouter recovery probe succeeded after attempts={Attempts}. Resuming Stage5. paused_for_s={PausedForSeconds}",
                        attempts,
                        pausedForSeconds);
                    return;
                }

                lastProbeErrorType = "availability_false";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Probe timeout; keep waiting.
                lastProbeErrorType = "probe_timeout";
            }
            catch (Exception ex)
            {
                lastProbeErrorType = ex.GetType().Name;
                _logger.LogWarning(ex, "OpenRouter recovery probe failed on attempt={Attempt}", attempts);
            }

            var pausedForSecondsHeartbeat = Math.Max(0, (DateTime.UtcNow - pausedSinceUtc).TotalSeconds);
            _logger.LogWarning(
                "OpenRouter recovery wait heartbeat: attempt={Attempt}, paused_for_s={PausedForSeconds}, poll_interval_s={PollIntervalSeconds}, probe_timeout_s={ProbeTimeoutSeconds}, last_probe_error={LastProbeError}",
                attempts,
                pausedForSecondsHeartbeat,
                OpenRouterRecoveryPollInterval.TotalSeconds,
                OpenRouterRecoveryProbeTimeout.TotalSeconds,
                lastProbeErrorType);
            await Task.Delay(OpenRouterRecoveryPollInterval, ct);
        }
    }

    private static string ResolveCheapBatchScope(string? chunkSummaryPrev, string? replySliceContext, string? ragContext)
    {
        return string.IsNullOrWhiteSpace(chunkSummaryPrev)
               && string.IsNullOrWhiteSpace(replySliceContext)
               && string.IsNullOrWhiteSpace(ragContext)
            ? "reanalysis"
            : "session_chunk";
    }

    private void LogCheapWorkerBatchSummary(
        string scope,
        int incomingCount,
        int allowedCount,
        CheapPassResult result,
        EpisodicGuardResult episodicGuard,
        long durationMs)
    {
        _logger.LogInformation(
            "Stage5 cheap worker summary: scope={Scope}, incoming={Incoming}, allowed={Allowed}, failed={Failed}, blocked_pending_summary={BlockedPendingSummary}, skipped_session_limit={SkippedSessionLimit}, skipped_no_session={SkippedNoSession}, balance_issue={BalanceIssue}, duration_ms={DurationMs}",
            scope,
            incomingCount,
            allowedCount,
            result.FailedMessageIds.Count,
            episodicGuard.BlockedPendingSummaryMessageIds.Count,
            episodicGuard.SkippedBySessionLimitMessageIds.Count,
            episodicGuard.SkippedNoSessionAssignmentMessageIds.Count,
            result.BalanceIssueDetected,
            durationMs);
    }

    private int GetCheapLlmParallelism()
    {
        return Math.Clamp(_settings.CheapLlmParallelism, 1, 16);
    }

    private int GetSessionChunkParallelism()
    {
        return Math.Clamp(_settings.SessionChunkParallelism, 1, 8);
    }

    private async Task<EpisodicGuardResult> ApplyEpisodicMemoryGuardAsync(List<Message> messages, CancellationToken ct)
    {
        var (limit, _) = GetEpisodicSessionLimitInfo();
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

    private (int Limit, string Mode) GetEpisodicSessionLimitInfo()
    {
        var explicitLimit = Math.Max(0, _settings.EpisodicMaxSessionsPerChat);
        if (explicitLimit > 0)
        {
            return (explicitLimit, "explicit");
        }

        if (_settings.EnableTestModeSessionCap)
        {
            return (Math.Max(0, _settings.TestModeMaxSessionsPerChat), "test_mode");
        }

        return (0, "disabled");
    }

    private static DateTime? ParseArchiveCutoffUtc(string? rawCutoffUtc)
    {
        if (string.IsNullOrWhiteSpace(rawCutoffUtc))
        {
            return null;
        }

        if (!DateTime.TryParse(
                rawCutoffUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    private List<Message> ApplyArchiveScope(List<Message> messages, string scope)
    {
        if (messages.Count == 0)
        {
            return messages;
        }

        if (!_settings.ArchiveOnlyMode && !_archiveCutoffUtc.HasValue)
        {
            return messages;
        }

        var filtered = messages
            .Where(IsInArchiveScope)
            .ToList();
        var skipped = messages.Count - filtered.Count;
        if (skipped > 0)
        {
            _logger.LogInformation(
                "Stage5 archive scope filtered messages: scope={Scope}, input={Input}, kept={Kept}, skipped={Skipped}, archive_only_mode={ArchiveOnlyMode}, archive_cutoff_utc={ArchiveCutoffUtc}",
                scope,
                messages.Count,
                filtered.Count,
                skipped,
                _settings.ArchiveOnlyMode,
                _archiveCutoffUtc);
        }

        return filtered;
    }

    private bool IsInArchiveScope(Message message)
    {
        if (_settings.ArchiveOnlyMode && message.Source != MessageSource.Archive)
        {
            return false;
        }

        if (_archiveCutoffUtc.HasValue && message.Timestamp > _archiveCutoffUtc.Value)
        {
            return false;
        }

        return true;
    }

    private int GetEpisodicSessionLimit() => GetEpisodicSessionLimitInfo().Limit;

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
            _logger.LogWarning(
                "Stage5 episodic guard quarantined messages due to repeated session-limit skips: count={Count}, threshold={Threshold}, reason={Reason}",
                quarantineIds.Count,
                SessionSkipQuarantineThreshold,
                SessionSkipQuarantineReason);
        }

        return quarantineIds;
    }

    private static string BuildSessionSkipCounterKey(long messageId)
    {
        return $"{SessionSkipCounterPrefix}:{messageId}";
    }

    private async Task EnsureSessionSlicesForMessagesAsync(IReadOnlyCollection<Message> messages, CancellationToken ct)
    {
        var configuredSessionLimit = GetEpisodicSessionLimit();
        if (messages.Count == 0)
        {
            return;
        }

        var applySessionCap = configuredSessionLimit > 0;
        var fetchWindowSessions = applySessionCap
            ? configuredSessionLimit
            : UncappedSessionSliceFetchWindowSessions;
        if (configuredSessionLimit <= 0)
        {
            _logger.LogDebug(
                "Stage5 session slicing runs in uncapped mode with safety fetch window. fetch_window_sessions={FetchWindowSessions}",
                fetchWindowSessions);
        }

        var chatIds = messages.Select(x => x.ChatId).Distinct().ToArray();
        var existingByChat = await _chatSessionRepository.GetByChatsAsync(chatIds, ct);
        var fetchLimit = Math.Max(
            500,
            Math.Max(GetFetchLimit(), Math.Max(1, _settings.SummaryDayMaxMessages) * fetchWindowSessions));
        var gap = TimeSpan.FromMinutes(Math.Max(1, _settings.EpisodicSessionGapMinutes));

        foreach (var chatId in chatIds)
        {
            var chatMessages = await _messageRepository.GetProcessedByChatAsync(chatId, fetchLimit, ct);
            chatMessages = ApplyArchiveScope(chatMessages, "session_slice_build");
            if (chatMessages.Count == 0)
            {
                continue;
            }

            var allSessions = SplitByGap(chatMessages, gap, Math.Max(1, _settings.EpisodicShortSessionMergeThreshold));
            var sessions = applySessionCap
                ? allSessions.Take(configuredSessionLimit).ToList()
                : allSessions;
            var existingSessions = existingByChat.GetValueOrDefault(chatId)?
                .OrderBy(x => x.SessionIndex)
                .ToList() ?? [];
            var existingByIndex = existingSessions.ToDictionary(x => x.SessionIndex);
            var windowStart = sessions.Count > 0 && sessions[0].Count > 0
                ? sessions[0][0].Timestamp
                : DateTime.MinValue;
            var baseSessionIndex = applySessionCap
                ? 0
                : ResolveWindowBaseSessionIndex(existingSessions, windowStart);

            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.Count == 0)
                {
                    continue;
                }

                var sessionIndex = baseSessionIndex + i;
                var existing = existingByIndex.GetValueOrDefault(sessionIndex);
                var sessionStart = session.First().Timestamp;
                var sessionEnd = session.Last().Timestamp;
                if (!applySessionCap && existing != null)
                {
                    if (i == 0 && sessionStart > existing.StartDate)
                    {
                        sessionStart = existing.StartDate;
                    }

                    if (i == sessions.Count - 1 && sessionEnd < existing.EndDate)
                    {
                        sessionEnd = existing.EndDate;
                    }
                }

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
                    SessionIndex = sessionIndex,
                    StartDate = sessionStart,
                    EndDate = sessionEnd,
                    LastMessageAt = sessionEnd,
                    Summary = summary,
                    IsFinalized = isFinalized
                }, ct);
            }
        }
    }

    private static int ResolveWindowBaseSessionIndex(List<ChatSession> existingSessions, DateTime windowStart)
    {
        if (existingSessions.Count == 0)
        {
            return 0;
        }

        var overlap = existingSessions.FirstOrDefault(x => x.StartDate <= windowStart && windowStart <= x.EndDate);
        if (overlap != null)
        {
            return overlap.SessionIndex;
        }

        var previous = existingSessions
            .Where(x => x.EndDate < windowStart)
            .OrderByDescending(x => x.SessionIndex)
            .FirstOrDefault();
        if (previous != null)
        {
            return previous.SessionIndex + 1;
        }

        return 0;
    }

    private List<List<Message>> SplitByGap(List<Message> messages, TimeSpan gap, int shortThreshold)
    {
        var maxBridgeGap = TimeSpan.FromMinutes(Math.Max(1, _settings.EpisodicShortSessionMaxBridgeGapMinutes));
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
                var shouldSplit = current.Count >= shortThreshold || delta > maxBridgeGap;
                if (shouldSplit)
                {
                    result.Add(current);
                    current = new List<Message> { message };
                }
                else
                {
                    // Sliding window extension for short sessions:
                    // skip this boundary and keep collecting until next eligible gap.
                    current.Add(message);
                }
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

    private List<List<AnalysisInputMessage>> BuildAdaptiveCheapChunks(List<AnalysisInputMessage> messages)
    {
        var result = new List<List<AnalysisInputMessage>>();
        if (messages.Count == 0)
        {
            return result;
        }

        var targetChars = Math.Max(4000, _settings.CheapChunkTargetChars);
        var maxChars = Math.Max(targetChars, _settings.CheapChunkMaxChars);
        var minMessages = Math.Max(2, _settings.CheapChunkMinMessages);
        var hardMaxMessages = Math.Max(minMessages, MaxCheapLlmBatchSize);
        var pauseGap = TimeSpan.FromMinutes(Math.Max(1, _settings.CheapChunkPauseGapMinutes));
        var ordered = messages
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.MessageId)
            .ToList();

        var current = new List<AnalysisInputMessage>();
        var currentChars = 0;
        foreach (var message in ordered)
        {
            var messageChars = EstimateCheapMessageCostChars(message);
            if (current.Count == 0)
            {
                current.Add(message);
                currentChars = messageChars;
                continue;
            }

            var last = current[^1];
            var delta = message.Timestamp - last.Timestamp;
            var nextChars = currentChars + messageChars;
            var splitByPause = delta > pauseGap && current.Count >= minMessages;
            var splitByBudget = nextChars > targetChars && current.Count >= minMessages;
            var splitByHardLimits = current.Count >= hardMaxMessages || nextChars > maxChars;
            if (splitByPause || splitByBudget || splitByHardLimits)
            {
                result.Add(current);
                current = new List<AnalysisInputMessage> { message };
                currentChars = messageChars;
                continue;
            }

            current.Add(message);
            currentChars = nextChars;
        }

        if (current.Count > 0)
        {
            result.Add(current);
        }

        if (result.Count >= 2 && result[^1].Count < minMessages)
        {
            var penultimate = result[^2];
            var last = result[^1];
            var combinedChars = penultimate.Sum(EstimateCheapMessageCostChars) + last.Sum(EstimateCheapMessageCostChars);
            if (penultimate.Count + last.Count <= hardMaxMessages && combinedChars <= maxChars)
            {
                penultimate.AddRange(last);
                result.RemoveAt(result.Count - 1);
            }
        }

        return result;
    }

    private static int EstimateCheapMessageCostChars(AnalysisInputMessage message)
    {
        var textLen = MessageContentBuilder.CollapseWhitespace(message.Text ?? string.Empty).Length;
        var boundedText = Math.Clamp(textLen, 40, 2200);
        const int metadataOverhead = 220;
        return metadataOverhead + boundedText;
    }

    private string ResolveSingleFallbackModel(string sourceModel, Exception ex)
    {
        if (ex is TaskCanceledException or TimeoutException or HttpRequestException)
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
        // Balanced mode: keep worker fan-out moderate for throughput without aggressive network pressure.
        return Math.Clamp(_settings.CheapBatchWorkers, 1, 4);
    }

    private bool IsExpensivePassEnabled()
    {
        return _settings.ExpensivePassEnabled && _settings.MaxExpensivePerBatch > 0;
    }

    private string ResolveSummaryHistoricalEmbeddingModel()
    {
        var configured = _embeddingSettings.Model?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return "text-embedding-3-small";
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
    private sealed record CheapItemResult(
        bool ValidationRejected,
        bool NeedsExpensiveMarked);
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

        const int shortChunkThreshold = 10;
        if (chunks.Count > 1)
        {
            RebalanceShortSessionChunks(chunks, shortChunkThreshold, hardMaxMessages, maxChars);
        }

        return chunks;
    }

    private static void RebalanceShortSessionChunks(
        List<List<Message>> chunks,
        int shortChunkThreshold,
        int hardMaxMessages,
        int maxChars)
    {
        var i = 0;
        while (i < chunks.Count)
        {
            var current = chunks[i];
            if (current.Count > shortChunkThreshold)
            {
                i++;
                continue;
            }

            var merged = false;
            if (i > 0)
            {
                var previous = chunks[i - 1];
                var combinedCount = previous.Count + current.Count;
                var combinedCost = previous.Sum(EstimateChunkCostChars) + current.Sum(EstimateChunkCostChars);
                if (combinedCount <= hardMaxMessages && combinedCost <= maxChars)
                {
                    previous.AddRange(current);
                    chunks.RemoveAt(i);
                    merged = true;
                    i = Math.Max(0, i - 1);
                }
            }

            if (merged)
            {
                continue;
            }

            if (i + 1 < chunks.Count)
            {
                var next = chunks[i + 1];
                var combinedCount = current.Count + next.Count;
                var combinedCost = current.Sum(EstimateChunkCostChars) + next.Sum(EstimateChunkCostChars);
                if (combinedCount <= hardMaxMessages && combinedCost <= maxChars)
                {
                    current.AddRange(next);
                    chunks.RemoveAt(i + 1);
                }
            }

            i++;
        }
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

    private static string BuildSessionSummaryCheckpointKey(long chatId, int sessionIndex)
    {
        return $"{SessionSummaryCheckpointPrefix}:{chatId}:{sessionIndex}";
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
