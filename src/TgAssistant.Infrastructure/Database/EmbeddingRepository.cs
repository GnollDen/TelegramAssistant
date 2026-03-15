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
        VALUES (@owner_type, @owner_id, @source_text, @model, (('[' || array_to_string(@vector::real[], ',') || ']')::vector), NOW())
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
            vector::real[] AS vector,
            created_at
        FROM text_embeddings
        WHERE owner_type = @owner_type
          AND model = @model
          AND vector IS NOT NULL
        ORDER BY (vector <=> @query_vector::vector) ASC
        LIMIT @limit;
        """;
    private const string GetByOwnerSql = """
        SELECT
            id,
            owner_type,
            owner_id,
            source_text,
            model,
            vector::real[] AS vector,
            created_at
        FROM text_embeddings
        WHERE owner_type = @owner_type
          AND owner_id = @owner_id
        ORDER BY created_at DESC
        LIMIT 1;
        """;
    private const string GetByOwnerWithModelSql = """
        SELECT
            id,
            owner_type,
            owner_id,
            source_text,
            model,
            vector::real[] AS vector,
            created_at
        FROM text_embeddings
        WHERE owner_type = @owner_type
          AND owner_id = @owner_id
          AND model = @model
        ORDER BY created_at DESC
        LIMIT 1;
        """;
    private const string FallbackCandidatesSql = """
        SELECT
            id,
            owner_type,
            owner_id,
            source_text,
            model,
            vector::real[] AS vector,
            created_at
        FROM text_embeddings
        WHERE owner_type = @owner_type
          AND model = @model
          AND vector IS NOT NULL
        ORDER BY created_at DESC
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

    public async Task<List<TextEmbedding>> FindNearestAsync(string ownerType, string model, float[] vector, int limit = 10, CancellationToken ct = default)
    {
        var normalizedOwnerType = ownerType.Trim();
        var normalizedModel = model.Trim();
        if (string.IsNullOrWhiteSpace(normalizedOwnerType) || string.IsNullOrWhiteSpace(normalizedModel) || vector.Length == 0)
        {
            return new List<TextEmbedding>();
        }

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
            cmd.Parameters.AddWithValue("model", normalizedModel);
            cmd.Parameters.AddWithValue("query_vector", queryVector);
            cmd.Parameters.AddWithValue("limit", safeLimit);

            var result = new List<TextEmbedding>(safeLimit);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(ReadEmbedding(reader));
            }

            return result;
        }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "42704" or "0A000")
        {
            return await FindNearestFallbackAsync(normalizedOwnerType, normalizedModel, vector, safeLimit, ct);
        }
    }

    public async Task<TextEmbedding?> GetByOwnerAsync(string ownerType, string ownerId, string? model = null, CancellationToken ct = default)
    {
        var normalizedType = ownerType.Trim();
        var normalizedId = ownerId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType) || string.IsNullOrWhiteSpace(normalizedId))
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        var hasModel = !string.IsNullOrWhiteSpace(model);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = hasModel ? GetByOwnerWithModelSql : GetByOwnerSql;
        cmd.Parameters.AddWithValue("owner_type", normalizedType);
        cmd.Parameters.AddWithValue("owner_id", normalizedId);
        if (hasModel)
        {
            cmd.Parameters.AddWithValue("model", model!.Trim());
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadEmbedding(reader);
    }

    private async Task<List<TextEmbedding>> FindNearestFallbackAsync(string ownerType, string model, float[] vector, int limit, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = FallbackCandidatesSql;
        cmd.Parameters.AddWithValue("owner_type", ownerType);
        cmd.Parameters.AddWithValue("model", model);
        cmd.Parameters.AddWithValue("limit", Math.Max(50, limit * 10));

        var candidates = new List<TextEmbedding>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                candidates.Add(ReadEmbedding(reader));
            }
        }

        return candidates
            .Select(x => new { Row = x, Score = Cosine(vector, x.Vector) })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Row)
            .ToList();
    }

    private static TextEmbedding ReadEmbedding(NpgsqlDataReader reader)
    {
        return new TextEmbedding
        {
            Id = reader.GetInt64(0),
            OwnerType = reader.GetString(1),
            OwnerId = reader.GetString(2),
            SourceText = reader.GetString(3),
            Model = reader.GetString(4),
            Vector = reader.IsDBNull(5) ? Array.Empty<float>() : reader.GetFieldValue<float[]>(5),
            CreatedAt = reader.GetDateTime(6)
        };
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
