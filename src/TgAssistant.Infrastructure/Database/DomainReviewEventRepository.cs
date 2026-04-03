using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class DomainReviewEventRepository : IDomainReviewEventRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IStage8RecomputeTriggerService _stage8RecomputeTriggerService;

    public DomainReviewEventRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IStage8RecomputeTriggerService stage8RecomputeTriggerService)
    {
        _dbFactory = dbFactory;
        _stage8RecomputeTriggerService = stage8RecomputeTriggerService;
    }

    public async Task<DomainReviewEvent> AddAsync(DomainReviewEvent evt, CancellationToken ct = default)
    {
        var row = new DbDomainReviewEvent
        {
            Id = evt.Id == Guid.Empty ? Guid.NewGuid() : evt.Id,
            ObjectType = evt.ObjectType,
            ObjectId = evt.ObjectId,
            Action = evt.Action,
            OldValueRef = evt.OldValueRef,
            NewValueRef = evt.NewValueRef,
            Reason = evt.Reason,
            Actor = evt.Actor,
            CreatedAt = evt.CreatedAt == default ? DateTime.UtcNow : evt.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.DomainReviewEvents.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        evt.Id = row.Id;
        evt.CreatedAt = row.CreatedAt;
        await _stage8RecomputeTriggerService.HandleDomainReviewEventAsync(evt, ct);
        return evt;
    }

    public async Task<List<DomainReviewEvent>> GetByObjectAsync(string objectType, string objectId, int limit = 100, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.DomainReviewEvents
                .AsNoTracking()
                .Where(x => x.ObjectType == objectType && x.ObjectId == objectId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Max(1, limit))
                .ToListAsync(ct);

            return rows.Select(x => new DomainReviewEvent
            {
                Id = x.Id,
                ObjectType = x.ObjectType,
                ObjectId = x.ObjectId,
                Action = x.Action,
                OldValueRef = x.OldValueRef,
                NewValueRef = x.NewValueRef,
                Reason = x.Reason,
                Actor = x.Actor,
                CreatedAt = x.CreatedAt
            }).ToList();
        }, ct);
    }

    private async Task<TResult> WithDbContextAsync<TResult>(Func<TgAssistantDbContext, Task<TResult>> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await action(ambientDb);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await action(db);
    }

    private async Task WithDbContextAsync(Func<TgAssistantDbContext, Task> action, CancellationToken ct)
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
}
