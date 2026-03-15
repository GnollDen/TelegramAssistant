using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task<List<MessageExtractionRecord>> GetExpensiveBacklogAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.MessageExtractions.AsNoTracking()
            .Where(x => x.NeedsExpensive && (x.ExpensiveNextRetryAt == null || x.ExpensiveNextRetryAt <= DateTime.UtcNow))
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
                    Source = (MessageSource)reader.GetInt16(20),
                    ProcessingStatus = (ProcessingStatus)reader.GetInt16(21),
                    ProcessedAt = reader.IsDBNull(22) ? null : reader.GetDateTime(22),
                    NeedsReanalysis = reader.GetBoolean(23),
                    CreatedAt = reader.GetDateTime(24)
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
