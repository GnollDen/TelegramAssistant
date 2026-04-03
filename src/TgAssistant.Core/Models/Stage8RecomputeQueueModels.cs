namespace TgAssistant.Core.Models;

public static class Stage8RecomputeTargetFamilies
{
    public const string Stage6Bootstrap = "stage6_bootstrap";
    public const string DossierProfile = "dossier_profile";
    public const string PairDynamics = "pair_dynamics";
    public const string TimelineObjects = "timeline_objects";

    public static IReadOnlyList<string> All { get; } =
    [
        Stage6Bootstrap,
        DossierProfile,
        PairDynamics,
        TimelineObjects
    ];
}

public static class Stage8RecomputeQueueStatuses
{
    public const string Pending = "pending";
    public const string Leased = "leased";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class Stage8RecomputeExecutionStatuses
{
    public const string NoWorkAvailable = "no_work_available";
    public const string Completed = "completed";
    public const string Rescheduled = "rescheduled";
    public const string FailedTerminally = "failed_terminally";
    public const string BlockedInvalidInput = ModelPassResultStatuses.BlockedInvalidInput;
}

public static class Stage8PromotionStates
{
    public const string Pending = "pending";
    public const string Promoted = "promoted";
    public const string PromotionBlocked = "promotion_blocked";
    public const string ClarificationBlocked = "clarification_blocked";
}

public class Stage8RecomputeQueueRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public string TargetFamily { get; set; } = string.Empty;
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
    public int Priority { get; set; } = 100;
    public int MaxAttempts { get; set; } = 5;
    public DateTime? AvailableAtUtc { get; set; }
}

public class Stage8RecomputeQueueItem
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public string TargetFamily { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public string? ActiveDedupeKey { get; set; }
    public string TriggerKind { get; set; } = string.Empty;
    public string? TriggerRef { get; set; }
    public string Status { get; set; } = Stage8RecomputeQueueStatuses.Pending;
    public int Priority { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime AvailableAtUtc { get; set; }
    public DateTime? LeasedUntilUtc { get; set; }
    public Guid? LeaseToken { get; set; }
    public string? LastError { get; set; }
    public string? LastResultStatus { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public class Stage8RecomputeExecutionResult
{
    public bool Executed { get; set; }
    public Stage8RecomputeQueueItem? QueueItem { get; set; }
    public string ExecutionStatus { get; set; } = string.Empty;
    public string? ResultStatus { get; set; }
    public Guid? ModelPassRunId { get; set; }
    public string? Error { get; set; }
}

public class Stage8OutcomeGateRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public string TargetFamily { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = ModelPassResultStatuses.BlockedInvalidInput;
    public Guid? ModelPassRunId { get; set; }
    public string? TriggerKind { get; set; }
    public string? TriggerRef { get; set; }
}

public class Stage8OutcomeGateResult
{
    public string ScopeKey { get; set; } = string.Empty;
    public string TargetFamily { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public int AffectedCount { get; set; }
    public int PromotedCount { get; set; }
    public int PromotionBlockedCount { get; set; }
    public int ClarificationBlockedCount { get; set; }
}
