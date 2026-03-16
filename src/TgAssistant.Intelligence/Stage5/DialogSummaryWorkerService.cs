using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class DialogSummaryWorkerService : BackgroundService
{
    private const string WatermarkKey = "stage5:summary_watermark";
    private const string ExtractionCompletionWatermarkKey = "stage5:summary_extraction_watermark";

    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMessageExtractionRepository _messageExtractionRepository;
    private readonly IChatDialogSummaryRepository _summaryRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly SummaryHistoricalRetrievalService _historicalRetrievalService;
    private readonly ILogger<DialogSummaryWorkerService> _logger;

    public DialogSummaryWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IMessageExtractionRepository messageExtractionRepository,
        IChatDialogSummaryRepository summaryRepository,
        IChatSessionRepository chatSessionRepository,
        IAnalysisStateRepository stateRepository,
        IExtractionErrorRepository extractionErrorRepository,
        OpenRouterAnalysisService analysisService,
        SummaryHistoricalRetrievalService historicalRetrievalService,
        ILogger<DialogSummaryWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _messageExtractionRepository = messageExtractionRepository;
        _summaryRepository = summaryRepository;
        _chatSessionRepository = chatSessionRepository;
        _stateRepository = stateRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _analysisService = analysisService;
        _historicalRetrievalService = historicalRetrievalService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled || !_settings.SummaryEnabled)
        {
            _logger.LogInformation("Stage5 summary worker is disabled");
            return;
        }

        _logger.LogInformation("Stage5 summary worker started. model={Model}", _settings.SummaryModel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var extractionWatermark = await _stateRepository.GetWatermarkAsync(ExtractionCompletionWatermarkKey, stoppingToken);
                var readyMessageIds = await _messageExtractionRepository.GetSummaryReadyMessageIdsAfterIdAsync(
                    extractionWatermark,
                    Math.Max(1, _settings.SummaryBatchSize),
                    stoppingToken);
                if (readyMessageIds.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SummaryPollIntervalSeconds)), stoppingToken);
                    continue;
                }

                var batch = await _messageRepository.GetByIdsAsync(readyMessageIds, stoppingToken);
                if (batch.Count == 0)
                {
                    var nextEmptyWatermark = readyMessageIds.Max();
                    await _stateRepository.SetWatermarkAsync(ExtractionCompletionWatermarkKey, nextEmptyWatermark, stoppingToken);
                    await _stateRepository.SetWatermarkAsync(WatermarkKey, nextEmptyWatermark, stoppingToken);
                    continue;
                }

                var summarizableBatch = FilterSummarizableMessages(batch);
                if (summarizableBatch.Count > 0)
                {
                    await BuildEpisodicChatSessionsAsync(summarizableBatch, stoppingToken);
                    await BuildDaySummariesAsync(summarizableBatch, stoppingToken);
                    await BuildSessionSummariesAsync(summarizableBatch, stoppingToken);
                }

                var nextExtractionWatermark = readyMessageIds.Max();
                await _stateRepository.SetWatermarkAsync(ExtractionCompletionWatermarkKey, nextExtractionWatermark, stoppingToken);
                await _stateRepository.SetWatermarkAsync(WatermarkKey, batch.Max(x => x.Id), stoppingToken);

                _logger.LogInformation(
                    "Stage5 summary pass done: touched_messages={TouchedCount}, summarizable_messages={SummarizableCount}, extraction_watermark={ExtractionWatermark}, watermark={Watermark}",
                    batch.Count,
                    summarizableBatch.Count,
                    nextExtractionWatermark,
                    batch.Max(x => x.Id));
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

            await _summaryRepository.UpsertAsync(new ChatDialogSummary
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

    private async Task BuildEpisodicChatSessionsAsync(List<Message> touchedMessages, CancellationToken ct)
    {
        var sessionLimit = Math.Max(1, _settings.TestModeMaxSessionsPerChat);
        var fetchLimit = Math.Max(500, _settings.SummaryDayMaxMessages * sessionLimit);
        var touchedChatIds = touchedMessages.Select(x => x.ChatId).Distinct().ToArray();
        var touchedIdsByChat = touchedMessages
            .GroupBy(x => x.ChatId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToHashSet());
        var existingSessionsByChat = await _chatSessionRepository.GetByChatsAsync(touchedChatIds, ct);

        foreach (var chatId in touchedChatIds)
        {
            var chatMessages = await _messageRepository.GetProcessedByChatAsync(chatId, fetchLimit, ct);
            if (chatMessages.Count == 0)
            {
                continue;
            }

            var sessions = SplitByGap(chatMessages, TimeSpan.FromMinutes(Math.Max(1, _settings.EpisodicSessionGapMinutes)))
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
                var sessionStart = session.First().Timestamp;
                var sessionEnd = session.Last().Timestamp;
                var boundsUnchanged = existing != null && existing.StartDate == sessionStart && existing.EndDate == sessionEnd;
                var hasExistingSummary = !string.IsNullOrWhiteSpace(existing?.Summary);
                var hasTouchedMessage = session.Any(x => touchedIds.Contains(x.Id));
                if (!hasTouchedMessage && boundsUnchanged && hasExistingSummary)
                {
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
                    boundsUnchanged &&
                    string.Equals(
                        MessageContentBuilder.CollapseWhitespace(existing.Summary),
                        MessageContentBuilder.CollapseWhitespace(summaryText),
                        StringComparison.Ordinal))
                {
                    continue;
                }

                await _chatSessionRepository.UpsertAsync(new ChatSession
                {
                    ChatId = chatId,
                    SessionIndex = i,
                    StartDate = sessionStart,
                    EndDate = sessionEnd,
                    Summary = summaryText
                }, ct);

                await _historicalRetrievalService.UpsertSessionSummaryEmbeddingAsync(chatId, i, summaryText, ct);
            }
        }
    }

    private async Task BuildSessionSummariesAsync(List<Message> touchedMessages, CancellationToken ct)
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

                await _summaryRepository.UpsertAsync(new ChatDialogSummary
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
            return ExtractSummary(response);
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

    private List<List<Message>> SplitByGap(List<Message> messages, TimeSpan? gapOverride = null)
    {
        var gap = gapOverride ?? TimeSpan.FromMinutes(Math.Max(1, _settings.SummarySessionGapMinutes));
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

    private const string SummaryPrompt = """
You are an analytical dialogue summarizer for long-term memory context.
Return ONLY JSON object: {"summary":"..."}.

Requirements:
- summarize people, commitments, plans, schedule changes, conflicts, health/work/finance/location/contact updates
- keep only durable, behaviorally relevant context and conversation trajectory
- mention key named entities exactly as in messages
- if `[HISTORICAL_CONTEXT_HINTS]` is present, use it only as supporting continuity/disambiguation context; if it conflicts with current-session messages, trust the current session
- avoid filler, jokes, and generic chatter unless it changes intent or relationship dynamics
- keep it factual and concise (4-8 sentences)
- no markdown, no extra fields
CRITICAL: The summary MUST be in Russian. Even if the input is short or contains slang, provide a Russian response. Latin-only output is strictly forbidden.
""";
}
