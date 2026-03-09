using Microsoft.EntityFrameworkCore;
using Npgsql;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class EmbeddingRepository : IEmbeddingRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private const string UpsertSql = """
        INSERT INTO text_embeddings(owner_type, owner_id, source_text, model, vector, created_at)
        VALUES (@owner_type, @owner_id, @source_text, @model, @vector, NOW())
        ON CONFLICT (owner_type, owner_id, model)
        DO UPDATE SET
            source_text = EXCLUDED.source_text,
            vector = EXCLUDED.vector,
            created_at = NOW();
        """;
    private const string NearestSql = """
        SELECT
            id,
            owner_type,
            owner_id,
            source_text,
            model,
            vector,
            created_at
        FROM text_embeddings
        WHERE owner_type = @owner_type
          AND vector IS NOT NULL
        ORDER BY (('[' || array_to_string(vector, ',') || ']')::vector <-> @query_vector::vector) ASC
        LIMIT @limit;
        """;

    public EmbeddingRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertAsync(TextEmbedding embedding, CancellationToken ct = default)
    {
        var ownerType = embedding.OwnerType.Trim();
        var ownerId = embedding.OwnerId.Trim();
        var model = embedding.Model.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = UpsertSql;
        cmd.Parameters.AddWithValue("owner_type", ownerType);
        cmd.Parameters.AddWithValue("owner_id", ownerId);
        cmd.Parameters.AddWithValue("source_text", embedding.SourceText ?? string.Empty);
        cmd.Parameters.AddWithValue("model", model);
        cmd.Parameters.AddWithValue("vector", embedding.Vector);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<TextEmbedding>> FindNearestAsync(string ownerType, float[] vector, int limit = 10, CancellationToken ct = default)
    {
        var normalizedOwnerType = ownerType.Trim();
        var safeLimit = Math.Max(1, limit);
        var queryVector = $"[{string.Join(",", vector.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]";
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync(ct);
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = NearestSql;
            cmd.Parameters.AddWithValue("owner_type", normalizedOwnerType);
            cmd.Parameters.AddWithValue("query_vector", queryVector);
            cmd.Parameters.AddWithValue("limit", safeLimit);

            var result = new List<TextEmbedding>(safeLimit);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new TextEmbedding
                {
                    Id = reader.GetInt64(0),
                    OwnerType = reader.GetString(1),
                    OwnerId = reader.GetString(2),
                    SourceText = reader.GetString(3),
                    Model = reader.GetString(4),
                    Vector = (float[])reader.GetValue(5),
                    CreatedAt = reader.GetDateTime(6)
                });
            }

            return result;
        }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "42704" or "0A000")
        {
            return await FindNearestFallbackAsync(normalizedOwnerType, vector, safeLimit, ct);
        }
    }

    private async Task<List<TextEmbedding>> FindNearestFallbackAsync(string ownerType, float[] vector, int limit, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var candidates = await db.TextEmbeddings
            .AsNoTracking()
            .Where(x => x.OwnerType == ownerType)
            .Take(Math.Max(50, limit * 10))
            .ToListAsync(ct);

        return candidates
            .Select(x => new { Row = x, Score = Cosine(vector, x.Vector) })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => new TextEmbedding
            {
                Id = x.Row.Id,
                OwnerType = x.Row.OwnerType,
                OwnerId = x.Row.OwnerId,
                SourceText = x.Row.SourceText,
                Model = x.Row.Model,
                Vector = x.Row.Vector,
                CreatedAt = x.Row.CreatedAt
            })
            .ToList();
    }

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return -1f;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0)
        {
            return -1f;
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
