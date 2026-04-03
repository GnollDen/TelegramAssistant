namespace TgAssistant.Core.Models;

public static class Stage7EventTypes
{
    public const string BootstrapAnchorEvent = "bootstrap_anchor_event";
}

public static class Stage7TimelineEpisodeTypes
{
    public const string BootstrapEpisode = "bootstrap_episode";
}

public static class Stage7StoryArcTypes
{
    public const string OperatorTrackedArc = "operator_tracked_arc";
}

public static class Stage7ClosureStates
{
    public const string Open = "open";
    public const string SemiClosed = "semi_closed";
    public const string Closed = "closed";
}

public class Stage7TimelineFormationRequest
{
    public Stage6BootstrapGraphResult BootstrapResult { get; set; } = new();
    public string RunKind { get; set; } = "manual";
    public string RequestedModel { get; set; } = "stage7-timeline-deterministic";
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
}

public class Stage7DurableEvent
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid PersonId { get; set; }
    public Guid? RelatedPersonId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string EventType { get; set; } = Stage7EventTypes.BootstrapAnchorEvent;
    public string Status { get; set; } = string.Empty;
    public float BoundaryConfidence { get; set; }
    public float EventConfidence { get; set; }
    public string ClosureState { get; set; } = Stage7ClosureStates.Open;
    public DateTime? OccurredFromUtc { get; set; }
    public DateTime? OccurredToUtc { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7DurableTimelineEpisode
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid PersonId { get; set; }
    public Guid? RelatedPersonId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string EpisodeType { get; set; } = Stage7TimelineEpisodeTypes.BootstrapEpisode;
    public string Status { get; set; } = string.Empty;
    public float BoundaryConfidence { get; set; }
    public string ClosureState { get; set; } = Stage7ClosureStates.Open;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7DurableStoryArc
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid PersonId { get; set; }
    public Guid? RelatedPersonId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string ArcType { get; set; } = Stage7StoryArcTypes.OperatorTrackedArc;
    public string Status { get; set; } = string.Empty;
    public float BoundaryConfidence { get; set; }
    public string ClosureState { get; set; } = Stage7ClosureStates.Open;
    public DateTime? OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7TimelineFormationResult
{
    public ModelPassAuditRecord AuditRecord { get; set; } = new();
    public bool Formed { get; set; }
    public Stage6BootstrapPersonRef? TrackedPerson { get; set; }
    public Stage6BootstrapPersonRef? OperatorPerson { get; set; }
    public Stage7DurableEvent? Event { get; set; }
    public Stage7DurableTimelineEpisode? TimelineEpisode { get; set; }
    public Stage7DurableStoryArc? StoryArc { get; set; }
    public List<Guid> EvidenceItemIds { get; set; } = [];
}
