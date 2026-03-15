using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
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
            db.IntelligenceClaims.AddRange(claims.Select(c => new DbIntelligenceClaim
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
}
