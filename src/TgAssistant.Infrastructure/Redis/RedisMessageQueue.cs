using System.Text.Json;
using System.Diagnostics;
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
    private readonly string _consumerName;
    private readonly Queue<RawTelegramMessage> _reclaimedBuffer = new();
    private readonly HashSet<string> _bufferedReclaimedIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _inflightMessageIds = new(StringComparer.Ordinal);
    private readonly object _reclaimedLock = new();
    private readonly object _inflightLock = new();
    private DateTime _lastReclaimAtUtc = DateTime.MinValue;
    private DateTime _lastPendingMetricsAtUtc = DateTime.MinValue;

    public RedisMessageQueue(IConnectionMultiplexer redis, IOptions<RedisSettings> settings, ILogger<RedisMessageQueue> logger)
    {
        _redis = redis;
        _settings = settings.Value;
        _logger = logger;
        _consumerName = BuildConsumerName(_settings.ConsumerName);
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

        _logger.LogInformation(
            "Redis queue initialized: stream={Stream} group={Group} configured_consumer={ConfiguredConsumer} effective_consumer={EffectiveConsumer}",
            _settings.StreamName,
            _settings.ConsumerGroup,
            _settings.ConsumerName,
            _consumerName);

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
        var entries = await db.StreamReadGroupAsync(_settings.StreamName, _settings.ConsumerGroup, _consumerName, position: StreamPosition.NewMessages, count: readCount);
        if (entries.Length == 0 && results.Count == 0)
        {
            await Task.Delay(timeout, ct);
            return results;
        }

        await DeserializeAndAppendEntriesAsync(db, entries, results, source: "new");
        return results;
    }

    public async Task AcknowledgeAsync(IEnumerable<string> messageIds, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ids = messageIds.Select(id => (RedisValue)id).ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        await db.StreamAcknowledgeAsync(_settings.StreamName, _settings.ConsumerGroup, ids);
        lock (_inflightLock)
        {
            foreach (var id in ids)
            {
                _inflightMessageIds.Remove(id.ToString());
            }
        }
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
                _consumerName,
                minIdleMs,
                nextStartId,
                take);

            var claimedEntries = result.ClaimedEntries;
            if (claimedEntries.Length == 0)
            {
                break;
            }

            reclaimed += await EnqueueReclaimedMessagesAsync(db, claimedEntries);
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
                _consumerName,
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

    private async Task DeserializeAndAppendEntriesAsync(IDatabase db, StreamEntry[] entries, List<RawTelegramMessage> results, string source)
    {
        var poisonIds = new List<RedisValue>();
        foreach (var entry in entries)
        {
            if (!TryDeserializeEntry(entry, out var msg, out var reason))
            {
                if (await TryQuarantinePoisonEntryAsync(db, entry, reason, source))
                {
                    poisonIds.Add(entry.Id);
                }

                continue;
            }

            if (!TryMarkInflight(msg.StreamId))
            {
                _logger.LogDebug(
                    "Suppressed duplicate stream entry delivery: source={Source} stream_id={StreamId} consumer={Consumer}",
                    source,
                    msg.StreamId,
                    _consumerName);
                continue;
            }

            results.Add(msg);
        }

        if (poisonIds.Count > 0)
        {
            await db.StreamAcknowledgeAsync(_settings.StreamName, _settings.ConsumerGroup, poisonIds.ToArray());
            _logger.LogWarning(
                "Acknowledged malformed stream entries after DLQ handoff: count={Count} stream={Stream} group={Group}",
                poisonIds.Count,
                _settings.StreamName,
                _settings.ConsumerGroup);
        }
    }

    private async Task<int> EnqueueReclaimedMessagesAsync(IDatabase db, StreamEntry[] entries)
    {
        var poisonIds = new List<RedisValue>();
        var added = 0;
        foreach (var entry in entries)
        {
            if (!TryDeserializeEntry(entry, out var msg, out var reason))
            {
                if (await TryQuarantinePoisonEntryAsync(db, entry, reason, "reclaim"))
                {
                    poisonIds.Add(entry.Id);
                }

                continue;
            }

            if (!TryBufferReclaimed(msg))
            {
                _logger.LogDebug(
                    "Suppressed duplicate reclaimed stream entry: stream_id={StreamId} consumer={Consumer}",
                    msg.StreamId,
                    _consumerName);
                continue;
            }

            lock (_reclaimedLock)
            {
                _reclaimedBuffer.Enqueue(msg);
            }

            added++;
        }

        if (poisonIds.Count > 0)
        {
            await db.StreamAcknowledgeAsync(_settings.StreamName, _settings.ConsumerGroup, poisonIds.ToArray());
            _logger.LogWarning(
                "Acknowledged malformed reclaimed entries after DLQ handoff: count={Count} stream={Stream} group={Group}",
                poisonIds.Count,
                _settings.StreamName,
                _settings.ConsumerGroup);
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
                var msg = _reclaimedBuffer.Dequeue();
                _bufferedReclaimedIds.Remove(msg.StreamId);
                if (!TryMarkInflight(msg.StreamId))
                {
                    continue;
                }

                results.Add(msg);
                drained++;
            }
        }

        if (drained > 0)
        {
            _logger.LogInformation(
                "Redis reclaimed messages delivered to worker: consumer={Consumer} delivered={Delivered}",
                _consumerName,
                drained);
        }
    }

    private bool TryDeserializeEntry(StreamEntry entry, out RawTelegramMessage message, out string reason)
    {
        try
        {
            var json = entry["data"].ToString();
            if (string.IsNullOrWhiteSpace(json))
            {
                message = new RawTelegramMessage();
                reason = "missing_or_empty_data_field";
                return false;
            }

            message = JsonSerializer.Deserialize<RawTelegramMessage>(json) ?? new RawTelegramMessage();
            if (string.IsNullOrWhiteSpace(message.StreamId))
            {
                message.StreamId = entry.Id.ToString();
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize stream entry {Id}", entry.Id);
            message = new RawTelegramMessage();
            reason = $"deserialization_failed:{ex.GetType().Name}";
            return false;
        }
    }

    private async Task<bool> TryQuarantinePoisonEntryAsync(IDatabase db, StreamEntry entry, string reason, string source)
    {
        try
        {
            var payload = entry["data"].ToString();
            await db.StreamAddAsync(_settings.DeadLetterStreamName, new NameValueEntry[]
            {
                new("source_stream", _settings.StreamName),
                new("source_group", _settings.ConsumerGroup),
                new("source_consumer", _consumerName),
                new("source_entry_id", entry.Id.ToString()),
                new("source_mode", source),
                new("reason", reason),
                new("failed_at_utc", DateTime.UtcNow.ToString("O")),
                new("raw_data", string.IsNullOrWhiteSpace(payload) ? "<empty>" : payload)
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish malformed stream entry to DLQ. entry_id={EntryId} dlq={DlqStream} reason={Reason}",
                entry.Id,
                _settings.DeadLetterStreamName,
                reason);
            return false;
        }
    }

    private bool TryBufferReclaimed(RawTelegramMessage msg)
    {
        lock (_reclaimedLock)
        {
            if (_bufferedReclaimedIds.Contains(msg.StreamId))
            {
                return false;
            }
        }

        lock (_inflightLock)
        {
            if (_inflightMessageIds.ContainsKey(msg.StreamId))
            {
                return false;
            }
        }

        lock (_reclaimedLock)
        {
            if (!_bufferedReclaimedIds.Add(msg.StreamId))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryMarkInflight(string streamId)
    {
        var now = DateTime.UtcNow;
        var dedupeWindow = GetInflightDedupeWindow();

        lock (_inflightLock)
        {
            if (_inflightMessageIds.TryGetValue(streamId, out var firstSeenAtUtc))
            {
                if (now - firstSeenAtUtc < dedupeWindow)
                {
                    return false;
                }
            }

            _inflightMessageIds[streamId] = now;
            return true;
        }
    }

    private TimeSpan GetInflightDedupeWindow()
    {
        var seconds = Math.Max(10, Math.Max(_settings.PendingMinIdleSeconds, _settings.PendingReclaimIntervalSeconds * 2));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string BuildConsumerName(string configuredConsumerName)
    {
        var baseName = string.IsNullOrWhiteSpace(configuredConsumerName) ? "worker" : configuredConsumerName.Trim();
        var machine = SanitizeToken(Environment.MachineName);
        var processId = Process.GetCurrentProcess().Id;
        return $"{baseName}-{machine}-{processId}";
    }

    private static string SanitizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')
            .ToArray();
        return new string(chars);
    }
}
