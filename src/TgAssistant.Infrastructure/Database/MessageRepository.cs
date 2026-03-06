using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class MessageRepository : IMessageRepository
{
    private readonly string _connectionString;
    private readonly ILogger<MessageRepository> _logger;

    public MessageRepository(IOptions<DatabaseSettings> settings, ILogger<MessageRepository> logger)
    {
        _connectionString = settings.Value.ConnectionString;
        _logger = logger;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<long> SaveBatchAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        long count = 0;
        foreach (var msg in messages)
        {
            await conn.ExecuteAsync("""
                INSERT INTO messages (
                    telegram_message_id, chat_id, sender_id, sender_name, timestamp,
                    text, media_type, media_path, media_transcription, media_description,
                    reply_to_message_id, edit_timestamp, reactions_json, forward_json,
                    source, processing_status, processed_at, 
                ) VALUES (
                    @TelegramMessageId, @ChatId, @SenderId, @SenderName, @Timestamp,
                    @Text, @MediaType, @MediaPath, @MediaTranscription, @MediaDescription,
                    @ReplyToMessageId, @EditTimestamp, @ReactionsJson, @ForwardJson,
                    @Source, @ProcessingStatus, @ProcessedAt
                ) ON CONFLICT DO NOTHING
                """, msg, tx);
            count++;
        }

        await tx.CommitAsync(ct);
        _logger.LogDebug("Saved batch of {Count} messages", count);
        return count;
    }

    public async Task<List<Message>> GetUnprocessedAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var results = await conn.QueryAsync<Message>(
            "SELECT * FROM messages WHERE processing_status = 0 ORDER BY timestamp LIMIT @Limit",
            new { Limit = limit });
        return results.ToList();
    }

    public async Task<List<Message>> GetByContactSinceAsync(long chatId, DateTime since, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var results = await conn.QueryAsync<Message>(
            "SELECT * FROM messages WHERE chat_id = @ChatId AND timestamp > @Since ORDER BY timestamp",
            new { ChatId = chatId, Since = since });
        return results.ToList();
    }

    public async Task MarkProcessedAsync(IEnumerable<long> messageIds, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE messages SET processing_status = 1, processed_at = NOW() WHERE id = ANY(@Ids)",
            new { Ids = messageIds.ToArray() });
    }
}
