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
    private readonly IChatSessionRepository _chatSessionRepository;

    public AnalysisContextBuilder(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IChatDialogSummaryRepository summaryRepository,
        IChatSessionRepository chatSessionRepository)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _summaryRepository = summaryRepository;
        _chatSessionRepository = chatSessionRepository;
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
        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync(messages.Select(x => x.ChatId).Distinct().ToArray(), ct);
        var historyLimit = Math.Max(1, _settings.HistoricalSummaryContextItems * 2);
        foreach (var chatId in messages.Select(x => x.ChatId).Distinct())
        {
            historicalByChat[chatId] = await _summaryRepository.GetRecentByChatAsync(chatId, historyLimit, ct);
        }

        var orderedMessages = messages.OrderBy(x => x.Id).ToList();
        var lookback = Math.Max(
            Math.Max(1, _settings.LocalBurstContextMessages),
            Math.Max(1, _settings.SessionContextLookbackMessages));
        var previousByMessageId = await _messageRepository.GetChatWindowsBeforeByMessageIdsAsync(
            orderedMessages.Select(x => x.Id).ToArray(),
            lookback,
            ct);
        var externalReplyContextByMessageId = await BuildExternalReplyContextAsync(
            orderedMessages,
            sessionsByChat,
            previousByMessageId,
            ct);

        foreach (var message in orderedMessages)
        {
            var previous = previousByMessageId.GetValueOrDefault(message.Id) ?? [];
            var context = new AnalysisMessageContext
            {
                LocalBurst = BuildLocalBurst(previous, message.Timestamp),
                SessionStart = BuildSessionStart(previous, message),
                ExternalReplyContext = externalReplyContextByMessageId.GetValueOrDefault(message.Id) ?? [],
                HistoricalSummaries = BuildHistoricalSummaries(
                    historicalByChat.GetValueOrDefault(message.ChatId) ?? [],
                    message.Timestamp),
                PreviousSessionSummary = BuildPreviousSessionSummary(
                    sessionsByChat.GetValueOrDefault(message.ChatId) ?? [],
                    message.Timestamp)
            };

            result[message.Id] = context;
        }

        return result;
    }

    private async Task<Dictionary<long, List<string>>> BuildExternalReplyContextAsync(
        List<Message> messages,
        Dictionary<long, List<ChatSession>> sessionsByChat,
        IReadOnlyDictionary<long, List<Message>> previousByMessageId,
        CancellationToken ct)
    {
        var result = new Dictionary<long, List<string>>();
        var candidates = new List<(long MessageId, DateTime MessageTimestamp, List<string> Lines)>();
        var groups = messages
            .Where(m => m.ReplyToMessageId.HasValue && m.ReplyToMessageId.Value > 0)
            .GroupBy(m => new { m.ChatId, m.Source })
            .ToList();

        foreach (var group in groups)
        {
            var replyIds = group
                .Select(m => m.ReplyToMessageId!.Value)
                .Distinct()
                .ToArray();
            if (replyIds.Length == 0)
            {
                continue;
            }

            var repliesByTelegramId = await _messageRepository.GetByTelegramMessageIdsAsync(
                group.Key.ChatId,
                group.Key.Source,
                replyIds,
                ct);

            foreach (var message in group.OrderBy(x => x.Id))
            {
                if (!repliesByTelegramId.TryGetValue(message.ReplyToMessageId!.Value, out var reply))
                {
                    continue;
                }

                var currentSessionStart = ResolveCurrentSessionStart(message, previousByMessageId);
                if (reply.Timestamp >= currentSessionStart)
                {
                    continue;
                }

                var lines = FormatExternalReplyContext(
                    reply,
                    ResolveSessionForTimestamp(sessionsByChat.GetValueOrDefault(message.ChatId) ?? [], reply.Timestamp));
                if (lines.Count == 0)
                {
                    continue;
                }

                candidates.Add((message.Id, message.Timestamp, lines));
            }
        }

        foreach (var candidate in candidates
                     .OrderByDescending(x => x.MessageTimestamp)
                     .Take(5))
        {
            result[candidate.MessageId] = candidate.Lines;
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

        var sessionStartIndex = FindSessionStartIndex(combined);

        return combined
            .Skip(sessionStartIndex)
            .Where(x => x.Id != currentMessage.Id)
            .Take(Math.Max(1, _settings.SessionStartContextMessages))
            .Select(FormatContextMessageLine)
            .ToList();
    }

    private DateTime ResolveCurrentSessionStart(
        Message message,
        IReadOnlyDictionary<long, List<Message>> previousByMessageId)
    {
        var combined = (previousByMessageId.GetValueOrDefault(message.Id) ?? []).ToList();
        combined.Add(message);
        return combined[FindSessionStartIndex(combined)].Timestamp;
    }

    private int FindSessionStartIndex(List<Message> combined)
    {
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

        return sessionStartIndex;
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

    private static string? BuildPreviousSessionSummary(List<ChatSession> sessions, DateTime currentTs)
    {
        if (sessions.Count == 0)
        {
            return null;
        }

        var currentSession = sessions.FirstOrDefault(session => session.StartDate <= currentTs && currentTs <= session.EndDate);
        if (currentSession == null)
        {
            currentSession = sessions
                .Where(session => session.StartDate <= currentTs)
                .OrderByDescending(session => session.SessionIndex)
                .FirstOrDefault();
        }

        if (currentSession == null || currentSession.SessionIndex <= 0)
        {
            return null;
        }

        var previousSession = sessions.FirstOrDefault(session => session.SessionIndex == currentSession.SessionIndex - 1);
        return previousSession == null
            ? null
            : MessageContentBuilder.CollapseWhitespace(previousSession.Summary);
    }

    private static ChatSession? ResolveSessionForTimestamp(List<ChatSession> sessions, DateTime timestamp)
    {
        return sessions.FirstOrDefault(session =>
            session.StartDate <= timestamp && timestamp <= session.EndDate);
    }

    private static List<string> FormatExternalReplyContext(Message reply, ChatSession? session)
    {
        var lines = new List<string>
        {
            $"[reply_message] ts={reply.Timestamp:O} sender=\"{(reply.SenderName ?? string.Empty).Trim()}\" text=\"{MessageContentBuilder.TruncateForContext(MessageContentBuilder.BuildSemanticContent(reply), 220)}\""
        };

        if (session != null)
        {
            lines.Add(
                $"[reply_session] session_index={session.SessionIndex} period={session.StartDate:O}..{session.EndDate:O}");

            var summary = MessageContentBuilder.TruncateForContext(
                MessageContentBuilder.CollapseWhitespace(session.Summary),
                320);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                lines.Add($"[reply_session_summary] {summary}");
            }
        }

        return lines;
    }
}
