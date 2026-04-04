namespace TgAssistant.Core.Models;

public static class ResolutionActionTypes
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Defer = "defer";
    public const string Clarify = "clarify";
    public const string Evidence = "evidence";
    public const string OpenWeb = "open-web";

    private static readonly string[] Ordered = 
    [
        Approve,
        Reject,
        Defer,
        Clarify,
        Evidence,
        OpenWeb
    ];

    private static readonly HashSet<string> MutatingSupported = new(StringComparer.Ordinal)
    {
        Approve,
        Reject,
        Defer,
        Clarify
    };

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static IReadOnlyList<string> All { get; } = Ordered;

    public static IReadOnlyCollection<string> Mutating => MutatingSupported;

    public static string Normalize(string? actionType)
    {
        return string.IsNullOrWhiteSpace(actionType)
            ? string.Empty
            : actionType.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? actionType)
        => Supported.Contains(Normalize(actionType));

    public static bool IsMutatingSupported(string? actionType)
        => MutatingSupported.Contains(Normalize(actionType));

    public static bool RequiresExplanation(string? actionType)
    {
        return Normalize(actionType) is Reject or Defer or Clarify;
    }
}

public static class OperatorSurfaceTypes
{
    public const string Telegram = "telegram";
    public const string Web = "web";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Telegram,
        Web
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? surface)
    {
        return string.IsNullOrWhiteSpace(surface)
            ? string.Empty
            : surface.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? surface)
        => Supported.Contains(Normalize(surface));
}

public static class OperatorModeTypes
{
    public const string Assistant = "assistant";
    public const string ResolutionQueue = "resolution_queue";
    public const string ResolutionDetail = "resolution_detail";
    public const string Clarification = "clarification";
    public const string Evidence = "evidence";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Assistant,
        ResolutionQueue,
        ResolutionDetail,
        Clarification,
        Evidence
    };

    private static readonly HashSet<string> MutatingAllowed = new(StringComparer.Ordinal)
    {
        ResolutionQueue,
        ResolutionDetail,
        Clarification
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode)
            ? string.Empty
            : mode.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? mode)
        => Supported.Contains(Normalize(mode));

    public static bool AllowsMutatingAction(string? mode)
        => MutatingAllowed.Contains(Normalize(mode));
}

public static class OperatorAuditDecisionOutcomes
{
    public const string Accepted = "accepted";
    public const string Denied = "denied";
}

public class OperatorIdentityContext
{
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorDisplay { get; set; } = string.Empty;
    public string SurfaceSubject { get; set; } = string.Empty;
    public string AuthSource { get; set; } = string.Empty;
    public DateTime AuthTimeUtc { get; set; }
}

public class OperatorWorkflowStepContext
{
    public string StepKind { get; set; } = string.Empty;
    public string StepState { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public Guid BoundTrackedPersonId { get; set; }
    public string BoundScopeItemKey { get; set; } = string.Empty;
}

public class OperatorSessionContext
{
    public string OperatorSessionId { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public DateTime AuthenticatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public Guid ActiveTrackedPersonId { get; set; }
    public string? ActiveScopeItemKey { get; set; }
    public string ActiveMode { get; set; } = string.Empty;
    public OperatorWorkflowStepContext? UnfinishedStep { get; set; }
}

public class ResolutionClarificationResponse
{
    public string QuestionKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string AnswerValue { get; set; } = string.Empty;
    public string? AnswerKind { get; set; }
    public string? Notes { get; set; }
}

public class ResolutionClarificationPayload
{
    public string? Summary { get; set; }
    public List<ResolutionClarificationResponse> Responses { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public class ResolutionActionRequest
{
    public string RequestId { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public ResolutionClarificationPayload? ClarificationPayload { get; set; }
    public OperatorIdentityContext OperatorIdentity { get; set; } = new();
    public OperatorSessionContext Session { get; set; } = new();
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ResolutionActionResult
{
    public bool Accepted { get; set; }
    public bool IdempotentReplay { get; set; }
    public string? FailureReason { get; set; }
    public Guid? ActionId { get; set; }
    public Guid? AuditEventId { get; set; }
    public Guid? TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string? ItemType { get; set; }
    public ResolutionRecomputeContract? Recompute { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

public static class ResolutionRecomputeLifecycleStatuses
{
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string ClarificationBlocked = "clarification_blocked";

    public static IReadOnlyList<string> All { get; } =
    [
        Running,
        Done,
        Failed,
        ClarificationBlocked
    ];
}

public static class ResolutionRecomputeMappingRules
{
    public const string AffectedFamilyExact = "affected_family_exact";
    public const string RuntimeControlScopeBootstrap = "runtime_control_scope_bootstrap";
    public const string RuntimeDefectScopeBootstrap = "runtime_defect_scope_bootstrap";
}

public class ResolutionRecomputeContract
{
    public bool Enqueued { get; set; }
    public string TriggerKind { get; set; } = string.Empty;
    public string TriggerRef { get; set; } = string.Empty;
    public string LifecycleStatus { get; set; } = ResolutionRecomputeLifecycleStatuses.Running;
    public DateTime? LifecycleUpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastResultStatus { get; set; }
    public string? FailureReason { get; set; }
    public List<ResolutionRecomputeTarget> Targets { get; set; } = [];
}

public class ResolutionRecomputeTarget
{
    public Guid? QueueItemId { get; set; }
    public string TargetFamily { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public string MappingRule { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string LifecycleStatus { get; set; } = ResolutionRecomputeLifecycleStatuses.Running;
    public DateTime? LifecycleUpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastResultStatus { get; set; }
    public string? FailureReason { get; set; }
}
