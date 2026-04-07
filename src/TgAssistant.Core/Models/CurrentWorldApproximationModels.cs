using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class CurrentWorldApproximationPublicationStates
{
    public const string Publishable = "publishable";
    public const string Unresolved = "unresolved";
    public const string InsufficientEvidence = "insufficient_evidence";

    public static readonly string[] Ordered =
    [
        Publishable,
        Unresolved,
        InsufficientEvidence
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? value)
        => !string.IsNullOrWhiteSpace(value) && Supported.Contains(value);
}

public static class CurrentWorldApproximationReadStates
{
    public const string RecomputedOnRead = "recomputed_on_read";
    public const string NoPublishableContent = "no_publishable_content";
}

public sealed class CurrentWorldApproximationReadRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public DateTime AsOfUtc { get; set; }
}

public sealed class CurrentWorldApproximationSnapshot
{
    [JsonPropertyName("snapshot_id")]
    public Guid SnapshotId { get; set; }

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("as_of_utc")]
    public DateTime AsOfUtc { get; set; }

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = CurrentWorldApproximationPublicationStates.InsufficientEvidence;

    [JsonPropertyName("read_state")]
    public string ReadState { get; set; } = CurrentWorldApproximationReadStates.RecomputedOnRead;

    [JsonPropertyName("uncertainty_refs")]
    public List<string> UncertaintyRefs { get; set; } = [];

    [JsonPropertyName("active_person_rows")]
    public List<ActivePersonRow> ActivePersonRows { get; set; } = [];

    [JsonPropertyName("inactive_person_rows")]
    public List<InactivePersonRow> InactivePersonRows { get; set; } = [];

    [JsonPropertyName("relationship_state_rows")]
    public List<RelationshipStateRow> RelationshipStateRows { get; set; } = [];

    [JsonPropertyName("active_condition_rows")]
    public List<ActiveConditionRow> ActiveConditionRows { get; set; } = [];

    [JsonPropertyName("recent_change_rows")]
    public List<RecentChangeRow> RecentChangeRows { get; set; } = [];
}

public sealed class ActivePersonRow
{
    [JsonPropertyName("person_row_id")]
    public Guid PersonRowId { get; set; }

    [JsonPropertyName("snapshot_id")]
    public Guid SnapshotId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("state_label")]
    public string StateLabel { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = CurrentWorldApproximationPublicationStates.InsufficientEvidence;
}

public sealed class InactivePersonRow
{
    [JsonPropertyName("inactive_person_row_id")]
    public Guid InactivePersonRowId { get; set; }

    [JsonPropertyName("snapshot_id")]
    public Guid SnapshotId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("inactive_reason")]
    public string InactiveReason { get; set; } = string.Empty;

    [JsonPropertyName("dropped_out_flag")]
    public bool DroppedOutFlag { get; set; }

    [JsonPropertyName("last_seen_utc")]
    public DateTime? LastSeenUtc { get; set; }

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = CurrentWorldApproximationPublicationStates.InsufficientEvidence;
}

public sealed class RelationshipStateRow
{
    [JsonPropertyName("relationship_row_id")]
    public Guid RelationshipRowId { get; set; }

    [JsonPropertyName("snapshot_id")]
    public Guid SnapshotId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("related_subject_ref")]
    public string RelatedSubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("relationship_state")]
    public string RelationshipState { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = CurrentWorldApproximationPublicationStates.InsufficientEvidence;
}

public sealed class ActiveConditionRow
{
    [JsonPropertyName("condition_row_id")]
    public Guid ConditionRowId { get; set; }

    [JsonPropertyName("snapshot_id")]
    public Guid SnapshotId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("condition_type")]
    public string ConditionType { get; set; } = string.Empty;

    [JsonPropertyName("condition_value")]
    public string ConditionValue { get; set; } = string.Empty;

    [JsonPropertyName("valid_from_utc")]
    public DateTime ValidFromUtc { get; set; }

    [JsonPropertyName("valid_to_utc")]
    public DateTime? ValidToUtc { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = CurrentWorldApproximationPublicationStates.InsufficientEvidence;
}

public sealed class RecentChangeRow
{
    [JsonPropertyName("change_row_id")]
    public Guid ChangeRowId { get; set; }

    [JsonPropertyName("snapshot_id")]
    public Guid SnapshotId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("change_type")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("change_summary")]
    public string ChangeSummary { get; set; } = string.Empty;

    [JsonPropertyName("changed_at_utc")]
    public DateTime ChangedAtUtc { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];

    [JsonPropertyName("source_ref_ids")]
    public List<string> SourceRefIds { get; set; } = [];

    [JsonPropertyName("publication_state")]
    public string PublicationState { get; set; } = CurrentWorldApproximationPublicationStates.InsufficientEvidence;
}

public sealed class CurrentWorldPairDynamicsReadSurface
{
    public Guid DurablePairDynamicsId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public Guid? RelatedPersonId { get; set; }
    public string? RelationshipStateHint { get; set; }
    public bool HasContradictionMarkers { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
    public List<string> SourceRefIds { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CurrentWorldTimelineReadSurface
{
    public Guid DurableTimelineEpisodeId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public Guid? RelatedPersonId { get; set; }
    public string ClosureState { get; set; } = string.Empty;
    public string? TimelinePrimaryActivityHint { get; set; }
    public bool HasContradictionMarkers { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
    public List<string> SourceRefIds { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; }
}
