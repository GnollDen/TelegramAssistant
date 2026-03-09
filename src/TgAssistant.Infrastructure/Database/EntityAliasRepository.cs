using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class EntityAliasRepository : IEntityAliasRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public EntityAliasRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertAliasAsync(
        Guid entityId,
        string alias,
        long? sourceMessageId = null,
        float confidence = 1.0f,
        CancellationToken ct = default)
    {
        var raw = alias?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var norm = Normalize(raw);
        if (norm.Length == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO entity_aliases (entity_id, alias, alias_norm, source_message_id, confidence, created_at, updated_at)
            VALUES ({entityId}, {raw}, {norm}, {sourceMessageId}, {confidence}, NOW(), NOW())
            ON CONFLICT (entity_id, alias_norm)
            DO UPDATE SET
                alias = EXCLUDED.alias,
                source_message_id = COALESCE(EXCLUDED.source_message_id, entity_aliases.source_message_id),
                confidence = GREATEST(entity_aliases.confidence, EXCLUDED.confidence),
                updated_at = NOW()
            """, ct);
    }

    private static string Normalize(string alias) => alias.Trim().ToLowerInvariant();
}
