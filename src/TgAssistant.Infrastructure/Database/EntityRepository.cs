using Microsoft.EntityFrameworkCore;
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        DbEntity? row = null;

        if (entity.TelegramUserId.HasValue)
        {
            row = await db.Entities.FirstOrDefaultAsync(x => x.TelegramUserId == entity.TelegramUserId, ct);
        }

        row ??= await db.Entities.FirstOrDefaultAsync(x => x.Name.ToLower() == entity.Name.ToLower(), ct);

        if (row == null)
        {
            row = new DbEntity
            {
                Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id,
                Name = entity.Name,
                Type = (short)entity.Type,
                Aliases = entity.Aliases.ToArray(),
                TelegramUserId = entity.TelegramUserId,
                TelegramUsername = entity.TelegramUsername,
                Metadata = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Entities.Add(row);
        }
        else
        {
            row.Name = entity.Name;
            row.Type = (short)entity.Type;
            row.Aliases = entity.Aliases.ToArray();
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

    public async Task<Entity?> FindByTelegramIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(x => x.TelegramUserId == telegramUserId, ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<Entity?> FindByNameOrAliasAsync(string name, CancellationToken ct = default)
    {
        var normalized = name.Trim().ToLowerInvariant();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Entities.AsNoTracking().FirstOrDefaultAsync(x => x.Name.ToLower() == normalized || x.Aliases.Any(a => a.ToLower() == normalized), ct);
        return row == null ? null : ToDomain(row);
    }

    public async Task<List<Entity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Entities.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static Entity ToDomain(DbEntity row)
    {
        return new Entity
        {
            Id = row.Id,
            Name = row.Name,
            Type = (EntityType)row.Type,
            Aliases = row.Aliases.ToList(),
            TelegramUserId = row.TelegramUserId,
            TelegramUsername = row.TelegramUsername,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}
