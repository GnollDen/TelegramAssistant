using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class StickerCacheRepository : IStickerCacheRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public StickerCacheRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<StickerCacheItem?> GetByHashAsync(string contentHash, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.StickerCache.FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);
        if (row == null)
        {
            return null;
        }

        row.HitCount += 1;
        row.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new StickerCacheItem
        {
            ContentHash = row.ContentHash,
            Description = row.Description,
            Model = row.Model
        };
    }

    public async Task UpsertAsync(string contentHash, string description, string model, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.StickerCache.FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);
        if (row == null)
        {
            db.StickerCache.Add(new DbStickerCache
            {
                ContentHash = contentHash,
                Description = description,
                Model = model,
                HitCount = 1,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.Description = description;
            row.Model = model;
            row.HitCount += 1;
            row.LastUsedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
