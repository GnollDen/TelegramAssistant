namespace TgAssistant.Core.Models;

public class ResolutionCaseReintegrationLedgerEntry
{
    public Guid Id { get; set; }
    public string ReintegrationEntryId { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string CarryForwardCaseId { get; set; } = string.Empty;
    public string OriginSourceKind { get; set; } = ReintegrationOriginSourceKinds.ResolutionAction;
    public string? PreviousStatus { get; set; }
    public string NextStatus { get; set; } = IterativeCaseStatuses.Open;
    public Guid? PredecessorLedgerEntryId { get; set; }
    public Guid? SuccessorLedgerEntryId { get; set; }
    public Guid? ResolutionActionId { get; set; }
    public Guid? ConflictSessionId { get; set; }
    public Guid? RecomputeQueueItemId { get; set; }
    public string? RecomputeTargetFamily { get; set; }
    public string? RecomputeTargetRef { get; set; }
    public string UnresolvedResidueJson { get; set; } = "{}";
    public DateTime RecordedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class ResolutionCaseReintegrationRecordRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string CarryForwardCaseId { get; set; } = string.Empty;
    public string? ReintegrationEntryId { get; set; }
    public string OriginSourceKind { get; set; } = ReintegrationOriginSourceKinds.ResolutionAction;
    public string? PreviousStatus { get; set; }
    public string NextStatus { get; set; } = IterativeCaseStatuses.Open;
    public Guid? PredecessorLedgerEntryId { get; set; }
    public Guid? ResolutionActionId { get; set; }
    public Guid? ConflictSessionId { get; set; }
    public Guid? RecomputeQueueItemId { get; set; }
    public string? RecomputeTargetFamily { get; set; }
    public string? RecomputeTargetRef { get; set; }
    public string? UnresolvedResidueJson { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public class ResolutionCaseReintegrationQuery
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? TrackedPersonId { get; set; }
    public string? ScopeItemKey { get; set; }
    public string? CarryForwardCaseId { get; set; }
    public string? NextStatus { get; set; }
    public int Limit { get; set; } = 50;
}
