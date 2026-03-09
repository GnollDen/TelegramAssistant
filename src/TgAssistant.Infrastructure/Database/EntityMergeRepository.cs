using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class EntityMergeRepository : IEntityMergeRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public EntityMergeRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<int> RefreshAliasMergeCandidatesAsync(int maxCandidates, CancellationToken ct = default)
    {
        var limit = Math.Max(1, maxCandidates);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH pairs AS (
                SELECT
                    LEAST(a1.entity_id, a2.entity_id) AS entity_low_id,
                    GREATEST(a1.entity_id, a2.entity_id) AS entity_high_id,
                    a1.alias_norm AS alias_norm,
                    LEAST(COUNT(*), 1000)::int AS evidence_count
                FROM entity_aliases a1
                JOIN entity_aliases a2
                  ON a1.alias_norm = a2.alias_norm
                 AND a1.entity_id < a2.entity_id
                GROUP BY LEAST(a1.entity_id, a2.entity_id), GREATEST(a1.entity_id, a2.entity_id), a1.alias_norm
                ORDER BY evidence_count DESC
                LIMIT {limit}
            )
            INSERT INTO entity_merge_candidates (entity_low_id, entity_high_id, alias_norm, evidence_count, status, created_at, updated_at)
            SELECT p.entity_low_id, p.entity_high_id, p.alias_norm, p.evidence_count, 0, NOW(), NOW()
            FROM pairs p
            ON CONFLICT (entity_low_id, entity_high_id, alias_norm)
            DO UPDATE SET
                evidence_count = GREATEST(entity_merge_candidates.evidence_count, EXCLUDED.evidence_count),
                updated_at = NOW()
            """, ct);

        return affected;
    }

    public async Task<int> RecomputeScoresAsync(int limit, CancellationToken ct = default)
    {
        var cappedLimit = Math.Max(1, limit);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH to_score AS (
                SELECT id
                FROM entity_merge_candidates
                WHERE status = 0
                ORDER BY updated_at DESC
                LIMIT {cappedLimit}
            )
            UPDATE entity_merge_candidates c
            SET
                score = GREATEST(0, LEAST(1,
                    0.10
                    + LEAST(c.evidence_count, 5) * 0.12
                    + CASE WHEN LENGTH(c.alias_norm) >= 6 THEN 0.08 ELSE 0 END
                    + CASE WHEN e1.telegram_user_id IS NOT NULL AND e2.telegram_user_id IS NOT NULL AND e1.telegram_user_id = e2.telegram_user_id THEN 0.40 ELSE 0 END
                    + CASE WHEN e1.actor_key IS NOT NULL AND e2.actor_key IS NOT NULL AND e1.actor_key = e2.actor_key THEN 0.50 ELSE 0 END
                )),
                review_priority = CASE
                    WHEN e1.actor_key IS NOT NULL AND e2.actor_key IS NOT NULL AND e1.actor_key = e2.actor_key THEN 0
                    WHEN e1.telegram_user_id IS NOT NULL AND e2.telegram_user_id IS NOT NULL AND e1.telegram_user_id = e2.telegram_user_id THEN 0
                    WHEN c.evidence_count >= 3 THEN 1
                    ELSE 2
                END,
                updated_at = NOW()
            FROM entities e1, entities e2
            WHERE c.id IN (SELECT id FROM to_score)
              AND c.entity_low_id = e1.id
              AND c.entity_high_id = e2.id
            """, ct);

        return affected;
    }

    public async Task<List<EntityMergeCandidate>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.EntityMergeCandidates.AsNoTracking()
            .Where(x => x.Status == (short)MergeDecision.Pending)
            .OrderBy(x => x.ReviewPriority)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.EvidenceCount)
            .ThenByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(x => new EntityMergeCandidate
        {
            Id = x.Id,
            EntityLowId = x.EntityLowId,
            EntityHighId = x.EntityHighId,
            AliasNorm = x.AliasNorm,
            EvidenceCount = x.EvidenceCount,
            Score = x.Score,
            ReviewPriority = x.ReviewPriority,
            Status = x.Status
        }).ToList();
    }

    public async Task<List<EntityMergeReviewItem>> GetReviewQueueAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.EntityMergeCandidates.AsNoTracking()
            .Where(c => c.Status == (short)MergeDecision.Pending)
            .Join(db.Entities.AsNoTracking(),
                c => c.EntityLowId,
                e => e.Id,
                (c, low) => new { c, low })
            .Join(db.Entities.AsNoTracking(),
                x => x.c.EntityHighId,
                e => e.Id,
                (x, high) => new EntityMergeReviewItem
                {
                    CandidateId = x.c.Id,
                    EntityLowId = x.c.EntityLowId,
                    EntityHighId = x.c.EntityHighId,
                    EntityLowName = x.low.Name,
                    EntityHighName = high.Name,
                    AliasNorm = x.c.AliasNorm,
                    EvidenceCount = x.c.EvidenceCount,
                    Score = x.c.Score,
                    ReviewPriority = x.c.ReviewPriority
                })
            .OrderBy(x => x.ReviewPriority)
            .ThenByDescending(x => x.Score)
            .ThenByDescending(x => x.EvidenceCount)
            .Take(limit);

        return await query.ToListAsync(ct);
    }

    public async Task<EntityMergeCandidate?> GetByIdAsync(long candidateId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var x = await db.EntityMergeCandidates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == candidateId, ct);
        if (x == null)
        {
            return null;
        }

        return new EntityMergeCandidate
        {
            Id = x.Id,
            EntityLowId = x.EntityLowId,
            EntityHighId = x.EntityHighId,
            AliasNorm = x.AliasNorm,
            EvidenceCount = x.EvidenceCount,
            Score = x.Score,
            ReviewPriority = x.ReviewPriority,
            Status = x.Status
        };
    }

    public async Task MarkDecisionAsync(long candidateId, MergeDecision decision, string? note = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.EntityMergeCandidates.FirstOrDefaultAsync(x => x.Id == candidateId, ct);
        if (row == null)
        {
            return;
        }

        row.Status = (short)decision;
        row.DecisionNote = string.IsNullOrWhiteSpace(note) ? row.DecisionNote : note.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO entity_merge_decisions (candidate_id, entity_low_id, entity_high_id, alias_norm, decision, note, created_at)
            VALUES ({candidateId}, {row.EntityLowId}, {row.EntityHighId}, {row.AliasNorm}, {(short)decision}, {note}, NOW())
            """, ct);
        await db.SaveChangesAsync(ct);
    }
}
