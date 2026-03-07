using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Infrastructure.Database;

public class StickerCacheRepository : IStickerCacheRepository
{
    private readonly string _connectionString;

    public StickerCacheRepository(IOptions<DatabaseSettings> settings)
    {
        _connectionString = settings.Value.ConnectionString;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<StickerCacheItem?> GetByHashAsync(string contentHash, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<StickerCacheItem>(new CommandDefinition(
            """
            SELECT content_hash AS ContentHash,
                   description AS Description,
                   model AS Model
            FROM sticker_cache
            WHERE content_hash = @ContentHash
            """,
            new { ContentHash = contentHash },
            cancellationToken: ct));
    }

    public async Task UpsertAsync(string contentHash, string description, string model, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO sticker_cache (content_hash, description, model, hit_count, last_used_at)
            VALUES (@ContentHash, @Description, @Model, 1, NOW())
            ON CONFLICT (content_hash) DO UPDATE
            SET description = EXCLUDED.description,
                model = EXCLUDED.model,
                hit_count = sticker_cache.hit_count + 1,
                last_used_at = NOW()
            """,
            new
            {
                ContentHash = contentHash,
                Description = description,
                Model = model
            },
            cancellationToken: ct));
    }
}
