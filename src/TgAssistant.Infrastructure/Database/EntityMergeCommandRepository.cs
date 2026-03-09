using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class EntityMergeCommandRepository : IEntityMergeCommandRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public EntityMergeCommandRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<EntityMergeCommand>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.EntityMergeCommands.AsNoTracking()
            .Where(x => x.Status == 0)
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        return rows.Select(x => new EntityMergeCommand
        {
            Id = x.Id,
            CandidateId = x.CandidateId,
            Command = x.Command,
            Reason = x.Reason
        }).ToList();
    }

    public async Task MarkDoneAsync(long commandId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.EntityMergeCommands.FirstOrDefaultAsync(x => x.Id == commandId, ct);
        if (row == null) return;
        row.Status = 1;
        row.Error = null;
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(long commandId, string error, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.EntityMergeCommands.FirstOrDefaultAsync(x => x.Id == commandId, ct);
        if (row == null) return;
        row.Status = 2;
        row.Error = string.IsNullOrWhiteSpace(error) ? "unknown_error" : error.Trim();
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
