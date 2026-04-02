using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Workers;

public class BatchWorkerService : BackgroundService
{
    private readonly IMessageQueue _queue;
    private readonly IMessageRepository _messageRepo;
    private readonly IRealtimeMessageSubstrateRepository _realtimeMessageSubstrateRepository;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly BatchWorkerSettings _settings;
    private readonly MediaSettings _mediaSettings;
    private readonly ILogger<BatchWorkerService> _logger;
    private readonly Dictionary<long, ChatBuffer> _buffers = new();

    public BatchWorkerService(
        IMessageQueue queue,
        IMessageRepository messageRepo,
        IRealtimeMessageSubstrateRepository realtimeMessageSubstrateRepository,
        IMediaProcessor mediaProcessor,
        IOptions<BatchWorkerSettings> settings,
        IOptions<MediaSettings> mediaSettings,
        ILogger<BatchWorkerService> logger)
    {
        _queue = queue;
        _messageRepo = messageRepo;
        _realtimeMessageSubstrateRepository = realtimeMessageSubstrateRepository;
        _mediaProcessor = mediaProcessor;
        _settings = settings.Value;
        _mediaSettings = mediaSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Batch worker starting...");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _queue.DequeueAsync(50, TimeSpan.FromSeconds(2), stoppingToken);
                var invalidStreamIds = new List<string>();
                foreach (var msg in messages)
                {
                    if (msg.ChatId <= 0)
                    {
                        invalidStreamIds.Add(msg.StreamId);
                        continue;
                    }

                    if (!_buffers.ContainsKey(msg.ChatId))
                    {
                        _buffers[msg.ChatId] = new ChatBuffer();
                    }

                    _buffers[msg.ChatId].Add(msg);
                }

                if (invalidStreamIds.Count > 0)
                {
                    _logger.LogWarning(
                        "Dropped realtime messages with invalid chat_id<=0: count={Count}",
                        invalidStreamIds.Count);
                    await _queue.AcknowledgeAsync(invalidStreamIds, stoppingToken);
                }

                var now = DateTime.UtcNow;
                var ready = _buffers.Where(kvp => ShouldFlush(kvp.Value, now)).Select(kvp => kvp.Key).ToList();
                foreach (var chatId in ready)
                {
                    await FlushBatchAsync(chatId, _buffers[chatId], stoppingToken);
                    _buffers.Remove(chatId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch worker loop");
                await Task.Delay(1000, stoppingToken);
            }
        }

        foreach (var (chatId, buffer) in _buffers)
        {
            await FlushBatchAsync(chatId, buffer, CancellationToken.None);
        }
    }

    private bool ShouldFlush(ChatBuffer buffer, DateTime now)
    {
        if (buffer.Messages.Count == 0)
        {
            return false;
        }

        if (buffer.Messages.Count >= _settings.MaxBatchSize)
        {
            return true;
        }

        if ((now - buffer.FirstMessageAt).TotalSeconds >= _settings.MaxBatchTimeSeconds)
        {
            return true;
        }

        if ((now - buffer.LastMessageAt).TotalSeconds >= _settings.SilenceTimeoutSeconds)
        {
            return true;
        }

        return false;
    }

