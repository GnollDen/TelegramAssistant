using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class FactRepository : IFactRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public FactRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Fact> UpsertAsync(Fact fact, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var existing = await db.Facts.FirstOrDefaultAsync(x => x.EntityId == fact.EntityId && x.Category == fact.Category && x.Key == fact.Key && x.Value == fact.Value && x.IsCurrent, ct);
            if (existing == null)
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
                    ValidFrom = fact.ValidFrom ?? DateTime.UtcNow,
                    ValidUntil = fact.ValidUntil,
                    IsCurrent = fact.IsCurrent,
                    DecayClass = NormalizeDecayClass(fact.DecayClass),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Facts.Add(row);
                await db.SaveChangesAsync(ct);
                return ToDomain(row);
            }

            existing.Confidence = Math.Max(existing.Confidence, fact.Confidence);
            existing.Status = (short)fact.Status;
            existing.SourceMessageId = fact.SourceMessageId ?? existing.SourceMessageId;
            existing.DecayClass = NormalizeDecayClass(fact.DecayClass);
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return ToDomain(existing);
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
                .OrderBy(f => f.CreatedAt)
                .ThenBy(f => f.Id)
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
                IsCurrent = true,
                DecayClass = NormalizeDecayClass(newFact.DecayClass),
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
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static string NormalizeDecayClass(string? decayClass)
    {
        return string.IsNullOrWhiteSpace(decayClass)
            ? "slow"
            : decayClass.Trim().ToLowerInvariant();
    }
}
