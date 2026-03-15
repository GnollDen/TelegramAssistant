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

    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatDialogSummaryRepository _summaryRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly OpenRouterAnalysisService _analysisService;
    private readonly ILogger<DialogSummaryWorkerService> _logger;

    public DialogSummaryWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IChatDialogSummaryRepository summaryRepository,
        IAnalysisStateRepository stateRepository,
        IExtractionErrorRepository extractionErrorRepository,
        OpenRouterAnalysisService analysisService,
        ILogger<DialogSummaryWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _summaryRepository = summaryRepository;
        _stateRepository = stateRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _analysisService = analysisService;
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
                var watermark = await _stateRepository.GetWatermarkAsync(WatermarkKey, stoppingToken);
                var batch = await _messageRepository.GetProcessedAfterIdAsync(
                    watermark,
                    Math.Max(1, _settings.SummaryBatchSize),
                    stoppingToken);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SummaryPollIntervalSeconds)), stoppingToken);
                    continue;
                }

                await BuildDaySummariesAsync(batch, stoppingToken);
                await BuildSessionSummariesAsync(batch, stoppingToken);

                var nextWatermark = batch.Max(x => x.Id);
                await _stateRepository.SetWatermarkAsync(WatermarkKey, nextWatermark, stoppingToken);
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
            if (dayMessages.Count < _settings.SummaryMinMessages)
            {
                continue;
            }

            var summaryText = await GenerateSummaryAsync(
                scope.Key.ChatId,
                "day",
                dayStart,
                dayEnd,
                dayMessages,
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
                StartMessageId = dayMessages.First().Id,
                EndMessageId = dayMessages.Last().Id,
                MessageCount = dayMessages.Count,
                Summary = summaryText
            }, ct);
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
            if (dayMessages.Count < _settings.SummaryMinMessages)
            {
                continue;
            }

            var touchedIds = scope.Select(x => x.Id).ToHashSet();
            foreach (var session in SplitByGap(dayMessages))
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
        CancellationToken ct)
    {
        var response = await _analysisService.SummarizeDialogAsync(
            string.IsNullOrWhiteSpace(_settings.SummaryModel) ? _settings.ExpensiveModel : _settings.SummaryModel,
            SummaryPrompt,
            chatId,
            scope,
            periodStart,
            periodEnd,
            messages,
            ct);
        return ExtractSummary(response);
    }

    private List<List<Message>> SplitByGap(List<Message> messages)
    {
        var gap = TimeSpan.FromMinutes(Math.Max(1, _settings.SummarySessionGapMinutes));
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
- avoid filler, jokes, and generic chatter unless it changes intent or relationship dynamics
- keep it factual and concise (4-8 sentences)
- no markdown, no extra fields
""";
}
