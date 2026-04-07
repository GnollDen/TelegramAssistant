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

public static class ConditionalKnowledgeRuleKinds
{
    public const string BaselineRule = "baseline_rule";
    public const string ExceptionRule = "exception_rule";
    public const string StyleDrift = "style_drift";
    public const string PhaseMarker = "phase_marker";

    public static readonly string[] Ordered =
    [
        BaselineRule,
        ExceptionRule,
        StyleDrift,
        PhaseMarker
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? value)
        => !string.IsNullOrWhiteSpace(value) && Supported.Contains(value);
}

public static class ConditionalKnowledgeStateStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Superseded = "superseded";

    public static readonly string[] Ordered =
    [
        Open,
        Closed,
        Superseded
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? value)
        => !string.IsNullOrWhiteSpace(value) && Supported.Contains(value);
}

public static class ConditionalKnowledgeFailureReasons
{
    public const string EvidenceRefsRequired = "conditional_evidence_refs_required";
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

public sealed class ConditionalKnowledgeState
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("fact_family")]
    public string FactFamily { get; set; } = string.Empty;

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("rule_kind")]
    public string RuleKind { get; set; } = ConditionalKnowledgeRuleKinds.BaselineRule;

    [JsonPropertyName("rule_id")]
    public Guid RuleId { get; set; }

    [JsonPropertyName("parent_rule_id")]
    public Guid? ParentRuleId { get; set; }

    [JsonPropertyName("baseline_value")]
    public string? BaselineValue { get; set; }

    [JsonPropertyName("exception_value")]
    public string? ExceptionValue { get; set; }

    [JsonPropertyName("style_label")]
    public string? StyleLabel { get; set; }

    [JsonPropertyName("phase_label")]
    public string? PhaseLabel { get; set; }

    [JsonPropertyName("phase_reason")]
    public string? PhaseReason { get; set; }

    [JsonPropertyName("condition_clause_ids")]
    public List<Guid> ConditionClauseIds { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("linked_temporal_state_ids")]
    public List<Guid> LinkedTemporalStateIds { get; set; } = [];

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("confidence")]
    public float? Confidence { get; set; }

    [JsonPropertyName("state_status")]
    public string StateStatus { get; set; } = ConditionalKnowledgeStateStatuses.Open;

    [JsonPropertyName("supersedes_state_id")]
    public Guid? SupersedesStateId { get; set; }

    [JsonPropertyName("superseded_by_state_id")]
    public Guid? SupersededByStateId { get; set; }

    [JsonPropertyName("trigger_kind")]
    public string TriggerKind { get; set; } = "manual";

    [JsonPropertyName("trigger_ref")]
    public string? TriggerRef { get; set; }

    [JsonPropertyName("trigger_model_pass_run_id")]
    public Guid? TriggerModelPassRunId { get; set; }

    [JsonPropertyName("created_at_utc")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("updated_at_utc")]
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ConditionalKnowledgeWriteRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string FactFamily { get; set; } = string.Empty;
    public string SubjectRef { get; set; } = string.Empty;
    public string RuleKind { get; set; } = ConditionalKnowledgeRuleKinds.BaselineRule;
    public Guid RuleId { get; set; }
    public Guid? ParentRuleId { get; set; }
    public string? BaselineValue { get; set; }
    public string? ExceptionValue { get; set; }
    public string? StyleLabel { get; set; }
    public string? PhaseLabel { get; set; }
    public string? PhaseReason { get; set; }
    public IReadOnlyCollection<Guid>? ConditionClauseIds { get; set; }
    public IReadOnlyCollection<string>? SourceRefIds { get; set; }
    public IReadOnlyCollection<Guid>? LinkedTemporalStateIds { get; set; }
    public IReadOnlyCollection<string>? EvidenceRefs { get; set; }
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public float? Confidence { get; set; }
    public string StateStatus { get; set; } = ConditionalKnowledgeStateStatuses.Open;
    public Guid? SupersedesStateId { get; set; }
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
    public Guid? TriggerModelPassRunId { get; set; }
}

public sealed class ConditionalKnowledgeScopeQuery
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string? FactFamily { get; set; }
    public string? SubjectRef { get; set; }
    public string? RuleKind { get; set; }
    public int Limit { get; set; } = 200;
}

public sealed class ConditionalKnowledgeSupersessionUpdateRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public Guid PreviousStateId { get; set; }
    public Guid SupersededByStateId { get; set; }
    public DateTime SupersededAtUtc { get; set; }
    public string NextStatus { get; set; } = ConditionalKnowledgeStateStatuses.Superseded;
}
