using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionCaseReintegrationService : IResolutionCaseReintegrationService
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
        var latestByScopeItem = await _repository.GetLatestByScopeItemAsync(scopeKey, scopeItemKey, ct);
        if (string.IsNullOrWhiteSpace(carryForwardCaseId))
        {
            carryForwardCaseId = latestByScopeItem?.CarryForwardCaseId ?? BuildCaseId();
        }

        if (string.Equals(carryForwardCaseId, scopeItemKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("carry_forward_case_id must be distinct from scope_item_key");
        }

        var latestByCase = await _repository.GetLatestByCaseIdAsync(scopeKey, carryForwardCaseId, ct);
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

        var persisted = await _repository.InsertAsync(entry, ct);
        if (latestByCase != null)
        {
            await _repository.LinkSuccessorAsync(latestByCase.Id, persisted.Id, ct);
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
