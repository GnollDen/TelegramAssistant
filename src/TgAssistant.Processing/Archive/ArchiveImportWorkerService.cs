using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive;

public class ArchiveImportWorkerService : BackgroundService
{
    private const string ExportPlaceholderMarker = "(File exceeds maximum size.";
    private const int JsonReadBufferSize = 64 * 1024;

    private readonly ArchiveImportSettings _settings;
    private readonly MediaSettings _mediaSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IArchiveImportRepository _archiveImportRepository;
    private readonly ILogger<ArchiveImportWorkerService> _logger;

    public ArchiveImportWorkerService(
        IOptions<ArchiveImportSettings> settings,
        IOptions<MediaSettings> mediaSettings,
        IMessageRepository messageRepository,
        IArchiveImportRepository archiveImportRepository,
        ILogger<ArchiveImportWorkerService> logger)
    {
        _settings = settings.Value;
        _mediaSettings = mediaSettings.Value;
        _messageRepository = messageRepository;
        _archiveImportRepository = archiveImportRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Archive import is disabled");
            return;
        }

        if (!File.Exists(_settings.SourcePath))
        {
            _logger.LogWarning("Archive file not found: {Path}", _settings.SourcePath);
            return;
        }

        ArchiveImportRun? run = null;
        try
        {
            var scan = await ScanArchiveAsync(_settings.SourcePath, stoppingToken);

            _logger.LogInformation(
                "Archive cost estimate: messages={Messages}, media={Media}, estimated_usd={Cost}",
                scan.CostEstimate.TotalMessages,
                scan.CostEstimate.MediaMessages,
                scan.CostEstimate.EstimatedCostUsd);

            var latestRun = await _archiveImportRepository.GetLatestRunAsync(_settings.SourcePath, stoppingToken);
            if (latestRun is { Status: ArchiveImportRunStatus.Completed }
                && latestRun.ImportedMessages >= latestRun.TotalMessages
                && latestRun.TotalMessages == scan.CostEstimate.TotalMessages)
            {
                _logger.LogInformation("Archive import already completed for this source. Skipping re-import.");
                return;
            }

            if (_settings.RequireCostConfirmation && !_settings.ConfirmProcessing)
            {
                await _archiveImportRepository.UpsertEstimateAsync(
                    _settings.SourcePath,
                    scan.CostEstimate,
                    ArchiveImportRunStatus.AwaitingConfirmation,
                    stoppingToken);

                _logger.LogWarning(
                    "Archive import paused awaiting confirmation. Set ArchiveImport__ConfirmProcessing=true to continue. Estimated cost: {Cost} USD",
                    scan.CostEstimate.EstimatedCostUsd);
                return;
            }

            run = await _archiveImportRepository.GetRunningRunAsync(_settings.SourcePath, stoppingToken)
                ?? await _archiveImportRepository.CreateRunAsync(new ArchiveImportRun
                {
                    SourcePath = _settings.SourcePath,
                    Status = ArchiveImportRunStatus.Running,
                    LastMessageIndex = -1,
                    ImportedMessages = 0,
                    QueuedMedia = 0,
                    TotalMessages = scan.CostEstimate.TotalMessages,
                    TotalMedia = scan.CostEstimate.MediaMessages,
                    EstimatedCostUsd = scan.CostEstimate.EstimatedCostUsd
                }, stoppingToken);

            var startIndex = run.LastMessageIndex + 1;
            if (startIndex > scan.LastMessageIndex)
            {
                await _archiveImportRepository.CompleteRunAsync(run.Id, ArchiveImportRunStatus.Completed, null, stoppingToken);
                _logger.LogInformation("Archive import already complete for {Path}", _settings.SourcePath);
                return;
            }

            _logger.LogInformation("Starting archive import from index {StartIndex}", startIndex);

            var buffer = new List<Message>(_settings.BatchSize);
            var importedMessages = run.ImportedMessages;
            var queuedMedia = run.QueuedMedia;
            var lastIndex = run.LastMessageIndex;

            await ParseArchiveMessagesAsync(
                _settings.SourcePath,
                async (item, ct) =>
                {
                    if (item.Index < startIndex)
                    {
                        return;
                    }

                    var mediaPath = ResolveMediaPath(item.RelativeMediaPath);
                    var hasMediaPath = item.MediaType != MediaType.None && !string.IsNullOrWhiteSpace(mediaPath);
                    var isPlaceholderPath = IsExportPlaceholderPath(item.RelativeMediaPath);
                    var isProcessableMediaType = hasMediaPath && IsProcessableMedia(item.MediaType);
                    var isBurstSkippedPhoto = scan.BurstSkipIndices.Contains(item.Index);
                    var isUnsupportedExt = hasMediaPath && IsUnsupportedArchiveExtension(mediaPath!);
                    var fileExists = hasMediaPath && !isPlaceholderPath && File.Exists(mediaPath!);

                    var shouldQueue = hasMediaPath
                                      && isProcessableMediaType
                                      && !isBurstSkippedPhoto
                                      && !isUnsupportedExt
                                      && !isPlaceholderPath
                                      && fileExists;

                    buffer.Add(new Message
                    {
                        TelegramMessageId = item.MessageId,
                        ChatId = item.ChatId,
                        SenderId = item.SenderId,
                        SenderName = item.SenderName,
                        Timestamp = item.Timestamp,
                        Text = item.Text,
                        MediaType = hasMediaPath ? item.MediaType : MediaType.None,
                        MediaPath = hasMediaPath ? mediaPath : null,
                        MediaDescription = BuildArchiveMediaDescription(
                            item.MediaType,
                            hasMediaPath,
                            isProcessableMediaType,
                            isBurstSkippedPhoto,
                            isUnsupportedExt,
                            isPlaceholderPath,
                            fileExists),
                        ReplyToMessageId = item.ReplyToMessageId,
                        ForwardJson = item.ForwardJson,
                        Source = MessageSource.Archive,
                        ProcessingStatus = shouldQueue ? ProcessingStatus.Pending : hasMediaPath ? ProcessingStatus.PendingReview : ProcessingStatus.Processed,
                        ProcessedAt = shouldQueue ? null : DateTime.UtcNow
                    });

                    lastIndex = item.Index;

                    if (buffer.Count >= _settings.BatchSize)
                    {
                        await FlushAsync(buffer, ct);
                        importedMessages += buffer.Count;
                        queuedMedia += buffer.Count(ShouldQueueForMediaProcessing);
                        await _archiveImportRepository.UpdateProgressAsync(run.Id, lastIndex, importedMessages, queuedMedia, ct);
                        _logger.LogInformation("Archive import progress: {Imported}/{Total}", importedMessages, scan.CostEstimate.TotalMessages);
                        buffer.Clear();
                    }
                },
                stoppingToken);

            if (buffer.Count > 0)
            {
                await FlushAsync(buffer, stoppingToken);
                importedMessages += buffer.Count;
                queuedMedia += buffer.Count(ShouldQueueForMediaProcessing);
                await _archiveImportRepository.UpdateProgressAsync(run.Id, lastIndex, importedMessages, queuedMedia, stoppingToken);
            }

            await _archiveImportRepository.CompleteRunAsync(run.Id, ArchiveImportRunStatus.Completed, null, stoppingToken);
            _logger.LogInformation("Archive import completed: {Imported} messages, {Media} media queued", importedMessages, queuedMedia);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Archive import cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive import failed");
            if (run != null)
            {
                await _archiveImportRepository.CompleteRunAsync(run.Id, ArchiveImportRunStatus.Failed, ex.Message, CancellationToken.None);
            }
        }
    }

    private async Task<ArchiveScanSummary> ScanArchiveAsync(string archiveJsonPath, CancellationToken ct)
    {
        var estimate = new ArchiveCostEstimate();
        var burstSkipIndices = new HashSet<int>();
        var byChat = new Dictionary<long, Queue<ArchiveMessageRecord>>();

        var threshold = Math.Max(1, _mediaSettings.PhotoBurstThreshold);
        var keepCount = Math.Max(0, _mediaSettings.PhotoBurstKeepCount);
        var window = TimeSpan.FromSeconds(Math.Max(1, _mediaSettings.PhotoBurstWindowSeconds));
        var lastMessageIndex = -1;

        var (chatName, chatId) = await ParseArchiveMessagesAsync(
            archiveJsonPath,
            (item, _) =>
            {
                lastMessageIndex = item.Index;
                UpdateEstimate(estimate, item.MediaType);
                estimate.TotalMessages++;
                UpdatePhotoBurstSkipIndices(item, byChat, burstSkipIndices, threshold, keepCount, window);
                return ValueTask.CompletedTask;
            },
            ct);

        estimate.EstimatedCostUsd = EstimateCost(estimate);
        return new ArchiveScanSummary(chatName, chatId, estimate, burstSkipIndices, lastMessageIndex);
    }

    private async Task<(string ChatName, long ChatId)> ParseArchiveMessagesAsync(
        string archiveJsonPath,
        Func<ArchiveMessageRecord, CancellationToken, ValueTask> onMessage,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(archiveJsonPath);
        var buffer = ArrayPool<byte>.Shared.Rent(JsonReadBufferSize);
        var state = new JsonReaderState(new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var bufferedBytes = 0;
        var isFinalBlock = false;
        var inMessagesArray = false;
        var messagesArrayDepth = -1;
        var messageIndex = -1;
        var chatName = "Unknown chat";
        var chatId = 0L;

        try
        {
            while (!isFinalBlock)
            {
                if (bufferedBytes == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, larger, 0, bufferedBytes);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                var bytesRead = await stream.ReadAsync(buffer.AsMemory(bufferedBytes), ct);
                isFinalBlock = bytesRead == 0;

                var dataLength = bufferedBytes + bytesRead;
                var reader = new Utf8JsonReader(
                    new ReadOnlySpan<byte>(buffer, 0, dataLength),
                    isFinalBlock,
                    state);
                var parsedBatch = new List<ArchiveMessageRecord>();

                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();

                    if (inMessagesArray)
                    {
                        if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == messagesArrayDepth)
                        {
                            inMessagesArray = false;
                            continue;
                        }

                        if (reader.CurrentDepth == messagesArrayDepth + 1)
                        {
                            messageIndex++;
                            using var valueDoc = JsonDocument.ParseValue(ref reader);
                            if (valueDoc.RootElement.ValueKind == JsonValueKind.Object
                                && TryParseMessageRecord(valueDoc.RootElement, messageIndex, chatId, out var item))
                            {
                                parsedBatch.Add(item);
                            }
                        }

                        continue;
                    }

                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    var propertyName = reader.GetString();
                    if (string.Equals(propertyName, "name", StringComparison.Ordinal))
                    {
                        if (!reader.Read())
                        {
                            break;
                        }

                        if (reader.TokenType == JsonTokenType.String)
                        {
                            chatName = reader.GetString() ?? "Unknown chat";
                        }
                        else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        {
                            using var _ = JsonDocument.ParseValue(ref reader);
                        }
                    }
                    else if (string.Equals(propertyName, "id", StringComparison.Ordinal))
                    {
                        if (!reader.Read())
                        {
                            break;
                        }

                        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var idAsNumber))
                        {
                            chatId = idAsNumber;
                        }
                        else if (reader.TokenType == JsonTokenType.String)
                        {
                            var raw = reader.GetString();
                            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idAsString))
                            {
                                chatId = idAsString;
                            }
                        }
                        else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        {
                            using var _ = JsonDocument.ParseValue(ref reader);
                        }
                    }
                    else if (string.Equals(propertyName, "messages", StringComparison.Ordinal))
                    {
                        if (!reader.Read())
                        {
                            break;
                        }

                        if (reader.TokenType == JsonTokenType.StartArray)
                        {
                            inMessagesArray = true;
                            messagesArrayDepth = reader.CurrentDepth;
                        }
                        else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                        {
                            using var _ = JsonDocument.ParseValue(ref reader);
                        }
                    }
                }

                var consumed = (int)reader.BytesConsumed;
                var remaining = dataLength - consumed;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(buffer, consumed, buffer, 0, remaining);
                }

                bufferedBytes = remaining;
                state = reader.CurrentState;

                foreach (var item in parsedBatch)
                {
                    await onMessage(item, ct);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return (chatName, chatId);
    }

    private static bool TryParseMessageRecord(JsonElement node, int index, long chatId, out ArchiveMessageRecord item)
    {
        item = null!;

        if (node.TryGetProperty("type", out var typeNode)
            && typeNode.GetString() is string nodeType
            && !string.Equals(nodeType, "message", StringComparison.Ordinal))
        {
            return false;
        }

        var messageId = node.TryGetProperty("id", out var idNode) && idNode.TryGetInt64(out var idValue)
            ? idValue
            : 0;
        if (messageId <= 0)
        {
            return false;
        }

        item = new ArchiveMessageRecord
        {
            Index = index,
            MessageId = messageId,
            ChatId = chatId,
            SenderId = ParseSenderId(node),
            SenderName = node.TryGetProperty("from", out var fromNode) ? fromNode.GetString() ?? string.Empty : string.Empty,
            Timestamp = ParseTimestamp(node),
            Text = ExtractText(node),
            MediaType = DetectMediaType(node),
            RelativeMediaPath = ExtractMediaPath(node),
            ReplyToMessageId = node.TryGetProperty("reply_to_message_id", out var replyNode) && replyNode.TryGetInt64(out var replyId) ? replyId : null,
            ForwardJson = ExtractForwardJson(node)
        };
        return true;
    }

    private async Task FlushAsync(List<Message> buffer, CancellationToken ct)
    {
        await _messageRepository.SaveBatchAsync(buffer, ct);
    }

    private string? ResolveMediaPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? normalized
            : Path.GetFullPath(Path.Combine(_settings.MediaBasePath, normalized));
    }

    private static bool IsProcessableMedia(MediaType type)
    {
        return type is MediaType.Photo or MediaType.Sticker or MediaType.Animation or MediaType.Voice or MediaType.Video or MediaType.VideoNote;
    }

    private static bool IsUnsupportedArchiveExtension(string mediaPath)
    {
        var ext = Path.GetExtension(mediaPath).ToLowerInvariant();
        return ext is ".tgs";
    }

    private static bool IsExportPlaceholderPath(string? relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath)
               && relativePath.Contains(ExportPlaceholderMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildArchiveMediaDescription(
        MediaType mediaType,
        bool hasMedia,
        bool isProcessableMedia,
        bool isBurstSkippedPhoto,
        bool isUnsupportedExt,
        bool isPlaceholderPath,
        bool fileExists)
    {
        if (!hasMedia)
        {
            return null;
        }

        if (isPlaceholderPath)
        {
            return "Skipped: Telegram export placeholder (file not downloaded)";
        }

        if (!fileExists)
        {
            return "Skipped: archive media file missing";
        }

        if (isBurstSkippedPhoto)
        {
            return "Skipped by burst policy: high-volume photo forwarding";
        }

        if (isUnsupportedExt)
        {
            return "Skipped by policy: unsupported archive media extension";
        }

        if (!isProcessableMedia)
        {
            return $"Skipped by policy: unsupported archive media type {mediaType}";
        }

        return null;
    }

    private bool ShouldQueueForMediaProcessing(Message message)
    {
        return message.ProcessingStatus == ProcessingStatus.Pending
               && message.MediaType != MediaType.None
               && !string.IsNullOrWhiteSpace(message.MediaPath);
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
        var imageCost = estimate.ImageLikeMedia * 0.00008m;
        var audioCost = estimate.AudioLikeMedia * 0.0005m;
        var videoCost = estimate.VideoLikeMedia * 0.0008m;
        return Math.Round(imageCost + audioCost + videoCost, 2, MidpointRounding.AwayFromZero);
    }

    private static void UpdatePhotoBurstSkipIndices(
        ArchiveMessageRecord item,
        Dictionary<long, Queue<ArchiveMessageRecord>> byChat,
        HashSet<int> skip,
        int threshold,
        int keepCount,
        TimeSpan window)
    {
        if (item.MediaType != MediaType.Photo || string.IsNullOrWhiteSpace(item.RelativeMediaPath))
        {
            return;
        }

        if (!byChat.TryGetValue(item.ChatId, out var queue))
        {
            queue = new Queue<ArchiveMessageRecord>();
            byChat[item.ChatId] = queue;
        }

        while (queue.Count > 0 && item.Timestamp - queue.Peek().Timestamp > window)
        {
            queue.Dequeue();
        }

        queue.Enqueue(item);
        if (queue.Count < threshold)
        {
            return;
        }

        foreach (var toSkip in queue.Skip(keepCount))
        {
            skip.Add(toSkip.Index);
        }
    }

    private sealed record ArchiveScanSummary(
        string ChatName,
        long ChatId,
        ArchiveCostEstimate CostEstimate,
        HashSet<int> BurstSkipIndices,
        int LastMessageIndex);
}
