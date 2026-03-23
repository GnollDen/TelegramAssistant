namespace TgAssistant.Core.Models;

public static class ChatCoordinationStates
{
    public const string HistoricalRequired = "historical_required";
    public const string BackfillActive = "backfill_active";
    public const string HandoverPending = "handover_pending";
    public const string RealtimeActive = "realtime_active";
    public const string DegradedBackfill = "degraded_backfill";
}

public static class ChatRuntimePhases
{
    public const string BackfillIngest = "backfill_ingest";
    public const string SliceBuild = "slice_build";
    public const string Stage5Process = "stage5_process";
    public const string TailReopen = "tail_reopen";
}

public class ChatCoordinationState
{
    public long ChatId { get; set; }
    public string State { get; set; } = ChatCoordinationStates.HistoricalRequired;
    public string Reason { get; set; } = string.Empty;
    public DateTime? LastBackfillStartedAt { get; set; }
    public DateTime? LastBackfillCompletedAt { get; set; }
    public DateTime? HandoverReadyAt { get; set; }
    public DateTime? RealtimeActivatedAt { get; set; }
    public DateTime? LastListenerSeenAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ChatPhaseGuardDecision
{
    public long ChatId { get; set; }
    public bool Allowed { get; set; }
    public string RequestedPhase { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public string DenyCode { get; set; } = string.Empty;
    public string DenyReason { get; set; } = string.Empty;
    public DateTime? CurrentLeaseExpiresAtUtc { get; set; }
    public bool CurrentLeaseIsFresh { get; set; }
    public bool RecoveryApplied { get; set; }
    public string RecoveryCode { get; set; } = string.Empty;
    public string RecoveryReason { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ChatPhaseLeaseRenewDecision
{
    public long ChatId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public bool Renewed { get; set; }
    public string DenyCode { get; set; } = string.Empty;
    public string DenyReason { get; set; } = string.Empty;
    public DateTime? PreviousLeaseExpiresAtUtc { get; set; }
    public DateTime? CurrentLeaseExpiresAtUtc { get; set; }
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ChatPhaseReleaseResult
{
    public long ChatId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public bool Released { get; set; }
    public bool OwnershipMismatch { get; set; }
    public string CurrentPhase { get; set; } = string.Empty;
    public string CurrentOwnerId { get; set; } = string.Empty;
    public DateTime? CurrentLeaseExpiresAtUtc { get; set; }
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BackupMetadataEvidence
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string ArtifactUri { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
}

public sealed class RiskOperationOverride
{
    public string OperatorIdentity { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ApprovalToken { get; set; } = string.Empty;
    public string AuditId { get; set; } = string.Empty;
}

public static class IntegrityPreflightStates
{
    public const string Clean = "clean";
    public const string Warning = "warning";
    public const string Unsafe = "unsafe";
}

public sealed class IntegrityPreflightCheck
{
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class IntegrityPreflightSummary
{
    public string Result { get; set; } = IntegrityPreflightStates.Clean;
    public string Scope { get; set; } = string.Empty;
    public List<IntegrityPreflightCheck> Checks { get; set; } = [];
    public List<string> BlockingReasons { get; set; } = [];
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}
