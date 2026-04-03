using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        var active = await db.RuntimeControlStates.FirstOrDefaultAsync(x => x.IsActive, ct);
        DbRuntimeControlState row;
        if (active != null && string.Equals(active.State, normalizedState, StringComparison.Ordinal))
        {
            active.Reason = normalizedReason;
            active.Source = normalizedSource;
            active.DetailsJson = normalizedDetailsJson;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Map(active);
        }

        if (active != null)
        {
            active.IsActive = false;
            active.DeactivatedAtUtc = now;
        }

        row = new DbRuntimeControlState
        {
            State = normalizedState,
            Reason = normalizedReason,
            Source = normalizedSource,
            DetailsJson = normalizedDetailsJson,
            IsActive = true,
            ActivatedAtUtc = now
        };
        db.RuntimeControlStates.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);

            var concurrent = await db.RuntimeControlStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.IsActive, ct)
                ?? throw new InvalidOperationException("Concurrent runtime control state activation failed and no active state row remained.");

            if (string.Equals(concurrent.State, normalizedState, StringComparison.Ordinal))
            {
                return Map(concurrent);
            }

            throw;
        }

        return Map(row);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException postgres
           && string.Equals(postgres.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal);

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
