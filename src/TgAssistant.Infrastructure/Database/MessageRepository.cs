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
            .Where(x => sourceKeys.Contains(x.Source) && chatKeys.Contains(x.ChatId))
            .ToListAsync(ct);

        var existingMap = existing.ToDictionary(
            x => $"{x.Source}:{x.ChatId}:{x.TelegramMessageId}",
            x => x,
            StringComparer.Ordinal);

        foreach (var msg in unique)
        {
            var key = $"{(short)msg.Source}:{msg.ChatId}:{msg.TelegramMessageId}";
            if (existingMap.TryGetValue(key, out var current))
            {
                if (ShouldUpdateExisting(current, msg))
                {
                    ApplyUpdate(current, msg);
                }
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
                MediaParalinguisticsJson = msg.MediaParalinguisticsJson,
                ReplyToMessageId = msg.ReplyToMessageId,
                EditTimestamp = msg.EditTimestamp,
                ReactionsJson = msg.ReactionsJson,
                ForwardJson = msg.ForwardJson,
                Source = (short)msg.Source,
                ProcessingStatus = (short)msg.ProcessingStatus,
                ProcessedAt = msg.ProcessedAt,
                NeedsReanalysis = msg.NeedsReanalysis
            });
        }

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Saved batch of {Count} messages", unique.Count);
        return unique.Count;
    }

    private static bool ShouldUpdateExisting(DbMessage current, Message incoming)
    {
        if (incoming.EditTimestamp != null)
        {
            if (current.EditTimestamp == null || incoming.EditTimestamp > current.EditTimestamp)
            {
                return true;
            }

            if (!string.Equals(current.Text, incoming.Text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (!string.Equals(current.ReactionsJson, incoming.ReactionsJson, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static void ApplyUpdate(DbMessage current, Message incoming)
    {
        current.Text = incoming.Text;
        current.EditTimestamp = incoming.EditTimestamp ?? current.EditTimestamp;
        current.ReactionsJson = incoming.ReactionsJson ?? current.ReactionsJson;
        current.ForwardJson = incoming.ForwardJson ?? current.ForwardJson;
        current.NeedsReanalysis = true;
        current.ProcessedAt = DateTime.UtcNow;
        if (current.ProcessingStatus == (short)ProcessingStatus.Failed)
        {
            current.ProcessingStatus = (short)ProcessingStatus.Processed;
        }
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
            .Where(x => x.Id > afterId
                        && x.ProcessingStatus == (short)ProcessingStatus.Processed
                        && (x.MediaType == (short)MediaType.None
                            || x.MediaDescription != null
                            || x.MediaTranscription != null))
            .OrderBy(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<List<Message>> GetNeedsReanalysisAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.NeedsReanalysis
                        && (x.MediaType == (short)MediaType.None
                            || x.MediaDescription != null
                            || x.MediaTranscription != null))
            .OrderBy(x => x.Id)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<Message?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<Dictionary<long, Message>> GetByTelegramMessageIdsAsync(
        long chatId,
        MessageSource source,
        IReadOnlyCollection<long> telegramMessageIds,
        CancellationToken ct = default)
    {
        var result = new Dictionary<long, Message>();
        if (telegramMessageIds.Count == 0)
        {
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId
                        && x.Source == (short)source
                        && telegramMessageIds.Contains(x.TelegramMessageId))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            result[row.TelegramMessageId] = ToDomain(row);
        }

        return result;
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

    public async Task MarkNeedsReanalysisDoneAsync(IEnumerable<long> messageIds, CancellationToken ct = default)
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
            row.NeedsReanalysis = false;
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

    public async Task<List<Message>> GetPendingVoiceParalinguisticsAsync(int limit, CancellationToken ct = default)
    {
        var voiceTypes = new short[] { (short)MediaType.Voice, (short)MediaType.VideoNote, (short)MediaType.Video };
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ProcessingStatus == (short)ProcessingStatus.Processed
                        && voiceTypes.Contains(x.MediaType)
                        && x.MediaPath != null
                        && x.MediaParalinguisticsJson == null)
            .OrderBy(x => x.Timestamp)
            .Take(Math.Max(1, limit))
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
        if (status == ProcessingStatus.Processed)
        {
            row.NeedsReanalysis = true;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateMediaParalinguisticsAsync(long messageId, string jsonPayload, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (row == null)
        {
            return;
        }

        row.MediaParalinguisticsJson = jsonPayload;
        row.NeedsReanalysis = true;
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
            MediaParalinguisticsJson = row.MediaParalinguisticsJson,
            ReplyToMessageId = row.ReplyToMessageId,
            EditTimestamp = row.EditTimestamp,
            ReactionsJson = row.ReactionsJson,
            ForwardJson = row.ForwardJson,
            Source = (MessageSource)row.Source,
            ProcessingStatus = (ProcessingStatus)row.ProcessingStatus,
            ProcessedAt = row.ProcessedAt,
            NeedsReanalysis = row.NeedsReanalysis,
            CreatedAt = row.CreatedAt
        };
    }
}
