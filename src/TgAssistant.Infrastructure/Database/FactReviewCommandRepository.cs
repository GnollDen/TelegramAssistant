using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class FactReviewCommandRepository : IFactReviewCommandRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly AnalysisSettings _analysisSettings;

    public FactReviewCommandRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IOptions<AnalysisSettings> analysisSettings)
    {
        _dbFactory = dbFactory;
        _analysisSettings = analysisSettings.Value;
    }

    public async Task<List<FactReviewCommand>> GetPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.FactReviewCommands.AsNoTracking()
            .Where(x => x.Status == 0)
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        return rows.Select(x => new FactReviewCommand
        {
            Id = x.Id,
            FactId = x.FactId,
            Command = x.Command,
            Reason = x.Reason
        }).ToList();
    }

    public async Task EnqueueAsync(Guid factId, string command, string? reason = null, CancellationToken ct = default)
    {
        var normalizedCommand = string.IsNullOrWhiteSpace(command) ? "approve" : command.Trim().ToLowerInvariant();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existsPending = await db.FactReviewCommands.AsNoTracking()
            .AnyAsync(x => x.FactId == factId && x.Status == 0 && x.Command == normalizedCommand, ct);
        if (existsPending)
        {
            return;
        }

        var reenqueueHours = Math.Max(1, _analysisSettings.FactReviewReenqueueHours);
        var cutoff = DateTime.UtcNow.AddHours(-reenqueueHours);
        var existsRecent = await db.FactReviewCommands.AsNoTracking()
            .AnyAsync(x => x.FactId == factId
                           && x.Command == normalizedCommand
                           && x.CreatedAt >= cutoff, ct);
        if (existsRecent)
        {
            return;
        }

        db.FactReviewCommands.Add(new DbFactReviewCommand
        {
            FactId = factId,
            Command = normalizedCommand,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Status = 0,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkDoneAsync(long commandId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.FactReviewCommands.FirstOrDefaultAsync(x => x.Id == commandId, ct);
        if (row == null) return;
        row.Status = 1;
        row.Error = null;
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFailedAsync(long commandId, string error, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.FactReviewCommands.FirstOrDefaultAsync(x => x.Id == commandId, ct);
        if (row == null) return;
        row.Status = 2;
        row.Error = string.IsNullOrWhiteSpace(error) ? "unknown_error" : error.Trim();
        row.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
