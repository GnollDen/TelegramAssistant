namespace TgAssistant.Core.Models;

public static class ModelNormalizationSchema
{
    public const int CurrentVersion = 1;
}

public static class ModelNormalizationTruthLayers
{
    public const string CanonicalTruth = "canonical_truth";
    public const string DerivedButDurable = "derived_but_durable";
    public const string ProposalLayer = "proposal_layer";
    public const string OperatorSuppliedButUnreconciled = "operator_supplied_but_unreconciled";
    public const string ConflictedOrObsolete = "conflicted_or_obsolete";

    public static IReadOnlyList<string> All { get; } =
    [
        CanonicalTruth,
        DerivedButDurable,
        ProposalLayer,
        OperatorSuppliedButUnreconciled,
        ConflictedOrObsolete
    ];
}

public class ModelNormalizationRequest
{
    public ModelPassEnvelope Envelope { get; set; } = new();
    public string RawModelOutput { get; set; } = string.Empty;
}

public class ModelNormalizationResult
{
    public Guid ModelPassRunId { get; set; }
    public int SchemaVersion { get; set; } = ModelNormalizationSchema.CurrentVersion;
    public string ScopeKey { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public string TruthLayer { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public Guid? SourceObjectId { get; set; }
    public Guid? EvidenceItemId { get; set; }
    public string Status { get; set; } = ModelPassResultStatuses.BlockedInvalidInput;
    public string? BlockedReason { get; set; }
    public ModelNormalizationCandidateCounts CandidateCounts { get; set; } = new();
    public ModelNormalizationPayload NormalizedPayload { get; set; } = new();
    public List<ModelNormalizationIssue> Issues { get; set; } = [];
}

public class ModelNormalizationCandidateCounts
{
    public int Facts { get; set; }
    public int Inferences { get; set; }
    public int Hypotheses { get; set; }
    public int Conflicts { get; set; }
}

public class ModelNormalizationPayload
{
    public List<NormalizedFactCandidate> Facts { get; set; } = [];
    public List<NormalizedInferenceCandidate> Inferences { get; set; } = [];
    public List<NormalizedHypothesisCandidate> Hypotheses { get; set; } = [];
    public List<NormalizedConflictCandidate> Conflicts { get; set; } = [];
}

public class ModelNormalizationIssue
{
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Path { get; set; }
}

public class NormalizedFactCandidate
{
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string TruthLayer { get; set; } = ModelNormalizationTruthLayers.CanonicalTruth;
    public float Confidence { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NormalizedInferenceCandidate
{
    public string InferenceType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectRef { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string TruthLayer { get; set; } = ModelNormalizationTruthLayers.DerivedButDurable;
    public float Confidence { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NormalizedHypothesisCandidate
{
    public string HypothesisType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectRef { get; set; } = string.Empty;
    public string Statement { get; set; } = string.Empty;
    public string TruthLayer { get; set; } = ModelNormalizationTruthLayers.ProposalLayer;
    public float Confidence { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NormalizedConflictCandidate
{
    public string ConflictType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string TruthLayer { get; set; } = ModelNormalizationTruthLayers.ConflictedOrObsolete;
    public string? RelatedObjectRef { get; set; }
    public float Confidence { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}
