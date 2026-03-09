using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class MessageExtractionRepository : IMessageExtractionRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

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
