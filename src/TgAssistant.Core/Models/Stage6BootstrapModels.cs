namespace TgAssistant.Core.Models;

public static class Stage6BootstrapStatuses
{
    public const string Ready = "ready";
    public const string NeedMoreData = ModelPassResultStatuses.NeedMoreData;
    public const string NeedOperatorClarification = ModelPassResultStatuses.NeedOperatorClarification;
    public const string BlockedInvalidInput = ModelPassResultStatuses.BlockedInvalidInput;
}

public static class Stage6BootstrapNodeTypes
{
    public const string OperatorRoot = "operator_root";
    public const string TrackedPersonSeed = "tracked_person_seed";
}

public static class Stage6BootstrapEdgeTypes
{
    public const string TrackedPersonAttachment = "tracked_person_attachment";
}

public static class Stage6BootstrapDiscoveryTypes
{
    public const string LinkedPerson = "linked_person";
    public const string CandidateIdentity = "candidate_identity";
    public const string Mention = "mention";
}

public static class Stage6BootstrapPoolOutputTypes
{
    public const string AmbiguityPool = "ambiguity_pool";
    public const string ContradictionPool = "contradiction_pool";
    public const string BootstrapSlice = "bootstrap_slice";
}

public class Stage6BootstrapRequest
{
    public Guid? PersonId { get; set; }
    public string? ScopeKey { get; set; }
    public string RunKind { get; set; } = "manual";
    public string RequestedModel { get; set; } = "stage6-bootstrap-deterministic";
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
}

public class Stage6BootstrapPersonRef
{
    public Guid PersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string PersonType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CanonicalName { get; set; } = string.Empty;
    public string PersonRef => $"person:{PersonId:D}";
}

public class Stage6BootstrapSourceRef
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public Guid? SourceObjectId { get; set; }
    public Guid? EvidenceItemId { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime? ObservedAtUtc { get; set; }
}

public class Stage6BootstrapScopeResolution
{
    public string ResolutionStatus { get; set; } = Stage6BootstrapStatuses.BlockedInvalidInput;
    public string ScopeKey { get; set; } = string.Empty;
    public Stage6BootstrapPersonRef? TrackedPerson { get; set; }
    public Stage6BootstrapPersonRef? OperatorPerson { get; set; }
    public int EvidenceCount { get; set; }
    public DateTime? LatestEvidenceAtUtc { get; set; }
    public List<Stage6BootstrapSourceRef> SourceRefs { get; set; } = [];
    public string? Reason { get; set; }
    public List<ModelPassUnknown> Unknowns { get; set; } = [];
    public List<ModelPassConflict> Conflicts { get; set; } = [];
}

public class Stage6BootstrapGraphNode
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public string NodeRef { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

public class Stage6BootstrapGraphEdge
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? LastModelPassRunId { get; set; }
    public string FromNodeRef { get; set; } = string.Empty;
    public string ToNodeRef { get; set; } = string.Empty;
    public string EdgeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

public class Stage6BootstrapDiscoveryOutput
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string DiscoveryType { get; set; } = string.Empty;
    public string DiscoveryKey { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public Guid? CandidateIdentityStateId { get; set; }
    public long? SourceMessageId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

public class Stage6BootstrapPoolOutput
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string OutputType { get; set; } = string.Empty;
    public string OutputKey { get; set; } = string.Empty;
    public Guid? CandidateIdentityStateId { get; set; }
    public Guid? RelationshipEdgeAnchorId { get; set; }
    public long? SourceMessageId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
}

public class Stage6BootstrapPoolOutputSet
{
    public List<Stage6BootstrapPoolOutput> AmbiguityOutputs { get; set; } = [];
    public List<Stage6BootstrapPoolOutput> ContradictionOutputs { get; set; } = [];
    public List<Stage6BootstrapPoolOutput> SliceOutputs { get; set; } = [];
}

public class Stage6BootstrapGraphResult
{
    public ModelPassAuditRecord AuditRecord { get; set; } = new();
    public bool GraphInitialized { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Stage6BootstrapPersonRef? TrackedPerson { get; set; }
    public Stage6BootstrapPersonRef? OperatorPerson { get; set; }
    public int EvidenceCount { get; set; }
    public DateTime? LatestEvidenceAtUtc { get; set; }
    public List<Stage6BootstrapGraphNode> Nodes { get; set; } = [];
    public List<Stage6BootstrapGraphEdge> Edges { get; set; } = [];
    public List<Stage6BootstrapDiscoveryOutput> DiscoveryOutputs { get; set; } = [];
    public List<Stage6BootstrapPoolOutput> AmbiguityOutputs { get; set; } = [];
    public List<Stage6BootstrapPoolOutput> ContradictionOutputs { get; set; } = [];
    public List<Stage6BootstrapPoolOutput> SliceOutputs { get; set; } = [];
}
