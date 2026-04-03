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
}

public static class ResolutionItemPriorities
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
}

public class ResolutionQueueRequest
{
    public Guid TrackedPersonId { get; set; }
    public string? ItemTypeFilter { get; set; }
    public int Limit { get; set; } = 50;
}

public class ResolutionDetailRequest
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public int EvidenceLimit { get; set; } = 5;
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
    public string AffectedFamily { get; set; } = string.Empty;
    public string AffectedObjectRef { get; set; } = string.Empty;
    public float TrustFactor { get; set; }
    public string Status { get; set; } = ResolutionItemStatuses.Open;
    public int EvidenceCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Priority { get; set; } = ResolutionItemPriorities.Medium;
    public string? RecommendedNextAction { get; set; }
}

public class ResolutionItemDetail : ResolutionItemSummary
{
    public string SourceKind { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
    public List<ResolutionDetailNote> Notes { get; set; } = [];
    public List<ResolutionEvidenceSummary> Evidence { get; set; } = [];
}

public class ResolutionDetailNote
{
    public string Kind { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class ResolutionEvidenceSummary
{
    public Guid EvidenceItemId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public float TrustFactor { get; set; }
    public DateTime? ObservedAtUtc { get; set; }
    public string? SourceRef { get; set; }
    public string? SourceLabel { get; set; }
}
