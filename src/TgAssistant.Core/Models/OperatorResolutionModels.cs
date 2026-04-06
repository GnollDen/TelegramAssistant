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
    public const string OfflineEvent = "offline_event";
    public const string ResolutionQueue = "resolution_queue";
    public const string ResolutionDetail = "resolution_detail";
    public const string Clarification = "clarification";
    public const string Evidence = "evidence";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Assistant,
        OfflineEvent,
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
    public Guid? ConflictResolutionSessionId { get; set; }
    public int? ConflictVerdictRevision { get; set; }
    public ResolutionConflictSessionVerdict? ConflictVerdict { get; set; }
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
    public Guid? ConflictResolutionSessionId { get; set; }
    public ResolutionRecomputeContract? Recompute { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

public static class ResolutionConflictSessionStates
{
    public const string RunningInitial = "running_initial";
    public const string AwaitingOperatorAnswer = "awaiting_operator_answer";
    public const string RunningFinal = "running_final";
    public const string ReadyForCommit = "ready_for_commit";
    public const string NeedsWebReview = "needs_web_review";
    public const string Fallback = "fallback";
    public const string HandedOff = "handed_off";
    public const string Expired = "expired";
    public const string Failed = "failed";
}

public static class ResolutionConflictSessionContract
{
    public const string Version = "ai_conflict_resolution_session_v1";
    public const string StepKind = "resolution_conflict_session_v1";
}

public class ResolutionConflictSessionBudget
{
    public int MaxModelCalls { get; set; } = 2;
    public int UsedModelCalls { get; set; }
    public int MaxOperatorQuestions { get; set; } = 1;
    public int UsedOperatorQuestions { get; set; }
    public int MaxOperatorAnswers { get; set; } = 1;
    public int UsedOperatorAnswers { get; set; }
    public int MaxRetrievalRounds { get; set; } = 0;
    public int UsedRetrievalRounds { get; set; }
    public int TtlSeconds { get; set; } = 1800;
}

public class ResolutionConflictSessionQuestion
{
    public string QuestionKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string AnswerKind { get; set; } = "free_text";
    public string? Notes { get; set; }
}

public class ResolutionConflictSessionOperatorInput
{
    public string QuestionKey { get; set; } = string.Empty;
    public string AnswerValue { get; set; } = string.Empty;
    public string AnswerKind { get; set; } = "free_text";
    public string? Notes { get; set; }
    public DateTime AnsweredAtUtc { get; set; }
}

public class ResolutionConflictSessionClaim
{
    public string ClaimType { get; set; } = ResolutionInterpretationClaimTypes.Hypothesis;
    public string Summary { get; set; } = string.Empty;
    public List<string> EvidenceRefs { get; set; } = [];
    public List<string> OperatorInputRefs { get; set; } = [];
}

public class ResolutionConflictSessionRejectedClaim
{
    public string Summary { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class ResolutionConflictNormalizationProposal
{
    public string RecommendedAction { get; set; } = ResolutionActionTypes.Clarify;
    public string Explanation { get; set; } = string.Empty;
    public ResolutionClarificationPayload? ClarificationPayload { get; set; }
}

public class ResolutionConflictConfidenceCalibration
{
    public float ConfidenceScore { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public class ResolutionConflictSessionVerdict
{
    public string ResolutionVerdict { get; set; } = ResolutionConflictSessionStates.NeedsWebReview;
    public List<ResolutionConflictSessionClaim> ResolvedClaims { get; set; } = [];
    public List<ResolutionConflictSessionRejectedClaim> RejectedClaims { get; set; } = [];
    public List<string> EvidenceRefsUsed { get; set; } = [];
    public List<string> OperatorInputsUsed { get; set; } = [];
    public List<string> RemainingUncertainties { get; set; } = [];
    public ResolutionConflictNormalizationProposal NormalizationProposal { get; set; } = new();
    public ResolutionConflictConfidenceCalibration ConfidenceCalibration { get; set; } = new();
}

public class ResolutionConflictSessionCasePacket
{
    public ResolutionItemDetail Item { get; set; } = new();
    public ResolutionInterpretationLoopResult? InterpretationLoop { get; set; }
    public List<ResolutionEvidenceSummary> Evidence { get; set; } = [];
    public List<ResolutionDetailNote> Notes { get; set; } = [];
    public List<string> DurableContextSummaries { get; set; } = [];
}

public class ResolutionConflictSessionView
{
    public Guid ConflictSessionId { get; set; }
    public string ContractVersion { get; set; } = ResolutionConflictSessionContract.Version;
    public string State { get; set; } = ResolutionConflictSessionStates.RunningInitial;
    public string? StateReason { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string Surface { get; set; } = OperatorSurfaceTypes.Web;
    public int Revision { get; set; } = 1;
    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public ResolutionConflictSessionBudget Budgets { get; set; } = new();
    public ResolutionConflictSessionCasePacket? InitialCasePacket { get; set; }
    public ResolutionConflictSessionQuestion? OperatorQuestion { get; set; }
    public ResolutionConflictSessionOperatorInput? OperatorAnswer { get; set; }
    public ResolutionConflictSessionVerdict? FinalVerdict { get; set; }
    public List<ResolutionInterpretationAuditEntry> AuditTrail { get; set; } = [];
    public string? FailureReason { get; set; }
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
