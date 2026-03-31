using System.Text.Json;
using System.Text.RegularExpressions;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class MessageContentBuilder
{
    private static readonly Regex ServicePlaceholderRegex = new(@"^\[[A-Z_]{2,32}\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SlashCommandRegex = new(@"^[\/!][\w@][\w@\-.]*(?:\s+\S+){0,2}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SlashCommandTokenRegex = new(@"^[\/!][\w@][\w@\-.]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlOnlyRegex = new(@"^(?:(?:https?:\/\/)|(?:www\.))\S+$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ParticipantMetaNameRegex = new(@"(?<prefix>(?:sender_name|from_sender|sender)="")(?<name>[^""]+)(?<suffix>"")", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            $"[meta] message_id={message.Id} sender_name=\"{(message.SenderName ?? string.Empty).Trim()}\"");

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
                    $"[reply_context] from_sender=\"{replyTo.SenderName}\" text=\"{replyText}\"");
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
        var mediaFallbackMarker = BuildMediaPresenceFallbackMarker(message);
        if (!string.IsNullOrWhiteSpace(mediaFallbackMarker))
        {
            parts.Add(mediaFallbackMarker);
        }
        if (HasUsableVoiceParalinguistics(message.MediaParalinguisticsJson)) parts.Add($"[voice_paralinguistics] {message.MediaParalinguisticsJson}");
        if (!string.IsNullOrWhiteSpace(context?.PreviousSessionSummary))
        {
            parts.Add($"[PREVIOUS SESSION SUMMARY]: {TruncateForContext(context.PreviousSessionSummary, 600)}");
        }
        var contextBlock = BuildContextBlock(context);
        if (!string.IsNullOrWhiteSpace(contextBlock))
        {
            parts.Add(contextBlock);
        }
        parts.Add($"[temporal_context] message_date={message.Timestamp:yyyy-MM-dd HH:mm}");

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

        if (context.ExternalReplyContext.Count > 0)
        {
            parts.Add("[EXTERNAL_REPLY_CONTEXT]");
            parts.AddRange(context.ExternalReplyContext);
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
    public static string BuildCheapBatchPrompt(
        List<AnalysisInputMessage> batch,
        string? chunkSummaryPrev = null,
        string? replySliceContext = null,
        string? ragContext = null)
    {
        var sb = new System.Text.StringBuilder();
        var participantRefByName = BuildParticipantRefs(batch);
        if (participantRefByName.Count > 0)
        {
            sb.AppendLine("[PARTICIPANTS]");
            foreach (var pair in participantRefByName.OrderBy(x => x.Value, StringComparer.Ordinal))
            {
                sb.AppendLine($"{pair.Value}=\"{pair.Key}\"");
            }

            sb.AppendLine("[/PARTICIPANTS]");
        }

        if (!string.IsNullOrWhiteSpace(chunkSummaryPrev))
        {
            sb.AppendLine("[CHUNK_SUMMARY_PREV]");
            sb.AppendLine(TruncateForContext(CollapseWhitespace(chunkSummaryPrev), 700));
            sb.AppendLine("[/CHUNK_SUMMARY_PREV]");
        }

        if (!string.IsNullOrWhiteSpace(replySliceContext))
        {
            sb.AppendLine("[REPLY_SLICE_CONTEXT]");
            sb.AppendLine(TruncateForContext(CollapseWhitespace(replySliceContext), 1000));
            sb.AppendLine("[/REPLY_SLICE_CONTEXT]");
        }

        if (!string.IsNullOrWhiteSpace(ragContext))
        {
            sb.AppendLine("[RAG_CONTEXT]");
            sb.AppendLine(TruncateForContext(CollapseWhitespace(ragContext), 900));
            sb.AppendLine("[/RAG_CONTEXT]");
        }

        sb.AppendLine("[CHUNK_PAYLOAD]");
        foreach (var msg in batch)
        {
            sb.AppendLine($"<message id=\"{msg.MessageId}\">");
            var payload = ReplaceParticipantRefs(msg.Text, participantRefByName);
            sb.AppendLine(TruncateForContext(payload, 2200));
            sb.AppendLine("</message>");
        }
        sb.AppendLine("[/CHUNK_PAYLOAD]");

        return sb.ToString();
    }

    /// <summary>
    /// Builds compact cheap-pass message payload without per-message historical/session context duplication.
    /// </summary>
    public static string BuildCheapChunkMessageText(Message message, Message? replyTo)
    {
        var parts = new List<string>();
        parts.Add($"[meta] message_id={message.Id} sender_name=\"{(message.SenderName ?? string.Empty).Trim()}\"");

        if (replyTo != null)
        {
            var replySender = (replyTo.SenderName ?? string.Empty).Trim();
            var replyText = TruncateForContext(CollapseWhitespace(BuildSemanticContent(replyTo)), 180);
            if (!string.IsNullOrWhiteSpace(replyText))
            {
                parts.Add($"[reply_context] from_sender=\"{replySender}\" text=\"{replyText}\"");
            }
        }

        var text = TruncateForContext(CollapseWhitespace(message.Text ?? string.Empty), 1400);
        if (!string.IsNullOrWhiteSpace(text))
        {
            parts.Add(text);
        }

        var voiceMessageMarker = BuildVoiceMessageMarker(message);
        if (!string.IsNullOrWhiteSpace(voiceMessageMarker))
        {
            parts.Add(voiceMessageMarker);
        }

        var mediaTranscription = TruncateForContext(CollapseWhitespace(message.MediaTranscription ?? string.Empty), 900);
        if (!string.IsNullOrWhiteSpace(mediaTranscription))
        {
            parts.Add($"[media_transcription] {mediaTranscription}");
        }

        var mediaDescription = TruncateForContext(CollapseWhitespace(message.MediaDescription ?? string.Empty), 480);
        if (!string.IsNullOrWhiteSpace(mediaDescription))
        {
            parts.Add($"[media_description] {mediaDescription}");
        }

        var mediaFallbackMarker = BuildMediaPresenceFallbackMarker(message);
        if (!string.IsNullOrWhiteSpace(mediaFallbackMarker))
        {
            parts.Add(mediaFallbackMarker);
        }

        if (HasUsableVoiceParalinguistics(message.MediaParalinguisticsJson))
        {
            parts.Add($"[voice_paralinguistics] {TruncateForContext(CollapseWhitespace(message.MediaParalinguisticsJson ?? string.Empty), 320)}");
        }

        var editTrackingSnippet = BuildEditTrackingSnippet(message.ForwardJson);
        if (!string.IsNullOrWhiteSpace(editTrackingSnippet))
        {
            parts.Add(editTrackingSnippet);
        }

        parts.Add($"[temporal_context] message_date={message.Timestamp:yyyy-MM-dd HH:mm}");
        return string.Join("\n", parts);
    }

    private static Dictionary<string, string> BuildParticipantRefs(IReadOnlyCollection<AnalysisInputMessage> batch)
    {
        var names = new List<string>();
        foreach (var message in batch)
        {
            var senderName = (message.SenderName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(senderName))
            {
                names.Add(senderName);
            }

            if (!string.IsNullOrWhiteSpace(message.Text))
            {
                foreach (Match match in ParticipantMetaNameRegex.Matches(message.Text))
                {
                    var capturedName = match.Groups["name"].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(capturedName))
                    {
                        names.Add(capturedName);
                    }
                }
            }
        }

        var distinctNames = names
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < distinctNames.Count; i++)
        {
            refs[distinctNames[i]] = $"p{i + 1}";
        }

        return refs;
    }

    private static string ReplaceParticipantRefs(string text, IReadOnlyDictionary<string, string> participantRefByName)
    {
        if (string.IsNullOrWhiteSpace(text) || participantRefByName.Count == 0)
        {
            return text;
        }

        return ParticipantMetaNameRegex.Replace(text, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            if (!participantRefByName.TryGetValue(name, out var reference))
            {
                return match.Value;
            }

            return $"{match.Groups["prefix"].Value}{reference}{match.Groups["suffix"].Value}";
        });
    }

    /// <summary>
    /// Builds prompt input for dialogue summarization.
    /// </summary>
    public static string BuildSummaryPrompt(
        long chatId,
        string scope,
        DateTime periodStart,
        DateTime periodEnd,
        List<Message> messages,
        IReadOnlyCollection<SummaryHistoricalHint>? historicalHints = null,
        IReadOnlyDictionary<long, string>? cheapJsonByMessageId = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[summary_meta] chat_id={chatId} scope={scope} period_start={periodStart:O} period_end={periodEnd:O} message_count={messages.Count}");
        var participantRefByName = BuildParticipantRefs(messages);
        if (participantRefByName.Count > 0)
        {
            sb.AppendLine("[PARTICIPANTS]");
            foreach (var pair in participantRefByName.OrderBy(x => x.Value, StringComparer.Ordinal))
            {
                sb.AppendLine($"{pair.Value}=\"{pair.Key}\"");
            }

            sb.AppendLine("[/PARTICIPANTS]");
        }

        if (historicalHints != null)
        {
            sb.AppendLine("[HISTORICAL_CONTEXT_HINTS]");
            if (historicalHints.Count == 0)
            {
                sb.AppendLine("none");
            }
            else
            {
                foreach (var hint in historicalHints)
                {
                    var compact = TruncateForContext(CollapseWhitespace(hint.Summary), 320);
                    sb.AppendLine($"- session_index={hint.SessionIndex} similarity={hint.Similarity:0.00} summary=\"{compact}\"");
                }
            }

            sb.AppendLine("[/HISTORICAL_CONTEXT_HINTS]");
        }

        sb.AppendLine("Messages:");
        foreach (var message in messages.OrderBy(x => x.Id))
        {
            var sender = string.IsNullOrWhiteSpace(message.SenderName) ? $"user:{message.SenderId}" : message.SenderName.Trim();
            if (participantRefByName.TryGetValue(sender, out var senderRef))
            {
                sender = senderRef;
            }

            var cheapJson = cheapJsonByMessageId?.GetValueOrDefault(message.Id);
            var text = BuildSemanticContent(message, cheapJson);
            var compact = TruncateForContext(text, 280);
            if (string.IsNullOrWhiteSpace(compact))
            {
                continue;
            }

            sb.AppendLine($"- [{message.Timestamp:yyyy-MM-dd HH:mm}] {sender}: Контекст сообщений (на русском): {compact}");
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> BuildParticipantRefs(IReadOnlyCollection<Message> messages)
    {
        var names = messages
            .Select(x => (x.SenderName ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < names.Count; i++)
        {
            refs[names[i]] = $"p{i + 1}";
        }

        return refs;
    }

    /// <summary>
    /// Builds semantic-only content used by extraction heuristics.
    /// </summary>
    public static string BuildSemanticContent(Message message)
    {
        return BuildSemanticContent(message, null);
    }

    public static bool IsServiceOrTechnicalNoise(Message message)
    {
        var text = CollapseWhitespace(message.Text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (text.Equals("[DELETED]", StringComparison.OrdinalIgnoreCase) ||
                ServicePlaceholderRegex.IsMatch(text))
            {
                return true;
            }

            if (SlashCommandRegex.IsMatch(text))
            {
                return true;
            }

            if (UrlOnlyRegex.IsMatch(text))
            {
                return true;
            }
        }

        return string.IsNullOrWhiteSpace(BuildSemanticContent(message));
    }

    public static bool IsTrashOnlySession(IReadOnlyCollection<Message> messages)
    {
        if (messages.Count == 0)
        {
            return true;
        }

        foreach (var message in messages)
        {
            var semantic = CollapseWhitespace(BuildSemanticContent(message));
            if (string.IsNullOrWhiteSpace(semantic))
            {
                continue;
            }

            if (semantic.Equals("[DELETED]", StringComparison.OrdinalIgnoreCase) ||
                ServicePlaceholderRegex.IsMatch(semantic) ||
                SlashCommandRegex.IsMatch(semantic) ||
                UrlOnlyRegex.IsMatch(semantic))
            {
                continue;
            }

            var tokens = semantic
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length > 0 && tokens.All(token => SlashCommandTokenRegex.IsMatch(token) || UrlOnlyRegex.IsMatch(token)))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds semantic content with optional extraction context from cheap_json.
    /// </summary>
    public static string BuildSemanticContent(Message message, string? cheapJson)
    {
        var parts = new List<string>(5);
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

        var mediaFallbackMarker = BuildMediaPresenceFallbackMarker(message);
        if (!string.IsNullOrWhiteSpace(mediaFallbackMarker))
        {
            parts.Add(mediaFallbackMarker);
        }

        var editTrackingSnippet = BuildEditTrackingSnippet(message.ForwardJson);
        if (!string.IsNullOrWhiteSpace(editTrackingSnippet))
        {
            parts.Add(editTrackingSnippet);
        }

        var extractionSnippet = BuildExtractionContextSnippet(cheapJson);
        if (!string.IsNullOrWhiteSpace(extractionSnippet))
        {
            parts.Add(extractionSnippet);
        }

        return string.Join(' ', parts).Trim();
    }

    private static string BuildEditTrackingSnippet(string? forwardJson)
    {
        if (string.IsNullOrWhiteSpace(forwardJson))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(forwardJson);
            if (!doc.RootElement.TryGetProperty("edit_tracking", out var tracking) || tracking.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var status = tracking.TryGetProperty("status", out var statusNode) && statusNode.ValueKind == JsonValueKind.String
                ? statusNode.GetString()
                : string.Empty;
            if (!string.Equals(status, "done", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var shouldAffectMemory = tracking.TryGetProperty("should_affect_memory", out var affectNode)
                                     && affectNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                                     && affectNode.GetBoolean();
            var addedImportant = tracking.TryGetProperty("added_important", out var addedNode)
                                 && addedNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                                 && addedNode.GetBoolean();
            var removedImportant = tracking.TryGetProperty("removed_important", out var removedNode)
                                   && removedNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                                   && removedNode.GetBoolean();
            if (!shouldAffectMemory && !addedImportant && !removedImportant)
            {
                return string.Empty;
            }

            var classification = tracking.TryGetProperty("classification", out var classNode) && classNode.ValueKind == JsonValueKind.String
                ? classNode.GetString() ?? "unknown"
                : "unknown";
            var summary = tracking.TryGetProperty("summary", out var summaryNode) && summaryNode.ValueKind == JsonValueKind.String
                ? summaryNode.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = "Обнаружено значимое редактирование сообщения.";
            }

            return $"[edit_delta] type={classification}; summary={TruncateForContext(CollapseWhitespace(summary), 260)}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string BuildExtractionContextSnippet(string? cheapJson)
    {
        if (string.IsNullOrWhiteSpace(cheapJson))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(cheapJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var lines = new List<string>(4);
            lines.AddRange(CollectExtractionLines(root, "claims", 2, "claim"));
            lines.AddRange(CollectExtractionLines(root, "facts", 2, "fact"));
            if (lines.Count == 0)
            {
                return string.Empty;
            }

            return "[cheap_extraction] " + string.Join(" | ", lines);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> CollectExtractionLines(JsonElement root, string arrayName, int limit, string label)
    {
        if (!root.TryGetProperty(arrayName, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
        {
            return [];
        }

        var result = new List<string>(limit);
        foreach (var node in arr.EnumerateArray().Take(limit))
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var entity = GetString(node, "entity_name");
            var category = GetString(node, "category");
            var key = GetString(node, "key");
            var value = GetString(node, "value");
            var trustFactor = ResolveTrustFactor(node);
            if (string.IsNullOrWhiteSpace(entity) && string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add($"{label}:{entity}|{category}:{key}={value} (trust={trustFactor:0.00})");
        }

        return result;
    }

    private static float ResolveTrustFactor(JsonElement node)
    {
        if (node.TryGetProperty("trust_factor", out var trustNode) && trustNode.ValueKind == JsonValueKind.Number)
        {
            return Math.Clamp((float)trustNode.GetDouble(), 0f, 1f);
        }

        if (node.TryGetProperty("confidence", out var confNode) && confNode.ValueKind == JsonValueKind.Number)
        {
            return Math.Clamp((float)confNode.GetDouble(), 0f, 1f);
        }

        return 0f;
    }

    private static string GetString(JsonElement node, string fieldName)
    {
        if (!node.TryGetProperty(fieldName, out var field) || field.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return field.GetString()?.Trim() ?? string.Empty;
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

    private static string BuildMediaPresenceFallbackMarker(Message message)
    {
        if (message.MediaType == MediaType.None)
        {
            return string.Empty;
        }

        var hasText = !string.IsNullOrWhiteSpace(message.Text);
        var hasTranscription = !string.IsNullOrWhiteSpace(message.MediaTranscription);
        var hasDescription = !string.IsNullOrWhiteSpace(message.MediaDescription);
        if (hasText || hasTranscription || hasDescription)
        {
            return string.Empty;
        }

        return $"[media_present] type={message.MediaType}; content_unavailable=true";
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
