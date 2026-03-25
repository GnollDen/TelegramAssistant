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
        return await WithDbContextAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var rowId = relationship.Id == Guid.Empty ? Guid.NewGuid() : relationship.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO relationships (
                    id,
                    from_entity_id,
                    to_entity_id,
                    type,
                    status,
                    confidence,
                    context_text,
                    source_message_id,
                    created_at,
                    updated_at
                )
                VALUES (
                    {rowId},
                    {relationship.FromEntityId},
                    {relationship.ToEntityId},
                    {relationship.Type},
                    {(short)relationship.Status},
                    {relationship.Confidence},
                    {relationship.ContextText},
                    {relationship.SourceMessageId},
                    {now},
                    {now}
                )
                ON CONFLICT (from_entity_id, to_entity_id, type)
                DO UPDATE
                SET status = EXCLUDED.status,
                    confidence = GREATEST(relationships.confidence, EXCLUDED.confidence),
                    context_text = COALESCE(EXCLUDED.context_text, relationships.context_text),
                    source_message_id = COALESCE(EXCLUDED.source_message_id, relationships.source_message_id),
                    updated_at = EXCLUDED.updated_at;
                """, ct);

            var persisted = await db.Relationships
                .AsNoTracking()
                .FirstAsync(
                    x => x.FromEntityId == relationship.FromEntityId
                      && x.ToEntityId == relationship.ToEntityId
                      && x.Type == relationship.Type,
                    ct);

            return new Relationship
            {
                Id = persisted.Id,
                FromEntityId = persisted.FromEntityId,
                ToEntityId = persisted.ToEntityId,
                Type = persisted.Type,
                Status = (ConfidenceStatus)persisted.Status,
                Confidence = persisted.Confidence,
                ContextText = persisted.ContextText,
                SourceMessageId = persisted.SourceMessageId,
                CreatedAt = persisted.CreatedAt,
                UpdatedAt = persisted.UpdatedAt
            };
        }, ct);
    }

    public async Task<List<Relationship>> GetByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
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
        }, ct);
    }

    public async Task<List<EntityRelationshipInfo>> GetByEntityWithNamesAsync(Guid entityId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await (from relationship in db.Relationships.AsNoTracking()
                              join fromEntity in db.Entities.AsNoTracking() on relationship.FromEntityId equals fromEntity.Id
                              join toEntity in db.Entities.AsNoTracking() on relationship.ToEntityId equals toEntity.Id
                              where relationship.FromEntityId == entityId || relationship.ToEntityId == entityId
                              orderby relationship.UpdatedAt descending, relationship.CreatedAt descending
                              select new EntityRelationshipInfo
                              {
                                  RelationshipId = relationship.Id,
                                  FromEntityId = relationship.FromEntityId,
                                  FromEntityName = fromEntity.Name,
                                  ToEntityId = relationship.ToEntityId,
                                  ToEntityName = toEntity.Name,
                                  Type = relationship.Type,
                                  Status = (ConfidenceStatus)relationship.Status,
                                  Confidence = relationship.Confidence,
                                  ContextText = relationship.ContextText,
                                  SourceMessageId = relationship.SourceMessageId,
                                  CreatedAt = relationship.CreatedAt,
                                  UpdatedAt = relationship.UpdatedAt
                              })
                .ToListAsync(ct);

            return rows;
        }, ct);
    }

    public async Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default)
    {
        await WithDbContextAsync(async db =>
        {
            var row = await db.Relationships.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) return;
            row.Status = (short)status;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private async Task<TResult> WithDbContextAsync<TResult>(
        Func<TgAssistantDbContext, Task<TResult>> action,
        CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await action(ambientDb);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await action(db);
    }

    private async Task WithDbContextAsync(
        Func<TgAssistantDbContext, Task> action,
        CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            await action(ambientDb);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await action(db);
    }
}
