using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class FactRepository : IFactRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private const string SearchSimilarFactsCosineSql = """
        SELECT
            f.id,
            f.entity_id,
            f.category,
            f.key,
            f.value,
            f.status,
            f.confidence,
            f.source_message_id,
            f.valid_from,
            f.valid_until,
            f.is_current,
            f.decay_class,
            f.created_at,
            f.updated_at
        FROM facts f
        JOIN text_embeddings e
          ON e.owner_type = 'fact'
         AND e.owner_id = f.id::text
         AND e.model = @model
        WHERE f.is_current = TRUE
          AND e.vector IS NOT NULL
        ORDER BY (e.vector <=> @query_vector::vector) ASC
        LIMIT @limit;
        """;
    private const string SearchSimilarFactsFallbackSql = """
        SELECT
            f.id,
            f.entity_id,
            f.category,
            f.key,
            f.value,
            f.status,
            f.confidence,
            f.source_message_id,
            f.valid_from,
            f.valid_until,
            f.is_current,
            f.decay_class,
            f.created_at,
            f.updated_at,
            e.vector::real[] AS embedding_vector
        FROM facts f
        JOIN text_embeddings e
          ON e.owner_type = 'fact'
         AND e.owner_id = f.id::text
         AND e.model = @model
        WHERE f.is_current = TRUE
          AND e.vector IS NOT NULL
        ORDER BY f.updated_at DESC
        LIMIT @limit;
        """;

    public FactRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Fact> UpsertAsync(Fact fact, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var normalizedDecayClass = NormalizeDecayClass(fact.DecayClass);
            if (!fact.IsCurrent)
            {
                var row = new DbFact
                {
                    Id = fact.Id == Guid.Empty ? Guid.NewGuid() : fact.Id,
                    EntityId = fact.EntityId,
                    Category = fact.Category,
                    Key = fact.Key,
                    Value = fact.Value,
                    Status = (short)fact.Status,
                    Confidence = fact.Confidence,
                    SourceMessageId = fact.SourceMessageId,
                    ValidFrom = fact.ValidFrom ?? now,
                    ValidUntil = fact.ValidUntil,
                    IsCurrent = fact.IsCurrent,
                    DecayClass = normalizedDecayClass,
                    IsUserConfirmed = fact.IsUserConfirmed,
                    TrustFactor = fact.TrustFactor,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Facts.Add(row);
                await db.SaveChangesAsync(ct);
                return ToDomain(row);
            }

            var rowId = fact.Id == Guid.Empty ? Guid.NewGuid() : fact.Id;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO facts (
                    id,
                    entity_id,
                    category,
                    key,
                    value,
                    status,
                    confidence,
                    source_message_id,
                    valid_from,
                    valid_until,
                    is_current,
                    decay_class,
                    is_user_confirmed,
                    trust_factor,
                    created_at,
                    updated_at
                )
                VALUES (
                    {rowId},
                    {fact.EntityId},
                    {fact.Category},
                    {fact.Key},
                    {fact.Value},
                    {(short)fact.Status},
                    {fact.Confidence},
                    {fact.SourceMessageId},
                    {fact.ValidFrom ?? now},
                    {fact.ValidUntil},
                    TRUE,
                    {normalizedDecayClass},
                    {fact.IsUserConfirmed},
                    {fact.TrustFactor},
                    {now},
                    {now}
                )
                ON CONFLICT (entity_id, category, key, value) WHERE is_current = TRUE
                DO UPDATE
                SET confidence = GREATEST(facts.confidence, EXCLUDED.confidence),
                    status = CASE
                        WHEN facts.status = {(short)ConfidenceStatus.Tentative}
                         AND EXCLUDED.status = {(short)ConfidenceStatus.Confirmed}
                            THEN facts.status
                        ELSE EXCLUDED.status
                    END,
                    source_message_id = COALESCE(EXCLUDED.source_message_id, facts.source_message_id),
                    decay_class = EXCLUDED.decay_class,
                    is_user_confirmed = facts.is_user_confirmed OR EXCLUDED.is_user_confirmed,
                    trust_factor = GREATEST(facts.trust_factor, EXCLUDED.trust_factor),
                    updated_at = EXCLUDED.updated_at;
                """, ct);

            var persisted = await db.Facts
                .AsNoTracking()
                .Where(x => x.EntityId == fact.EntityId
                            && x.Category == fact.Category
                            && x.Key == fact.Key
                            && x.Value == fact.Value
                            && x.IsCurrent)
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstAsync(ct);
            return ToDomain(persisted);
        }, ct);
    }

    public async Task<Fact?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Facts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<Fact>> GetCurrentByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Facts.AsNoTracking().Where(x => x.EntityId == entityId && x.IsCurrent).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<DossierFactPage> GetDossierFactsPageAsync(Guid entityId, int limit, string? categoryFilter, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var query = db.Facts.AsNoTracking().Where(x => x.EntityId == entityId);
            if (!string.IsNullOrWhiteSpace(categoryFilter))
            {
                var normalized = categoryFilter.Trim();
                query = query.Where(x => x.Category.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(x => x.IsCurrent)
                .ThenByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .Take(Math.Max(1, limit))
                .ToListAsync(ct);

            return new DossierFactPage
            {
                TotalCount = total,
                Facts = rows.Select(ToDomain).ToList()
            };
        }, ct);
    }

    public async Task<List<Fact>> GetAllByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Facts
                .AsNoTracking()
                .Where(x => x.EntityId == entityId)
                .OrderByDescending(x => x.IsCurrent)
                .ThenByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ToListAsync(ct);

            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<List<Fact>> SearchSimilarFactsAsync(string model, float[] queryEmbedding, int limit = 10, CancellationToken ct = default)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        if (queryEmbedding.Length == 0 || string.IsNullOrWhiteSpace(normalizedModel))
        {
            return new List<Fact>();
        }

        var safeLimit = Math.Max(1, limit);
        var queryVector = $"[{string.Join(",", queryEmbedding.Select(x => x.ToString(CultureInfo.InvariantCulture)))}]";

        try
        {
            return await ExecuteSimilarFactsQueryAsync(SearchSimilarFactsCosineSql, normalizedModel, queryVector, safeLimit, ct);
        }
        catch (PostgresException ex) when (ex.SqlState is "42883" or "42704" or "0A000")
        {
            return await SearchSimilarFactsFallbackAsync(normalizedModel, queryEmbedding, safeLimit, ct);
        }
    }

    public async Task<List<Fact>> GetWithoutEmbeddingAsync(string model, int limit, CancellationToken ct = default)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedModel))
        {
            return new List<Fact>();
        }

        var safeLimit = Math.Max(1, limit);
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Facts
                .AsNoTracking()
                .Where(f => !db.TextEmbeddings.Any(e =>
                    e.OwnerType == "fact" &&
                    e.OwnerId == f.Id.ToString() &&
                    e.Model == normalizedModel))
                .OrderByDescending(f => f.IsCurrent)
                .ThenByDescending(f => f.CreatedAt)
                .ThenByDescending(f => f.Id)
                .Take(safeLimit)
                .ToListAsync(ct);

            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<long> CountWithoutEmbeddingAsync(string model, CancellationToken ct = default)
    {
        var normalizedModel = (model ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedModel))
        {
            return 0;
        }

        return await WithDbContextAsync(async db =>
        {
            return await db.Facts
                .AsNoTracking()
                .LongCountAsync(f => !db.TextEmbeddings.Any(e =>
                    e.OwnerType == "fact" &&
                    e.OwnerId == f.Id.ToString() &&
                    e.Model == normalizedModel), ct);
        }, ct);
    }

    public async Task SupersedeFactAsync(Guid oldFactId, Fact newFact, CancellationToken ct = default)
    {
        await WithDbContextAsync(async db =>
        {
            var old = await db.Facts.FirstOrDefaultAsync(x => x.Id == oldFactId, ct);
            if (old != null)
            {
                old.IsCurrent = false;
                old.ValidUntil = DateTime.UtcNow;
                old.UpdatedAt = DateTime.UtcNow;
            }

            db.Facts.Add(new DbFact
            {
                Id = newFact.Id == Guid.Empty ? Guid.NewGuid() : newFact.Id,
                EntityId = newFact.EntityId,
                Category = newFact.Category,
                Key = newFact.Key,
                Value = newFact.Value,
                Status = (short)newFact.Status,
                Confidence = newFact.Confidence,
                SourceMessageId = newFact.SourceMessageId,
                ValidFrom = newFact.ValidFrom ?? DateTime.UtcNow,
                ValidUntil = newFact.ValidUntil,
                IsCurrent = true,
                DecayClass = NormalizeDecayClass(newFact.DecayClass),
                IsUserConfirmed = newFact.IsUserConfirmed,
                TrustFactor = newFact.TrustFactor,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default)
    {
        await WithDbContextAsync(async db =>
        {
            var row = await db.Facts.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null) return;
            row.Status = (short)status;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private async Task<List<Fact>> ExecuteSimilarFactsQueryAsync(string sql, string model, string queryVector, int limit, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("model", model);
        cmd.Parameters.AddWithValue("query_vector", queryVector);
        cmd.Parameters.AddWithValue("limit", limit);

        var result = new List<Fact>(limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(ReadFact(reader));
        }

        return result;
    }

    private async Task<List<Fact>> SearchSimilarFactsFallbackAsync(string model, float[] queryEmbedding, int limit, CancellationToken ct)
    {
        var sampleSize = Math.Max(limit * 30, 300);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SearchSimilarFactsFallbackSql;
        cmd.Parameters.AddWithValue("model", model);
        cmd.Parameters.AddWithValue("limit", sampleSize);

        var candidates = new List<(Fact Fact, float[] Vector)>(sampleSize);
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                candidates.Add((
                    ReadFact(reader),
                    reader.IsDBNull(14) ? Array.Empty<float>() : reader.GetFieldValue<float[]>(14)));
            }
        }

        return candidates
            .Select(x => new { x.Fact, Score = Cosine(queryEmbedding, x.Vector) })
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => x.Fact)
            .ToList();
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

    private static Fact ToDomain(DbFact row)
    {
        return new Fact
        {
            Id = row.Id,
            EntityId = row.EntityId,
            Category = row.Category,
            Key = row.Key,
            Value = row.Value,
            Status = (ConfidenceStatus)row.Status,
            Confidence = row.Confidence,
            SourceMessageId = row.SourceMessageId,
            ValidFrom = row.ValidFrom,
            ValidUntil = row.ValidUntil,
            IsCurrent = row.IsCurrent,
            DecayClass = row.DecayClass,
            IsUserConfirmed = row.IsUserConfirmed,
            TrustFactor = row.TrustFactor,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static Fact ReadFact(NpgsqlDataReader reader)
    {
        return new Fact
        {
            Id = reader.GetGuid(0),
            EntityId = reader.GetGuid(1),
            Category = reader.GetString(2),
            Key = reader.GetString(3),
            Value = reader.GetString(4),
            Status = (ConfidenceStatus)reader.GetInt16(5),
            Confidence = reader.GetFloat(6),
            SourceMessageId = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            ValidFrom = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            ValidUntil = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            IsCurrent = reader.GetBoolean(10),
            DecayClass = reader.GetString(11),
            IsUserConfirmed = false,
            TrustFactor = 1.0f,
            CreatedAt = reader.GetDateTime(12),
            UpdatedAt = reader.GetDateTime(13)
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

    private static string NormalizeDecayClass(string? decayClass)
    {
        return string.IsNullOrWhiteSpace(decayClass)
            ? "slow"
            : decayClass.Trim().ToLowerInvariant();
    }
}