    private async Task FlushBatchAsync(long chatId, ChatBuffer buffer, CancellationToken ct)
    {
        if (chatId <= 0)
        {
            _logger.LogWarning(
                "Dropped buffered batch with invalid chat_id={ChatId}, count={Count}",
                chatId,
                buffer.Messages.Count);
            await _queue.AcknowledgeAsync(buffer.Messages.Select(x => x.StreamId), ct);
            return;
        }

        _logger.LogInformation("Flushing batch: chat {ChatId}, {Count} messages", chatId, buffer.Messages.Count);

        var dbMessages = new List<Message>();
        var streamIds = buffer.Messages
            .Select(x => x.StreamId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var uniqueMessages = DeduplicateByLogicalKey(buffer.Messages);
        var duplicateCount = buffer.Messages.Count - uniqueMessages.Count;
        if (duplicateCount > 0)
        {
            _logger.LogWarning(
                "Detected duplicate logical messages in realtime batch: chat_id={ChatId}, input_count={InputCount}, unique_count={UniqueCount}, duplicate_count={DuplicateCount}",
                chatId,
                buffer.Messages.Count,
                uniqueMessages.Count,
                duplicateCount);
        }

        var skippedPhotoIds = BuildPhotoBurstSkipSet(uniqueMessages);
        if (skippedPhotoIds.Count > 0)
        {
            _logger.LogInformation(
                "Photo burst policy applied for chat {ChatId}: skipped {Skipped} items",
                chatId,
                skippedPhotoIds.Count);
        }

        var mediaTasks = uniqueMessages
            .Where(m => m.MediaType != MediaType.None && m.MediaPath != null && !skippedPhotoIds.Contains(m.MessageId))
            .Select(async msg => (msg, result: await _mediaProcessor.ProcessAsync(msg.MediaPath!, msg.MediaType, ct)))
            .ToList();

        var mediaResults = await Task.WhenAll(mediaTasks);
        var mediaLookup = mediaResults
            .GroupBy(r => r.msg.MessageId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.msg.EditTimestamp ?? DateTime.MinValue)
                    .ThenByDescending(x => x.msg.Timestamp)
                    .Select(x => x.result)
                    .First());

        foreach (var raw in uniqueMessages)
        {
            var dbMsg = new Message
            {
                TelegramMessageId = raw.MessageId,
                ChatId = raw.ChatId,
                SenderId = raw.SenderId,
                SenderName = raw.SenderName,
                Timestamp = raw.Timestamp,
                Text = raw.Text,
                MediaType = raw.MediaType,
                MediaPath = raw.MediaPath,
                ReplyToMessageId = raw.ReplyToMessageId,
                EditTimestamp = raw.EditTimestamp,
                ReactionsJson = raw.ReactionsJson,
                ForwardJson = raw.ForwardJson,
                Source = MessageSource.Realtime,
                ProcessingStatus = ProcessingStatus.Processed,
                ProcessedAt = DateTime.UtcNow
            };

            if (skippedPhotoIds.Contains(raw.MessageId))
            {
                dbMsg.MediaDescription = "Skipped by burst policy: high-volume photo forwarding";
            }
            else if (mediaLookup.TryGetValue(raw.MessageId, out var mediaResult))
            {
                if (mediaResult.Success)
                {
                    dbMsg.MediaTranscription = mediaResult.Transcription;
                    dbMsg.MediaDescription = mediaResult.Description;
                }
                else
                {
                    dbMsg.ProcessingStatus = ProcessingStatus.PendingReview;
                    dbMsg.MediaDescription = $"Unrecognized: {mediaResult.FailureReason}";
                }
            }

            dbMessages.Add(dbMsg);
        }

        await _messageRepo.SaveBatchAsync(dbMessages, ct);
        var persistedByTelegramMessageId = await _messageRepo.GetByTelegramMessageIdsAsync(
            chatId,
            MessageSource.Realtime,
            uniqueMessages.Select(x => x.MessageId).Distinct().ToArray(),
            ct);
        if (persistedByTelegramMessageId.Count != uniqueMessages.Count)
        {
            _logger.LogWarning(
                "Realtime batch persisted with missing canonical message lookups: chat_id={ChatId}, expected={ExpectedCount}, resolved={ResolvedCount}",
                chatId,
                uniqueMessages.Count,
                persistedByTelegramMessageId.Count);
        }

        await _realtimeMessageSubstrateRepository.UpsertRealtimeBatchAsync(
            persistedByTelegramMessageId.Values.ToList(),
            ct);
        await _queue.AcknowledgeAsync(streamIds, ct);
        _logger.LogInformation("Batch flushed: chat {ChatId}, {Count} saved", chatId, dbMessages.Count);
    }

    private static List<RawTelegramMessage> DeduplicateByLogicalKey(List<RawTelegramMessage> messages)
    {
        return messages
            .GroupBy(x => BuildLogicalMessageKey(x.ChatId, x.MessageId), StringComparer.Ordinal)
            .Select(g => SelectPreferredMessage(g))
            .ToList();
    }

    private static string BuildLogicalMessageKey(long chatId, long messageId)
        => $"{chatId}:{messageId}";

    private static RawTelegramMessage SelectPreferredMessage(IEnumerable<RawTelegramMessage> group)
    {
        return group
            .OrderByDescending(x => x.EditTimestamp ?? DateTime.MinValue)
            .ThenByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.MediaType != MediaType.None && !string.IsNullOrWhiteSpace(x.MediaPath))
            .ThenByDescending(x => x.Text?.Length ?? 0)
            .First();
    }

    private HashSet<long> BuildPhotoBurstSkipSet(List<RawTelegramMessage> messages)
    {
        var skip = new HashSet<long>();

        if (!_mediaSettings.EnablePhotoBurstGuard)
        {
            return skip;
        }

        var photoMessages = messages
            .Where(m => m.MediaType == MediaType.Photo && !string.IsNullOrWhiteSpace(m.MediaPath))
            .ToList();

        if (photoMessages.Count < _mediaSettings.PhotoBurstThreshold)
        {
            return skip;
        }

        var keepCount = Math.Max(0, _mediaSettings.PhotoBurstKeepCount);
        foreach (var msg in photoMessages.Skip(keepCount))
        {
            skip.Add(msg.MessageId);
        }

        return skip;
    }

    private class ChatBuffer
    {
        public List<RawTelegramMessage> Messages { get; } = new();
        public DateTime FirstMessageAt { get; private set; }
        public DateTime LastMessageAt { get; private set; }

        public void Add(RawTelegramMessage msg)
        {
            if (Messages.Count == 0)
            {
                FirstMessageAt = DateTime.UtcNow;
            }

            LastMessageAt = DateTime.UtcNow;
            Messages.Add(msg);
        }
    }
}
