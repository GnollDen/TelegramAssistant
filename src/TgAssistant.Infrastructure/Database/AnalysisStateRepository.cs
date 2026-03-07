using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class AnalysisStateRepository : IAnalysisStateRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public AnalysisStateRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<long> GetWatermarkAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.AnalysisStates.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        return row?.Value ?? 0;
    }

    public async Task SetWatermarkAsync(string key, long value, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.AnalysisStates.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row == null)
        {
            db.AnalysisStates.Add(new DbAnalysisState
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
