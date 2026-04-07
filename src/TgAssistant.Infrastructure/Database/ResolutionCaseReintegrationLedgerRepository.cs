using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionCaseReintegrationLedgerRepository : IResolutionCaseReintegrationLedgerRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ResolutionCaseReintegrationLedgerRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ResolutionCaseReintegrationLedgerEntry> InsertAsync(
        ResolutionCaseReintegrationLedgerEntry entry,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = Map(entry);
        db.ResolutionCaseReintegrationLedgerEntries.Add(row);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<ResolutionCaseReintegrationLedgerEntry?> GetLatestByScopeItemAsync(
        string scopeKey,
        string scopeItemKey,
        CancellationToken ct = default)
    {
        var normalizedScopeKey = NormalizeRequired(scopeKey);
        var normalizedScopeItemKey = NormalizeRequired(scopeItemKey);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ResolutionCaseReintegrationLedgerEntries
            .AsNoTracking()
            .Where(x => x.ScopeKey == normalizedScopeKey && x.ScopeItemKey == normalizedScopeItemKey)
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return row == null ? null : Map(row);
    }

    public async Task<ResolutionCaseReintegrationLedgerEntry?> GetLatestByCaseIdAsync(
        string scopeKey,
        string carryForwardCaseId,
        CancellationToken ct = default)
    {
        var normalizedScopeKey = NormalizeRequired(scopeKey);
        var normalizedCaseId = NormalizeRequired(carryForwardCaseId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ResolutionCaseReintegrationLedgerEntries
            .AsNoTracking()
            .Where(x => x.ScopeKey == normalizedScopeKey && x.CarryForwardCaseId == normalizedCaseId)
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return row == null ? null : Map(row);
    }

    public async Task<ResolutionCaseReintegrationLedgerEntry?> GetByIdAsync(
        Guid id,
        CancellationToken ct = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ResolutionCaseReintegrationLedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return row == null ? null : Map(row);
    }

    public async Task<List<ResolutionCaseReintegrationLedgerEntry>> QueryAsync(
        ResolutionCaseReintegrationQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedScopeKey = NormalizeRequired(query.ScopeKey);
        var normalizedScopeItemKey = NormalizeOptional(query.ScopeItemKey);
        var normalizedCaseId = NormalizeOptional(query.CarryForwardCaseId);
        var limit = Math.Clamp(query.Limit <= 0 ? 100 : query.Limit, 1, 500);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = db.ResolutionCaseReintegrationLedgerEntries
            .AsNoTracking()
            .Where(x => x.ScopeKey == normalizedScopeKey);

        if (!string.IsNullOrWhiteSpace(normalizedScopeItemKey))
        {
            rows = rows.Where(x => x.ScopeItemKey == normalizedScopeItemKey);
        }

        if (!string.IsNullOrWhiteSpace(normalizedCaseId))
        {
            rows = rows.Where(x => x.CarryForwardCaseId == normalizedCaseId);
        }

        if (query.TrackedPersonId.HasValue && query.TrackedPersonId.Value != Guid.Empty)
        {
            rows = rows.Where(x => x.TrackedPersonId == query.TrackedPersonId.Value);
        }

        var normalizedNextStatus = NormalizeOptional(query.NextStatus);
        if (!string.IsNullOrWhiteSpace(normalizedNextStatus))
        {
            rows = rows.Where(x => x.NextStatus == normalizedNextStatus);
        }

        var result = await rows
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);

        return result.Select(Map).ToList();
    }

    public async Task LinkSuccessorAsync(
        Guid predecessorLedgerEntryId,
        Guid successorLedgerEntryId,
        CancellationToken ct = default)
    {
        if (predecessorLedgerEntryId == Guid.Empty)
        {
            throw new InvalidOperationException("Predecessor ledger entry id is required.");
        }

        if (successorLedgerEntryId == Guid.Empty)
        {
            throw new InvalidOperationException("Successor ledger entry id is required.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var predecessor = await db.ResolutionCaseReintegrationLedgerEntries
            .FirstOrDefaultAsync(x => x.Id == predecessorLedgerEntryId, ct);
        var successor = await db.ResolutionCaseReintegrationLedgerEntries
            .FirstOrDefaultAsync(x => x.Id == successorLedgerEntryId, ct);

        if (predecessor == null || successor == null)
        {
            throw new InvalidOperationException("Cannot link successor: ledger entry not found.");
        }

        predecessor.SuccessorLedgerEntryId = successorLedgerEntryId;
        predecessor.UpdatedAtUtc = DateTime.UtcNow;
        successor.PredecessorLedgerEntryId = predecessorLedgerEntryId;
        successor.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static DbResolutionCaseReintegrationLedgerEntry Map(ResolutionCaseReintegrationLedgerEntry entry)
    {
        return new DbResolutionCaseReintegrationLedgerEntry
        {
            Id = entry.Id,
            ReintegrationEntryId = entry.ReintegrationEntryId,
            ScopeKey = entry.ScopeKey,
            ScopeItemKey = entry.ScopeItemKey,
            TrackedPersonId = entry.TrackedPersonId,
            CarryForwardCaseId = entry.CarryForwardCaseId,
            OriginSourceKind = entry.OriginSourceKind,
            PreviousStatus = entry.PreviousStatus,
            NextStatus = entry.NextStatus,
            PredecessorLedgerEntryId = entry.PredecessorLedgerEntryId,
            SuccessorLedgerEntryId = entry.SuccessorLedgerEntryId,
            ResolutionActionId = entry.ResolutionActionId,
            ConflictSessionId = entry.ConflictSessionId,
            RecomputeQueueItemId = entry.RecomputeQueueItemId,
            RecomputeTargetFamily = entry.RecomputeTargetFamily,
            RecomputeTargetRef = entry.RecomputeTargetRef,
            UnresolvedResidueJson = string.IsNullOrWhiteSpace(entry.UnresolvedResidueJson) ? "{}" : entry.UnresolvedResidueJson,
            RecordedAtUtc = entry.RecordedAtUtc,
            CreatedAtUtc = entry.CreatedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc
        };
    }

    private static ResolutionCaseReintegrationLedgerEntry Map(DbResolutionCaseReintegrationLedgerEntry row)
    {
        return new ResolutionCaseReintegrationLedgerEntry
        {
            Id = row.Id,
            ReintegrationEntryId = row.ReintegrationEntryId,
            ScopeKey = row.ScopeKey,
            ScopeItemKey = row.ScopeItemKey,
            TrackedPersonId = row.TrackedPersonId,
            CarryForwardCaseId = row.CarryForwardCaseId,
            OriginSourceKind = row.OriginSourceKind,
            PreviousStatus = row.PreviousStatus,
            NextStatus = row.NextStatus,
            PredecessorLedgerEntryId = row.PredecessorLedgerEntryId,
            SuccessorLedgerEntryId = row.SuccessorLedgerEntryId,
            ResolutionActionId = row.ResolutionActionId,
            ConflictSessionId = row.ConflictSessionId,
            RecomputeQueueItemId = row.RecomputeQueueItemId,
            RecomputeTargetFamily = row.RecomputeTargetFamily,
            RecomputeTargetRef = row.RecomputeTargetRef,
            UnresolvedResidueJson = row.UnresolvedResidueJson,
            RecordedAtUtc = row.RecordedAtUtc,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        };
    }

    private static string NormalizeRequired(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Required value is missing.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
