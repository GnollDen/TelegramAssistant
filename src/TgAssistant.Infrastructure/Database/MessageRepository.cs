using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class MessageRepository : IMessageRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<MessageRepository> _logger;

    public MessageRepository(IDbContextFactory<TgAssistantDbContext> dbFactory, ILogger<MessageRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<long> SaveBatchAsync(IEnumerable<Message> messages, CancellationToken ct = default)
    {
        var items = messages.ToList();
        if (items.Count == 0)
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var unique = items
            .GroupBy(x => new { x.Source, x.ChatId, x.TelegramMessageId })
            .Select(g => g.First())
            .ToList();

        var sourceKeys = unique.Select(x => (short)x.Source).Distinct().ToList();
        var chatKeys = unique.Select(x => x.ChatId).Distinct().ToList();

        var existing = await db.Messages
            .AsNoTracking()
            .Where(x => sourceKeys.Contains(x.Source) && chatKeys.Contains(x.ChatId))
            .Select(x => new { x.Source, x.ChatId, x.TelegramMessageId })
            .ToListAsync(ct);

        var existingSet = existing
            .Select(x => $"{x.Source}:{x.ChatId}:{x.TelegramMessageId}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var msg in unique)
        {
            var key = $"{(short)msg.Source}:{msg.ChatId}:{msg.TelegramMessageId}";
            if (existingSet.Contains(key))
            {
                continue;
            }

            db.Messages.Add(new DbMessage
            {
                TelegramMessageId = msg.TelegramMessageId,
                ChatId = msg.ChatId,
                SenderId = msg.SenderId,
                SenderName = msg.SenderName,
                Timestamp = msg.Timestamp,
                Text = msg.Text,
                MediaType = (short)msg.MediaType,
                MediaPath = msg.MediaPath,
                MediaTranscription = msg.MediaTranscription,
                MediaDescription = msg.MediaDescription,
                ReplyToMessageId = msg.ReplyToMessageId,
                EditTimestamp = msg.EditTimestamp,
                ReactionsJson = msg.ReactionsJson,
                ForwardJson = msg.ForwardJson,
                Source = (short)msg.Source,
                ProcessingStatus = (short)msg.ProcessingStatus,
                ProcessedAt = msg.ProcessedAt
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Saved batch of {Count} messages", unique.Count);
        return unique.Count;
    }

    public async Task<List<Message>> GetUnprocessedAsync(int limit = 100, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ProcessingStatus == (short)ProcessingStatus.Pending)
            .OrderBy(x => x.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<List<Message>> GetByContactSinceAsync(long chatId, DateTime since, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId && x.Timestamp > since)
            .OrderBy(x => x.Timestamp)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<List<Message>> GetProcessedAfterIdAsync(long afterId, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.Id > afterId && x.ProcessingStatus == (short)ProcessingStatus.Processed)
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task MarkProcessedAsync(IEnumerable<long> messageIds, CancellationToken ct = default)
    {
        var ids = messageIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        foreach (var row in rows)
        {
            row.ProcessingStatus = (short)ProcessingStatus.Processed;
            row.ProcessedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Message>> GetPendingArchiveMediaAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.Source == (short)MessageSource.Archive
                        && x.ProcessingStatus == (short)ProcessingStatus.Pending
                        && x.MediaType != (short)MediaType.None
                        && x.MediaPath != null)
            .OrderBy(x => x.Timestamp)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task UpdateMediaProcessingResultAsync(long messageId, MediaProcessingResult result, ProcessingStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (row == null)
        {
            return;
        }

        row.MediaTranscription = result.Transcription;
        row.MediaDescription = result.Success ? result.Description : $"Unrecognized: {result.FailureReason}";
        row.ProcessingStatus = (short)status;
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static Message ToDomain(DbMessage row)
    {
        return new Message
        {
            Id = row.Id,
            TelegramMessageId = row.TelegramMessageId,
            ChatId = row.ChatId,
            SenderId = row.SenderId,
            SenderName = row.SenderName,
            Timestamp = row.Timestamp,
            Text = row.Text,
            MediaType = (MediaType)row.MediaType,
            MediaPath = row.MediaPath,
            MediaTranscription = row.MediaTranscription,
            MediaDescription = row.MediaDescription,
            ReplyToMessageId = row.ReplyToMessageId,
            EditTimestamp = row.EditTimestamp,
            ReactionsJson = row.ReactionsJson,
            ForwardJson = row.ForwardJson,
            Source = (MessageSource)row.Source,
            ProcessingStatus = (ProcessingStatus)row.ProcessingStatus,
            ProcessedAt = row.ProcessedAt,
            CreatedAt = row.CreatedAt
        };
    }
}
