using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Redis;

public class RedisMessageQueue : IMessageQueue
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisSettings _settings;
    private readonly ILogger<RedisMessageQueue> _logger;
    private readonly Queue<RawTelegramMessage> _reclaimedBuffer = new();
    private readonly object _reclaimedLock = new();
    private DateTime _lastReclaimAtUtc = DateTime.MinValue;
    private DateTime _lastPendingMetricsAtUtc = DateTime.MinValue;

    public RedisMessageQueue(IConnectionMultiplexer redis, IOptions<RedisSettings> settings, ILogger<RedisMessageQueue> logger)
    {
        _redis = redis;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var db = _redis.GetDatabase();
        try
        {
            await db.StreamCreateConsumerGroupAsync(_settings.StreamName, _settings.ConsumerGroup, StreamPosition.NewMessages, createStream: true);
            _logger.LogInformation("Created consumer group {Group} on stream {Stream}", _settings.ConsumerGroup, _settings.StreamName);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            _logger.LogDebug("Consumer group {Group} already exists", _settings.ConsumerGroup);
        }

        if (_settings.EnablePendingReclaim)
        {
            await ReclaimPendingAsync(db, runReclaim: true, logPendingMetrics: true);
        }
    }

    public async Task EnqueueAsync(RawTelegramMessage message, CancellationToken ct = default)
    {
        if (ShouldDropAsNoise(message))
        {
            _logger.LogDebug(
                "Dropped noisy realtime envelope before Redis enqueue. msg_id={MessageId}, chat_id={ChatId}, text={Text}",
                message.MessageId,
                message.ChatId,
                message.Text);
            return;
        }

        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(message);
        var id = await db.StreamAddAsync(_settings.StreamName, new NameValueEntry[] { new("data", json) });
        _logger.LogDebug("Enqueued message {MsgId} to stream as {StreamId}", message.MessageId, id);
    }

    private static bool ShouldDropAsNoise(RawTelegramMessage message)
    {
        if (message.ChatId <= 0)
        {
            return true;
        }

        var text = message.Text?.Trim();
        var hasText = !string.IsNullOrWhiteSpace(text);
        var hasMedia = message.MediaType != MediaType.None || !string.IsNullOrWhiteSpace(message.MediaPath);
        var hasReactions = !string.IsNullOrWhiteSpace(message.ReactionsJson);
        var hasForward = !string.IsNullOrWhiteSpace(message.ForwardJson);
        if (!hasText && !hasMedia && !hasReactions && !hasForward)
        {
            return true;
        }

        return false;
    }

    public async Task<List<RawTelegramMessage>> DequeueAsync(int maxCount, TimeSpan timeout, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var results = new List<RawTelegramMessage>();
        await RunPendingMaintenanceIfDueAsync(db);

        DrainReclaimedMessages(results, maxCount);
        if (results.Count >= maxCount)
        {
            return results;
        }

        var readCount = Math.Max(1, maxCount - results.Count);
        var entries = await db.StreamReadGroupAsync(_settings.StreamName, _settings.ConsumerGroup, _settings.ConsumerName, position: StreamPosition.NewMessages, count: readCount);
        if (entries.Length == 0 && results.Count == 0)
        {
            await Task.Delay(timeout, ct);
            return results;
        }

        DeserializeAndAppendEntries(entries, results);
        return results;
    }

    public async Task AcknowledgeAsync(IEnumerable<string> messageIds, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ids = messageIds.Select(id => (RedisValue)id).ToArray();
        await db.StreamAcknowledgeAsync(_settings.StreamName, _settings.ConsumerGroup, ids);
    }

    private async Task RunPendingMaintenanceIfDueAsync(IDatabase db)
    {
        if (!_settings.EnablePendingReclaim)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var reclaimInterval = Math.Max(1, _settings.PendingReclaimIntervalSeconds);
        var metricsInterval = Math.Max(1, _settings.PendingMetricsLogIntervalSeconds);
        var reclaimDue = (now - _lastReclaimAtUtc).TotalSeconds >= reclaimInterval;
        var metricsDue = (now - _lastPendingMetricsAtUtc).TotalSeconds >= metricsInterval;
        if (!reclaimDue && !metricsDue)
        {
            return;
        }

        await ReclaimPendingAsync(db, runReclaim: reclaimDue, logPendingMetrics: metricsDue);
    }

    private async Task ReclaimPendingAsync(IDatabase db, bool runReclaim, bool logPendingMetrics)
    {
        var pendingInfo = await db.StreamPendingAsync(_settings.StreamName, _settings.ConsumerGroup);
        var pendingCount = pendingInfo.PendingMessageCount;
        if (logPendingMetrics)
        {
            var maxIdleMs = await GetMaxPendingIdleMsAsync(db, pendingInfo);
            _logger.LogInformation(
                "Redis stream pending status: stream={Stream} group={Group} pending={PendingCount} max_idle_ms={MaxIdleMs} lowest_id={LowestId} highest_id={HighestId}",
                _settings.StreamName,
                _settings.ConsumerGroup,
                pendingCount,
                maxIdleMs,
                pendingInfo.LowestPendingMessageId.IsNull ? "<none>" : pendingInfo.LowestPendingMessageId.ToString(),
                pendingInfo.HighestPendingMessageId.IsNull ? "<none>" : pendingInfo.HighestPendingMessageId.ToString());
            _lastPendingMetricsAtUtc = DateTime.UtcNow;
        }

        if (!runReclaim || pendingCount <= 0)
        {
            if (runReclaim)
            {
                _lastReclaimAtUtc = DateTime.UtcNow;
            }

            return;
        }

        var minIdleMs = Math.Max(1000L, _settings.PendingMinIdleSeconds * 1000L);
        var batchSize = Math.Max(1, _settings.PendingReclaimBatchSize);
        var nextStartId = (RedisValue)"0-0";
        var reclaimed = 0;
        while (reclaimed < batchSize)
        {
            var take = Math.Max(1, batchSize - reclaimed);
            var result = await db.StreamAutoClaimAsync(
                _settings.StreamName,
                _settings.ConsumerGroup,
                _settings.ConsumerName,
                minIdleMs,
                nextStartId,
                take);

            var claimedEntries = result.ClaimedEntries;
            if (claimedEntries.Length == 0)
            {
                break;
            }

            reclaimed += EnqueueReclaimedMessages(claimedEntries);
            nextStartId = result.NextStartId;
            if (nextStartId.IsNull)
            {
                break;
            }
        }

        if (reclaimed > 0)
        {
            _logger.LogWarning(
                "Redis pending reclaim executed: stream={Stream} group={Group} consumer={Consumer} reclaimed={Reclaimed} min_idle_ms={MinIdleMs}",
                _settings.StreamName,
                _settings.ConsumerGroup,
                _settings.ConsumerName,
                reclaimed,
                minIdleMs);
        }

        _lastReclaimAtUtc = DateTime.UtcNow;
    }

    private async Task<long> GetMaxPendingIdleMsAsync(IDatabase db, StreamPendingInfo pendingInfo)
    {
        var sampleSize = Math.Max(1, _settings.PendingMetricsSampleSize);
        if (pendingInfo.PendingMessageCount <= 0 || pendingInfo.Consumers.Length == 0)
        {
            return 0;
        }

        long maxIdleMs = 0;
        foreach (var consumer in pendingInfo.Consumers)
        {
            if (consumer.PendingMessageCount <= 0)
            {
                continue;
            }

            var consumerSampleSize = Math.Max(1, Math.Min(sampleSize, (int)Math.Min(int.MaxValue, consumer.PendingMessageCount)));
            var pendingMessages = await db.StreamPendingMessagesAsync(
                _settings.StreamName,
                _settings.ConsumerGroup,
                consumerSampleSize,
                consumer.Name,
                minId: null,
                maxId: null);
            if (pendingMessages.Length == 0)
            {
                continue;
            }

            var consumerMaxIdle = pendingMessages.Max(x => x.IdleTimeInMilliseconds);
            if (consumerMaxIdle > maxIdleMs)
            {
                maxIdleMs = consumerMaxIdle;
            }
        }

        return maxIdleMs;
    }

    private void DeserializeAndAppendEntries(StreamEntry[] entries, List<RawTelegramMessage> results)
    {
        foreach (var entry in entries)
        {
            if (!TryDeserializeEntry(entry, out var msg))
            {
                continue;
            }

            results.Add(msg);
        }
    }

    private int EnqueueReclaimedMessages(StreamEntry[] entries)
    {
        var added = 0;
        foreach (var entry in entries)
        {
            if (!TryDeserializeEntry(entry, out var msg))
            {
                continue;
            }

            lock (_reclaimedLock)
            {
                _reclaimedBuffer.Enqueue(msg);
            }

            added++;
        }

        return added;
    }

    private void DrainReclaimedMessages(List<RawTelegramMessage> results, int maxCount)
    {
        var drained = 0;
        lock (_reclaimedLock)
        {
            while (results.Count < maxCount && _reclaimedBuffer.Count > 0)
            {
                results.Add(_reclaimedBuffer.Dequeue());
                drained++;
            }
        }

        if (drained > 0)
        {
            _logger.LogInformation(
                "Redis reclaimed messages delivered to worker: consumer={Consumer} delivered={Delivered}",
                _settings.ConsumerName,
                drained);
        }
    }

    private bool TryDeserializeEntry(StreamEntry entry, out RawTelegramMessage message)
    {
        try
        {
            var json = entry["data"].ToString();
            message = JsonSerializer.Deserialize<RawTelegramMessage>(json) ?? new RawTelegramMessage();
            if (string.IsNullOrWhiteSpace(message.StreamId))
            {
                message.StreamId = entry.Id.ToString();
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize stream entry {Id}", entry.Id);
            message = new RawTelegramMessage();
            return false;
        }
    }
}
