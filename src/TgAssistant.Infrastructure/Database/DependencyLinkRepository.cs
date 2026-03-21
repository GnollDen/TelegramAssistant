using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class DependencyLinkRepository : IDependencyLinkRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public DependencyLinkRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<DependencyLink> CreateDependencyLinkAsync(DependencyLink link, CancellationToken ct = default)
    {
        var row = new DbDependencyLink
        {
            Id = link.Id == Guid.Empty ? Guid.NewGuid() : link.Id,
            UpstreamType = link.UpstreamType,
            UpstreamId = link.UpstreamId,
            DownstreamType = link.DownstreamType,
            DownstreamId = link.DownstreamId,
            LinkType = link.LinkType,
            LinkReason = link.LinkReason,
            CreatedAt = link.CreatedAt == default ? DateTime.UtcNow : link.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.DependencyLinks.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<DependencyLink>> GetByUpstreamAsync(string upstreamType, string upstreamId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.DependencyLinks
                .AsNoTracking()
                .Where(x => x.UpstreamType == upstreamType && x.UpstreamId == upstreamId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<List<DependencyLink>> GetByDownstreamAsync(string downstreamType, string downstreamId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.DependencyLinks
                .AsNoTracking()
                .Where(x => x.DownstreamType == downstreamType && x.DownstreamId == downstreamId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static DependencyLink ToDomain(DbDependencyLink row) => new()
    {
        Id = row.Id,
        UpstreamType = row.UpstreamType,
        UpstreamId = row.UpstreamId,
        DownstreamType = row.DownstreamType,
        DownstreamId = row.DownstreamId,
        LinkType = row.LinkType,
        LinkReason = row.LinkReason,
        CreatedAt = row.CreatedAt
    };

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
