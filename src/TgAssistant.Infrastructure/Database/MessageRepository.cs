using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        var invalidChatCount = items.Count(x => x.ChatId <= 0);
        if (invalidChatCount > 0)
        {
            _logger.LogWarning(
                "Dropped messages with invalid chat_id<=0 before DB save: count={Count}",
                invalidChatCount);
            items = items.Where(x => x.ChatId > 0).ToList();
            if (items.Count == 0)
            {
                return 0;
            }
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

        var updatedAndMarkedForReanalysis = 0;
        foreach (var msg in unique)
        {
            var key = $"{(short)msg.Source}:{msg.ChatId}:{msg.TelegramMessageId}";
            if (existingMap.TryGetValue(key, out var current))
            {
                if (ShouldUpdateExisting(current, msg))
                {
                    ApplyUpdate(current, msg);
                    updatedAndMarkedForReanalysis++;
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
        if (updatedAndMarkedForReanalysis > 0)
        {
            _logger.LogInformation(
                "Marked messages for reanalysis: count={Count}, reason={ReasonCode}",
                updatedAndMarkedForReanalysis,
                "message_updated");
        }
        _logger.LogDebug("Saved batch of {Count} messages", unique.Count);
        return unique.Count;
    }

    private static bool ShouldUpdateExisting(DbMessage current, Message incoming)
    {
        if (IsDeletedMarker(incoming.Text) && !IsDeletedMarker(current.Text))
        {
            return true;
        }

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

    private static bool IsDeletedMarker(string? text)
    {
        return string.Equals(text?.Trim(), "[DELETED]", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyUpdate(DbMessage current, Message incoming)
    {
        var oldText = current.Text;
        current.Text = incoming.Text;
        current.EditTimestamp = incoming.EditTimestamp ?? current.EditTimestamp;
        current.ReactionsJson = incoming.ReactionsJson ?? current.ReactionsJson;
        current.ForwardJson = BuildForwardJsonWithEditTracking(
            current.ForwardJson,
            incoming.ForwardJson,
            oldText,
            incoming.Text,
            incoming.EditTimestamp);
        current.NeedsReanalysis = true;
        current.ProcessedAt = DateTime.UtcNow;
        if (current.ProcessingStatus == (short)ProcessingStatus.Failed)
        {
            current.ProcessingStatus = (short)ProcessingStatus.Processed;
        }
    }

    private static string? BuildForwardJsonWithEditTracking(
        string? currentForwardJson,
        string? incomingForwardJson,
        string? oldText,
        string? newText,
        DateTime? incomingEditTimestamp)
    {
        JsonObject root = [];
        if (!string.IsNullOrWhiteSpace(currentForwardJson))
        {
            root = ParseJsonObject(currentForwardJson) ?? new JsonObject();
        }

        if (!string.IsNullOrWhiteSpace(incomingForwardJson))
        {
            var incomingObj = ParseJsonObject(incomingForwardJson);
            if (incomingObj != null)
            {
                foreach (var pair in incomingObj)
                {
                    root[pair.Key] = pair.Value?.DeepClone();
                }
            }
            else
            {
                root["forward_raw"] = incomingForwardJson;
            }
        }

        if (incomingEditTimestamp != null && !string.Equals(oldText, newText, StringComparison.Ordinal))
        {
            var tracking = root["edit_tracking"] as JsonObject ?? new JsonObject();
            tracking["status"] = "pending";
            tracking["edited_at_utc"] = incomingEditTimestamp.Value.ToUniversalTime().ToString("O");
            tracking["before"] = TruncateForJson(oldText, 3000);
            tracking["after"] = TruncateForJson(newText, 3000);
            tracking["classification"] = null;
            tracking["summary"] = null;
            tracking["should_affect_memory"] = null;
            tracking["added_important"] = null;
            tracking["removed_important"] = null;
            tracking["confidence"] = null;
            tracking["analyzed_at_utc"] = null;
            root["edit_tracking"] = tracking;
        }

        return root.Count == 0 ? null : root.ToJsonString();
    }

    private static JsonObject? ParseJsonObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string TruncateForJson(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim();
        if (text.Length <= maxChars)
        {
            return text;
        }

        return text[..maxChars].TrimEnd() + "...";
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

    public async Task<Dictionary<long, List<long>>> ResolveChatsByTelegramMessageIdsAsync(
        IReadOnlyCollection<long> telegramMessageIds,
        MessageSource source,
        CancellationToken ct = default)
    {
        var result = new Dictionary<long, List<long>>();
        if (telegramMessageIds.Count == 0)
        {
            return result;
        }

        var ids = telegramMessageIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.Source == (short)source && ids.Contains(x.TelegramMessageId))
            .Select(x => new { x.TelegramMessageId, x.ChatId })
            .Distinct()
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.TelegramMessageId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ChatId).Distinct().OrderBy(x => x).ToList());
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

    public async Task<List<Message>> GetByIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => messageIds.Contains(x.Id)
                        && x.ProcessingStatus == (short)ProcessingStatus.Processed
                        && (x.MediaType == (short)MediaType.None
                            || x.MediaDescription != null
                            || x.MediaTranscription != null))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<List<Message>> GetChatWindowBeforeAsync(long chatId, long beforeMessageId, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId
                        && x.Id < beforeMessageId
                        && x.ProcessingStatus == (short)ProcessingStatus.Processed
                        && (x.MediaType == (short)MediaType.None
                            || x.MediaDescription != null
                            || x.MediaTranscription != null))
            .OrderByDescending(x => x.Id)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        rows.Reverse();
        return rows.Select(ToDomain).ToList();
    }

    public async Task<Dictionary<long, List<Message>>> GetChatWindowsBeforeByMessageIdsAsync(
        IReadOnlyCollection<long> messageIds,
        int limit,
        CancellationToken ct = default)
    {
        var result = new Dictionary<long, List<Message>>();
        if (messageIds.Count == 0)
        {
            return result;
        }

        var ids = messageIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return result;
        }

        foreach (var id in ids)
        {
            result[id] = [];
        }

        var safeLimit = Math.Max(1, limit);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH target AS (
                SELECT id, chat_id
                FROM messages
                WHERE id = ANY(@message_ids)
            )
            SELECT
                t.id AS target_message_id,
                m.id,
                m.telegram_message_id,
                m.chat_id,
                m.sender_id,
                m.sender_name,
                m.timestamp,
                m.text,
                m.media_type,
                m.media_path,
                m.media_description,
                m.media_transcription,
                m.media_paralinguistics_json,
                m.reply_to_message_id,
                m.edit_timestamp,
                m.reactions_json,
                m.forward_json,
                m.processing_status,
                m.source,
                m.processed_at,
                m.needs_reanalysis,
                m.created_at
            FROM target t
            JOIN LATERAL (
                SELECT
                    pm.id,
                    pm.telegram_message_id,
                    pm.chat_id,
                    pm.sender_id,
                    pm.sender_name,
                    pm.timestamp,
                    pm.text,
                    pm.media_type,
                    pm.media_path,
                    pm.media_description,
                    pm.media_transcription,
                    pm.media_paralinguistics_json,
                    pm.reply_to_message_id,
                    pm.edit_timestamp,
                    pm.reactions_json,
                    pm.forward_json,
                    pm.processing_status,
                    pm.source,
                    pm.processed_at,
                    pm.needs_reanalysis,
                    pm.created_at
                FROM messages pm
                WHERE pm.chat_id = t.chat_id
                  AND pm.id < t.id
                  AND pm.processing_status = @processed_status
                  AND (
                      pm.media_type = @none_media_type
                      OR pm.media_description IS NOT NULL
                      OR pm.media_transcription IS NOT NULL
                  )
                ORDER BY pm.id DESC
                LIMIT @limit
            ) m ON TRUE
            ORDER BY target_message_id ASC, m.id ASC;
            """;
        cmd.Parameters.Add(new NpgsqlParameter("message_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = ids
        });
        cmd.Parameters.AddWithValue("processed_status", (short)ProcessingStatus.Processed);
        cmd.Parameters.AddWithValue("none_media_type", (short)MediaType.None);
        cmd.Parameters.AddWithValue("limit", safeLimit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var targetMessageId = reader.GetInt64(0);
            if (!result.TryGetValue(targetMessageId, out var bucket))
            {
                bucket = [];
                result[targetMessageId] = bucket;
            }

            bucket.Add(new Message
            {
                Id = reader.GetInt64(1),
                TelegramMessageId = reader.GetInt64(2),
                ChatId = reader.GetInt64(3),
                SenderId = reader.GetInt64(4),
                SenderName = reader.GetString(5),
                Timestamp = reader.GetDateTime(6),
                Text = reader.IsDBNull(7) ? null : reader.GetString(7),
                MediaType = (MediaType)reader.GetInt16(8),
                MediaPath = reader.IsDBNull(9) ? null : reader.GetString(9),
                MediaDescription = reader.IsDBNull(10) ? null : reader.GetString(10),
                MediaTranscription = reader.IsDBNull(11) ? null : reader.GetString(11),
                MediaParalinguisticsJson = reader.IsDBNull(12) ? null : reader.GetString(12),
                ReplyToMessageId = reader.IsDBNull(13) ? null : reader.GetInt64(13),
                EditTimestamp = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                ReactionsJson = reader.IsDBNull(15) ? null : reader.GetString(15),
                ForwardJson = reader.IsDBNull(16) ? null : reader.GetString(16),
                ProcessingStatus = (ProcessingStatus)reader.GetInt16(17),
                Source = (MessageSource)reader.GetInt16(18),
                ProcessedAt = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                NeedsReanalysis = reader.GetBoolean(20),
                CreatedAt = reader.GetDateTime(21)
            });
        }

        return result;
    }

    public async Task<List<Message>> GetChatWindowAroundAsync(
        long chatId,
        long centerMessageId,
        int beforeCount,
        int afterCount,
        CancellationToken ct = default)
    {
        var safeBefore = Math.Max(0, beforeCount);
        var safeAfter = Math.Max(0, afterCount);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var center = await db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == centerMessageId && x.ChatId == chatId, ct);
        if (center == null)
        {
            return [];
        }

        var before = safeBefore == 0
            ? new List<DbMessage>()
            : await db.Messages
                .AsNoTracking()
                .Where(x => x.ChatId == chatId
                            && x.ProcessingStatus == (short)ProcessingStatus.Processed
                            && (x.Timestamp < center.Timestamp
                                || (x.Timestamp == center.Timestamp && x.Id < center.Id))
                            && (x.MediaType == (short)MediaType.None
                                || x.MediaDescription != null
                                || x.MediaTranscription != null))
                .OrderByDescending(x => x.Timestamp)
                .ThenByDescending(x => x.Id)
                .Take(safeBefore)
                .ToListAsync(ct);

        var after = safeAfter == 0
            ? new List<DbMessage>()
            : await db.Messages
                .AsNoTracking()
                .Where(x => x.ChatId == chatId
                            && x.ProcessingStatus == (short)ProcessingStatus.Processed
                            && (x.Timestamp > center.Timestamp
                                || (x.Timestamp == center.Timestamp && x.Id > center.Id))
                            && (x.MediaType == (short)MediaType.None
                                || x.MediaDescription != null
                                || x.MediaTranscription != null))
                .OrderBy(x => x.Timestamp)
                .ThenBy(x => x.Id)
                .Take(safeAfter)
                .ToListAsync(ct);

        before.Reverse();
        return before
            .Concat([center])
            .Concat(after)
            .Select(ToDomain)
            .ToList();
    }

    public async Task<List<Message>> GetByChatAndPeriodAsync(long chatId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId
                        && x.Timestamp >= fromUtc
                        && x.Timestamp <= toUtc
                        && x.ProcessingStatus == (short)ProcessingStatus.Processed
                        && (x.MediaType == (short)MediaType.None
                            || x.MediaDescription != null
                            || x.MediaTranscription != null))
            .OrderBy(x => x.Id)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<List<Message>> GetProcessedByChatAsync(long chatId, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId
                        && x.ProcessingStatus == (short)ProcessingStatus.Processed
                        && (x.MediaType == (short)MediaType.None
                            || x.MediaDescription != null
                            || x.MediaTranscription != null))
            .OrderByDescending(x => x.Id)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        rows.Reverse();
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

    public async Task<List<EditDiffCandidate>> GetPendingEditDiffCandidatesAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.EditTimestamp != null
                        && x.ForwardJson != null
                        && EF.Functions.Like(x.ForwardJson!, "%\"edit_tracking\"%")
                        && EF.Functions.Like(x.ForwardJson!, "%\"status\":\"pending\"%"))
            .OrderByDescending(x => x.EditTimestamp)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        var result = new List<EditDiffCandidate>(rows.Count);
        foreach (var row in rows)
        {
            var tracking = TryGetEditTracking(row.ForwardJson);
            if (tracking == null)
            {
                continue;
            }

            var before = tracking["before"]?.GetValue<string>() ?? string.Empty;
            var after = tracking["after"]?.GetValue<string>() ?? string.Empty;
            var status = tracking["status"]?.GetValue<string>() ?? string.Empty;
            if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new EditDiffCandidate
            {
                MessageId = row.Id,
                ChatId = row.ChatId,
                EditedAtUtc = row.EditTimestamp,
                BeforeText = before,
                AfterText = after
            });
        }

        return result;
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
            row.NeedsReanalysis = false;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkNeedsReanalysisAsync(IEnumerable<long> messageIds, string reasonCode = "unspecified", CancellationToken ct = default)
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
            row.NeedsReanalysis = true;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Marked messages for reanalysis: count={Count}, reason={ReasonCode}",
            rows.Count,
            NormalizeReanalysisReasonCode(reasonCode));
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

    public async Task SaveEditDiffAnalysisAsync(
        long messageId,
        string classification,
        string summary,
        bool shouldAffectMemory,
        bool addedImportant,
        bool removedImportant,
        float confidence,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (row == null)
        {
            return;
        }

        var root = ParseJsonObject(row.ForwardJson ?? "{}") ?? new JsonObject();
        var tracking = root["edit_tracking"] as JsonObject ?? new JsonObject();
        tracking["status"] = "done";
        tracking["classification"] = string.IsNullOrWhiteSpace(classification) ? "unknown" : classification.Trim();
        tracking["summary"] = TruncateForJson(summary, 900);
        tracking["should_affect_memory"] = shouldAffectMemory;
        tracking["added_important"] = addedImportant;
        tracking["removed_important"] = removedImportant;
        tracking["confidence"] = Math.Clamp(confidence, 0f, 1f);
        tracking["analyzed_at_utc"] = DateTime.UtcNow.ToString("O");
        root["edit_tracking"] = tracking;
        row.ForwardJson = root.ToJsonString();
        row.NeedsReanalysis = shouldAffectMemory || addedImportant || removedImportant;
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (row.NeedsReanalysis)
        {
            _logger.LogInformation(
                "Marked message for reanalysis: message_id={MessageId}, reason={ReasonCode}",
                messageId,
                "edit_diff_affects_memory");
        }
    }

    private static JsonObject? TryGetEditTracking(string? forwardJson)
    {
        var root = ParseJsonObject(forwardJson ?? string.Empty);
        if (root == null)
        {
            return null;
        }

        return root["edit_tracking"] as JsonObject;
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
        if (status == ProcessingStatus.Processed)
        {
            _logger.LogInformation(
                "Marked message for reanalysis: message_id={MessageId}, reason={ReasonCode}",
                messageId,
                "media_processed");
        }
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
        _logger.LogInformation(
            "Marked message for reanalysis: message_id={MessageId}, reason={ReasonCode}",
            messageId,
            "paralinguistics_updated");
    }

    public async Task UpdateVoiceProcessingResultAsync(
        long messageId,
        string? transcription,
        string? paralinguisticsJson,
        bool needsReanalysis,
        bool clearMediaPath,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Messages.FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (row == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(transcription))
        {
            row.MediaTranscription = transcription.Trim();
        }

        if (!string.IsNullOrWhiteSpace(paralinguisticsJson))
        {
            row.MediaParalinguisticsJson = paralinguisticsJson.Trim();
        }

        if (clearMediaPath)
        {
            row.MediaPath = null;
        }

        row.NeedsReanalysis = needsReanalysis;
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (needsReanalysis)
        {
            _logger.LogInformation(
                "Marked message for reanalysis: message_id={MessageId}, reason={ReasonCode}",
                messageId,
                "voice_processing_updated");
        }
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

    private static string NormalizeReanalysisReasonCode(string? reasonCode)
    {
        var normalized = reasonCode?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "unspecified" : normalized;
    }
}
