using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class EntityRepository : IEntityRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public EntityRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Entity> UpsertAsync(Entity entity, CancellationToken ct = default)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await UpsertCoreAsync(ambientDb, entity, ct);
        }

        var retries = 0;
        while (true)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await UpsertCoreAsync(db, entity, ct);
                await tx.CommitAsync(ct);
                return entity;
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && retries < 2)
            {
                retries++;
                await tx.RollbackAsync(ct);
            }
        }
    }

    private static async Task<Entity> UpsertCoreAsync(TgAssistantDbContext db, Entity entity, CancellationToken ct)
    {
        var lockKey = BuildEntityLockKey(entity);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", ct);
        var row = await FindMatchForUpsertAsync(db, entity, ct);

        if (row == null)
        {
            row = new DbEntity
            {
                Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id,
                Name = entity.Name,
                Type = (short)entity.Type,
                Aliases = entity.Aliases.ToArray(),
                ActorKey = string.IsNullOrWhiteSpace(entity.ActorKey) ? null : entity.ActorKey.Trim(),
                TelegramUserId = entity.TelegramUserId,
                TelegramUsername = entity.TelegramUsername,
                Metadata = JsonDocument.Parse("{}"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Entities.Add(row);
        }
        else
        {
            var incomingName = entity.Name.Trim();
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                row.Name = incomingName;
            }
            else if (!string.IsNullOrWhiteSpace(incomingName) &&
                     !string.Equals(row.Name, incomingName, StringComparison.OrdinalIgnoreCase))
            {
                row.Aliases = row.Aliases
                    .Append(incomingName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            row.Type = (short)entity.Type;
            if (entity.Aliases.Count > 0)
            {
                row.Aliases = row.Aliases
                    .Concat(entity.Aliases)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            row.ActorKey = string.IsNullOrWhiteSpace(row.ActorKey) ? entity.ActorKey?.Trim() : row.ActorKey;
            row.TelegramUserId = entity.TelegramUserId ?? row.TelegramUserId;
            row.TelegramUsername = entity.TelegramUsername ?? row.TelegramUsername;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        entity.Id = row.Id;
        entity.CreatedAt = row.CreatedAt;
        entity.UpdatedAt = row.UpdatedAt;
        return entity;
    }

    private static async Task<DbEntity?> FindMatchForUpsertAsync(TgAssistantDbContext db, Entity entity, CancellationToken ct)
    {
        DbEntity? row = null;

        if (!string.IsNullOrWhiteSpace(entity.ActorKey))
        {
            var actorKey = entity.ActorKey.Trim();
            row = await db.Entities.FirstOrDefaultAsync(x => x.ActorKey == actorKey, ct);
        }

        if (entity.TelegramUserId.HasValue)
        {
            row ??= await db.Entities.FirstOrDefaultAsync(x => x.TelegramUserId == entity.TelegramUserId, ct);
        }

        var normalizedName = entity.Name.Trim().ToLowerInvariant();
        row ??= await db.Entities.FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedName, ct);
        return row;
    }

    private static string BuildEntityLockKey(Entity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ActorKey))
        {
            return "actor:" + entity.ActorKey.Trim().ToLowerInvariant();
        }

        if (entity.TelegramUserId.HasValue)
        {
            return "tg:" + entity.TelegramUserId.Value;
        }

        return "name:" + entity.Name.Trim().ToLowerInvariant();
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    public async Task<Entity?> FindByTelegramIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<Entity?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<Entity?> FindByActorKeyAsync(string actorKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorKey))
        {
            return null;
        }

        var normalized = actorKey.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(x => x.ActorKey == normalized, ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<Entity?> FindByNameOrAliasAsync(string name, CancellationToken ct = default)
    {
        var normalized = name.Trim().ToLowerInvariant();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(
            x => x.Name.ToLower() == normalized
                 || x.Aliases.Any(a => a.ToLower() == normalized)
                 || db.EntityAliases.Any(a => a.EntityId == x.Id && a.AliasNorm == normalized),
            ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<Entity?> FindBestByNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var exact = await FindByNameOrAliasAsync(name, ct);
        if (exact != null)
        {
            return exact;
        }

        var normalized = name.Trim();
        var normalizedLower = normalized.ToLowerInvariant();
        var likePattern = $"%{normalized}%";

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Entities
            .AsNoTracking()
            .Where(x =>
                EF.Functions.ILike(x.Name, likePattern) ||
                db.EntityAliases.Any(a => a.EntityId == x.Id && EF.Functions.ILike(a.Alias, likePattern)))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(120)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            rows = await db.Entities
                .AsNoTracking()
                .Where(x => x.Aliases.Any(a => a.ToLower().Contains(normalizedLower)))
                .OrderByDescending(x => x.UpdatedAt)
                .Take(120)
                .ToListAsync(ct);
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var entityIds = rows.Select(x => x.Id).ToList();
        var aliasRows = await db.EntityAliases
            .AsNoTracking()
            .Where(x => entityIds.Contains(x.EntityId))
            .Select(x => new { x.EntityId, x.AliasNorm })
            .ToListAsync(ct);

        var aliasLookup = aliasRows
            .GroupBy(x => x.EntityId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyCollection<string>)x
                    .Select(v => v.AliasNorm)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToArray());

        var best = rows
            .Select(row => new
            {
                Row = row,
                Rank = CalculateEntityNameMatchRank(
                    row,
                    aliasLookup.GetValueOrDefault(row.Id) ?? Array.Empty<string>(),
                    normalizedLower)
            })
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.Row.UpdatedAt)
            .FirstOrDefault();

        return best == null ? null : ToDomain(best.Row);
    }

    public async Task<List<Entity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Entities.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<List<Entity>> GetUpdatedSinceAsync(DateTime sinceUtc, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Entities
            .AsNoTracking()
            .Where(x => x.UpdatedAt >= sinceUtc)
            .OrderBy(x => x.UpdatedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task MergeIntoAsync(Guid targetEntityId, Guid sourceEntityId, CancellationToken ct = default)
    {
        if (targetEntityId == sourceEntityId)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var target = await db.Entities.FirstOrDefaultAsync(x => x.Id == targetEntityId, ct);
        var source = await db.Entities.FirstOrDefaultAsync(x => x.Id == sourceEntityId, ct);
        if (target == null || source == null)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        var mergedAliases = target.Aliases
            .Concat(source.Aliases)
            .Append(source.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        target.Aliases = mergedAliases;
        target.TelegramUserId ??= source.TelegramUserId;
        target.TelegramUsername ??= source.TelegramUsername;
        target.ActorKey ??= source.ActorKey;
        target.UpdatedAt = DateTime.UtcNow;

        // Preserve target truth on conflicting current facts: source current facts become historical.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE facts s
            SET is_current = FALSE,
                valid_until = COALESCE(valid_until, NOW()),
                updated_at = NOW()
            FROM facts t
            WHERE s.entity_id = {sourceEntityId}
              AND s.is_current = TRUE
              AND t.entity_id = {targetEntityId}
              AND t.is_current = TRUE
              AND s.category = t.category
              AND s.key = t.key
              AND s.value <> t.value
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE facts SET entity_id = {targetEntityId} WHERE entity_id = {sourceEntityId};
            UPDATE relationships SET from_entity_id = {targetEntityId} WHERE from_entity_id = {sourceEntityId};
            UPDATE relationships SET to_entity_id = {targetEntityId} WHERE to_entity_id = {sourceEntityId};
            UPDATE daily_summaries SET entity_id = {targetEntityId} WHERE entity_id = {sourceEntityId};
            UPDATE analysis_sessions SET entity_id = {targetEntityId} WHERE entity_id = {sourceEntityId};
            UPDATE entity_aliases SET entity_id = {targetEntityId} WHERE entity_id = {sourceEntityId};
            UPDATE intelligence_observations SET entity_id = {targetEntityId} WHERE entity_id = {sourceEntityId};
            UPDATE intelligence_claims SET entity_id = {targetEntityId} WHERE entity_id = {sourceEntityId};
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM entity_aliases a
            USING entity_aliases d
            WHERE a.id > d.id
              AND a.entity_id = d.entity_id
              AND a.alias_norm = d.alias_norm
            """, ct);

        // Deduplicate exact facts after move.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM facts a
            USING facts d
            WHERE a.id > d.id
              AND a.entity_id = d.entity_id
              AND a.category = d.category
              AND a.key = d.key
              AND a.value = d.value
              AND a.is_current = d.is_current
            """, ct);

        // Deduplicate relationships after move.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM relationships a
            USING relationships d
            WHERE a.id > d.id
              AND a.from_entity_id = d.from_entity_id
              AND a.to_entity_id = d.to_entity_id
              AND a.type = d.type
              AND COALESCE(a.source_message_id, -1) = COALESCE(d.source_message_id, -1)
            """, ct);

        db.Entities.Remove(source);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static int CalculateEntityNameMatchRank(
        DbEntity row,
        IReadOnlyCollection<string> aliasNorms,
        string queryNorm)
    {
        var nameNorm = row.Name.Trim().ToLowerInvariant();
        if (nameNorm == queryNorm)
        {
            return 0;
        }

        if (row.Aliases.Any(a => string.Equals(a.Trim(), queryNorm, StringComparison.OrdinalIgnoreCase)) ||
            aliasNorms.Contains(queryNorm, StringComparer.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (nameNorm.StartsWith(queryNorm, StringComparison.Ordinal))
        {
            return 2;
        }

        if (row.Aliases.Any(a => a.Trim().StartsWith(queryNorm, StringComparison.OrdinalIgnoreCase)) ||
            aliasNorms.Any(a => a.StartsWith(queryNorm, StringComparison.Ordinal)))
        {
            return 3;
        }

        if (nameNorm.Contains(queryNorm, StringComparison.Ordinal))
        {
            return 4;
        }

        if (row.Aliases.Any(a => a.Trim().Contains(queryNorm, StringComparison.OrdinalIgnoreCase)) ||
            aliasNorms.Any(a => a.Contains(queryNorm, StringComparison.Ordinal)))
        {
            return 5;
        }

        return 6;
    }

    private static Entity ToDomain(DbEntity row)
    {
        return new Entity
        {
            Id = row.Id,
            Name = row.Name,
            Type = (EntityType)row.Type,
            Aliases = row.Aliases.ToList(),
            ActorKey = row.ActorKey,
            TelegramUserId = row.TelegramUserId,
            TelegramUsername = row.TelegramUsername,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}
