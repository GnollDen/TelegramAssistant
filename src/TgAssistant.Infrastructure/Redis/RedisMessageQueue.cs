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
    }

    public async Task EnqueueAsync(RawTelegramMessage message, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(message);
        var id = await db.StreamAddAsync(_settings.StreamName, new NameValueEntry[] { new("data", json) });
        _logger.LogDebug("Enqueued message {MsgId} to stream as {StreamId}", message.MessageId, id);
    }

    public async Task<List<RawTelegramMessage>> DequeueAsync(int maxCount, TimeSpan timeout, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var results = new List<RawTelegramMessage>();
        var entries = await db.StreamReadGroupAsync(_settings.StreamName, _settings.ConsumerGroup, _settings.ConsumerName, position: StreamPosition.NewMessages, count: maxCount);
        if (entries.Length == 0)
        {
            await Task.Delay(timeout, ct);
            return results;
        }
        foreach (var entry in entries)
        {
            try
            {
                var json = entry["data"].ToString();
                var msg = JsonSerializer.Deserialize<RawTelegramMessage>(json);
                if (msg != null) { msg.StreamId = entry.Id.ToString(); results.Add(msg); }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to deserialize stream entry {Id}", entry.Id); }
        }
        return results;
    }

    public async Task AcknowledgeAsync(IEnumerable<string> messageIds, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var ids = messageIds.Select(id => (RedisValue)id).ToArray();
        await db.StreamAcknowledgeAsync(_settings.StreamName, _settings.ConsumerGroup, ids);
    }
}
