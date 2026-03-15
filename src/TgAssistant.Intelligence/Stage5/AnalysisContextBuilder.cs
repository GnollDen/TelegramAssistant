using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class AnalysisContextBuilder
{
    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatDialogSummaryRepository _summaryRepository;

    public AnalysisContextBuilder(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IChatDialogSummaryRepository summaryRepository)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _summaryRepository = summaryRepository;
    }

    /// <summary>
    /// Builds three-level context for a message batch: local burst, session start and historical summaries.
    /// </summary>
    public async Task<Dictionary<long, AnalysisMessageContext>> BuildBatchContextsAsync(List<Message> messages, CancellationToken ct)
    {
        var result = new Dictionary<long, AnalysisMessageContext>(messages.Count);
        if (messages.Count == 0)
        {
            return result;
        }

        var historicalByChat = new Dictionary<long, List<ChatDialogSummary>>();
        var historyLimit = Math.Max(1, _settings.HistoricalSummaryContextItems * 2);
        foreach (var chatId in messages.Select(x => x.ChatId).Distinct())
        {
            historicalByChat[chatId] = await _summaryRepository.GetRecentByChatAsync(chatId, historyLimit, ct);
        }

        var lookback = Math.Max(
            Math.Max(1, _settings.LocalBurstContextMessages),
            Math.Max(1, _settings.SessionContextLookbackMessages));

        foreach (var message in messages.OrderBy(x => x.Id))
        {
            var previous = await _messageRepository.GetChatWindowBeforeAsync(message.ChatId, message.Id, lookback, ct);
            var context = new AnalysisMessageContext
            {
                LocalBurst = BuildLocalBurst(previous, message.Timestamp),
                SessionStart = BuildSessionStart(previous, message),
                HistoricalSummaries = BuildHistoricalSummaries(
                    historicalByChat.GetValueOrDefault(message.ChatId) ?? [],
                    message.Timestamp)
            };

            result[message.Id] = context;
        }

        return result;
    }

    private List<string> BuildLocalBurst(List<Message> previousMessages, DateTime currentTs)
    {
        var window = TimeSpan.FromMinutes(Math.Max(1, _settings.LocalBurstContextWindowMinutes));
        var burst = previousMessages
            .Where(x => (currentTs - x.Timestamp) <= window)
            .TakeLast(Math.Max(1, _settings.LocalBurstContextMessages))
            .Select(FormatContextMessageLine)
            .ToList();

        return burst;
    }

    private List<string> BuildSessionStart(List<Message> previousMessages, Message currentMessage)
    {
        var combined = previousMessages.ToList();
        combined.Add(currentMessage);

        var gap = TimeSpan.FromMinutes(Math.Max(1, _settings.SessionContextGapMinutes));
        var sessionStartIndex = Math.Max(0, combined.Count - 1);
        for (var i = combined.Count - 1; i > 0; i--)
        {
            var delta = combined[i].Timestamp - combined[i - 1].Timestamp;
            if (delta > gap)
            {
                sessionStartIndex = i;
                break;
            }

            sessionStartIndex = i - 1;
        }

        return combined
            .Skip(sessionStartIndex)
            .Where(x => x.Id != currentMessage.Id)
            .Take(Math.Max(1, _settings.SessionStartContextMessages))
            .Select(FormatContextMessageLine)
            .ToList();
    }

    private List<string> BuildHistoricalSummaries(List<ChatDialogSummary> summaries, DateTime currentTs)
    {
        return summaries
            .Where(x => x.PeriodEnd < currentTs)
            .OrderByDescending(x => x.PeriodEnd)
            .Take(Math.Max(1, _settings.HistoricalSummaryContextItems))
            .Select(FormatSummaryLine)
            .ToList();
    }

    private static string FormatContextMessageLine(Message message)
    {
        var sender = string.IsNullOrWhiteSpace(message.SenderName) ? $"user:{message.SenderId}" : message.SenderName.Trim();
        var semantic = MessageContentBuilder.BuildSemanticContent(message);
        var text = MessageContentBuilder.TruncateForContext(semantic, 220);
        return $"[{message.Timestamp:yyyy-MM-dd HH:mm}] {sender}: {text}";
    }

    private static string FormatSummaryLine(ChatDialogSummary summary)
    {
        var label = summary.SummaryType == ChatDialogSummaryType.Day ? "day" : "session";
        var compact = MessageContentBuilder.TruncateForContext(summary.Summary, 360);
        return $"[{label} {summary.PeriodStart:yyyy-MM-dd HH:mm}-{summary.PeriodEnd:yyyy-MM-dd HH:mm}] {compact}";
    }
}
