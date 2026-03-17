using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive;

public class TelegramDesktopArchiveParser
{
    private readonly ILogger<TelegramDesktopArchiveParser> _logger;

    public TelegramDesktopArchiveParser(ILogger<TelegramDesktopArchiveParser> logger)
    {
        _logger = logger;
    }

    public async Task<ArchiveParseResult> ParseAsync(string archiveJsonPath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(archiveJsonPath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        var chatName = root.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "Unknown chat" : "Unknown chat";
        var chatId = ParseChatId(root);
        if (chatId <= 0)
        {
            _logger.LogWarning("Archive parsed with invalid chat_id={ChatId}. Returning empty result", chatId);
            return new ArchiveParseResult(chatName, chatId, [], new ArchiveCostEstimate());
        }

        var messages = new List<ArchiveMessageRecord>();
        var estimate = new ArchiveCostEstimate();

        if (!root.TryGetProperty("messages", out var messagesNode) || messagesNode.ValueKind != JsonValueKind.Array)
        {
            return new ArchiveParseResult(chatName, chatId, messages, estimate);
        }

        var index = -1;
        foreach (var node in messagesNode.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            index++;

            if (node.TryGetProperty("type", out var typeNode) && typeNode.GetString() is string nodeType && nodeType != "message")
            {
                continue;
            }

            var messageId = node.TryGetProperty("id", out var idNode) && idNode.TryGetInt64(out var idValue)
                ? idValue
                : 0;

            if (messageId <= 0)
            {
                continue;
            }

            var text = ExtractText(node);
            var mediaType = DetectMediaType(node);
            var relativeMediaPath = ExtractMediaPath(node);

            var record = new ArchiveMessageRecord
            {
                Index = index,
                MessageId = messageId,
                ChatId = chatId,
                SenderId = ParseSenderId(node),
                SenderName = node.TryGetProperty("from", out var fromNode) ? fromNode.GetString() ?? string.Empty : string.Empty,
                Timestamp = ParseTimestamp(node),
                Text = text,
                MediaType = mediaType,
                RelativeMediaPath = relativeMediaPath,
                ReplyToMessageId = node.TryGetProperty("reply_to_message_id", out var replyNode) && replyNode.TryGetInt64(out var replyId) ? replyId : null,
                ForwardJson = ExtractForwardJson(node)
            };

            messages.Add(record);
            UpdateEstimate(estimate, record.MediaType);
            estimate.TotalMessages++;
        }

        estimate.EstimatedCostUsd = EstimateCost(estimate);

        _logger.LogInformation("Archive parsed: {ChatName} ({ChatId}), {Count} messages", chatName, chatId, messages.Count);

        return new ArchiveParseResult(chatName, chatId, messages, estimate);
    }

    private static long ParseChatId(JsonElement root)
    {
        if (root.TryGetProperty("id", out var idNode) && idNode.TryGetInt64(out var idValue))
        {
            return idValue;
        }

        return 0;
    }

    private static long ParseSenderId(JsonElement node)
    {
        if (!node.TryGetProperty("from_id", out var fromIdNode))
        {
            return 0;
        }

        var fromId = fromIdNode.GetString();
        if (string.IsNullOrWhiteSpace(fromId))
        {
            return 0;
        }

        var digits = new string(fromId.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var parsed) ? parsed : 0;
    }

    private static DateTime ParseTimestamp(JsonElement node)
    {
        if (node.TryGetProperty("date_unixtime", out var unixNode))
        {
            var unixRaw = unixNode.GetString();
            if (long.TryParse(unixRaw, out var unixTs))
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime;
            }
        }

        if (node.TryGetProperty("date", out var dateNode))
        {
            var dateRaw = dateNode.GetString();
            if (DateTime.TryParse(dateRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return DateTime.UtcNow;
    }

    private static string? ExtractText(JsonElement node)
    {
        if (!node.TryGetProperty("text", out var textNode))
        {
            return null;
        }

        return textNode.ValueKind switch
        {
            JsonValueKind.String => textNode.GetString(),
            JsonValueKind.Array => FlattenTextArray(textNode),
            _ => null
        };
    }

    private static string? FlattenTextArray(JsonElement textNode)
    {
        var sb = new StringBuilder();
        foreach (var segment in textNode.EnumerateArray())
        {
            switch (segment.ValueKind)
            {
                case JsonValueKind.String:
                    sb.Append(segment.GetString());
                    break;
                case JsonValueKind.Object:
                    if (segment.TryGetProperty("text", out var richText))
                    {
                        sb.Append(richText.GetString());
                    }
                    break;
            }
        }

        var text = sb.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static MediaType DetectMediaType(JsonElement node)
    {
        if (node.TryGetProperty("media_type", out var mediaTypeNode))
        {
            var mediaType = mediaTypeNode.GetString()?.ToLowerInvariant();
            return mediaType switch
            {
                "photo" => MediaType.Photo,
                "video_file" => MediaType.Video,
                "video_message" => MediaType.VideoNote,
                "voice_message" => MediaType.Voice,
                "sticker" => MediaType.Sticker,
                "animation" => MediaType.Animation,
                "file" => MediaType.Document,
                _ => MediaType.None
            };
        }

        var path = ExtractMediaPath(node);
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaType.None;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => MediaType.Photo,
            ".ogg" or ".mp3" or ".wav" or ".m4a" => MediaType.Voice,
            ".mp4" or ".mov" or ".mkv" => MediaType.Video,
            ".gif" => MediaType.Animation,
            _ => MediaType.Document
        };
    }

    private static string? ExtractMediaPath(JsonElement node)
    {
        if (node.TryGetProperty("photo", out var photoNode))
        {
            return photoNode.GetString();
        }

        if (node.TryGetProperty("file", out var fileNode))
        {
            return fileNode.GetString();
        }

        return null;
    }

    private static string? ExtractForwardJson(JsonElement node)
    {
        if (!node.TryGetProperty("forwarded_from", out var forwardNode))
        {
            return null;
        }

        var value = forwardNode.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return JsonSerializer.Serialize(new { from_name = value });
    }

    private static void UpdateEstimate(ArchiveCostEstimate estimate, MediaType mediaType)
    {
        if (mediaType == MediaType.None)
        {
            return;
        }

        estimate.MediaMessages++;
        switch (mediaType)
        {
            case MediaType.Photo:
            case MediaType.Sticker:
            case MediaType.Animation:
                estimate.ImageLikeMedia++;
                break;
            case MediaType.Voice:
                estimate.AudioLikeMedia++;
                break;
            case MediaType.Video:
            case MediaType.VideoNote:
                estimate.VideoLikeMedia++;
                break;
        }
    }

    private static decimal EstimateCost(ArchiveCostEstimate estimate)
    {
        // Conservative rough estimate for planning before import.
        var imageCost = estimate.ImageLikeMedia * 0.00008m;
        var audioCost = estimate.AudioLikeMedia * 0.0005m;
        var videoCost = estimate.VideoLikeMedia * 0.0008m;
        return Math.Round(imageCost + audioCost + videoCost, 2, MidpointRounding.AwayFromZero);
    }
}

public sealed record ArchiveParseResult(string ChatName, long ChatId, IReadOnlyList<ArchiveMessageRecord> Messages, ArchiveCostEstimate CostEstimate);

public class ArchiveMessageRecord
{
    public int Index { get; set; }
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Text { get; set; }
    public MediaType MediaType { get; set; }
    public string? RelativeMediaPath { get; set; }
    public long? ReplyToMessageId { get; set; }
    public string? ForwardJson { get; set; }
}
