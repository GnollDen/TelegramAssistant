using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class BudgetOpsRepository : IBudgetOpsRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public BudgetOpsRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertBudgetOperationalStateAsync(BudgetOperationalState state, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var pathKey = state.PathKey.Trim();
        var row = await db.BudgetOperationalStates.FirstOrDefaultAsync(x => x.PathKey == pathKey, ct);
        if (row == null)
        {
            row = new DbBudgetOperationalState
            {
                PathKey = pathKey,
                Modality = state.Modality,
                State = state.State,
                Reason = state.Reason,
                DetailsJson = string.IsNullOrWhiteSpace(state.DetailsJson) ? "{}" : state.DetailsJson,
                UpdatedAt = state.UpdatedAt == default ? DateTime.UtcNow : state.UpdatedAt
            };
            db.BudgetOperationalStates.Add(row);
        }
        else
        {
            row.Modality = state.Modality;
            row.State = state.State;
            row.Reason = state.Reason;
            row.DetailsJson = string.IsNullOrWhiteSpace(state.DetailsJson) ? "{}" : state.DetailsJson;
            row.UpdatedAt = state.UpdatedAt == default ? DateTime.UtcNow : state.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<BudgetOperationalState?> GetBudgetOperationalStateAsync(string pathKey, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.BudgetOperationalStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PathKey == pathKey.Trim(), ct);
        return row == null ? null : Map(row);
    }

    public async Task<List<BudgetOperationalState>> GetBudgetOperationalStatesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.BudgetOperationalStates
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.PathKey)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    private static BudgetOperationalState Map(DbBudgetOperationalState row)
    {
        return new BudgetOperationalState
        {
            PathKey = row.PathKey,
            Modality = row.Modality,
            State = row.State,
            Reason = row.Reason,
            DetailsJson = string.IsNullOrWhiteSpace(row.DetailsJson) ? "{}" : row.DetailsJson,
            UpdatedAt = row.UpdatedAt
        };
    }
}
