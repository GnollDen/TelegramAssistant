using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Workers;

/// <summary>
/// Reads messages from Redis Stream, groups into batches by chat_id,
/// processes media through Gemini, writes to PostgreSQL.
/// </summary>
public class BatchWorkerService : BackgroundService
{
    private readonly IMessageQueue _queue;
    private readonly IMessageRepository _messageRepo;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly BatchWorkerSettings _settings;
    private readonly ILogger<BatchWorkerService> _logger;

    // In-memory buffer: chat_id -> list of messages
    private readonly Dictionary<long, ChatBuffer> _buffers = new();

    public BatchWorkerService(
        IMessageQueue queue,
        IMessageRepository messageRepo,
        IMediaProcessor mediaProcessor,
        IOptions<BatchWorkerSettings> settings,
        ILogger<BatchWorkerService> logger)
    {
        _queue = queue;
        _messageRepo = messageRepo;
        _mediaProcessor = mediaProcessor;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Batch worker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Read from Redis (block up to 2 seconds)
                var messages = await _queue.DequeueAsync(
                    maxCount: 50,
                    timeout: TimeSpan.FromSeconds(2),
                    ct: stoppingToken);

                // Add to buffers
                foreach (var msg in messages)
                {
                    if (!_buffers.ContainsKey(msg.ChatId))
                        _buffers[msg.ChatId] = new ChatBuffer();
                    
                    _buffers[msg.ChatId].Add(msg);
                }

                // Check which buffers are ready to flush
                var now = DateTime.UtcNow;
                var readyChats = _buffers
                    .Where(kvp => ShouldFlush(kvp.Value, now))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var chatId in readyChats)
                {
                    var buffer = _buffers[chatId];
                    await FlushBatchAsync(chatId, buffer, stoppingToken);
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

        // Flush remaining buffers on shutdown
        foreach (var (chatId, buffer) in _buffers)
        {
            await FlushBatchAsync(chatId, buffer, CancellationToken.None);
        }
    }

    private bool ShouldFlush(ChatBuffer buffer, DateTime now)
    {
        if (buffer.Messages.Count == 0) return false;
        
        // Trigger 1: max batch size reached
        if (buffer.Messages.Count >= _settings.MaxBatchSize) return true;
        
        // Trigger 2: max time since first message
        if ((now - buffer.FirstMessageAt).TotalSeconds >= _settings.MaxBatchTimeSeconds) return true;
        
        // Trigger 3: silence timeout
        if ((now - buffer.LastMessageAt).TotalSeconds >= _settings.SilenceTimeoutSeconds) return true;
        
        return false;
    }

    private async Task FlushBatchAsync(long chatId, ChatBuffer buffer, CancellationToken ct)
    {
        _logger.LogInformation(
            "Flushing batch: chat {ChatId}, {Count} messages",
            chatId, buffer.Messages.Count);

        var dbMessages = new List<Message>();
        var streamIds = new List<string>();

        // Process media in parallel
        var mediaTasks = buffer.Messages
            .Where(m => m.MediaType != MediaType.None && m.MediaPath != null)
            .Select(async msg =>
            {
                var result = await _mediaProcessor.ProcessAsync(msg.MediaPath!, msg.MediaType, ct);
                return (msg, result);
            })
            .ToList();

        var mediaResults = await Task.WhenAll(mediaTasks);
        var mediaLookup = mediaResults.ToDictionary(r => r.msg.MessageId, r => r.result);

        // Build DB records
        foreach (var raw in buffer.Messages)
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
                Source = MessageSource.Realtime,
                ProcessingStatus = ProcessingStatus.Processed,
                ProcessedAt = DateTime.UtcNow
            };

            // Apply media processing results
            if (mediaLookup.TryGetValue(raw.MessageId, out var mediaResult))
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
            streamIds.Add(raw.StreamId);
        }

        // Write to DB
        await _messageRepo.SaveBatchAsync(dbMessages, ct);

        // ACK Redis
        await _queue.AcknowledgeAsync(streamIds, ct);

        _logger.LogInformation("Batch flushed: chat {ChatId}, {Count} messages saved", chatId, dbMessages.Count);
    }

    private class ChatBuffer
    {
        public List<RawTelegramMessage> Messages { get; } = new();
        public DateTime FirstMessageAt { get; private set; }
        public DateTime LastMessageAt { get; private set; }

        public void Add(RawTelegramMessage msg)
        {
            if (Messages.Count == 0)
                FirstMessageAt = DateTime.UtcNow;
            
            LastMessageAt = DateTime.UtcNow;
            Messages.Add(msg);
        }
    }
}
