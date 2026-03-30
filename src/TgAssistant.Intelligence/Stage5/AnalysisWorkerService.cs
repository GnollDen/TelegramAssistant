using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
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
    private const string ValidationRejectCounterPrefix = "stage5:validation:msg";
    private const int SessionSkipQuarantineThreshold = 3;
    private const string SessionSkipQuarantineReason = "session_limit_skipped_more_than_3_times";
    private const int ValidationRejectQuarantineThreshold = 3;
    private const string ValidationRejectQuarantineReason = "stage5_validation_rejected_retries_exhausted";
    private const int SessionSkipQuarantineRecoveryAgeHours = 6;
    private const int UncappedSessionSliceFetchWindowSessions = 200;
    private const int MaxCheapLlmBatchSize = 100;
    private static readonly Regex ServicePlaceholderRegex = new(@"^\[[A-Z_]{2,32}\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CyrillicRegex = new(@"[\p{IsCyrillic}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan BatchThrottleDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan OpenRouterRecoveryProbeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OpenRouterRecoveryPollInterval = TimeSpan.FromSeconds(15);

    private readonly AnalysisSettings _settings;
    private readonly ChatCoordinationSettings _coordinationSettings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageExtractionRepository _extractionRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IPromptTemplateRepository _promptRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IChatCoordinationService _chatCoordinationService;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly ExpensivePassResolver _expensivePassResolver;
    private readonly ExtractionApplier _extractionApplier;
    private readonly MessageContentBuilder _messageContentBuilder;
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<AnalysisWorkerService> _logger;
    private readonly ManagedPromptTemplate _cheapExtractionPromptContract;
    private readonly string _phaseOwnerId = $"stage5_worker:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private readonly DateTime? _archiveCutoffUtc;
    private long _stage5PhaseGuardDeniedCount;
    private long _sliceBuildPhaseGuardDeniedCount;
    private long _phaseGuardRecoveryAppliedCount;
    private long _phaseLeaseRenewDeniedCount;
    private long _stage5PhaseReleaseMismatchCount;
    private long _sliceBuildPhaseReleaseMismatchCount;

    public AnalysisWorkerService(
        IOptions<AnalysisSettings> settings,
        IOptions<ChatCoordinationSettings> coordinationSettings,
        IOptions<EmbeddingSettings> embeddingSettings,
        IMessageRepository messageRepository,
        IMessageExtractionRepository extractionRepository,
        IExtractionErrorRepository extractionErrorRepository,
        IAnalysisStateRepository stateRepository,
        IPromptTemplateRepository promptRepository,
        IChatSessionRepository chatSessionRepository,
        IChatCoordinationService chatCoordinationService,
        OpenRouterAnalysisService analysisService,
        ExpensivePassResolver expensivePassResolver,
        ExtractionApplier extractionApplier,
        MessageContentBuilder messageContentBuilder,
        SummaryHistoricalRetrievalService historicalRetrievalService,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<AnalysisWorkerService> logger)
    {
        _settings = settings.Value;
        _coordinationSettings = coordinationSettings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _messageRepository = messageRepository;
        _extractionRepository = extractionRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _stateRepository = stateRepository;
        _promptRepository = promptRepository;
        _chatSessionRepository = chatSessionRepository;
        _chatCoordinationService = chatCoordinationService;
        _analysisService = analysisService;
        _expensivePassResolver = expensivePassResolver;
        _extractionApplier = extractionApplier;
        _messageContentBuilder = messageContentBuilder;
        _historicalRetrievalService = historicalRetrievalService;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
        _cheapExtractionPromptContract = Stage5PromptCatalog.ResolveCheapExtraction(_settings.CheapPromptId);
        _archiveCutoffUtc = ParseArchiveCutoffUtc(_settings.ArchiveCutoffUtc);
        if (!string.IsNullOrWhiteSpace(_settings.ArchiveCutoffUtc) && !_archiveCutoffUtc.HasValue)
        {
            _logger.LogWarning(
                "Stage5 archive cutoff parse failed. value={ArchiveCutoffUtc}. Expected ISO-8601 UTC format, example: 2026-03-06T23:59:59Z",
                _settings.ArchiveCutoffUtc);
        }

        if (!string.Equals(
                _cheapExtractionPromptContract.Id,
                (_settings.CheapPromptId ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Stage5 cheap prompt id is unknown, fallback applied. configured={ConfiguredPromptId}, selected={SelectedPromptId}",
                _settings.CheapPromptId,
                _cheapExtractionPromptContract.Id);
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
            "Stage5 analysis worker started. cheap_model={Cheap}, cheap_prompt_id={CheapPromptId}, cheap_prompt_version={CheapPromptVersion}, expensive_model={Expensive}, cheap_parallelism={CheapParallelism}, cheap_batch_workers={CheapBatchWorkers}, session_chunk_size={SessionChunkSize}, session_chunk_parallelism={SessionChunkParallelism}, cheap_chunk_target_chars={CheapChunkTargetChars}, cheap_chunk_max_chars={CheapChunkMaxChars}, cheap_chunk_min_messages={CheapChunkMinMessages}, cheap_chunk_pause_gap_min={CheapChunkPauseGapMinutes}, archive_only_mode={ArchiveOnlyMode}, archive_cutoff_utc={ArchiveCutoffUtc}",
            _settings.CheapModel,
            _cheapExtractionPromptContract.Id,
            _cheapExtractionPromptContract.Version,
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
        _logger.LogInformation(
            "Stage5 session finalization semantics: chat_sessions.is_finalized is cold-path only in current runtime profile; stage5 hot/session-first path does not finalize sessions.");

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
                    async ct => (await GetPromptAsync(Stage5PromptCatalog.ExpensiveReasoning, ct)).SystemPrompt,
                    stoppingToken);
                if (expensiveResolved > 0)
                {
                    await DelayBetweenBatchesAsync(stoppingToken);
                }

                var processedSessions = await ProcessSessionFirstPassAsync(stoppingToken);
                var recoveredFromQuarantine = await RecoverSessionSkipQuarantineAsync(stoppingToken);
                if (recoveredFromQuarantine > 0)
                {
                    _logger.LogInformation(
                        "Stage5 quarantine recovery pass done: recovered={Recovered}, reason={Reason}, min_age_h={MinAgeHours}",
                        recoveredFromQuarantine,
                        SessionSkipQuarantineReason,
                        SessionSkipQuarantineRecoveryAgeHours);
                }

                var reanalysis = ApplyArchiveScope(
                    await _messageRepository.GetNeedsReanalysisAsync(GetFetchLimit(), stoppingToken),
                    "reanalysis");
                if (reanalysis.Count > 0)
                {
                    var anyBalanceIssue = false;
                    var handledMessages = 0;
                    foreach (var byChat in reanalysis
                                 .GroupBy(x => x.ChatId)
                                 .OrderBy(x => x.Key))
                    {
                        if (!await TryAcquireStage5PhaseAsync(byChat.Key, "reanalysis_batch", stoppingToken))
                        {
                            continue;
                        }

                        var leaseHeartbeat = StartPhaseLeaseHeartbeat(
                            byChat.Key,
                            ChatRuntimePhases.Stage5Process,
                            "reanalysis_batch_lease_heartbeat",
                            stoppingToken);
                        var releaseMismatch = false;
                        using var leaseScope = CreateLeaseLinkedTokenSource(leaseHeartbeat, stoppingToken);
                        var leaseCt = leaseScope.Token;
                        try
                        {
                            var chatMessages = byChat.ToList();
                            var reanalysisResult = await ProcessCheapBatchesAsync(chatMessages, leaseCt);
                            var succeededReanalysis = chatMessages
                                .Select(x => x.Id)
                                .Where(id => !reanalysisResult.FailedMessageIds.Contains(id));
                            await _messageRepository.MarkNeedsReanalysisDoneAsync(succeededReanalysis, leaseCt);
                            handledMessages += chatMessages.Count;
                            anyBalanceIssue |= reanalysisResult.BalanceIssueDetected;
                        }
                        catch (OperationCanceledException) when (leaseHeartbeat.LeaseLost && !stoppingToken.IsCancellationRequested)
                        {
                            _logger.LogWarning(
                                "Stage5 reanalysis workload stopped after lease loss: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}",
                                byChat.Key,
                                ChatRuntimePhases.Stage5Process,
                                _phaseOwnerId);
                        }
                        finally
                        {
                            await StopPhaseLeaseHeartbeatAsync(leaseHeartbeat);
                            releaseMismatch = !await ReleaseStage5PhaseAsync(byChat.Key, "reanalysis_batch_done", stoppingToken);
                        }

                        if (releaseMismatch)
                        {
                            _logger.LogWarning(
                                "Stage5 reanalysis pass interrupted due to release mismatch escalation: chat_id={ChatId}",
                                byChat.Key);
                            break;
                        }
                    }

                    if (anyBalanceIssue)
                    {
                        await WaitForOpenRouterRecoveryAsync(stoppingToken);
                    }

                    _logger.LogInformation("Stage5 reanalysis pass done: processed={Count}", handledMessages);
                    await LogStage5OperationalSignalsAsync(stoppingToken);
                    await DelayBetweenBatchesAsync(stoppingToken);
                    continue;
                }

                var seededMessages = await SeedSessionsFromProcessedBacklogAsync(stoppingToken);
                if (processedSessions > 0 || seededMessages > 0)
                {
                    await LogStage5OperationalSignalsAsync(stoppingToken);
                    await DelayBetweenBatchesAsync(stoppingToken);
                    continue;
                }

                await LogStage5OperationalSignalsAsync(stoppingToken);
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
        var sequentiallyBlockedChatIds = new HashSet<long>();
        foreach (var session in sessions)
        {
            if (sequentiallyBlockedChatIds.Contains(session.ChatId))
            {
                continue;
            }

            if (!await CanProcessSessionSequentiallyAsync(session, ct))
            {
                _logger.LogInformation(
                    "Stage5 session-first skipped blocked chat to preserve strict order locally: chat_id={ChatId}, session_index={SessionIndex}",
                    session.ChatId,
                    session.SessionIndex);
                sequentiallyBlockedChatIds.Add(session.ChatId);
                continue;
            }

            if (!await TryAcquireStage5PhaseAsync(session.ChatId, "session_first_claim", ct))
            {
                continue;
            }

            var leaseHeartbeat = StartPhaseLeaseHeartbeat(
                session.ChatId,
                ChatRuntimePhases.Stage5Process,
                "session_first_lease_heartbeat",
                ct);
            var releaseMismatch = false;
            using var leaseScope = CreateLeaseLinkedTokenSource(leaseHeartbeat, ct);
            var leaseCt = leaseScope.Token;
            try
            {
                var maxSessionMessages = Math.Max(500, Math.Max(_settings.SummarySessionMaxMessages * 10, _settings.SessionChunkSize * 10));
                var sessionMessages = await _messageRepository.GetByChatAndPeriodAsync(
                    session.ChatId,
                    session.StartDate,
                    session.EndDate,
                    maxSessionMessages,
                    leaseCt);
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
                    maxAnalyzedSessionEndMs = await MarkSessionAnalyzedAndAdvanceWatermarkAsync(session, maxAnalyzedSessionEndMs, leaseCt);
                    analyzedSessionsCount++;
                    continue;
                }

                var extractionByMessageId = await _extractionRepository.GetCheapJsonByMessageIdsAsync(
                    sessionMessages.Select(x => x.Id).ToArray(),
                    leaseCt);
                var quarantinedMessageIds = await _extractionRepository.GetQuarantinedMessageIdsAsync(
                    sessionMessages.Select(x => x.Id).ToArray(),
                    leaseCt);
                var actionableMissingArtifacts = CountActionableMissingSessionArtifacts(
                    sessionMessages,
                    extractionByMessageId,
                    quarantinedMessageIds);
                if (!string.IsNullOrWhiteSpace(session.Summary) && actionableMissingArtifacts == 0)
                {
                    _logger.LogInformation(
                        "Stage5 session-first hard stop: skipping stale pending session already covered by artifacts. chat_id={ChatId}, session_index={SessionIndex}, messages={MessageCount}",
                        session.ChatId,
                        session.SessionIndex,
                        sessionMessages.Count);
                    maxAnalyzedSessionEndMs = await MarkSessionAnalyzedAndAdvanceWatermarkAsync(session, maxAnalyzedSessionEndMs, leaseCt);
                    analyzedSessionsCount++;
                    continue;
                }

                var chunkResult = await ProcessSessionInChunksAsync(session, sessionMessages, leaseCt);
                if (chunkResult.BalanceIssueDetected)
                {
                    await WaitForOpenRouterRecoveryAsync(leaseCt);
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

                await EnsureSessionSummaryAfterSliceAsync(session, sessionMessages, leaseCt);
                maxAnalyzedSessionEndMs = await MarkSessionAnalyzedAndAdvanceWatermarkAsync(session, maxAnalyzedSessionEndMs, leaseCt);
                analyzedSessionsCount++;
            }
            catch (OperationCanceledException) when (leaseHeartbeat.LeaseLost && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Stage5 session-first workload stopped after lease loss: chat_id={ChatId}, session_index={SessionIndex}, phase={Phase}, owner_id={OwnerId}",
                    session.ChatId,
                    session.SessionIndex,
                    ChatRuntimePhases.Stage5Process,
                    _phaseOwnerId);
                break;
            }
            finally
            {
                await StopPhaseLeaseHeartbeatAsync(leaseHeartbeat);
                releaseMismatch = !await ReleaseStage5PhaseAsync(session.ChatId, "session_first_done", ct);
            }

            if (releaseMismatch)
            {
                _logger.LogWarning(
                    "Stage5 session-first pass interrupted due to release mismatch escalation: chat_id={ChatId}, session_index={SessionIndex}",
                    session.ChatId,
                    session.SessionIndex);
                break;
            }
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
        var previousSessionSummary = chatSessions
            .FirstOrDefault(x => x.SessionIndex == session.SessionIndex - 1)?
            .Summary;
        var previousChunkSummary = startIndex > 0 && startIndex - 1 < chunks.Count
            ? BuildChunkSummary(chunks[startIndex - 1], [])
            : null;
        var sessionChunkParallelism = GetSessionChunkParallelism();
        if (sessionChunkParallelism > 1)
        {
            _logger.LogInformation(
                "Stage5 chunk continuity guard enabled: forcing sequential chunk mode for strict CHUNK_SUMMARY_PREV semantics. chat_id={ChatId}, session_index={SessionIndex}, requested_parallelism={RequestedParallelism}",
                session.ChatId,
                session.SessionIndex,
                sessionChunkParallelism);
            sessionChunkParallelism = 1;
        }

        if (sessionChunkParallelism <= 1 || (chunks.Count - startIndex) <= 1)
        {
            for (var i = startIndex; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var replySliceContext = await BuildReplySliceContextAsync(session, chunk, chatSessions, ct);
                var effectiveChunkSummaryPrev = BuildPreviousSlicePrompt(
                    session,
                    i,
                    previousChunkSummary,
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

                previousChunkSummary = BuildChunkSummary(chunk, []);
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
                        i > 0 ? BuildChunkSummary(chunks[i - 1], []) : previousSessionSummary,
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
            await _stateRepository.ResetWatermarksIfExistAsync([checkpointKey], ct);
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
        if (chunkIndex <= 0)
        {
            return BuildBootstrapChunkContext(
                "chunk_start",
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

            var cheapPrompt = await GetPromptAsync(_cheapExtractionPromptContract, ct);
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

        var successfulMessageIds = new List<long>(messages.Count);
        var failedValidationMessageIds = new List<long>();
        var failedItemMessageIds = new List<long>();

        foreach (var message in analyzableMessages)
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

            if (itemResult.Succeeded)
            {
                successfulMessageIds.Add(message.Id);
                continue;
            }

            failedChunkMessageIds.TryAdd(message.Id, 0);
            if (itemResult.ValidationRejected)
            {
                failedValidationMessageIds.Add(message.Id);
            }
            else
            {
                failedItemMessageIds.Add(message.Id);
            }
        }

        var analyzableMessageIds = analyzableMessages
            .Select(x => x.Id)
            .ToHashSet();
        var skippedMessageIds = messages
            .Where(x => !analyzableMessageIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToArray();
        await MarkProcessedMessagesAsync(skippedMessageIds, ct);

        if (failedValidationMessageIds.Count > 0)
        {
            var newlyQuarantined = await IncrementValidationRejectCountersAndQuarantineAsync(failedValidationMessageIds, ct);
            foreach (var quarantinedId in newlyQuarantined)
            {
                failedChunkMessageIds.TryRemove(quarantinedId, out _);
            }

            var retryValidationIds = failedValidationMessageIds
                .Where(x => !newlyQuarantined.Contains(x))
                .ToList();
            if (retryValidationIds.Count > 0)
            {
                await _messageRepository.MarkNeedsReanalysisAsync(
                    retryValidationIds,
                    "stage5_validation_rejected",
                    ct);
            }

            _logger.LogWarning(
                "Stage5 handled validation-rejected messages: total={TotalCount}, queued_for_retry={RetryCount}, quarantined={QuarantinedCount}, quarantine_threshold={Threshold}, quarantine_reason={Reason}",
                failedValidationMessageIds.Count,
                retryValidationIds.Count,
                newlyQuarantined.Count,
                ValidationRejectQuarantineThreshold,
                ValidationRejectQuarantineReason);
        }

        if (failedItemMessageIds.Count > 0)
        {
            await _messageRepository.MarkNeedsReanalysisAsync(
                failedItemMessageIds,
                "stage5_cheap_item_failed",
                ct);
            _logger.LogWarning(
                "Stage5 queued failed cheap items for retry: count={Count}",
                failedItemMessageIds.Count);
        }

        await ResetValidationRejectCountersAsync(successfulMessageIds, ct);
        await MarkProcessedMessagesAsync(successfulMessageIds, ct);

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

    private async Task MarkProcessedMessagesAsync(
        IEnumerable<long> messageIds,
        CancellationToken ct)
    {
        var processedMessageIds = messageIds
            .Distinct()
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
            if (!byId.TryGetValue(message.Id, out var extracted))
            {
                _logger.LogWarning(
                    "Stage5 extraction validation rejected message_id={MessageId}: missing_extraction_item",
                    message.Id);

                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_validation",
                    reason: "missing_extraction_item",
                    messageId: message.Id,
                    payload: $"model={modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel)}",
                    ct: ct);

                return new CheapItemResult(Succeeded: false, ValidationRejected: true, NeedsExpensiveMarked: false);
            }

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

                return new CheapItemResult(Succeeded: false, ValidationRejected: true, NeedsExpensiveMarked: false);
            }

            var needsExpensive = IsExpensivePassEnabled() &&
                                 ExtractionRefiner.ShouldRunExpensivePass(message, extracted, _settings);
            await _extractionRepository.UpsertCheapAsync(message.Id, JsonSerializer.Serialize(extracted, ExtractionSerializationOptions.SnakeCase), needsExpensive, ct);

            if (!needsExpensive)
            {
                await _extractionApplier.ApplyExtractionAsync(message.Id, extracted, message, ct);
            }

            return new CheapItemResult(Succeeded: true, ValidationRejected: false, NeedsExpensiveMarked: needsExpensive);
        }
        catch (Exception ex)
        {
            var model = modelByMessageId.GetValueOrDefault(message.Id, _settings.CheapModel);
            var dbSaveErrorPayload = BuildDbSaveErrorPayload(ex, "stage5_cheap_apply_extraction");
            var reason = dbSaveErrorPayload == null ? ex.Message : "db_save_error";
            _logger.LogWarning(ex, "Stage5 cheap item failed for message_id={MessageId}", message.Id);
            await _extractionErrorRepository.LogAsync(
                stage: "stage5_cheap_item",
                reason: reason,
                messageId: message.Id,
                payload: dbSaveErrorPayload == null
                    ? $"model={model};exception={ex.GetType().Name}"
                    : $"{dbSaveErrorPayload};model={model}",
                ct: ct);
            return new CheapItemResult(Succeeded: false, ValidationRejected: false, NeedsExpensiveMarked: false);
        }
    }

    private static string? BuildDbSaveErrorPayload(Exception ex, string defaultOperationClass)
    {
        var dbUpdateException = ex as DbUpdateException;
        var postgres = ex as PostgresException
                       ?? dbUpdateException?.InnerException as PostgresException
                       ?? ex.InnerException as PostgresException;
        if (dbUpdateException == null && postgres == null)
        {
            return null;
        }

        var operationClass = ResolveDbOperationClass(ex, defaultOperationClass);
        var innerType = dbUpdateException?.InnerException?.GetType().Name
                        ?? ex.InnerException?.GetType().Name
                        ?? "none";
        var innerMessage = SanitizeErrorValue(dbUpdateException?.InnerException?.Message ?? ex.InnerException?.Message);
        var sqlState = postgres?.SqlState ?? "n/a";
        var dbErrorCode = postgres?.SqlState ?? "n/a";
        var constraint = SanitizeErrorValue(postgres?.ConstraintName) ?? "n/a";
        var table = SanitizeErrorValue(postgres?.TableName) ?? "n/a";
        var schema = SanitizeErrorValue(postgres?.SchemaName) ?? "n/a";
        var dbMessage = SanitizeErrorValue(postgres?.MessageText) ?? "n/a";

        return $"operation_class={operationClass};exception={ex.GetType().Name};inner_exception={innerType};inner_message={innerMessage ?? "n/a"};sql_state={sqlState};db_error_code={dbErrorCode};constraint={constraint};table={table};schema={schema};db_message={dbMessage}";
    }

    private static string ResolveDbOperationClass(Exception ex, string defaultOperationClass)
    {
        var stack = ex.ToString();
        if (stack.Contains("FactRepository.SupersedeFactAsync", StringComparison.Ordinal))
        {
            return "fact_supersede";
        }

        if (stack.Contains("FactRepository.UpsertAsync", StringComparison.Ordinal))
        {
            return "fact_upsert";
        }

        if (stack.Contains("IntelligenceRepository", StringComparison.Ordinal))
        {
            return "intelligence_persist";
        }

        return defaultOperationClass;
    }

    private static string? SanitizeErrorValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace(';', ',')
            .Trim();
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
        else if (LooksFallbackLikeSummary(summary))
        {
            _logger.LogInformation(
                "Stage5 summary output looked fallback/truncated-like; replaced with deterministic compact summary. chat_id={ChatId}, session_index={SessionIndex}",
                session.ChatId,
                session.SessionIndex);
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
            var summaryPrompt = await GetPromptAsync(Stage5PromptCatalog.SessionSummary, ct);
            var response = await _analysisService.SummarizeDialogAsync(
                string.IsNullOrWhiteSpace(_settings.SummaryModel) ? _settings.ExpensiveModel : _settings.SummaryModel,
                summaryPrompt.SystemPrompt,
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

        if (TryExtractSummaryFromJson(raw, out var summary))
        {
            return summary;
        }

        var extractedJson = TryExtractJsonObject(raw);
        if (!string.IsNullOrWhiteSpace(extractedJson) && TryExtractSummaryFromJson(extractedJson, out summary))
        {
            return summary;
        }

        var collapsed = MessageContentBuilder.CollapseWhitespace(raw);
        return StripCommonSummaryWrappers(collapsed);
    }

    private static bool TryExtractSummaryFromJson(string raw, out string summary)
    {
        summary = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("summary", out var summaryElement) || summaryElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var extracted = MessageContentBuilder.CollapseWhitespace(summaryElement.GetString() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(extracted))
            {
                return false;
            }

            summary = StripCommonSummaryWrappers(extracted);
            return !string.IsNullOrWhiteSpace(summary);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return raw[start..(end + 1)];
    }

    private static string StripCommonSummaryWrappers(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = normalized.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("```", string.Empty, StringComparison.Ordinal);

        if (normalized.StartsWith("summary:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["summary:".Length..].Trim();
        }

        if (normalized.StartsWith("сводка:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["сводка:".Length..].Trim();
        }

        return MessageContentBuilder.CollapseWhitespace(normalized);
    }

    private static bool LooksFallbackLikeSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return true;
        }

        var normalized = MessageContentBuilder.CollapseWhitespace(summary).ToLowerInvariant();
        if (normalized.Length < 42)
        {
            return true;
        }

        if (normalized.Contains("сводка сессии недоступна", StringComparison.Ordinal)
            || normalized.Contains("не удалось сформировать", StringComparison.Ordinal)
            || normalized.Contains("недостаточно данных", StringComparison.Ordinal))
        {
            return true;
        }

        return normalized.Contains("...", StringComparison.Ordinal) && normalized.Length < 90;
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
        foreach (var managedPrompt in Stage5PromptCatalog.ManagedPrompts)
        {
            await EnsurePromptAsync(managedPrompt, ct);
        }
    }

    private async Task EnsurePromptAsync(ManagedPromptTemplate contract, CancellationToken ct)
    {
        var existing = await _promptRepository.GetByIdAsync(contract.Id, ct);
        var managed = contract.ToTemplate();
        if (existing != null &&
            string.Equals(existing.Version, managed.Version, StringComparison.Ordinal) &&
            string.Equals(existing.Checksum, managed.Checksum, StringComparison.Ordinal) &&
            string.Equals(existing.SystemPrompt, managed.SystemPrompt, StringComparison.Ordinal))
        {
            return;
        }

        await _promptRepository.UpsertAsync(managed, ct);
    }

    private async Task<PromptTemplate> GetPromptAsync(ManagedPromptTemplate contract, CancellationToken ct)
    {
        var prompt = await _promptRepository.GetByIdAsync(contract.Id, ct);
        return prompt ?? contract.ToTemplate();
    }

    private static Task DelayBetweenBatchesAsync(CancellationToken ct)
    {
        return Task.Delay(BatchThrottleDelay, ct);
    }

    private async Task<int> RecoverSessionSkipQuarantineAsync(CancellationToken ct)
    {
        var staleBeforeUtc = DateTime.UtcNow.AddHours(-SessionSkipQuarantineRecoveryAgeHours);
        var limit = Math.Max(1, GetFetchLimit());
        return await _extractionRepository.ReleaseQuarantineForRetryAsync(
            SessionSkipQuarantineReason,
            staleBeforeUtc,
            limit,
            ct);
    }

    private async Task LogStage5OperationalSignalsAsync(CancellationToken ct)
    {
        var staleBefore = DateTime.UtcNow.AddMinutes(-Math.Max(1, _settings.SessionAnalysisMinIdleMinutes));
        var pendingSessionsQueue = await _chatSessionRepository.CountPendingAnalysisSessionsAsync(staleBefore, ct);
        var reanalysisBacklog = await _messageRepository.CountNeedsReanalysisProcessedAsync(ct);
        var quarantineMetrics = await _extractionRepository.GetQuarantineMetricsAsync(
            DateTime.UtcNow.AddHours(-SessionSkipQuarantineRecoveryAgeHours),
            ct);
        _logger.LogInformation(
            "Stage5 operational signals: pending_sessions_queue={PendingSessionsQueue}, reanalysis_backlog={ReanalysisBacklog}, quarantine_total={QuarantineTotal}, quarantine_stuck={QuarantineStuck}, phase_guard_denied_stage5={Stage5Deny}, phase_guard_denied_slice_build={SliceBuildDeny}, phase_guard_recovery_applied={RecoveryApplied}, phase_lease_renew_denied={LeaseRenewDenied}, phase_release_mismatch_stage5={Stage5ReleaseMismatch}, phase_release_mismatch_slice_build={SliceBuildReleaseMismatch}",
            pendingSessionsQueue,
            reanalysisBacklog,
            quarantineMetrics.Total,
            quarantineMetrics.Stuck,
            Interlocked.Read(ref _stage5PhaseGuardDeniedCount),
            Interlocked.Read(ref _sliceBuildPhaseGuardDeniedCount),
            Interlocked.Read(ref _phaseGuardRecoveryAppliedCount),
            Interlocked.Read(ref _phaseLeaseRenewDeniedCount),
            Interlocked.Read(ref _stage5PhaseReleaseMismatchCount),
            Interlocked.Read(ref _sliceBuildPhaseReleaseMismatchCount));
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

    private async Task ResetValidationRejectCountersAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct)
    {
        var keys = messageIds
            .Where(x => x > 0)
            .Distinct()
            .Select(BuildValidationRejectCounterKey)
            .ToArray();
        await _stateRepository.ResetWatermarksIfExistAsync(keys, ct);
    }

    private async Task<HashSet<long>> IncrementValidationRejectCountersAndQuarantineAsync(
        IReadOnlyCollection<long> messageIds,
        CancellationToken ct)
    {
        var quarantineIds = new HashSet<long>();
        foreach (var messageId in messageIds.Where(x => x > 0).Distinct())
        {
            var key = BuildValidationRejectCounterKey(messageId);
            var next = await _stateRepository.GetWatermarkAsync(key, ct) + 1;
            await _stateRepository.SetWatermarkAsync(key, next, ct);
            if (next > ValidationRejectQuarantineThreshold)
            {
                quarantineIds.Add(messageId);
            }
        }

        if (quarantineIds.Count > 0)
        {
            await _extractionRepository.QuarantineMessagesAsync(
                quarantineIds.ToArray(),
                ValidationRejectQuarantineReason,
                ct);
        }

        return quarantineIds;
    }

    private static string BuildValidationRejectCounterKey(long messageId)
    {
        return $"{ValidationRejectCounterPrefix}:{messageId}";
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
        var hotSessionGap = TimeSpan.FromMinutes(Math.Max(1, _settings.HotSessionGapMinutes));

        foreach (var chatId in chatIds)
        {
            if (!await TryAcquireSliceBuildPhaseAsync(chatId, "session_slice_build_claim", ct))
            {
                continue;
            }

            var leaseHeartbeat = StartPhaseLeaseHeartbeat(
                chatId,
                ChatRuntimePhases.SliceBuild,
                "session_slice_build_lease_heartbeat",
                ct);
            var releaseMismatch = false;
            using var leaseScope = CreateLeaseLinkedTokenSource(leaseHeartbeat, ct);
            var leaseCt = leaseScope.Token;
            try
            {
                var chatMessages = await _messageRepository.GetProcessedByChatAsync(chatId, fetchLimit, leaseCt);
                chatMessages = ApplyArchiveScope(chatMessages, "session_slice_build");
                if (chatMessages.Count == 0)
                {
                    continue;
                }
                var extractedByMessageId = await _extractionRepository.GetCheapJsonByMessageIdsAsync(
                    chatMessages.Select(x => x.Id).ToArray(),
                    leaseCt);
                var quarantinedMessageIds = await _extractionRepository.GetQuarantinedMessageIdsAsync(
                    chatMessages.Select(x => x.Id).ToArray(),
                    leaseCt);

                var allowShortSessionMerge = ShouldApplyShortSessionMerge(chatMessages, hotSessionGap);
                var allSessions = SplitByGap(
                    chatMessages,
                    hotSessionGap,
                    Math.Max(1, _settings.EpisodicShortSessionMergeThreshold),
                    allowShortSessionMerge);
                var sessions = applySessionCap
                    ? allSessions.Take(configuredSessionLimit).ToList()
                    : allSessions;
                var existingSessions = existingByChat.GetValueOrDefault(chatId)?
                    .OrderBy(x => x.SessionIndex)
                    .ToList() ?? [];

                if (!applySessionCap && existingSessions.Count > 0)
                {
                    // In uncapped mode we only reslice a short tail near the latest known session.
                    // Rebuilding a very large historical window can remap indexes and inflate session rows.
                    var latestKnownEnd = existingSessions[^1].EndDate;
                    var tailLookback = TimeSpan.FromMinutes(Math.Max(15, _settings.HotSessionGapMinutes * 6));
                    var tailWindowStart = latestKnownEnd - tailLookback;
                    chatMessages = chatMessages
                        .Where(x => x.Timestamp >= tailWindowStart)
                        .OrderBy(x => x.Timestamp)
                        .ThenBy(x => x.Id)
                        .ToList();
                    if (chatMessages.Count == 0)
                    {
                        continue;
                    }

                    allowShortSessionMerge = ShouldApplyShortSessionMerge(chatMessages, hotSessionGap);
                    allSessions = SplitByGap(
                        chatMessages,
                        hotSessionGap,
                        Math.Max(1, _settings.EpisodicShortSessionMergeThreshold),
                        allowShortSessionMerge);
                    sessions = allSessions;
                }

                var existingByIndex = existingSessions.ToDictionary(x => x.SessionIndex);
                var windowStart = sessions.Count > 0 && sessions[0].Count > 0
                    ? sessions[0][0].Timestamp
                    : DateTime.MinValue;
                var baseSessionIndex = applySessionCap
                    ? 0
                    : ResolveWindowBaseSessionIndex(existingSessions, windowStart);
                var maxComputedSessionIndex = sessions.Count == 0
                    ? baseSessionIndex
                    : baseSessionIndex + sessions.Count - 1;

                if (!applySessionCap && existingSessions.Count > 0 && sessions.Count > 0)
                {
                    var maxExistingSessionIndex = existingSessions[^1].SessionIndex;
                    var maxAllowedGrowthPerPass = Math.Max(50, _coordinationSettings.TailReopenMaxSessionLag + 10);
                    var maxAllowedSessionIndex = maxExistingSessionIndex + maxAllowedGrowthPerPass;
                    if (maxComputedSessionIndex > maxAllowedSessionIndex)
                    {
                        _logger.LogWarning(
                            "Stage5 session slicing growth guard trimmed computed session window: chat_id={ChatId}, computed_max_session_index={ComputedMax}, allowed_max_session_index={AllowedMax}, existing_max_session_index={ExistingMax}, planned_sessions={PlannedSessions}",
                            chatId,
                            maxComputedSessionIndex,
                            maxAllowedSessionIndex,
                            maxExistingSessionIndex,
                            sessions.Count);
                        var allowedCount = Math.Max(1, maxAllowedSessionIndex - baseSessionIndex + 1);
                        sessions = sessions.Take(allowedCount).ToList();
                        maxComputedSessionIndex = baseSessionIndex + sessions.Count - 1;
                    }
                }

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
                    var pendingRealtimeWithoutExtraction = session
                        .Where(x => x.Source == MessageSource.Realtime)
                        .Count(x => !extractedByMessageId.ContainsKey(x.Id));
                    var actionableMissingArtifacts = CountActionableMissingSessionArtifacts(
                        session,
                        extractedByMessageId,
                        quarantinedMessageIds);
                    var shouldReopenForPendingRealtimeArtifacts =
                        existing?.IsAnalyzed == true
                        && existing.IsFinalized == false
                        && actionableMissingArtifacts > 0
                        && IsTailReopenEligible(sessionIndex, maxComputedSessionIndex, sessionEnd);
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
                        if (shouldReopenForPendingRealtimeArtifacts)
                        {
                            await _chatSessionRepository.MarkNeedsAnalysisAsync([existing.Id], leaseCt);
                            _logger.LogInformation(
                                "Stage5 reopened analyzed session due to actionable realtime artifacts in tail window: chat_id={ChatId}, session_index={SessionIndex}, pending_realtime_without_extraction={PendingCount}, actionable_missing_artifacts={ActionableMissing}",
                                chatId,
                                sessionIndex,
                                pendingRealtimeWithoutExtraction,
                                actionableMissingArtifacts);
                        }
                        continue;
                    }

                    if (shouldReopenForPendingRealtimeArtifacts)
                    {
                        await _chatSessionRepository.MarkNeedsAnalysisAsync([existing!.Id], leaseCt);
                        _logger.LogInformation(
                            "Stage5 reopened analyzed session due to actionable realtime artifacts in tail window after session-boundary update: chat_id={ChatId}, session_index={SessionIndex}, pending_realtime_without_extraction={PendingCount}, actionable_missing_artifacts={ActionableMissing}",
                            chatId,
                            sessionIndex,
                            pendingRealtimeWithoutExtraction,
                            actionableMissingArtifacts);
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
                        IsFinalized = isFinalized,
                        IsAnalyzed = shouldReopenForPendingRealtimeArtifacts
                            ? false
                            : (existing?.IsAnalyzed ?? false)
                    }, leaseCt);
                }
            }
            catch (OperationCanceledException) when (leaseHeartbeat.LeaseLost && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Slice-build workload stopped after lease loss: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}",
                    chatId,
                    ChatRuntimePhases.SliceBuild,
                    _phaseOwnerId);
                break;
            }
            finally
            {
                await StopPhaseLeaseHeartbeatAsync(leaseHeartbeat);
                releaseMismatch = !await ReleaseSliceBuildPhaseAsync(chatId, "session_slice_build_done", ct);
            }

            if (releaseMismatch)
            {
                _logger.LogWarning(
                    "Slice-build pass interrupted due to release mismatch escalation: chat_id={ChatId}",
                    chatId);
                break;
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

    private bool IsTailReopenEligible(int sessionIndex, int maxSessionIndex, DateTime sessionEndUtc)
    {
        var lag = Math.Max(0, _coordinationSettings.TailReopenMaxSessionLag);
        var windowHours = Math.Max(0, _coordinationSettings.TailReopenMaxWindowHours);
        var indexGate = sessionIndex >= Math.Max(0, maxSessionIndex - lag);
        if (!indexGate)
        {
            return false;
        }

        if (windowHours <= 0)
        {
            return true;
        }

        return sessionEndUtc >= DateTime.UtcNow.AddHours(-windowHours);
    }

    private static int CountActionableMissingSessionArtifacts(
        IReadOnlyCollection<Message> sessionMessages,
        IReadOnlyDictionary<long, string> extractionByMessageId,
        IReadOnlyCollection<long> quarantinedMessageIds)
    {
        if (sessionMessages.Count == 0)
        {
            return 0;
        }

        var coveredTelegramMessageIds = sessionMessages
            .Where(message => message.TelegramMessageId > 0 && extractionByMessageId.ContainsKey(message.Id))
            .Select(message => message.TelegramMessageId)
            .ToHashSet();
        var quarantinedIds = quarantinedMessageIds.ToHashSet();
        var missing = 0;

        foreach (var message in sessionMessages)
        {
            if (extractionByMessageId.ContainsKey(message.Id))
            {
                continue;
            }

            if (quarantinedIds.Contains(message.Id))
            {
                continue;
            }

            if (MessageContentBuilder.IsServiceOrTechnicalNoise(message))
            {
                continue;
            }

            if (message.TelegramMessageId > 0 && coveredTelegramMessageIds.Contains(message.TelegramMessageId))
            {
                continue;
            }

            missing++;
        }

        return missing;
    }

    private bool ShouldApplyShortSessionMerge(List<Message> messages, TimeSpan hotSessionGap)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        // Merge short sessions only on safe cold/archive path to avoid breaking hot-tail runtime slicing.
        if (messages.All(x => x.Source == MessageSource.Archive))
        {
            return true;
        }

        var lastTimestamp = messages.Max(x => x.Timestamp);
        var coldCutoff = DateTime.UtcNow - hotSessionGap;
        return lastTimestamp <= coldCutoff;
    }

    private List<List<Message>> SplitByGap(List<Message> messages, TimeSpan gap, int shortThreshold, bool allowShortSessionMerge)
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
                var shouldSplit = !allowShortSessionMerge || current.Count >= shortThreshold || delta > maxBridgeGap;
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
        if (ex is TaskCanceledException or TimeoutException or HttpRequestException or InvalidDataException or JsonException)
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
        bool Succeeded,
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

    private async Task<bool> TryAcquireStage5PhaseAsync(long chatId, string reason, CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return true;
        }

        var decision = await _chatCoordinationService.TryAcquirePhaseAsync(
            chatId,
            ChatRuntimePhases.Stage5Process,
            _phaseOwnerId,
            reason,
            ct: ct);
        if (decision.Allowed)
        {
            if (decision.RecoveryApplied)
            {
                Interlocked.Increment(ref _phaseGuardRecoveryAppliedCount);
            }
            return true;
        }

        if (decision.RecoveryApplied)
        {
            Interlocked.Increment(ref _phaseGuardRecoveryAppliedCount);
        }
        Interlocked.Increment(ref _stage5PhaseGuardDeniedCount);

        _logger.LogWarning(
            "Stage5 phase guard blocked processing: chat_id={ChatId}, requested_phase={RequestedPhase}, current_phase={CurrentPhase}, deny_code={DenyCode}, deny_reason={DenyReason}",
            chatId,
            decision.RequestedPhase,
            decision.CurrentPhase,
            decision.DenyCode,
            decision.DenyReason);
        return false;
    }

    private async Task<bool> ReleaseStage5PhaseAsync(long chatId, string reason, CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return true;
        }

        var releaseResult = await _chatCoordinationService.ReleasePhaseAsync(
            chatId,
            ChatRuntimePhases.Stage5Process,
            _phaseOwnerId,
            reason,
            ct.IsCancellationRequested ? CancellationToken.None : ct);
        if (releaseResult.Released)
        {
            return true;
        }

        Interlocked.Increment(ref _stage5PhaseReleaseMismatchCount);

        await _extractionErrorRepository.LogAsync(
            stage: "stage5_phase_release_mismatch",
            reason: releaseResult.OwnershipMismatch
                ? "stage5 release denied by ownership mismatch"
                : "stage5 release denied",
            payload: $"chat_id={chatId};phase={ChatRuntimePhases.Stage5Process};owner_id={_phaseOwnerId};current_phase={releaseResult.CurrentPhase};current_owner={releaseResult.CurrentOwnerId}",
            ct: CancellationToken.None);
        _logger.LogError(
            "Stage5 phase release escalation: chat_id={ChatId}, requested_phase={RequestedPhase}, owner_id={OwnerId}, ownership_mismatch={OwnershipMismatch}, current_phase={CurrentPhase}, current_owner={CurrentOwner}, current_lease_expires_at_utc={LeaseExpiresAtUtc}",
            chatId,
            ChatRuntimePhases.Stage5Process,
            _phaseOwnerId,
            releaseResult.OwnershipMismatch,
            releaseResult.CurrentPhase,
            releaseResult.CurrentOwnerId,
            releaseResult.CurrentLeaseExpiresAtUtc);
        return false;
    }

    private async Task<bool> TryAcquireSliceBuildPhaseAsync(long chatId, string reason, CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return true;
        }

        var decision = await _chatCoordinationService.TryAcquirePhaseAsync(
            chatId,
            ChatRuntimePhases.SliceBuild,
            _phaseOwnerId,
            reason,
            ct: ct);
        if (decision.Allowed)
        {
            if (decision.RecoveryApplied)
            {
                Interlocked.Increment(ref _phaseGuardRecoveryAppliedCount);
            }
            return true;
        }

        if (decision.RecoveryApplied)
        {
            Interlocked.Increment(ref _phaseGuardRecoveryAppliedCount);
        }
        Interlocked.Increment(ref _sliceBuildPhaseGuardDeniedCount);

        _logger.LogWarning(
            "Slice-build phase guard blocked processing: chat_id={ChatId}, requested_phase={RequestedPhase}, current_phase={CurrentPhase}, deny_code={DenyCode}, deny_reason={DenyReason}",
            chatId,
            decision.RequestedPhase,
            decision.CurrentPhase,
            decision.DenyCode,
            decision.DenyReason);
        return false;
    }

    private async Task<bool> ReleaseSliceBuildPhaseAsync(long chatId, string reason, CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return true;
        }

        var releaseResult = await _chatCoordinationService.ReleasePhaseAsync(
            chatId,
            ChatRuntimePhases.SliceBuild,
            _phaseOwnerId,
            reason,
            ct.IsCancellationRequested ? CancellationToken.None : ct);
        if (releaseResult.Released)
        {
            return true;
        }

        Interlocked.Increment(ref _sliceBuildPhaseReleaseMismatchCount);

        await _extractionErrorRepository.LogAsync(
            stage: "slice_build_phase_release_mismatch",
            reason: releaseResult.OwnershipMismatch
                ? "slice_build release denied by ownership mismatch"
                : "slice_build release denied",
            payload: $"chat_id={chatId};phase={ChatRuntimePhases.SliceBuild};owner_id={_phaseOwnerId};current_phase={releaseResult.CurrentPhase};current_owner={releaseResult.CurrentOwnerId}",
            ct: CancellationToken.None);
        _logger.LogError(
            "Slice-build phase release escalation: chat_id={ChatId}, requested_phase={RequestedPhase}, owner_id={OwnerId}, ownership_mismatch={OwnershipMismatch}, current_phase={CurrentPhase}, current_owner={CurrentOwner}, current_lease_expires_at_utc={LeaseExpiresAtUtc}",
            chatId,
            ChatRuntimePhases.SliceBuild,
            _phaseOwnerId,
            releaseResult.OwnershipMismatch,
            releaseResult.CurrentPhase,
            releaseResult.CurrentOwnerId,
            releaseResult.CurrentLeaseExpiresAtUtc);
        return false;
    }

    private PhaseLeaseHeartbeatHandle StartPhaseLeaseHeartbeat(
        long chatId,
        string phase,
        string reason,
        CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return PhaseLeaseHeartbeatHandle.None;
        }

        var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaseLostCts = new CancellationTokenSource();
        var interval = ResolvePhaseLeaseRenewInterval();
        var task = Task.Run(
            () => RunPhaseLeaseHeartbeatLoopAsync(chatId, phase, reason, interval, heartbeatCts.Token, leaseLostCts),
            CancellationToken.None);
        return new PhaseLeaseHeartbeatHandle(heartbeatCts, leaseLostCts, task);
    }

    private async Task StopPhaseLeaseHeartbeatAsync(PhaseLeaseHeartbeatHandle heartbeat)
    {
        if (!heartbeat.IsActive)
        {
            return;
        }

        heartbeat.HeartbeatTokenSource!.Cancel();
        try
        {
            await heartbeat.RunTask!;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            heartbeat.HeartbeatTokenSource.Dispose();
            heartbeat.LeaseLostTokenSource?.Dispose();
        }
    }

    private static CancellationTokenSource CreateLeaseLinkedTokenSource(PhaseLeaseHeartbeatHandle heartbeat, CancellationToken ct)
    {
        return heartbeat.LeaseLostTokenSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, heartbeat.LeaseLostTokenSource.Token);
    }

    private async Task RunPhaseLeaseHeartbeatLoopAsync(
        long chatId,
        string phase,
        string reason,
        TimeSpan interval,
        CancellationToken ct,
        CancellationTokenSource leaseLostCts)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var renewDecision = await _chatCoordinationService.TryRenewPhaseLeaseAsync(
                    chatId,
                    phase,
                    _phaseOwnerId,
                    reason,
                    ct);
                if (renewDecision.Renewed)
                {
                    _logger.LogDebug(
                        "Phase lease renewed: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}, lease_expires_at_utc={LeaseExpiresAtUtc}",
                        chatId,
                        phase,
                        _phaseOwnerId,
                        renewDecision.CurrentLeaseExpiresAtUtc);
                    continue;
                }

                _logger.LogWarning(
                    "Phase lease renewal stopped: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}, deny_code={DenyCode}, deny_reason={DenyReason}",
                    chatId,
                    phase,
                    _phaseOwnerId,
                    renewDecision.DenyCode,
                    renewDecision.DenyReason);
                Interlocked.Increment(ref _phaseLeaseRenewDeniedCount);
                if (!leaseLostCts.IsCancellationRequested)
                {
                    leaseLostCts.Cancel();
                }
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Phase lease heartbeat iteration failed: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}",
                    chatId,
                    phase,
                    _phaseOwnerId);
            }
        }
    }

    private TimeSpan ResolvePhaseLeaseRenewInterval()
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _coordinationSettings.PhaseGuardLeaseTtlMinutes));
        var seconds = Math.Max(5, ttl.TotalSeconds / 3d);
        return TimeSpan.FromSeconds(seconds);
    }

    private sealed class PhaseLeaseHeartbeatHandle
    {
        public static PhaseLeaseHeartbeatHandle None { get; } = new();

        public PhaseLeaseHeartbeatHandle()
        {
        }

        public PhaseLeaseHeartbeatHandle(
            CancellationTokenSource heartbeatTokenSource,
            CancellationTokenSource leaseLostTokenSource,
            Task runTask)
        {
            HeartbeatTokenSource = heartbeatTokenSource;
            LeaseLostTokenSource = leaseLostTokenSource;
            RunTask = runTask;
        }

        public CancellationTokenSource? HeartbeatTokenSource { get; }
        public CancellationTokenSource? LeaseLostTokenSource { get; }
        public Task? RunTask { get; }
        public bool IsActive => HeartbeatTokenSource is not null && RunTask is not null;
        public bool LeaseLost => LeaseLostTokenSource?.IsCancellationRequested == true;
    }
}
