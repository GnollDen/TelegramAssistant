using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class ResolutionItemTypes
{
    public const string Clarification = "clarification";
    public const string Review = "review";
    public const string Contradiction = "contradiction";
    public const string MissingData = "missing_data";
    public const string BlockedBranch = "blocked_branch";

    public static IReadOnlyList<string> All { get; } =
    [
        Clarification,
        Review,
        Contradiction,
        MissingData,
        BlockedBranch
    ];
}

public static class ResolutionItemStatuses
{
    public const string Open = "open";
    public const string Blocked = "blocked";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string AttentionRequired = "attention_required";
    public const string Degraded = "degraded";

    public static IReadOnlyList<string> All { get; } =
    [
        Open,
        Blocked,
        Queued,
        Running,
        AttentionRequired,
        Degraded
    ];
}

public static class ResolutionItemPriorities
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static IReadOnlyList<string> All { get; } =
    [
        Critical,
        High,
        Medium,
        Low
    ];
}

public class ResolutionQueueRequest
{
    public Guid TrackedPersonId { get; set; }
    public List<string> ItemTypes { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<string> Priorities { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
    public string SortBy { get; set; } = ResolutionQueueSortFields.Priority;
    public string SortDirection { get; set; } = ResolutionSortDirections.Desc;
    public int Limit { get; set; } = 50;
}

public class ResolutionDetailRequest
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public int EvidenceLimit { get; set; } = 5;
    public string EvidenceSortBy { get; set; } = ResolutionEvidenceSortFields.ObservedAt;
    public string EvidenceSortDirection { get; set; } = ResolutionSortDirections.Desc;
    public bool IncludeInterpretation { get; set; } = true;
}

public class ResolutionRuntimeStateSummary
{
    public string State { get; set; } = RuntimeControlStates.Normal;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTime ActivatedAtUtc { get; set; }
}

public class ResolutionQueueResult
{
    public bool ScopeBound { get; set; }
    public string? ScopeFailureReason { get; set; }
    public Guid? TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string? TrackedPersonDisplayName { get; set; }
    public ResolutionRuntimeStateSummary? RuntimeState { get; set; }
    public int TotalOpenCount { get; set; }
    public int FilteredCount { get; set; }
    public List<ResolutionFacetCount> ItemTypeCounts { get; set; } = [];
    public List<ResolutionFacetCount> StatusCounts { get; set; } = [];
    public List<ResolutionFacetCount> PriorityCounts { get; set; } = [];
    public List<ResolutionItemSummary> Items { get; set; } = [];
}

public class ResolutionDetailResult
{
    public bool ScopeBound { get; set; }
    public string? ScopeFailureReason { get; set; }
    public bool ItemFound { get; set; }
    public ResolutionItemDetail? Item { get; set; }
}

public class ResolutionItemSummary
{
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string? HumanShortTitle { get; set; }
    public string? WhatHappened { get; set; }
    public string? WhyOperatorAnswerNeeded { get; set; }
    public string? WhatToDoPrompt { get; set; }
    public string? EvidenceHint { get; set; }
    public string? SecondaryText { get; set; }
    public string AffectedFamily { get; set; } = string.Empty;
    public string AffectedObjectRef { get; set; } = string.Empty;
    public float TrustFactor { get; set; }
    public string Status { get; set; } = ResolutionItemStatuses.Open;
    public int EvidenceCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Priority { get; set; } = ResolutionItemPriorities.Medium;
    public string? RecommendedNextAction { get; set; }
    public List<string> AvailableActions { get; set; } = [];
}

public class ResolutionItemDetail : ResolutionItemSummary
{
    public string SourceKind { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
    public string EvidenceRationaleSummary { get; set; } = string.Empty;
    public string AutoResolutionGap { get; set; } = string.Empty;
    public string OperatorDecisionFocus { get; set; } = string.Empty;
    public bool RationaleIsHeuristic { get; set; } = true;
    public ResolutionInterpretationLoopResult? InterpretationLoop { get; set; }
    public List<ResolutionDetailNote> Notes { get; set; } = [];
    public List<ResolutionEvidenceSummary> Evidence { get; set; } = [];
}

public static class ResolutionInterpretationContextTypes
{
    public const string None = "none";
    public const string AdditionalEvidence = "additional_evidence";
    public const string DurableContext = "durable_context";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        None,
        AdditionalEvidence,
        DurableContext
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static bool IsSupported(string? value)
        => Supported.Contains(Normalize(value));

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? None : value.Trim().ToLowerInvariant();
}

public static class ResolutionInterpretationReviewRecommendations
{
    public const string Review = "review";
    public const string NoReview = "no_review";
}

public class ResolutionInterpretationLoopRequest
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public ResolutionItemSummary Item { get; set; } = new();
    public string SourceKind { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
    public List<ResolutionDetailNote> Notes { get; set; } = [];
    public List<ResolutionEvidenceSummary> Evidence { get; set; } = [];
    public List<string> DurableContextSummaries { get; set; } = [];
}

public class ResolutionInterpretationLoopResult
{
    public bool Applied { get; set; }
    public bool UsedFallback { get; set; }

