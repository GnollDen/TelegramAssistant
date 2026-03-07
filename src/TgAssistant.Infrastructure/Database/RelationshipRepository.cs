using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class RelationshipRepository : IRelationshipRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public RelationshipRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Relationship> UpsertAsync(Relationship relationship, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Relationships.FirstOrDefaultAsync(x => x.FromEntityId == relationship.FromEntityId && x.ToEntityId == relationship.ToEntityId && x.Type == relationship.Type, ct);
        if (existing == null)
        {
            db.Relationships.Add(new DbRelationship
            {
                Id = relationship.Id == Guid.Empty ? Guid.NewGuid() : relationship.Id,
                FromEntityId = relationship.FromEntityId,
                ToEntityId = relationship.ToEntityId,
                Type = relationship.Type,
                Status = (short)relationship.Status,
                Confidence = relationship.Confidence,
                ContextText = relationship.ContextText,
                SourceMessageId = relationship.SourceMessageId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Status = (short)relationship.Status;
            existing.Confidence = Math.Max(existing.Confidence, relationship.Confidence);
            existing.ContextText = relationship.ContextText ?? existing.ContextText;
            existing.SourceMessageId = relationship.SourceMessageId ?? existing.SourceMessageId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return relationship;
    }

    public async Task<List<Relationship>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Relationships.AsNoTracking().Where(x => x.FromEntityId == entityId || x.ToEntityId == entityId).ToListAsync(ct);
        return rows.Select(x => new Relationship
        {
            Id = x.Id,
            FromEntityId = x.FromEntityId,
            ToEntityId = x.ToEntityId,
            Type = x.Type,
            Status = (ConfidenceStatus)x.Status,
            Confidence = x.Confidence,
            ContextText = x.ContextText,
            SourceMessageId = x.SourceMessageId,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }

    public async Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Relationships.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return;
        row.Status = (short)status;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
