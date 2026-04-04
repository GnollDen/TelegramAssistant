namespace TgAssistant.Core.Models;

public static class Stage8RelatedConflictTypes
{
    public const string RecomputedContradiction = "stage8_related_contradiction";
}

public static class Stage8RelatedConflictOperationKinds
{
    public const string Create = "create";
    public const string Refresh = "refresh";
    public const string Resolve = "resolve";
    public const string Unchanged = "unchanged";
}

public sealed class Stage8RelatedConflictReevaluationRequest
{
    public Guid QueueItemId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public string TargetFamily { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public string? TriggerKind { get; set; }
    public string? TriggerRef { get; set; }
    public Guid? ModelPassRunId { get; set; }
}

public sealed class Stage8RelatedConflictReevaluationResult
{
    public bool Applied { get; set; }
    public string? SkipReason { get; set; }
    public int CreatedCount { get; set; }
    public int RefreshedCount { get; set; }
    public int ResolvedCount { get; set; }
    public int UnchangedCount { get; set; }
    public List<Guid> ActiveConflictIds { get; set; } = [];
    public List<Guid> ResolvedConflictIds { get; set; } = [];
}

public sealed class Stage8RelatedConflictSnapshot
{
    public Guid MetadataId { get; set; }
    public string ObjectFamily { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public int ContradictionCount { get; set; }
}

public sealed class Stage8RelatedConflictOperation
{
    public string Kind { get; set; } = string.Empty;
    public Guid? ExistingConflictId { get; set; }
    public Guid MetadataId { get; set; }
    public string ObjectFamily { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