    [JsonPropertyName("context_sufficient")]
    public bool ContextSufficient { get; set; } = true;

    [JsonPropertyName("requested_context_type")]
    public string RequestedContextType { get; set; } = ResolutionInterpretationContextTypes.None;

    [JsonPropertyName("interpretation_summary")]
    public string InterpretationSummary { get; set; } = string.Empty;

    [JsonPropertyName("key_claims")]
    public List<ResolutionInterpretationClaim> KeyClaims { get; set; } = [];

    [JsonPropertyName("explicit_uncertainties")]
    public List<string> ExplicitUncertainties { get; set; } = [];

    [JsonPropertyName("review_recommendation")]
    public ResolutionInterpretationReviewRecommendation ReviewRecommendation { get; set; } = new();

    [JsonPropertyName("evidence_refs_used")]
    public List<string> EvidenceRefsUsed { get; set; } = [];

    public string? FailureReason { get; set; }
    public List<ResolutionInterpretationAuditEntry> AuditTrail { get; set; } = [];
}

public class ResolutionInterpretationClaim
{
    [JsonPropertyName("claim_type")]
    public string ClaimType { get; set; } = ResolutionInterpretationClaimTypes.Hypothesis;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];
}

public static class ResolutionInterpretationClaimTypes
{
    public const string Fact = "fact";
    public const string Inference = "inference";
    public const string Hypothesis = "hypothesis";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Fact,
        Inference,
        Hypothesis
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Hypothesis;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return Supported.Contains(normalized)
            ? normalized
            : Hypothesis;
    }
}

public class ResolutionInterpretationReviewRecommendation
{
    [JsonPropertyName("decision")]
    public string Decision { get; set; } = ResolutionInterpretationReviewRecommendations.Review;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public class ResolutionInterpretationAuditEntry
{
    [JsonPropertyName("step")]
    public string Step { get; set; } = string.Empty;

    [JsonPropertyName("retrieval_round")]
    public int RetrievalRound { get; set; }

    [JsonPropertyName("requested_context_type")]
    public string RequestedContextType { get; set; } = ResolutionInterpretationContextTypes.None;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("latency_ms")]
    public int? LatencyMs { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("observed_at_utc")]
    public DateTime ObservedAtUtc { get; set; }
}

public class ResolutionInterpretationModelRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public int RetrievalRound { get; set; }
    public IReadOnlyCollection<string> AllowedContextTypes { get; set; } = [];
    public ResolutionInterpretationLoopRequest Context { get; set; } = new();
    public ResolutionInterpretationAdditionalContext AdditionalContext { get; set; } = new();
}

public class ResolutionInterpretationAdditionalContext
{
    public List<ResolutionEvidenceSummary> Evidence { get; set; } = [];
    public List<string> DurableContextSummaries { get; set; } = [];
}

public class ResolutionInterpretationModelResponse
{
    public ResolutionInterpretationLoopResult Interpretation { get; set; } = new();
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int LatencyMs { get; set; }
    public int TotalTokens { get; set; }
}

public class ResolutionDetailNote
{
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public static class ResolutionDecisionLinkTypes
{
    public const string DecisionUnit = "decision_unit";
    public const string Criterion = "criterion";
    public const string Claim = "claim";
    public const string ReviewQuestion = "review_question";
}

public static class ResolutionDecisionStances
{
    public const string Supports = "supports";
    public const string Challenges = "challenges";
    public const string Ambiguous = "ambiguous";
}

public static class ResolutionDecisionHeuristicCalibrations
{
    public const string Low = "low";
    public const string Medium = "medium";
}

public class ResolutionEvidenceDecisionLinkage
{
    public string LinkType { get; set; } = ResolutionDecisionLinkTypes.Criterion;
    public string LinkTarget { get; set; } = string.Empty;
    public string? ReviewQuestion { get; set; }
    public string Stance { get; set; } = ResolutionDecisionStances.Ambiguous;
    public string Summary { get; set; } = string.Empty;
    public bool IsHeuristic { get; set; } = true;
    public string? HeuristicCalibration { get; set; }
}

public class ResolutionEvidenceSummary
{
    public Guid EvidenceItemId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public float TrustFactor { get; set; }
    public DateTime? ObservedAtUtc { get; set; }
    public string? SenderDisplay { get; set; }
    public string? SourceRef { get; set; }
    public string? SourceLabel { get; set; }
    public string RelevanceHint { get; set; } = string.Empty;
    public bool RelevanceHintIsHeuristic { get; set; } = true;
    public ResolutionEvidenceDecisionLinkage? DecisionLinkage { get; set; }
}
