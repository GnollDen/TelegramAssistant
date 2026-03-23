using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class DialogSummaryWorkerService : BackgroundService
{
    private const string SessionSummaryCheckpointPrefix = "stage5:summary:session";
    private static readonly TimeSpan HotSessionIdleTimeout = TimeSpan.FromMinutes(15);
    private static readonly Regex CyrillicRegex = new(@"[\p{IsCyrillic}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AnalysisSettings _settings;
    private readonly AggregationSettings _aggregationSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageExtractionRepository _messageExtractionRepository;
    private readonly IChatDialogSummaryRepository _summaryRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<DialogSummaryWorkerService> _logger;

    public DialogSummaryWorkerService(
        IOptions<AnalysisSettings> settings,
        IOptions<AggregationSettings> aggregationSettings,
        IMessageRepository messageRepository,
        IMessageExtractionRepository messageExtractionRepository,
        IChatDialogSummaryRepository summaryRepository,
        IChatSessionRepository chatSessionRepository,
        IAnalysisStateRepository stateRepository,
        IExtractionErrorRepository extractionErrorRepository,
        OpenRouterAnalysisService analysisService,
        SummaryHistoricalRetrievalService historicalRetrievalService,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<DialogSummaryWorkerService> logger)
    {
        _settings = settings.Value;
        _aggregationSettings = aggregationSettings.Value;
        _messageRepository = messageRepository;
        _messageExtractionRepository = messageExtractionRepository;
        _summaryRepository = summaryRepository;
        _chatSessionRepository = chatSessionRepository;
        _stateRepository = stateRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _analysisService = analysisService;
        _historicalRetrievalService = historicalRetrievalService;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.SummaryEnabled || !_settings.SummaryWorkerEnabled)
        {
            _logger.LogInformation(
                "Stage5 summary worker is disabled. analysis_enabled={AnalysisEnabled}, summary_enabled={SummaryEnabled}, summary_worker_enabled={SummaryWorkerEnabled}. Inline session summary still runs in AnalysisWorker when Stage5 analysis is enabled.",
                _settings.Enabled,
                _settings.SummaryEnabled,
                _settings.SummaryWorkerEnabled);
            return;
        }

        _logger.LogInformation("Stage5 summary worker started. model={Model}", _settings.SummaryModel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var budgetDecision = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
                {
                    PathKey = "stage5_summary_worker",
                    Modality = BudgetModalities.TextAnalysis,
                    IsImportScope = false,
                    IsOptionalPath = true
                }, stoppingToken);
                if (budgetDecision.ShouldPausePath || budgetDecision.ShouldDegradeOptionalPath)
                {
                    _logger.LogWarning(
                        "Stage5 summary worker paused by budget guardrail. state={State}, reason={Reason}",
                        budgetDecision.State,
                        budgetDecision.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SummaryPollIntervalSeconds)), stoppingToken);
                    continue;
                }

                var staleBeforeUtc = DateTime.UtcNow - HotSessionIdleTimeout;
                var candidatesByChat = await _chatSessionRepository.GetPendingAggregationCandidatesAsync(staleBeforeUtc, stoppingToken);
                var candidates = candidatesByChat
                    .Values
                    .SelectMany(x => x)
                    .OrderBy(x => string.IsNullOrWhiteSpace(x.Summary) ? 0 : 1)
                    .ThenBy(x => x.LastMessageAt)
                    .ThenBy(x => x.ChatId)
                    .ThenBy(x => x.SessionIndex)
                    .Take(Math.Max(1, _settings.SummaryBatchSize))
                    .ToList();
                if (candidates.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SummaryPollIntervalSeconds)), stoppingToken);
                    continue;
                }

                var processed = 0;
                var skipped = 0;
                var deferredByHotSession = 0;
                foreach (var session in candidates)
                {
                    var checkpointKey = BuildSessionSummaryCheckpointKey(session.ChatId, session.SessionIndex);
                    var sessionEndMs = new DateTimeOffset(session.EndDate).ToUnixTimeMilliseconds();
                    var checkpoint = await _stateRepository.GetWatermarkAsync(checkpointKey, stoppingToken);
                    if (checkpoint >= sessionEndMs)
                    {
                        skipped++;
                        continue;
                    }

                    var maxSessionMessages = Math.Max(500, Math.Max(_settings.SummarySessionMaxMessages * 10, _settings.SessionChunkSize * 10));
                    var sessionMessages = await _messageRepository.GetByChatAndPeriodAsync(
                        session.ChatId,
                        session.StartDate,
                        session.EndDate,
                        maxSessionMessages,
                        stoppingToken);
                    sessionMessages = sessionMessages
                        .Where(x => x.Timestamp >= session.StartDate && x.Timestamp <= session.EndDate)
                        .OrderBy(x => x.Timestamp)
                        .ThenBy(x => x.Id)
                        .ToList();
                    if (sessionMessages.Count == 0)
                    {
                        await _stateRepository.SetWatermarkAsync(checkpointKey, sessionEndMs, stoppingToken);
                        skipped++;
                        continue;
                    }

                    if (!IsArchiveOnlySession(sessionMessages) && !IsSessionCooledDown(session.LastMessageAt, out var remaining))
                    {
                        deferredByHotSession++;
                        _logger.LogInformation(
                            "Stage5 summary skipped hot slice: chat_id={ChatId}, session_index={SessionIndex}, last_message_utc={LastMessageUtc}, idle_required_min={IdleMinutes}, idle_remaining={Remaining}",
                            session.ChatId,
                            session.SessionIndex,
                            session.LastMessageAt,
                            HotSessionIdleTimeout.TotalMinutes,
                            remaining);
                        continue;
                    }

                    if (MessageContentBuilder.IsTrashOnlySession(sessionMessages))
                    {
                        _logger.LogInformation(
                            "Stage5 summary skipped trash-only slice: chat_id={ChatId}, session_index={SessionIndex}, messages={MessageCount}",
                            session.ChatId,
                            session.SessionIndex,
                            sessionMessages.Count);
                        await _stateRepository.SetWatermarkAsync(checkpointKey, sessionEndMs, stoppingToken);
                        skipped++;
                        continue;
                    }

                    var sessionInput = FilterSummarizableMessages(sessionMessages)
                        .Take(Math.Max(1, _settings.SummarySessionMaxMessages))
                        .ToList();
                    if (sessionInput.Count < _settings.SummaryMinMessages)
                    {
                        sessionInput = sessionMessages
                            .Take(Math.Max(1, _settings.SummarySessionMaxMessages))
                            .ToList();
                    }

                    string summaryText;
                    if (sessionInput.Count < _settings.SummaryMinMessages)
                    {
                        summaryText = BuildFallbackSummary(sessionInput);
                    }
                    else
                    {
                        var historicalHints = await _historicalRetrievalService.GetHintsAsync(session.ChatId, session.SessionIndex, sessionInput, stoppingToken);
                        summaryText = await GenerateSummaryAsync(
                            session.ChatId,
                            "episodic_slice",
                            session.StartDate,
                            session.EndDate,
                            sessionInput,
                            historicalHints,
                            stoppingToken);
                    }

                    if (string.IsNullOrWhiteSpace(summaryText))
                    {
                        summaryText = BuildFallbackSummary(sessionInput);
                    }

                    if (!string.Equals(
                            MessageContentBuilder.CollapseWhitespace(session.Summary),
                            MessageContentBuilder.CollapseWhitespace(summaryText),
                            StringComparison.Ordinal))
                    {
                        await _chatSessionRepository.UpsertAsync(new ChatSession
                        {
                            Id = session.Id,
                            ChatId = session.ChatId,
                            SessionIndex = session.SessionIndex,
                            StartDate = session.StartDate,
                            EndDate = session.EndDate,
                            LastMessageAt = session.LastMessageAt,
                            Summary = summaryText,
                            IsFinalized = session.IsFinalized,
                            IsAnalyzed = session.IsAnalyzed
                        }, stoppingToken);
                        await _historicalRetrievalService.UpsertSessionSummaryEmbeddingAsync(
                            session.ChatId,
                            session.SessionIndex,
                            summaryText,
                            stoppingToken);
                    }

                    await _stateRepository.SetWatermarkAsync(checkpointKey, sessionEndMs, stoppingToken);
                    processed++;
                }

                _logger.LogInformation(
                    "Stage5 summary slice pass done: candidates={CandidateCount}, processed={ProcessedCount}, skipped={SkippedCount}, hot_deferred={HotDeferredCount}",
                    candidates.Count,
                    processed,
                    skipped,
                    deferredByHotSession);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage5 summary loop failed");
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_summary_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SummaryPollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private static string BuildSessionSummaryCheckpointKey(long chatId, int sessionIndex)
    {
        return $"{SessionSummaryCheckpointPrefix}:{chatId}:{sessionIndex}";
    }

    private async Task BuildDaySummariesAsync(List<Message> touchedMessages, CancellationToken ct)
    {
        var byChatAndDay = touchedMessages
            .GroupBy(x => new { x.ChatId, Day = DateOnly.FromDateTime(x.Timestamp) })
            .ToList();

        foreach (var scope in byChatAndDay)
        {
            var dayStart = scope.Key.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd = scope.Key.Day.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var dayMessages = await _messageRepository.GetByChatAndPeriodAsync(
                scope.Key.ChatId,
                dayStart,
                dayEnd,
                Math.Max(_settings.SummaryMinMessages, _settings.SummaryDayMaxMessages),
                ct);
            var summarizableDayMessages = FilterSummarizableMessages(dayMessages);
            if (summarizableDayMessages.Count < _settings.SummaryMinMessages)
            {
                continue;
            }

            var summaryText = await GenerateSummaryAsync(
                scope.Key.ChatId,
                "day",
                dayStart,
                dayEnd,
                summarizableDayMessages,
                historicalHints: null,
                ct);
            if (string.IsNullOrWhiteSpace(summaryText))
            {
                continue;
            }

            await UpsertDialogSummaryIfChangedAsync(new ChatDialogSummary
            {
                ChatId = scope.Key.ChatId,
                SummaryType = ChatDialogSummaryType.Day,
                PeriodStart = dayStart,
                PeriodEnd = dayEnd,
                StartMessageId = summarizableDayMessages.First().Id,
                EndMessageId = summarizableDayMessages.Last().Id,
                MessageCount = summarizableDayMessages.Count,
                Summary = summaryText
            }, ct);
        }
    }

    private async Task<bool> BuildEpisodicChatSessionsAsync(List<Message> touchedMessages, CancellationToken ct)
    {
        var deferredByHotSession = false;
        var (sessionLimit, sessionLimitMode) = GetSessionBuildLimitInfo();
        var fetchLimit = Math.Max(500, _settings.SummaryDayMaxMessages * sessionLimit);
        var touchedChatIds = touchedMessages.Select(x => x.ChatId).Distinct().ToArray();
        var touchedIdsByChat = touchedMessages
            .GroupBy(x => x.ChatId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToHashSet());
        var existingSessionsByChat = await _chatSessionRepository.GetByChatsAsync(touchedChatIds, ct);

        _logger.LogInformation(
            "Stage5 summary session build limit applied: session_limit={SessionLimit}, mode={Mode}",
            sessionLimit,
            sessionLimitMode);

        foreach (var chatId in touchedChatIds)
        {
            var chatMessages = await _messageRepository.GetProcessedByChatAsync(chatId, fetchLimit, ct);
            if (chatMessages.Count == 0)
            {
                continue;
            }

            var hotSessionGap = TimeSpan.FromMinutes(Math.Max(1, _settings.HotSessionGapMinutes));
            var allowShortSessionMerge = ShouldApplyShortSessionMerge(chatMessages, hotSessionGap);
            var sessions = SplitByGap(chatMessages, hotSessionGap, allowShortSessionMerge)
                .Take(sessionLimit)
                .ToList();
                var existingByIndex = existingSessionsByChat.GetValueOrDefault(chatId)?
                .ToDictionary(x => x.SessionIndex) ?? new Dictionary<int, ChatSession>();
            var touchedIds = touchedIdsByChat.GetValueOrDefault(chatId) ?? [];

            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.Count == 0)
                {
                    continue;
                }

                var existing = existingByIndex.GetValueOrDefault(i);
                if (existing?.IsFinalized == true)
                {
                    continue;
                }
                var sessionStart = session.First().Timestamp;
                var sessionEnd = session.Last().Timestamp;
                var boundsUnchanged = existing != null && existing.StartDate == sessionStart && existing.EndDate == sessionEnd;
                var hasExistingSummary = !string.IsNullOrWhiteSpace(existing?.Summary);
                var hasTouchedMessage = session.Any(x => touchedIds.Contains(x.Id));
                if (!hasTouchedMessage && boundsUnchanged && hasExistingSummary)
                {
                    continue;
                }

                if (!IsArchiveOnlySession(session) && !IsSessionCooledDown(sessionEnd, out var remaining))
                {
                    deferredByHotSession = true;
                    _logger.LogInformation(
                        "Stage5 summary skipped hot session: chat_id={ChatId}, session_index={SessionIndex}, last_message_utc={LastMessageUtc}, idle_required_min={IdleMinutes}, idle_remaining={Remaining}",
                        chatId,
                        i,
                        sessionEnd,
                        HotSessionIdleTimeout.TotalMinutes,
                        remaining);
                    continue;
                }

                if (MessageContentBuilder.IsTrashOnlySession(session))
                {
                    _logger.LogInformation(
                        "Stage5 summary skipped trash-only session: chat_id={ChatId}, session_index={SessionIndex}, messages={MessageCount}",
                        chatId,
                        i,
                        session.Count);
                    continue;
                }

                var sessionInput = FilterSummarizableMessages(session)
                    .Take(Math.Max(1, _settings.SummarySessionMaxMessages))
                    .ToList();
                if (sessionInput.Count < _settings.SummaryMinMessages)
                {
                    if (hasExistingSummary && boundsUnchanged)
                    {
                        continue;
                    }

                    sessionInput = session.Take(Math.Max(1, _settings.SummarySessionMaxMessages)).ToList();
                }

                if (sessionInput.Count == 0)
                {
                    continue;
                }

                string summaryText;
                if (sessionInput.Count < _settings.SummaryMinMessages)
                {
                    summaryText = BuildFallbackSummary(sessionInput);
                }
                else
                {
                    var historicalHints = await _historicalRetrievalService.GetHintsAsync(chatId, i, sessionInput, ct);
                    summaryText = await GenerateSummaryAsync(
                        chatId,
                        "episodic_session",
                        sessionStart,
                        sessionEnd,
                        sessionInput,
                        historicalHints,
                        ct);
                }

                if (string.IsNullOrWhiteSpace(summaryText))
                {
                    summaryText = BuildFallbackSummary(sessionInput);
                }

                if (existing != null &&
                    string.Equals(
                        MessageContentBuilder.CollapseWhitespace(existing.Summary),
                        MessageContentBuilder.CollapseWhitespace(summaryText),
                        StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "Stage5 summary idempotent skip (session unchanged): chat_id={ChatId}, session_index={SessionIndex}",
                        chatId,
                        i);
                    continue;
                }

                await _chatSessionRepository.UpsertAsync(new ChatSession
                {
                    ChatId = chatId,
                    SessionIndex = i,
                    StartDate = sessionStart,
                    EndDate = sessionEnd,
                    LastMessageAt = sessionEnd,
                    Summary = summaryText,
                    IsFinalized = ShouldAutoFinalizeArchiveSession(sessionEnd),
                    IsAnalyzed = true
                }, ct);

                await _historicalRetrievalService.UpsertSessionSummaryEmbeddingAsync(chatId, i, summaryText, ct);
            }
        }

        return deferredByHotSession;
    }

    private (int SessionLimit, string Mode) GetSessionBuildLimitInfo()
    {
        var explicitLimit = Math.Max(0, _settings.EpisodicMaxSessionsPerChat);
        if (explicitLimit > 0)
        {
            return (explicitLimit, "explicit");
        }

        if (_settings.EnableTestModeSessionCap)
        {
            return (Math.Max(1, _settings.TestModeMaxSessionsPerChat), "test_mode");
        }

        // In production with no explicit cap, use a conservative operational ceiling.
        return (100, "default_safe");
    }

    private async Task<bool> BuildSessionSummariesAsync(List<Message> touchedMessages, CancellationToken ct)
    {
        var deferredByHotSession = false;
        var byChatAndDay = touchedMessages
            .GroupBy(x => new { x.ChatId, Day = DateOnly.FromDateTime(x.Timestamp) })
            .ToList();

        foreach (var scope in byChatAndDay)
        {
            var dayStart = scope.Key.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd = scope.Key.Day.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var dayMessages = await _messageRepository.GetByChatAndPeriodAsync(
                scope.Key.ChatId,
                dayStart,
                dayEnd,
                Math.Max(_settings.SummaryMinMessages, _settings.SummaryDayMaxMessages),
                ct);
            var summarizableDayMessages = FilterSummarizableMessages(dayMessages);
            if (summarizableDayMessages.Count < _settings.SummaryMinMessages)
            {
                continue;
            }

            var touchedIds = scope.Select(x => x.Id).ToHashSet();
            foreach (var session in SplitByGap(summarizableDayMessages))
            {
                if (session.Count < _settings.SummaryMinMessages)
                {
                    continue;
                }

                if (!session.Any(x => touchedIds.Contains(x.Id)))
                {
                    continue;
                }

                var sessionEnd = session.Last().Timestamp;
                if (!IsArchiveOnlySession(session) && !IsSessionCooledDown(sessionEnd, out var remaining))
                {
                    deferredByHotSession = true;
                    _logger.LogInformation(
                        "Stage5 summary skipped hot day-session: chat_id={ChatId}, last_message_utc={LastMessageUtc}, idle_required_min={IdleMinutes}, idle_remaining={Remaining}",
                        scope.Key.ChatId,
                        sessionEnd,
                        HotSessionIdleTimeout.TotalMinutes,
                        remaining);
                    continue;
                }

                if (MessageContentBuilder.IsTrashOnlySession(session))
                {
                    _logger.LogInformation(
                        "Stage5 summary skipped trash-only day-session: chat_id={ChatId}, messages={MessageCount}",
                        scope.Key.ChatId,
                        session.Count);
                    continue;
                }

                var sessionInput = session.Take(Math.Max(_settings.SummaryMinMessages, _settings.SummarySessionMaxMessages)).ToList();
                var summaryText = await GenerateSummaryAsync(
                    scope.Key.ChatId,
                    "session",
                    session.First().Timestamp,
                    session.Last().Timestamp,
                    sessionInput,
                    historicalHints: null,
                    ct);
                if (string.IsNullOrWhiteSpace(summaryText))
                {
                    continue;
                }

                await UpsertDialogSummaryIfChangedAsync(new ChatDialogSummary
                {
                    ChatId = scope.Key.ChatId,
                    SummaryType = ChatDialogSummaryType.Session,
                    PeriodStart = session.First().Timestamp,
                    PeriodEnd = session.Last().Timestamp,
                    StartMessageId = session.First().Id,
                    EndMessageId = session.Last().Id,
                    MessageCount = session.Count,
                    Summary = summaryText
                }, ct);
            }
        }

        return deferredByHotSession;
    }

    private async Task<string> GenerateSummaryAsync(
        long chatId,
        string scope,
        DateTime periodStart,
        DateTime periodEnd,
        List<Message> messages,
        IReadOnlyCollection<SummaryHistoricalHint>? historicalHints,
        CancellationToken ct)
    {
        var cheapJsonByMessageId = await _messageExtractionRepository.GetCheapJsonByMessageIdsAsync(
            messages.Select(x => x.Id).ToArray(),
            ct);

        try
        {
            var response = await _analysisService.SummarizeDialogAsync(
                string.IsNullOrWhiteSpace(_settings.SummaryModel) ? _settings.ExpensiveModel : _settings.SummaryModel,
                SummaryPrompt,
                chatId,
                scope,
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
                    "Stage5 summary response has no Cyrillic symbols for likely Russian chat. chat_id={ChatId}, scope={Scope}, period_start={PeriodStart}, period_end={PeriodEnd}",
                    chatId,
                    scope,
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
                "Stage5 summary generation failed; will fallback/skip. chat_id={ChatId}, scope={Scope}, period_start={PeriodStart}, period_end={PeriodEnd}, message_count={MessageCount}",
                chatId,
                scope,
                periodStart,
                periodEnd,
                messages.Count);
            return string.Empty;
        }
    }

    private static bool ShouldApplyShortSessionMerge(List<Message> messages, TimeSpan hotSessionGap)
    {
        if (messages.Count == 0)
        {
            return false;
        }

        if (messages.All(x => x.Source == MessageSource.Archive))
        {
            return true;
        }

        var lastTimestamp = messages.Max(x => x.Timestamp);
        var coldCutoff = DateTime.UtcNow - hotSessionGap;
        return lastTimestamp <= coldCutoff;
    }

    private List<List<Message>> SplitByGap(List<Message> messages, TimeSpan? gapOverride = null, bool allowShortSessionMerge = false)
    {
        var gap = gapOverride ?? TimeSpan.FromMinutes(Math.Max(1, _settings.SummarySessionGapMinutes));
        var shortThreshold = Math.Max(1, _settings.EpisodicShortSessionMergeThreshold);
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
                    // keep collecting across this gap until next eligible boundary.
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

    private static string BuildFallbackSummary(List<Message> messages)
    {
        var lines = messages
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

    private async Task UpsertDialogSummaryIfChangedAsync(ChatDialogSummary candidate, CancellationToken ct)
    {
        var existing = await _summaryRepository.GetByScopeAsync(
            candidate.ChatId,
            candidate.SummaryType,
            candidate.PeriodStart,
            candidate.PeriodEnd,
            ct);
        if (existing != null &&
            existing.IsFinalized)
        {
            _logger.LogInformation(
                "Stage5 summary skip finalized scope: chat_id={ChatId}, type={SummaryType}, period_start={PeriodStart}, period_end={PeriodEnd}",
                candidate.ChatId,
                candidate.SummaryType,
                candidate.PeriodStart,
                candidate.PeriodEnd);
            return;
        }

        if (existing != null &&
            string.Equals(
                MessageContentBuilder.CollapseWhitespace(existing.Summary),
                MessageContentBuilder.CollapseWhitespace(candidate.Summary),
                StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Stage5 summary idempotent skip (dialog summary unchanged): chat_id={ChatId}, type={SummaryType}, period_start={PeriodStart}, period_end={PeriodEnd}",
                candidate.ChatId,
                candidate.SummaryType,
                candidate.PeriodStart,
                candidate.PeriodEnd);
            return;
        }

        await _summaryRepository.UpsertAsync(candidate, ct);
    }

    private static bool ContainsCyrillic(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && CyrillicRegex.IsMatch(value);
    }

    private static bool IsLikelyRussianSession(IReadOnlyCollection<Message> messages)
    {
        return messages.Any(message => ContainsCyrillic(MessageContentBuilder.BuildSemanticContent(message)));
    }

    private static bool IsSessionCooledDown(DateTime lastMessageTimestampUtc, out TimeSpan idleRemaining)
    {
        var elapsed = DateTime.UtcNow - lastMessageTimestampUtc;
        idleRemaining = HotSessionIdleTimeout - elapsed;
        return elapsed >= HotSessionIdleTimeout;
    }

    private static bool IsArchiveOnlySession(IReadOnlyCollection<Message> session)
    {
        return session.Count > 0 && session.All(x => x.Source == MessageSource.Archive);
    }

    private bool ShouldAutoFinalizeArchiveSession(DateTime sessionEndUtc)
    {
        var archiveThresholdHours = Math.Max(1, _aggregationSettings.ArchiveThresholdHours);
        return DateTime.UtcNow - sessionEndUtc >= TimeSpan.FromHours(archiveThresholdHours);
    }

    private const string SummaryPrompt = """
You are an analytical dialogue summarizer for long-term memory context.
Return ONLY JSON object: {"summary":"..."}.

Requirements:
- summarize people, commitments, plans, schedule changes, conflicts, health/work/finance/location/contact updates
- keep only durable, behaviorally relevant context and conversation trajectory
- mention key named entities exactly as in messages
- if `[PARTICIPANTS]` block is present, treat `pN` labels in message lines as participant references and resolve them to real names in summary text
- if `[HISTORICAL_CONTEXT_HINTS]` is present, use it only as supporting continuity/disambiguation context; if it conflicts with current-session messages, trust the current session
- avoid filler, jokes, and generic chatter unless it changes intent or relationship dynamics
- keep it factual and concise (4-8 sentences)
- no markdown, no extra fields
CRITICAL: The summary MUST be in Russian. Even if the input is short or contains slang, provide a Russian response. Latin-only output is strictly forbidden.
""";
}
