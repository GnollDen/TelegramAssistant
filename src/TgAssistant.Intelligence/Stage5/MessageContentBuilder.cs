using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class MessageContentBuilder
{
    private readonly IMessageRepository _messageRepository;

    public MessageContentBuilder(IMessageRepository messageRepository)
    {
        _messageRepository = messageRepository;
    }

    /// <summary>
    /// Builds LLM-ready message content with metadata and optional reply context.
    /// </summary>
    public static string BuildMessageText(Message message, Message? replyTo)
    {
        var parts = new List<string>();
        parts.Add(
            $"[meta] message_id={message.Id} telegram_message_id={message.TelegramMessageId} chat_id={message.ChatId} sender_id={message.SenderId} sender_name=\"{(message.SenderName ?? string.Empty).Trim()}\" ts={message.Timestamp:O} reply_to={message.ReplyToMessageId?.ToString() ?? "null"}");

        if (replyTo != null)
        {
            var replyText = TruncateForContext(replyTo.Text, 240);
            if (!string.IsNullOrWhiteSpace(replyTo.MediaTranscription))
            {
                replyText = string.IsNullOrWhiteSpace(replyText)
                    ? TruncateForContext(replyTo.MediaTranscription, 240)
                    : $"{replyText} | media: {TruncateForContext(replyTo.MediaTranscription, 120)}";
            }

            if (!string.IsNullOrWhiteSpace(replyText))
            {
                parts.Add(
                    $"[reply_context] from_sender=\"{replyTo.SenderName}\" ts={replyTo.Timestamp:O} text=\"{replyText}\"");
            }
        }

        if (!string.IsNullOrWhiteSpace(message.Text)) parts.Add(message.Text);
        if (!string.IsNullOrWhiteSpace(message.MediaTranscription)) parts.Add($"[media_transcription] {message.MediaTranscription}");
        if (!string.IsNullOrWhiteSpace(message.MediaDescription)) parts.Add($"[media_description] {message.MediaDescription}");
        if (!string.IsNullOrWhiteSpace(message.MediaParalinguisticsJson)) parts.Add($"[voice_paralinguistics] {message.MediaParalinguisticsJson}");
        return string.Join("\n", parts);
    }

    /// <summary>
    /// Builds cheap-pass batch prompt for OpenRouter extraction.
    /// </summary>
    public static string BuildCheapBatchPrompt(List<AnalysisInputMessage> batch)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Analyze these chat messages and return one extraction item per message.");
        foreach (var msg in batch)
        {
            sb.AppendLine($"<message id=\"{msg.MessageId}\" sender_name=\"{msg.SenderName}\" ts=\"{msg.Timestamp:O}\">");
            sb.AppendLine(msg.Text);
            sb.AppendLine("</message>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds semantic-only content used by extraction heuristics.
    /// </summary>
    public static string BuildSemanticContent(Message message)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            parts.Add(message.Text);
        }

        if (!string.IsNullOrWhiteSpace(message.MediaTranscription))
        {
            parts.Add(message.MediaTranscription);
        }

        if (!string.IsNullOrWhiteSpace(message.MediaDescription))
        {
            parts.Add(message.MediaDescription);
        }

        return string.Join(' ', parts).Trim();
    }

    /// <summary>
    /// Truncates text for prompt context boundaries.
    /// </summary>
    public static string TruncateForContext(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        if (text.Length <= maxLen)
        {
            return text;
        }

        return text[..maxLen].TrimEnd() + "...";
    }

    /// <summary>
    /// Loads reply messages for a batch keyed by source message id.
    /// </summary>
    public async Task<Dictionary<long, Message>> LoadReplyContextAsync(List<Message> messages, CancellationToken ct)
    {
        var result = new Dictionary<long, Message>();
        var groups = messages
            .Where(m => m.ReplyToMessageId.HasValue && m.ReplyToMessageId.Value > 0)
            .GroupBy(m => new { m.ChatId, m.Source })
            .ToList();

        foreach (var group in groups)
        {
            var replyIds = group
                .Where(m => m.ReplyToMessageId.HasValue)
                .Select(m => m.ReplyToMessageId!.Value)
                .Distinct()
                .ToList();
            if (replyIds.Count == 0)
            {
                continue;
            }

            var byTelegramId = await _messageRepository.GetByTelegramMessageIdsAsync(
                group.Key.ChatId,
                group.Key.Source,
                replyIds,
                ct);

            foreach (var message in group)
            {
                if (!message.ReplyToMessageId.HasValue)
                {
                    continue;
                }

                if (byTelegramId.TryGetValue(message.ReplyToMessageId.Value, out var reply))
                {
                    result[message.Id] = reply;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Loads reply message for a single source message.
    /// </summary>
    public async Task<Message?> LoadReplyMessageAsync(Message? message, CancellationToken ct)
    {
        if (message?.ReplyToMessageId is null || message.ReplyToMessageId.Value <= 0)
        {
            return null;
        }

        var byTelegramId = await _messageRepository.GetByTelegramMessageIdsAsync(
            message.ChatId,
            message.Source,
            [message.ReplyToMessageId.Value],
            ct);

        return byTelegramId.GetValueOrDefault(message.ReplyToMessageId.Value);
    }

    /// <summary>
    /// Splits text into non-empty meaningful lines.
    /// </summary>
    public static IEnumerable<string> SplitMeaningfulLines(string content)
    {
        return content
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    /// <summary>
    /// Collapses repeated whitespace into single spaces.
    /// </summary>
    public static string CollapseWhitespace(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
    }
}
