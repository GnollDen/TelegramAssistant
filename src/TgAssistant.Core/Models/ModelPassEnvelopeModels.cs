namespace TgAssistant.Core.Models;

public static class ModelPassEnvelopeSchema
{
    public const int CurrentVersion = 1;
}

public static class ModelPassResultStatuses
{
    public const string ResultReady = "result_ready";
    public const string NeedMoreData = "need_more_data";
    public const string NeedOperatorClarification = "need_operator_clarification";
    public const string BlockedInvalidInput = "blocked_invalid_input";

    public static IReadOnlyList<string> All { get; } =
    [
        ResultReady,
        NeedMoreData,
        NeedOperatorClarification,
        BlockedInvalidInput
    ];
}

public class ModelPassEnvelope
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public int SchemaVersion { get; set; } = ModelPassEnvelopeSchema.CurrentVersion;
    public string Stage { get; set; } = string.Empty;
    public string PassFamily { get; set; } = string.Empty;
    public string RunKind { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public ModelPassScope Scope { get; set; } = new();
    public ModelPassTarget Target { get; set; } = new();
    public Guid? PersonId { get; set; }
    public Guid? SourceObjectId { get; set; }
    public Guid? EvidenceItemId { get; set; }
    public string? RequestedModel { get; set; }
    public string? TriggerKind { get; set; }
    public string? TriggerRef { get; set; }
    public List<ModelPassSourceRef> SourceRefs { get; set; } = [];
    public ModelPassTruthSummary TruthSummary { get; set; } = new();
    public List<ModelPassConflict> Conflicts { get; set; } = [];
    public List<ModelPassUnknown> Unknowns { get; set; } = [];
    public string ResultStatus { get; set; } = ModelPassResultStatuses.BlockedInvalidInput;
    public ModelPassOutputSummary OutputSummary { get; set; } = new();
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ModelPassScope
{
    public string ScopeType { get; set; } = string.Empty;
    public string ScopeRef { get; set; } = string.Empty;
    public List<string> AdditionalRefs { get; set; } = [];
}

public class ModelPassTarget
{
    public string TargetType { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
}

public class ModelPassSourceRef
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public Guid? SourceObjectId { get; set; }
    public Guid? EvidenceItemId { get; set; }
}

public class ModelPassTruthSummary
{
    public string TruthLayer { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> CanonicalRefs { get; set; } = [];
}

public class ModelPassConflict
{
    public string ConflictType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RelatedObjectRef { get; set; }
}

public class ModelPassUnknown
{
    public string UnknownType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
}

public class ModelPassOutputSummary
{
    public string Summary { get; set; } = string.Empty;
    public string? BlockedReason { get; set; }
}
