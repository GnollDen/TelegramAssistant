using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class ConditionalKnowledgeOperatorValues
{
    public const string Eq = "eq";
    public const string Neq = "neq";
    public const string Contains = "contains";
    public const string In = "in";
    public const string Gte = "gte";
    public const string Lte = "lte";

    public static readonly string[] Ordered =
    [
        Eq,
        Neq,
        Contains,
        In,
        Gte,
        Lte
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? value)
        => !string.IsNullOrWhiteSpace(value) && Supported.Contains(value);
}

public sealed class ConditionClause
{
    [JsonPropertyName("condition_clause_id")]
    public Guid ConditionClauseId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("attribute")]
    public string Attribute { get; set; } = string.Empty;

    [JsonPropertyName("operator")]
    public string Operator { get; set; } = ConditionalKnowledgeOperatorValues.Eq;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("linked_temporal_state_ids")]
    public List<Guid> LinkedTemporalStateIds { get; set; } = [];
}

public sealed class ConditionalBaselineRule
{
    [JsonPropertyName("rule_id")]
    public Guid RuleId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("fact_family")]
    public string FactFamily { get; set; } = string.Empty;

    [JsonPropertyName("baseline_value")]
    public string BaselineValue { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("linked_temporal_state_ids")]
    public List<Guid> LinkedTemporalStateIds { get; set; } = [];
}

public sealed class ConditionalExceptionRule
{
    [JsonPropertyName("exception_id")]
    public Guid ExceptionId { get; set; }

    [JsonPropertyName("rule_id")]
    public Guid RuleId { get; set; }

    [JsonPropertyName("condition_clause_ids")]
    public List<Guid> ConditionClauseIds { get; set; } = [];

    [JsonPropertyName("exception_value")]
    public string ExceptionValue { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("linked_temporal_state_ids")]
    public List<Guid> LinkedTemporalStateIds { get; set; } = [];
}

public sealed class ConditionalStyleDriftMarker
{
    [JsonPropertyName("style_drift_id")]
    public Guid StyleDriftId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("style_label")]
    public string StyleLabel { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("linked_temporal_state_ids")]
    public List<Guid> LinkedTemporalStateIds { get; set; } = [];
}

public sealed class ConditionalPhaseMarker
{
    [JsonPropertyName("phase_marker_id")]
    public Guid PhaseMarkerId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("phase_label")]
    public string PhaseLabel { get; set; } = string.Empty;

    [JsonPropertyName("phase_reason")]
    public string PhaseReason { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("linked_temporal_state_ids")]
    public List<Guid> LinkedTemporalStateIds { get; set; } = [];
}
