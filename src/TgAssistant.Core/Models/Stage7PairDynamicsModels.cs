namespace TgAssistant.Core.Models;

public static class Stage7PairDynamicsTypes
{
    public const string OperatorTrackedPair = "operator_tracked_pair";
}

public class Stage7PairDynamicsFormationRequest
{
    public Stage6BootstrapGraphResult BootstrapResult { get; set; } = new();
    public string RunKind { get; set; } = "manual";
    public string RequestedModel { get; set; } = "stage7-pair-dynamics-deterministic";
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
}

public class Stage7DurablePairDynamics
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid LeftPersonId { get; set; }
    public Guid RightPersonId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string PairDynamicsType { get; set; } = Stage7PairDynamicsTypes.OperatorTrackedPair;
    public string Status { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public string CurrentRevisionHash { get; set; } = string.Empty;
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7DurablePairDynamicsRevision
{
    public Guid Id { get; set; }
    public Guid DurablePairDynamicsId { get; set; }
    public int RevisionNumber { get; set; }
    public string RevisionHash { get; set; } = string.Empty;
    public Guid? ModelPassRunId { get; set; }
    public float Confidence { get; set; }
    public float Freshness { get; set; }
    public float Stability { get; set; }
    public string ContradictionMarkersJson { get; set; } = "[]";
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class Stage7PairDynamicsFormationResult
{
    public ModelPassAuditRecord AuditRecord { get; set; } = new();
    public bool Formed { get; set; }
    public Stage6BootstrapPersonRef? TrackedPerson { get; set; }
    public Stage6BootstrapPersonRef? OperatorPerson { get; set; }
    public Stage7DurablePairDynamics? PairDynamics { get; set; }
    public Stage7DurablePairDynamicsRevision? CurrentRevision { get; set; }
    public List<Guid> EvidenceItemIds { get; set; } = [];
}
