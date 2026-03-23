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
        var detailsJson = string.IsNullOrWhiteSpace(state.DetailsJson) ? "{}" : state.DetailsJson;
        var updatedAt = state.UpdatedAt == default ? DateTime.UtcNow : state.UpdatedAt;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into ops_budget_operational_states (path_key, modality, state, reason, details_json, updated_at)
            values ({pathKey}, {state.Modality}, {state.State}, {state.Reason}, {detailsJson}::jsonb, {updatedAt})
            on conflict (path_key) do update set
                modality = excluded.modality,
                state = excluded.state,
                reason = excluded.reason,
                details_json = excluded.details_json,
                updated_at = excluded.updated_at
            """,
            ct);
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
