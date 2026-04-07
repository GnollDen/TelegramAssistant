using System.Text.Json.Serialization;

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
    public Guid? CarryForwardCaseId { get; set; }
    public Guid? ReintegrationEntryId { get; set; }
    public string? OriginSourceKind { get; set; }
    public Guid? PredecessorCarryForwardCaseId { get; set; }
    public Guid? SuccessorCarryForwardCaseId { get; set; }
    public string? PreviousCaseStatus { get; set; }
    public string? NextCaseStatus { get; set; }
    public string? RecomputeTargetFamily { get; set; }
    public string? RecomputeTargetRef { get; set; }
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
    public Guid? CarryForwardCaseId { get; set; }
    public Guid? ReintegrationEntryId { get; set; }
    public string? OriginSourceKind { get; set; }
    public Guid? PredecessorCarryForwardCaseId { get; set; }
    public Guid? SuccessorCarryForwardCaseId { get; set; }
    public string? PreviousCaseStatus { get; set; }
    public string? NextCaseStatus { get; set; }
    public string? RecomputeTargetFamily { get; set; }
    public string? RecomputeTargetRef { get; set; }
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

public static class ConflictResolutionSessionToolNames
{
    public const string GetNeighborMessages = "get_neighbor_messages";
    public const string GetEvidenceRefs = "get_evidence_refs";
    public const string GetDurableContext = "get_durable_context";
    public const string AskOperatorQuestion = "ask_operator_question";

    public static IReadOnlyList<string> Allowed { get; } =
    [
        GetNeighborMessages,
        GetEvidenceRefs,
        GetDurableContext,
        AskOperatorQuestion
    ];

    public static string Normalize(string? toolName)
        => string.IsNullOrWhiteSpace(toolName) ? string.Empty : toolName.Trim().ToLowerInvariant();

    public static bool IsAllowed(string? toolName)
        => Allowed.Contains(Normalize(toolName), StringComparer.Ordinal);
}

public static class ConflictResolutionSessionToolDecisions
{
    public const string Accepted = "accepted";
    public const string ToolNotAllowed = "tool_not_allowed";
    public const string CrossScopeToolRequestRejected = "cross_scope_tool_request_rejected";
}

public static class ConflictResolutionSessionToolContract
{
    public const int MaxToolRequestsPerRound = 4;
    public const int MaxRequestItemsPerTool = 5;

    public static bool TryNormalize(
        ResolutionConflictSessionToolRequest? request,
        string expectedScopeItemKey,
        out ResolutionConflictSessionToolRequest normalized,
        out string decision)
    {
        normalized = new ResolutionConflictSessionToolRequest();
        var scopeItemKey = string.IsNullOrWhiteSpace(expectedScopeItemKey)
            ? string.Empty
            : expectedScopeItemKey.Trim();
        if (request == null)
        {
            decision = ConflictResolutionSessionToolDecisions.ToolNotAllowed;
            return false;
        }

        normalized.ToolName = ConflictResolutionSessionToolNames.Normalize(request.ToolName);
        normalized.RequestScope = string.IsNullOrWhiteSpace(request.RequestScope)
            ? string.Empty
            : request.RequestScope.Trim();
        normalized.RequestItems = (request.RequestItems ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(MaxRequestItemsPerTool)
            .ToList();

        if (!ConflictResolutionSessionToolNames.IsAllowed(normalized.ToolName))
        {
            decision = ConflictResolutionSessionToolDecisions.ToolNotAllowed;
            return false;
        }

        if (!string.Equals(normalized.RequestScope, scopeItemKey, StringComparison.Ordinal))
        {
            decision = ConflictResolutionSessionToolDecisions.CrossScopeToolRequestRejected;
            return false;
        }

        decision = ConflictResolutionSessionToolDecisions.Accepted;
        return true;
    }
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

public class ResolutionConflictSessionToolRequest
{
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("request_scope")]
    public string RequestScope { get; set; } = string.Empty;

    [JsonPropertyName("request_items")]
    public List<string> RequestItems { get; set; } = [];
}

public class ResolutionConflictSessionToolRequestManifest
{
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("request_scope")]
    public string RequestScope { get; set; } = string.Empty;

    [JsonPropertyName("request_items")]
    public List<string> RequestItems { get; set; } = [];
}

public class ResolutionConflictSessionToolResponseManifest
{
    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("request_scope")]
    public string RequestScope { get; set; } = string.Empty;

    [JsonPropertyName("request_items")]
    public List<string> RequestItems { get; set; } = [];

    [JsonPropertyName("decision")]
    public string Decision { get; set; } = ConflictResolutionSessionToolDecisions.Accepted;

    [JsonPropertyName("response_refs")]
    public List<string> ResponseRefs { get; set; } = [];
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
    public ConflictResolutionStructuredVerdict? StructuredVerdict { get; set; }
}

