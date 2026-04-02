using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Core.Normalization;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class IntelligenceRepository : IIntelligenceRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public IntelligenceRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task ReplaceMessageIntelligenceAsync(
        long messageId,
        IReadOnlyCollection<IntelligenceObservation> observations,
        IReadOnlyCollection<IntelligenceClaim> claims,
        CancellationToken ct = default)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            await ReplaceInternalAsync(ambientDb, messageId, observations, claims, ct);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ReplaceInternalAsync(db, messageId, observations, claims, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<IntelligenceClaim>> GetClaimsByMessageAsync(long messageId, CancellationToken ct = default)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await GetClaimsByMessageInternalAsync(ambientDb, messageId, ct);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await GetClaimsByMessageInternalAsync(db, messageId, ct);
    }

    public async Task<List<IntelligenceClaim>> GetClaimsByMessagesAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default)
    {
        if (messageIds.Count == 0)
        {
            return [];
        }

        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await GetClaimsByMessagesInternalAsync(ambientDb, messageIds, ct);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await GetClaimsByMessagesInternalAsync(db, messageIds, ct);
    }

    public async Task<List<IntelligenceClaim>> GetClaimsByChatAndPeriodAsync(
        long chatId,
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct = default)
    {
        var safeLimit = Math.Max(1, limit);
        if (toUtc < fromUtc)
        {
            return [];
        }

        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await GetClaimsByChatAndPeriodInternalAsync(ambientDb, chatId, fromUtc, toUtc, safeLimit, ct);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await GetClaimsByChatAndPeriodInternalAsync(db, chatId, fromUtc, toUtc, safeLimit, ct);
    }

    private static async Task ReplaceInternalAsync(
        TgAssistantDbContext db,
        long messageId,
        IReadOnlyCollection<IntelligenceObservation> observations,
        IReadOnlyCollection<IntelligenceClaim> claims,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM intelligence_observations WHERE message_id = {messageId};",
            ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM intelligence_claims WHERE message_id = {messageId};",
            ct);

        if (observations.Count > 0)
        {
            db.IntelligenceObservations.AddRange(observations.Select(o => new DbIntelligenceObservation
            {
                MessageId = messageId,
                EntityId = o.EntityId,
                SubjectName = o.SubjectName,
                ObservationType = o.ObservationType,
                ObjectName = o.ObjectName,
                Value = o.Value,
                Evidence = o.Evidence,
                Confidence = o.Confidence,
                CreatedAt = o.CreatedAt == default ? DateTime.UtcNow : o.CreatedAt
            }));
        }

        if (claims.Count > 0)
        {
            var deduplicatedClaims = claims
                .GroupBy(
                    c => new
                    {
                        c.EntityId,
                        Category = (c.Category ?? string.Empty).Trim().ToLowerInvariant(),
                        Key = (c.Key ?? string.Empty).Trim().ToLowerInvariant(),
                        CanonValue = EntityAliasNormalizer.NormalizeForFactValue(c.Value)
                    })
                .Select(group => group
                    .OrderByDescending(x => x.Confidence)
                    .ThenByDescending(x => x.CreatedAt)
                    .First())
                .ToList();

            db.IntelligenceClaims.AddRange(deduplicatedClaims.Select(c => new DbIntelligenceClaim
            {
                MessageId = messageId,
                EntityId = c.EntityId,
                EntityName = c.EntityName,
                ClaimType = c.ClaimType,
                Category = c.Category,
                Key = c.Key,
                Value = c.Value,
                Evidence = c.Evidence,
                Status = (short)c.Status,
                Confidence = c.Confidence,
                CreatedAt = c.CreatedAt == default ? DateTime.UtcNow : c.CreatedAt
            }));
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<IntelligenceClaim>> GetClaimsByMessageInternalAsync(
        TgAssistantDbContext db,
        long messageId,
        CancellationToken ct)
    {
        var rows = await db.IntelligenceClaims
            .AsNoTracking()
            .Where(x => x.MessageId == messageId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    private static async Task<List<IntelligenceClaim>> GetClaimsByMessagesInternalAsync(
        TgAssistantDbContext db,
        IReadOnlyCollection<long> messageIds,
        CancellationToken ct)
    {
        var rows = await db.IntelligenceClaims
            .AsNoTracking()
            .Where(x => messageIds.Contains(x.MessageId))
            .OrderByDescending(x => x.Confidence)
            .ThenBy(x => x.MessageId)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    private static IntelligenceClaim ToDomain(DbIntelligenceClaim row)
    {
        return new IntelligenceClaim
        {
            Id = row.Id,
            MessageId = row.MessageId,
            EntityId = row.EntityId,
            EntityName = row.EntityName,
            ClaimType = row.ClaimType,
            Category = row.Category,
            Key = row.Key,
            Value = row.Value,
            Evidence = row.Evidence,
            Status = (ConfidenceStatus)row.Status,
            Confidence = row.Confidence,
            CreatedAt = row.CreatedAt
        };
    }

    private static async Task<List<IntelligenceClaim>> GetClaimsByChatAndPeriodInternalAsync(
        TgAssistantDbContext db,
        long chatId,
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct)
    {
        var rows = await db.IntelligenceClaims
            .AsNoTracking()
            .Join(
                db.Messages.AsNoTracking(),
                claim => claim.MessageId,
                message => message.Id,
                (claim, message) => new { claim, message.ChatId, message.Timestamp })
            .Where(x => x.ChatId == chatId && x.Timestamp >= fromUtc && x.Timestamp <= toUtc)
            .OrderByDescending(x => x.claim.Confidence)
            .ThenByDescending(x => x.claim.MessageId)
            .ThenByDescending(x => x.claim.Id)
            .Take(limit)
            .Select(x => x.claim)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }
}
