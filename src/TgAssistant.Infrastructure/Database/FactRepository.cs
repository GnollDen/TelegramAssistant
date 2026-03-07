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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Facts.FirstOrDefaultAsync(x => x.EntityId == fact.EntityId && x.Category == fact.Category && x.Key == fact.Key && x.Value == fact.Value && x.IsCurrent, ct);
        if (existing == null)
        {
            db.Facts.Add(new DbFact
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Confidence = Math.Max(existing.Confidence, fact.Confidence);
            existing.Status = (short)fact.Status;
            existing.SourceMessageId = fact.SourceMessageId ?? existing.SourceMessageId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return fact;
    }

    public async Task<List<Fact>> GetCurrentByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Facts.AsNoTracking().Where(x => x.EntityId == entityId && x.IsCurrent).ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task SupersedeFactAsync(Guid oldFactId, Fact newFact, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
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
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Facts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null) return;
        row.Status = (short)status;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
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
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}
