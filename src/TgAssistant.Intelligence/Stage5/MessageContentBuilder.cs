using System.Text.Json;
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
    public static string BuildMessageText(Message message, Message? replyTo, AnalysisMessageContext? context = null)
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
        var voiceMessageMarker = BuildVoiceMessageMarker(message);
        if (!string.IsNullOrWhiteSpace(voiceMessageMarker))
        {
            parts.Add(voiceMessageMarker);
        }

        if (!string.IsNullOrWhiteSpace(message.MediaTranscription)) parts.Add($"[media_transcription] {message.MediaTranscription}");
        if (!string.IsNullOrWhiteSpace(message.MediaDescription)) parts.Add($"[media_description] {message.MediaDescription}");
        if (HasUsableVoiceParalinguistics(message.MediaParalinguisticsJson)) parts.Add($"[voice_paralinguistics] {message.MediaParalinguisticsJson}");
        var contextBlock = BuildContextBlock(context);
        if (!string.IsNullOrWhiteSpace(contextBlock))
        {
            parts.Add(contextBlock);
        }
        parts.Add($"[temporal_context] message_date={message.Timestamp:O}");

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Builds explicit three-layer context block for extraction prompts.
    /// </summary>
    public static string BuildContextBlock(AnalysisMessageContext? context)
    {
        if (context == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (context.LocalBurst.Count > 0)
        {
            parts.Add("[local_burst_context]");
            parts.AddRange(context.LocalBurst);
        }

        if (context.SessionStart.Count > 0)
        {
            parts.Add("[session_start_context]");
            parts.AddRange(context.SessionStart);
        }

        if (context.HistoricalSummaries.Count > 0)
        {
            parts.Add("[historical_context]");
            parts.AddRange(context.HistoricalSummaries);
        }

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
    /// Builds prompt input for dialogue summarization.
    /// </summary>
    public static string BuildSummaryPrompt(long chatId, string scope, DateTime periodStart, DateTime periodEnd, List<Message> messages)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[summary_meta] chat_id={chatId} scope={scope} period_start={periodStart:O} period_end={periodEnd:O} message_count={messages.Count}");
        sb.AppendLine("Messages:");
        foreach (var message in messages.OrderBy(x => x.Id))
        {
            var sender = string.IsNullOrWhiteSpace(message.SenderName) ? $"user:{message.SenderId}" : message.SenderName.Trim();
            var text = BuildSemanticContent(message);
            var compact = TruncateForContext(text, 280);
            if (string.IsNullOrWhiteSpace(compact))
            {
                continue;
            }

            sb.AppendLine($"- [{message.Timestamp:yyyy-MM-dd HH:mm}] {sender}: {compact}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds semantic-only content used by extraction heuristics.
    /// </summary>
    public static string BuildSemanticContent(Message message)
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            parts.Add(message.Text);
        }

        var voiceMessageMarker = BuildVoiceMessageMarker(message);
        if (!string.IsNullOrWhiteSpace(voiceMessageMarker))
        {
            parts.Add(voiceMessageMarker);
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

    private static string BuildVoiceMessageMarker(Message message)
    {
        if (!IsVoiceLikeMediaType(message.MediaType))
        {
            return string.Empty;
        }

        var transcription = TruncateForContext(message.MediaTranscription, 600);
        var tone = TryGetVoiceToneLabel(message.MediaParalinguisticsJson);
        if (string.IsNullOrWhiteSpace(transcription) && string.IsNullOrWhiteSpace(tone))
        {
            return string.Empty;
        }

        var prefix = string.IsNullOrWhiteSpace(tone)
            ? "[Voice Message]"
            : $"[Voice Message: {tone}]";

        return string.IsNullOrWhiteSpace(transcription)
            ? prefix
            : $"{prefix} \"{transcription}\"";
    }

    private static bool IsVoiceLikeMediaType(MediaType mediaType)
    {
        return mediaType is MediaType.Voice or MediaType.VideoNote or MediaType.Video;
    }

    private static string TryGetVoiceToneLabel(string? jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var statusNode)
                && statusNode.ValueKind == JsonValueKind.String
                && string.Equals(statusNode.GetString(), "failed", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (root.TryGetProperty("primary_emotion", out var primaryEmotionNode)
                && primaryEmotionNode.ValueKind == JsonValueKind.String)
            {
                var primaryEmotion = primaryEmotionNode.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(primaryEmotion))
                {
                    return $"{primaryEmotion} tone";
                }
            }
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static bool HasUsableVoiceParalinguistics(string? jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            return !(doc.RootElement.TryGetProperty("status", out var statusNode)
                     && statusNode.ValueKind == JsonValueKind.String
                     && string.Equals(statusNode.GetString(), "failed", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
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
