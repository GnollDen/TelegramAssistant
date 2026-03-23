using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class MessageExtractionRepository : IMessageExtractionRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private const string RefinementCandidatesSql = """
        SELECT
            me.id AS extraction_id,
            me.message_id,
            me.cheap_json::text AS cheap_json,
            me.updated_at AS extraction_updated_at,
            COALESCE(ic.claim_count, 0) AS claim_count,
            m.telegram_message_id,
            m.chat_id,
            m.sender_id,
            m.sender_name,
            m.timestamp,
            m.text,
            m.media_type,
            m.media_path,
            m.media_transcription,
            m.media_description,
            m.media_paralinguistics_json,
            m.reply_to_message_id,
            m.edit_timestamp,
            m.reactions_json,
            m.forward_json,
            me.needs_expensive,
            m.source,
            m.processing_status,
            m.processed_at,
            m.needs_reanalysis,
            m.created_at
        FROM message_extractions me
        JOIN messages m ON m.id = me.message_id
        LEFT JOIN LATERAL (
            SELECT COUNT(*)::INT AS claim_count
            FROM intelligence_claims ic
            WHERE ic.message_id = me.message_id
        ) ic ON TRUE
        WHERE m.processing_status = @processed_status
          AND COALESCE(me.is_quarantined, FALSE) = FALSE
          AND me.id > @after_extraction_id
          AND (
              char_length(COALESCE(m.text, '')) +
              char_length(COALESCE(m.media_transcription, '')) +
              char_length(COALESCE(m.media_description, ''))
          ) >= @min_message_length
          AND (
              COALESCE(ic.claim_count, 0) = 0
              OR me.updated_at < @cheap_prompt_updated_at
              OR me.updated_at <= NOW() - make_interval(hours => @stale_after_hours)
              OR (
                  jsonb_array_length(
                      CASE
                          WHEN jsonb_typeof(me.cheap_json -> 'claims') = 'array'
                              THEN me.cheap_json -> 'claims'
                          ELSE '[]'::jsonb
                      END
                  ) = 0
                  AND jsonb_array_length(
                      CASE
                          WHEN jsonb_typeof(me.cheap_json -> 'facts') = 'array'
                              THEN me.cheap_json -> 'facts'
                          ELSE '[]'::jsonb
                      END
                  ) <= 1
              )
              OR EXISTS (
                  SELECT 1
                  FROM jsonb_array_elements(
                      CASE
                          WHEN jsonb_typeof(me.cheap_json -> 'claims') = 'array'
                              THEN me.cheap_json -> 'claims'
                          ELSE '[]'::jsonb
                      END
                  ) claim_node
                  WHERE (
                      CASE
                          WHEN COALESCE(claim_node ->> 'confidence', '') ~ '^[0-9]+(\\.[0-9]+)?$'
                              THEN (claim_node ->> 'confidence')::DOUBLE PRECISION
                          ELSE 1.0
                      END
                  ) < @low_confidence_threshold
              )
              OR EXISTS (
                  SELECT 1
                  FROM jsonb_array_elements(
                      CASE
                          WHEN jsonb_typeof(me.cheap_json -> 'facts') = 'array'
                              THEN me.cheap_json -> 'facts'
                          ELSE '[]'::jsonb
                      END
                  ) fact_node
                  WHERE (
                      CASE
                          WHEN COALESCE(fact_node ->> 'confidence', '') ~ '^[0-9]+(\\.[0-9]+)?$'
                              THEN (fact_node ->> 'confidence')::DOUBLE PRECISION
                          ELSE 1.0
                      END
                  ) < @low_confidence_threshold
              )
              OR COALESCE((me.cheap_json ->> 'requires_expensive')::BOOLEAN, FALSE) = TRUE
          )
        ORDER BY me.id ASC
        LIMIT @limit;
        """;

    public MessageExtractionRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertCheapAsync(long messageId, string cheapJson, bool needsExpensive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var safeJson = string.IsNullOrWhiteSpace(cheapJson) ? "{}" : cheapJson;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO message_extractions (message_id, cheap_json, needs_expensive, created_at, updated_at)
            VALUES ({messageId}, {safeJson}::jsonb, {needsExpensive}, NOW(), NOW())
            ON CONFLICT (message_id)
            DO UPDATE SET
                cheap_json = EXCLUDED.cheap_json,
                needs_expensive = EXCLUDED.needs_expensive,
                expensive_retry_count = CASE WHEN EXCLUDED.needs_expensive THEN 0 ELSE message_extractions.expensive_retry_count END,
                expensive_next_retry_at = NULL,
                expensive_last_error = NULL,
                updated_at = NOW()
            """, ct);
    }

    public async Task QuarantineMessagesAsync(IReadOnlyCollection<long> messageIds, string reason, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
        {
            return;
        }

        var ids = messageIds
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        var safeReason = string.IsNullOrWhiteSpace(reason) ? "manual_quarantine" : reason.Trim();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO message_extractions (
                message_id,
                cheap_json,
                needs_expensive,
                is_quarantined,
                quarantine_reason,
                quarantined_at,
                created_at,
                updated_at
            )
            SELECT
                src.message_id,
                '{}'::jsonb,
                FALSE,
                TRUE,
                @reason,
                NOW(),
                NOW(),
                NOW()
            FROM unnest(@message_ids::bigint[]) AS src(message_id)
            ON CONFLICT (message_id)
            DO UPDATE SET
                is_quarantined = TRUE,
                quarantine_reason = EXCLUDED.quarantine_reason,
                quarantined_at = EXCLUDED.quarantined_at,
                needs_expensive = FALSE,
                expensive_next_retry_at = NULL,
                updated_at = NOW();
            """;
        cmd.Parameters.Add(new NpgsqlParameter("message_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        {
            Value = ids
        });
        cmd.Parameters.AddWithValue("reason", safeReason);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ReleaseQuarantineForRetryAsync(
        string reason,
        DateTime quarantinedBeforeUtc,
        int limit,
        CancellationToken ct = default)
    {
        var safeReason = string.IsNullOrWhiteSpace(reason) ? "manual_quarantine" : reason.Trim();
        var safeLimit = Math.Max(1, limit);
        var safeQuarantinedBeforeUtc = DateTime.SpecifyKind(quarantinedBeforeUtc, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH eligible AS (
                SELECT me.message_id
                FROM message_extractions me
                JOIN messages m ON m.id = me.message_id
                WHERE COALESCE(me.is_quarantined, FALSE) = TRUE
                  AND COALESCE(me.quarantine_reason, '') = @reason
                  AND me.quarantined_at IS NOT NULL
                  AND me.quarantined_at <= @quarantined_before_utc
                  AND m.processing_status = @processed_status
                ORDER BY me.quarantined_at ASC, me.message_id ASC
                LIMIT @limit
            ),
            upd_extractions AS (
                UPDATE message_extractions me
                SET is_quarantined = FALSE,
                    quarantine_reason = NULL,
                    quarantined_at = NULL,
                    updated_at = NOW()
                FROM eligible e
                WHERE me.message_id = e.message_id
                RETURNING me.message_id
            )
            UPDATE messages m
            SET needs_reanalysis = TRUE,
                processed_at = NOW()
            FROM upd_extractions u
            WHERE m.id = u.message_id;
            """;
        cmd.Parameters.AddWithValue("reason", safeReason);
        cmd.Parameters.AddWithValue("quarantined_before_utc", safeQuarantinedBeforeUtc);
        cmd.Parameters.AddWithValue("processed_status", (short)ProcessingStatus.Processed);
        cmd.Parameters.AddWithValue("limit", safeLimit);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<QuarantineMetrics> GetQuarantineMetricsAsync(DateTime stuckBeforeUtc, CancellationToken ct = default)
    {
        var safeStuckBeforeUtc = DateTime.SpecifyKind(stuckBeforeUtc, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var total = await db.MessageExtractions
            .AsNoTracking()
            .LongCountAsync(x => x.IsQuarantined, ct);
        var stuck = await db.MessageExtractions
            .AsNoTracking()
            .LongCountAsync(
                x => x.IsQuarantined
                     && x.QuarantinedAt != null
                     && x.QuarantinedAt <= safeStuckBeforeUtc,
                ct);
        return new QuarantineMetrics
        {
            Total = total,
            Stuck = stuck
        };
    }

    public async Task<HashSet<long>> GetQuarantinedMessageIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.MessageExtractions
            .AsNoTracking()
            .Where(x => x.IsQuarantined && messageIds.Contains(x.MessageId))
            .Select(x => x.MessageId)
            .ToListAsync(ct);
        return rows.ToHashSet();
    }

    public async Task<Dictionary<long, string>> GetCheapJsonByMessageIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.MessageExtractions
            .AsNoTracking()
            .Where(x => !x.IsQuarantined && messageIds.Contains(x.MessageId))
            .ToDictionaryAsync(x => x.MessageId, x => x.CheapJson, ct);
    }

    public async Task<List<MessageExtractionRecord>> GetExpensiveBacklogAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.MessageExtractions.AsNoTracking()
            .Where(x => x.NeedsExpensive
                        && !x.IsQuarantined
                        && (x.ExpensiveNextRetryAt == null || x.ExpensiveNextRetryAt <= DateTime.UtcNow))
            .OrderBy(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(x => new MessageExtractionRecord
        {
            Id = x.Id,
            MessageId = x.MessageId,
            CheapJson = x.CheapJson,
            ExpensiveJson = x.ExpensiveJson,
            NeedsExpensive = x.NeedsExpensive,
            ExpensiveRetryCount = x.ExpensiveRetryCount,
            ExpensiveNextRetryAt = x.ExpensiveNextRetryAt,
            ExpensiveLastError = x.ExpensiveLastError
        }).ToList();
    }

    public async Task<List<RefinementCandidate>> GetRefinementCandidatesAsync(
        long afterExtractionId,
        int limit,
        int minMessageLength,
        int staleAfterHours,
        DateTime cheapPromptUpdatedAtUtc,
        float lowConfidenceThreshold,
        CancellationToken ct = default)
    {
        var safeAfterExtractionId = Math.Max(0, afterExtractionId);
        var safeLimit = Math.Max(1, limit);
        var safeMinLength = Math.Max(1, minMessageLength);
        var safeStaleHours = Math.Max(1, staleAfterHours);
        var safePromptUpdatedAt = DateTime.SpecifyKind(cheapPromptUpdatedAtUtc, DateTimeKind.Utc);
        var safeLowConfidenceThreshold = Math.Clamp(lowConfidenceThreshold, 0.05f, 1.0f);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = RefinementCandidatesSql;
        cmd.Parameters.AddWithValue("after_extraction_id", safeAfterExtractionId);
        cmd.Parameters.AddWithValue("limit", safeLimit);
        cmd.Parameters.AddWithValue("min_message_length", safeMinLength);
        cmd.Parameters.AddWithValue("stale_after_hours", safeStaleHours);
        cmd.Parameters.AddWithValue("cheap_prompt_updated_at", safePromptUpdatedAt);
        cmd.Parameters.AddWithValue("low_confidence_threshold", safeLowConfidenceThreshold);
        cmd.Parameters.AddWithValue("processed_status", (short)ProcessingStatus.Processed);

        var result = new List<RefinementCandidate>(safeLimit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var messageId = reader.GetInt64(1);
            result.Add(new RefinementCandidate
            {
                ExtractionId = reader.GetInt64(0),
                MessageId = messageId,
                CheapJson = reader.GetString(2),
                ExtractionUpdatedAt = reader.GetDateTime(3),
                ExistingClaimsCount = reader.GetInt32(4),
                NeedsExpensive = reader.GetBoolean(20),
                Message = new Message
                {
                    Id = messageId,
                    TelegramMessageId = reader.GetInt64(5),
                    ChatId = reader.GetInt64(6),
                    SenderId = reader.GetInt64(7),
                    SenderName = reader.GetString(8),
                    Timestamp = reader.GetDateTime(9),
                    Text = reader.IsDBNull(10) ? null : reader.GetString(10),
                    MediaType = (MediaType)reader.GetInt16(11),
                    MediaPath = reader.IsDBNull(12) ? null : reader.GetString(12),
                    MediaTranscription = reader.IsDBNull(13) ? null : reader.GetString(13),
                    MediaDescription = reader.IsDBNull(14) ? null : reader.GetString(14),
                    MediaParalinguisticsJson = reader.IsDBNull(15) ? null : reader.GetString(15),
                    ReplyToMessageId = reader.IsDBNull(16) ? null : reader.GetInt64(16),
                    EditTimestamp = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                    ReactionsJson = reader.IsDBNull(18) ? null : reader.GetString(18),
                    ForwardJson = reader.IsDBNull(19) ? null : reader.GetString(19),
                    Source = (MessageSource)reader.GetInt16(21),
                    ProcessingStatus = (ProcessingStatus)reader.GetInt16(22),
                    ProcessedAt = reader.IsDBNull(23) ? null : reader.GetDateTime(23),
                    NeedsReanalysis = reader.GetBoolean(24),
                    CreatedAt = reader.GetDateTime(25)
                }
            });
        }

        return result;
    }

    public async Task ResolveExpensiveAsync(long extractionId, string expensiveJson, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var safeJson = string.IsNullOrWhiteSpace(expensiveJson) ? "{}" : expensiveJson;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE message_extractions
            SET expensive_json = {safeJson}::jsonb,
                needs_expensive = FALSE,
                expensive_retry_count = 0,
                expensive_next_retry_at = NULL,
                expensive_last_error = NULL,
                updated_at = NOW()
            WHERE id = {extractionId}
            """, ct);
    }

    public async Task<ExpensiveRetryResult> MarkExpensiveFailedAsync(
        long extractionId,
        string? error,
        int maxRetries,
        int baseDelaySeconds,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.MessageExtractions.FirstOrDefaultAsync(x => x.Id == extractionId, ct);
        if (row == null)
        {
            return new ExpensiveRetryResult { Found = false };
        }

        var nextRetryCount = row.ExpensiveRetryCount + 1;
        var safeMaxRetries = Math.Max(1, maxRetries);
        var isExhausted = nextRetryCount >= safeMaxRetries;

        row.ExpensiveRetryCount = nextRetryCount;
        row.ExpensiveLastError = string.IsNullOrWhiteSpace(error) ? "unknown_error" : error.Trim();
        row.UpdatedAt = DateTime.UtcNow;

        if (isExhausted)
        {
            row.ExpensiveNextRetryAt = null;
            row.NeedsExpensive = false;
        }
        else
        {
            var delay = ComputeRetryDelay(nextRetryCount, baseDelaySeconds);
            row.ExpensiveNextRetryAt = DateTime.UtcNow.Add(delay);
            row.NeedsExpensive = true;
        }

        await db.SaveChangesAsync(ct);
        return new ExpensiveRetryResult
        {
            Found = true,
            IsExhausted = isExhausted,
            RetryCount = nextRetryCount,
            NextRetryAt = row.ExpensiveNextRetryAt
        };
    }

    private static TimeSpan ComputeRetryDelay(int retryCount, int baseDelaySeconds)
    {
        var baseSeconds = Math.Max(5, baseDelaySeconds);
        var shift = Math.Min(10, Math.Max(0, retryCount - 1));
        var multiplier = 1 << shift;
        var seconds = Math.Min(3600, baseSeconds * multiplier);
        return TimeSpan.FromSeconds(seconds);
    }
}
