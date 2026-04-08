using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionCaseReintegrationService : IResolutionCaseReintegrationService, IResolutionCaseReintegrationTransactionalService
{
    private readonly IResolutionCaseReintegrationLedgerRepository _repository;

    public ResolutionCaseReintegrationService(IResolutionCaseReintegrationLedgerRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResolutionCaseReintegrationLedgerEntry> RecordAsync(
        ResolutionCaseReintegrationRecordRequest request,
        CancellationToken ct = default)
    {
        return await RecordInternalAsync(
            request,
            queryLatestByScopeItem: (scopeKey, scopeItemKey, token) => _repository.GetLatestByScopeItemAsync(scopeKey, scopeItemKey, token),
            queryLatestByCaseId: (scopeKey, caseId, token) => _repository.GetLatestByCaseIdAsync(scopeKey, caseId, token),
            insert: (entry, token) => _repository.InsertAsync(entry, token),
            linkSuccessor: (predecessorId, successorId, token) => _repository.LinkSuccessorAsync(predecessorId, successorId, token),
            ct);
    }

    public async Task<ResolutionCaseReintegrationLedgerEntry> RecordWithinDbContextAsync(
        TgAssistantDbContext db,
        ResolutionCaseReintegrationRecordRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        return await RecordInternalAsync(
            request,
            queryLatestByScopeItem: (scopeKey, scopeItemKey, token) => QueryLatestByScopeItemAsync(db, scopeKey, scopeItemKey, token),
            queryLatestByCaseId: (scopeKey, caseId, token) => QueryLatestByCaseIdAsync(db, scopeKey, caseId, token),
            insert: (entry, token) => InsertAsync(db, entry, token),
            linkSuccessor: (predecessorId, successorId, token) => LinkSuccessorAsync(db, predecessorId, successorId, token),
            ct);
    }

    private static async Task<ResolutionCaseReintegrationLedgerEntry?> QueryLatestByScopeItemAsync(
        TgAssistantDbContext db,
        string scopeKey,
        string scopeItemKey,
        CancellationToken ct)
    {
        var row = await db.ResolutionCaseReintegrationLedgerEntries
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.ScopeItemKey == scopeItemKey)
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return row == null ? null : Map(row);
    }

    private static async Task<ResolutionCaseReintegrationLedgerEntry?> QueryLatestByCaseIdAsync(
        TgAssistantDbContext db,
        string scopeKey,
        string carryForwardCaseId,
        CancellationToken ct)
    {
        var row = await db.ResolutionCaseReintegrationLedgerEntries
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.CarryForwardCaseId == carryForwardCaseId)
            .OrderByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return row == null ? null : Map(row);
    }

    private static Task<ResolutionCaseReintegrationLedgerEntry> InsertAsync(
        TgAssistantDbContext db,
        ResolutionCaseReintegrationLedgerEntry entry,
        CancellationToken _)
    {
        var row = Map(entry);
        db.ResolutionCaseReintegrationLedgerEntries.Add(row);
        return Task.FromResult(Map(row));
    }

    private static async Task LinkSuccessorAsync(
        TgAssistantDbContext db,
        Guid predecessorLedgerEntryId,
        Guid successorLedgerEntryId,
        CancellationToken ct)
    {
        var predecessor = db.ResolutionCaseReintegrationLedgerEntries.Local
            .FirstOrDefault(x => x.Id == predecessorLedgerEntryId);
        if (predecessor == null)
        {
            predecessor = await db.ResolutionCaseReintegrationLedgerEntries
                .FirstOrDefaultAsync(x => x.Id == predecessorLedgerEntryId, ct);
        }

        var successor = db.ResolutionCaseReintegrationLedgerEntries.Local
            .FirstOrDefault(x => x.Id == successorLedgerEntryId);
        if (successor == null)
        {
            successor = await db.ResolutionCaseReintegrationLedgerEntries
                .FirstOrDefaultAsync(x => x.Id == successorLedgerEntryId, ct);
        }

        if (predecessor == null || successor == null)
        {
            throw new InvalidOperationException("Cannot link successor: ledger entry not found.");
        }

        var nowUtc = DateTime.UtcNow;
        successor.PredecessorLedgerEntryId = predecessorLedgerEntryId;
        successor.UpdatedAtUtc = nowUtc;

        // Ensure successor row exists before predecessor points to it to satisfy FK ordering.
        if (db.Entry(successor).State == EntityState.Added)
        {
            await db.SaveChangesAsync(ct);
        }

        predecessor.SuccessorLedgerEntryId = successorLedgerEntryId;
        predecessor.UpdatedAtUtc = nowUtc;
    }

    private static async Task<ResolutionCaseReintegrationLedgerEntry> RecordInternalAsync(
        ResolutionCaseReintegrationRecordRequest request,
        Func<string, string, CancellationToken, Task<ResolutionCaseReintegrationLedgerEntry?>> queryLatestByScopeItem,
        Func<string, string, CancellationToken, Task<ResolutionCaseReintegrationLedgerEntry?>> queryLatestByCaseId,
        Func<ResolutionCaseReintegrationLedgerEntry, CancellationToken, Task<ResolutionCaseReintegrationLedgerEntry>> insert,
        Func<Guid, Guid, CancellationToken, Task> linkSuccessor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = NormalizeRequired(request.ScopeKey, nameof(request.ScopeKey));
        var scopeItemKey = NormalizeRequired(request.ScopeItemKey, nameof(request.ScopeItemKey));
        var originSourceKind = NormalizeRequired(request.OriginSourceKind, nameof(request.OriginSourceKind));
        var nextStatus = NormalizeRequired(request.NextStatus, nameof(request.NextStatus));

        if (!ReintegrationOriginSourceKinds.IsSupported(originSourceKind))
        {
            throw new InvalidOperationException($"Unsupported origin_source_kind: {originSourceKind}");
        }

        if (!IterativeCaseStatuses.IsSupported(nextStatus))
        {
            throw new InvalidOperationException($"Unsupported next_status: {nextStatus}");
        }

        if (request.TrackedPersonId == Guid.Empty)
        {
            throw new InvalidOperationException("tracked_person_id is required.");
        }

        var carryForwardCaseId = NormalizeOptional(request.CarryForwardCaseId);
        var latestByScopeItem = await queryLatestByScopeItem(scopeKey, scopeItemKey, ct);
        if (string.IsNullOrWhiteSpace(carryForwardCaseId))
        {
            carryForwardCaseId = latestByScopeItem?.CarryForwardCaseId ?? BuildCaseId();
        }

        if (string.Equals(carryForwardCaseId, scopeItemKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("carry_forward_case_id must be distinct from scope_item_key");
        }

        var latestByCase = await queryLatestByCaseId(scopeKey, carryForwardCaseId, ct);
        if (latestByCase != null && !string.Equals(latestByCase.ScopeItemKey, scopeItemKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(ReintegrationLedgerFailureReasons.CrossScopeLinkageRejected);
        }

        if (request.ExpectedPreviousLedgerEntryId.HasValue
            && latestByCase != null
            && request.ExpectedPreviousLedgerEntryId.Value != latestByCase.Id)
        {
            throw new InvalidOperationException(ReintegrationLedgerFailureReasons.StaleRecomputeLinkageRejected);
        }

        var previousStatus = latestByCase?.NextStatus;
        if (latestByCase != null && !IterativeCaseStatuses.CanTransition(previousStatus, nextStatus))
        {
            throw new InvalidOperationException(ReintegrationLedgerFailureReasons.InvalidStatusTransition);
        }

        var recomputeTargetFamily = NormalizeOptional(request.RecomputeTargetFamily);
        var recomputeTargetRef = NormalizeOptional(request.RecomputeTargetRef);
        if (string.Equals(nextStatus, IterativeCaseStatuses.ResolvedByAi, StringComparison.Ordinal))
        {
            if (request.RecomputeQueueItemId == null
                || string.IsNullOrWhiteSpace(recomputeTargetFamily)
                || string.IsNullOrWhiteSpace(recomputeTargetRef))
            {
                throw new InvalidOperationException(ReintegrationLedgerFailureReasons.RecomputeTupleRequired);
            }
        }

        var nowUtc = DateTime.UtcNow;
        var entry = new ResolutionCaseReintegrationLedgerEntry
        {
            Id = Guid.NewGuid(),
            ReintegrationEntryId = $"reintegration_entry:{Guid.NewGuid():D}",
            ScopeKey = scopeKey,
            ScopeItemKey = scopeItemKey,
            TrackedPersonId = request.TrackedPersonId,
            CarryForwardCaseId = carryForwardCaseId,
            OriginSourceKind = originSourceKind,
            PreviousStatus = previousStatus,
            NextStatus = nextStatus,
            PredecessorLedgerEntryId = latestByCase?.Id,
            SuccessorLedgerEntryId = null,
            ResolutionActionId = request.ResolutionActionId,
            ConflictSessionId = request.ConflictSessionId,
            RecomputeQueueItemId = request.RecomputeQueueItemId,
            RecomputeTargetFamily = recomputeTargetFamily,
            RecomputeTargetRef = recomputeTargetRef,
            UnresolvedResidueJson = NormalizeOptional(request.UnresolvedResidueJson) ?? "{}",
            RecordedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var persisted = await insert(entry, ct);
        if (latestByCase != null)
        {
            await linkSuccessor(latestByCase.Id, persisted.Id, ct);
        }

        return persisted;
    }

    public Task<List<ResolutionCaseReintegrationLedgerEntry>> QueryAsync(
        ResolutionCaseReintegrationQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _repository.QueryAsync(query, ct);
    }

    private static string BuildCaseId()
        => $"carry_case:{Guid.NewGuid():D}";

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

    private static string NormalizeRequired(string? value, string argumentName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{argumentName} is required.", argumentName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
