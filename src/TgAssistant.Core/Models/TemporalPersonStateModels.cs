using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class TemporalPersonStateFactCategories
{
    public const string Stable = "stable";
    public const string Temporal = "temporal";
    public const string EventConditioned = "event_conditioned";
    public const string Contested = "contested";

    public static readonly string[] Ordered =
    [
        Stable,
        Temporal,
        EventConditioned,
        Contested,
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? category)
    {
        return !string.IsNullOrWhiteSpace(category) && Supported.Contains(category);
    }
}

public static class TemporalPersonStateStatuses
{
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Superseded = "superseded";

    public static readonly string[] Ordered =
    [
        Open,
        Closed,
        Superseded,
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) && Supported.Contains(status);
    }
}

public static class TemporalSingleValuedFactFamilies
{
    public const string ProfileStatus = "profile_status";
    public const string ProfileLocation = "profile_location";
    public const string RelationshipState = "relationship_state";
    public const string TimelinePrimaryActivity = "timeline_primary_activity";

    public static readonly string[] Ordered =
    [
        ProfileStatus,
        ProfileLocation,
        RelationshipState,
        TimelinePrimaryActivity,
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool Contains(string? factType)
    {
        return !string.IsNullOrWhiteSpace(factType) && Supported.Contains(factType);
    }
}

public class TemporalPersonState
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("fact_type")]
    public string FactType { get; set; } = string.Empty;

    [JsonPropertyName("fact_category")]
    public string FactCategory { get; set; } = TemporalPersonStateFactCategories.Stable;

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

    [JsonPropertyName("state_status")]
    public string StateStatus { get; set; } = TemporalPersonStateStatuses.Open;

    [JsonPropertyName("supersedes_state_id")]
    public Guid? SupersedesStateId { get; set; }

    [JsonPropertyName("superseded_by_state_id")]
    public Guid? SupersededByStateId { get; set; }

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

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

public class TemporalPersonStateWriteRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string SubjectRef { get; set; } = string.Empty;
    public string FactType { get; set; } = string.Empty;
    public string FactCategory { get; set; } = TemporalPersonStateFactCategories.Stable;
    public string Value { get; set; } = string.Empty;
    public DateTime ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public float? Confidence { get; set; }
    public IReadOnlyCollection<string>? EvidenceRefs { get; set; }
    public string StateStatus { get; set; } = TemporalPersonStateStatuses.Open;
    public Guid? SupersedesStateId { get; set; }
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
    public Guid? TriggerModelPassRunId { get; set; }
}

public class TemporalPersonStateScopeQuery
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string? SubjectRef { get; set; }
    public string? FactType { get; set; }
    public int Limit { get; set; } = 200;
}

public class TemporalPersonStateSupersessionUpdateRequest
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public Guid PreviousStateId { get; set; }
    public Guid SupersededByStateId { get; set; }
    public DateTime SupersededAtUtc { get; set; }
    public string NextStatus { get; set; } = TemporalPersonStateStatuses.Superseded;
}

public static class TemporalPersonStateContract
{
    public static bool IsCurrentOpenState(TemporalPersonState state, DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!string.Equals(state.StateStatus, TemporalPersonStateStatuses.Open, StringComparison.Ordinal))
        {
            return false;
        }

        if (state.ValidFromUtc > asOfUtc)
        {
            return false;
        }

        return state.ValidToUtc is null || state.ValidToUtc > asOfUtc;
    }

    public static bool IsHistoricalClosedState(TemporalPersonState state, DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ValidToUtc is DateTime validToUtc && validToUtc <= asOfUtc)
        {
            return true;
        }

        return string.Equals(state.StateStatus, TemporalPersonStateStatuses.Closed, StringComparison.Ordinal)
            || string.Equals(state.StateStatus, TemporalPersonStateStatuses.Superseded, StringComparison.Ordinal);
    }
}
