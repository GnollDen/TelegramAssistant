using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class RuntimeControlStateRepository : IRuntimeControlStateRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public RuntimeControlStateRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<RuntimeControlStateRecord?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.RuntimeControlStates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, ct);
        return row == null ? null : Map(row);
    }

    public async Task<RuntimeControlStateRecord> SetActiveAsync(
        string state,
        string reason,
        string source,
        string detailsJson,
        CancellationToken ct = default)
    {
        var normalizedState = string.IsNullOrWhiteSpace(state) ? RuntimeControlStates.Normal : state.Trim();
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim();
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "runtime_control_service" : source.Trim();
        var normalizedDetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson.Trim();
        var now = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var active = await db.RuntimeControlStates.FirstOrDefaultAsync(x => x.IsActive, ct);
        if (active != null && string.Equals(active.State, normalizedState, StringComparison.Ordinal))
        {
            active.Reason = normalizedReason;
            active.Source = normalizedSource;
            active.DetailsJson = normalizedDetailsJson;
            await db.SaveChangesAsync(ct);
            return Map(active);
        }

        if (active != null)
        {
            active.IsActive = false;
            active.DeactivatedAtUtc = now;
        }

        var row = new DbRuntimeControlState
        {
            State = normalizedState,
            Reason = normalizedReason,
            Source = normalizedSource,
            DetailsJson = normalizedDetailsJson,
            IsActive = true,
            ActivatedAtUtc = now
        };
        db.RuntimeControlStates.Add(row);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    private static RuntimeControlStateRecord Map(DbRuntimeControlState row)
    {
        return new RuntimeControlStateRecord
        {
            Id = row.Id,
            State = row.State,
            Reason = row.Reason,
            Source = row.Source,
            DetailsJson = row.DetailsJson,
            IsActive = row.IsActive,
            ActivatedAtUtc = row.ActivatedAtUtc,
            DeactivatedAtUtc = row.DeactivatedAtUtc
        };
    }
}