public static class ConflictResolutionStructuredDecisionTypes
{
    public const string Apply = "apply";
    public const string Defer = "defer";
    public const string Escalate = "escalate";
    public const string RejectScope = "reject_scope";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Apply,
        Defer,
        Escalate,
        RejectScope
    };

    public static bool IsSupported(string? value)
        => Supported.Contains(Normalize(value));

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

public static class ConflictResolutionStructuredPublicationStates
{
    public const string Publishable = "publishable";
    public const string InsufficientEvidence = "insufficient_evidence";
    public const string EscalationOnly = "escalation_only";
    public const string ManualReviewRequired = "manual_review_required";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Publishable,
        InsufficientEvidence,
        EscalationOnly,
        ManualReviewRequired
    };

    public static bool IsSupported(string? value)
        => Supported.Contains(Normalize(value));

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

public sealed class ConflictResolutionStructuredVerdict
{
    [JsonPropertyName("verdict_id")]
    public string VerdictId { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("scope_item_key")]
    public string ScopeItemKey { get; set; } = string.Empty;

    [JsonPropertyName("carry_forward_case_id")]
    public Guid? CarryForwardCaseId { get; set; }

    [JsonPropertyName("decision")]
    public string Decision { get; set; } = ConflictResolutionStructuredDecisionTypes.Defer;

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = ConflictResolutionStructuredPublicationStates.ManualReviewRequired;

    [JsonPropertyName("claim_rows")]
    public List<ConflictResolutionStructuredClaimRow> ClaimRows { get; set; } = [];

    [JsonPropertyName("uncertainty_rows")]
    public List<string> UncertaintyRows { get; set; } = [];

    [JsonPropertyName("normalization_plan")]
    public ConflictResolutionStructuredNormalizationPlan NormalizationPlan { get; set; } = new();

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ConflictResolutionStructuredClaimRow
{
    [JsonPropertyName("claim_type")]
    public string ClaimType { get; set; } = ResolutionInterpretationClaimTypes.Hypothesis;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];
}

public sealed class ConflictResolutionStructuredNormalizationPlan
{
    [JsonPropertyName("recommended_action")]
    public string RecommendedAction { get; set; } = ResolutionActionTypes.Clarify;

    [JsonPropertyName("explanation")]
    public string Explanation { get; set; } = string.Empty;

    [JsonPropertyName("clarification_payload")]
    public ResolutionClarificationPayload? ClarificationPayload { get; set; }
}

public static class ConflictResolutionStructuredVerdictContract
{
    public static bool TryValidate(ConflictResolutionStructuredVerdict? verdict, out string failureReason)
    {
        if (verdict == null)
        {
            failureReason = "structured_verdict_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(verdict.VerdictId))
        {
            failureReason = "structured_verdict_id_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(verdict.ScopeKey))
        {
            failureReason = "structured_scope_key_required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(verdict.ScopeItemKey))
        {
            failureReason = "structured_scope_item_key_required";
            return false;
        }

        if (!ConflictResolutionStructuredDecisionTypes.IsSupported(verdict.Decision))
        {
            failureReason = "structured_decision_invalid";
            return false;
        }

        if (!ConflictResolutionStructuredPublicationStates.IsSupported(verdict.PublicationState))
        {
            failureReason = "structured_publication_state_invalid";
            return false;
        }

        if (verdict.ClaimRows == null)
        {
            failureReason = "structured_claim_rows_required";
            return false;
        }

        if (verdict.UncertaintyRows == null)
        {
            failureReason = "structured_uncertainty_rows_required";
            return false;
        }

        if (verdict.NormalizationPlan == null)
        {
            failureReason = "structured_normalization_plan_required";
            return false;
        }

        if (verdict.EvidenceRefs == null)
        {
            failureReason = "structured_evidence_refs_required";
            return false;
        }

        if (verdict.CreatedAtUtc == default)
        {
            failureReason = "structured_created_at_required";
            return false;
        }

        for (var i = 0; i < verdict.ClaimRows.Count; i++)
        {
            var row = verdict.ClaimRows[i];
            if (row == null)
            {
                failureReason = $"structured_claim_row_{i}_required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.ClaimType))
            {
                failureReason = $"structured_claim_row_{i}_claim_type_required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(row.Summary))
            {
                failureReason = $"structured_claim_row_{i}_summary_required";
                return false;
            }

            if (row.EvidenceRefs == null)
            {
                failureReason = $"structured_claim_row_{i}_evidence_refs_required";
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }
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
    public Guid? CarryForwardCaseId { get; set; }
    public Guid? ReintegrationEntryId { get; set; }
    public string? OriginSourceKind { get; set; }
    public Guid? PredecessorCarryForwardCaseId { get; set; }
    public Guid? SuccessorCarryForwardCaseId { get; set; }
    public string? PreviousCaseStatus { get; set; }
    public string? NextCaseStatus { get; set; }
    public string? RecomputeTargetFamily { get; set; }
    public string? RecomputeTargetRef { get; set; }
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
