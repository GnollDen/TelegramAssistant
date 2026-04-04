namespace TgAssistant.Core.Models;

public static class OperatorOfflineEventStatuses
{
    public const string Draft = "draft";
    public const string Captured = "captured";
    public const string Saved = "saved";
    public const string Archived = "archived";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Draft,
        Captured,
        Saved,
        Archived
    };

    public static string Normalize(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? Draft
            : status.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? status)
        => Supported.Contains(Normalize(status));
}

public class OperatorOfflineEventCreateRequest : OperatorContractRequestBase
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RecordingReference { get; set; }
    public string Status { get; set; } = OperatorOfflineEventStatuses.Draft;
    public string CapturePayloadJson { get; set; } = "{}";
    public string ClarificationStateJson { get; set; } = "{}";
    public string TimelineLinkageJson { get; set; } = "{}";
    public float? Confidence { get; set; }
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SavedAtUtc { get; set; }
}

public class OperatorOfflineEventRecord
{
    public Guid OfflineEventId { get; set; }
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RecordingReference { get; set; }
    public string Status { get; set; } = OperatorOfflineEventStatuses.Draft;
    public string CapturePayloadJson { get; set; } = "{}";
    public string ClarificationStateJson { get; set; } = "{}";
    public string TimelineLinkageJson { get; set; } = "{}";
    public float? Confidence { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorDisplay { get; set; } = string.Empty;
    public string OperatorSessionId { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public string SurfaceSubject { get; set; } = string.Empty;
    public string AuthSource { get; set; } = string.Empty;
    public DateTime AuthTimeUtc { get; set; }
    public DateTime SessionAuthenticatedAtUtc { get; set; }
    public DateTime SessionLastSeenAtUtc { get; set; }
    public DateTime? SessionExpiresAtUtc { get; set; }
    public string ActiveMode { get; set; } = string.Empty;
    public string? UnfinishedStepKind { get; set; }
    public string? UnfinishedStepState { get; set; }
    public DateTime? UnfinishedStepStartedAtUtc { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime? SavedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public static class OperatorOfflineEventSortFields
{
    public const string UpdatedAt = "updated_at";
    public const string CapturedAt = "captured_at";
    public const string SavedAt = "saved_at";
    public const string CreatedAt = "created_at";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        UpdatedAt,
        CapturedAt,
        SavedAt,
        CreatedAt
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? UpdatedAt
            : sortBy.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? sortBy)
        => Supported.Contains(Normalize(sortBy));
}

public class OperatorOfflineEventQueryRequest
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public List<string> Statuses { get; set; } = [];
    public string SortBy { get; set; } = OperatorOfflineEventSortFields.UpdatedAt;
    public string SortDirection { get; set; } = ResolutionSortDirections.Desc;
    public int Limit { get; set; } = 50;
}

public class OperatorOfflineEventTimelineLinkageMetadata
{
    public bool HasLinkage { get; set; }
    public string LinkageStatus { get; set; } = "unlinked";
    public string? TargetFamily { get; set; }
    public string? TargetRef { get; set; }
    public DateTime? LinkedAtUtc { get; set; }
    public string RawJson { get; set; } = "{}";
}

public class OperatorOfflineEventReadSummary
{
    public Guid OfflineEventId { get; set; }
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RecordingReference { get; set; }
    public string Status { get; set; } = OperatorOfflineEventStatuses.Draft;
    public float? Confidence { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime? SavedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public OperatorOfflineEventTimelineLinkageMetadata TimelineLinkage { get; set; } = new();
}

public class OperatorOfflineEventQueryResult
{
    public bool ScopeBound { get; set; }
    public string? ScopeFailureReason { get; set; }
    public Guid? TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public List<OperatorOfflineEventReadSummary> Items { get; set; } = [];
}

public class OperatorOfflineEventClarificationQuestionSummary
{
    public string Key { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float ExpectedInformationGain { get; set; }
    public int PriorityRank { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class OperatorOfflineEventClarificationHistoryEntry
{
    public string QuestionKey { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public bool UnknownPattern { get; set; }
    public bool RepetitionDetected { get; set; }
    public int NewTokenCount { get; set; }
    public float InformationGain { get; set; }
    public DateTime CapturedAtUtc { get; set; }
}

public class OperatorOfflineEventClarificationView
{
    public string LoopStatus { get; set; } = "unknown";
    public string StopReason { get; set; } = "none";
    public string? StopDetail { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public float? PartialConfidence { get; set; }
    public string? NextQuestionKey { get; set; }
    public int QuestionCount { get; set; }
    public int AnsweredCount { get; set; }
    public int HistoryCount { get; set; }
    public DateTime? LastAnsweredAtUtc { get; set; }
    public List<OperatorOfflineEventClarificationQuestionSummary> Questions { get; set; } = [];
    public List<OperatorOfflineEventClarificationHistoryEntry> History { get; set; } = [];
}

public class OperatorOfflineEventDetailView
{
    public bool ScopeBound { get; set; }
    public bool Found { get; set; }
    public string? ScopeFailureReason { get; set; }
    public Guid OfflineEventId { get; set; }
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RecordingReference { get; set; }
    public string Status { get; set; } = OperatorOfflineEventStatuses.Draft;
    public float? Confidence { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public DateTime? SavedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string ExtractedInterpretation { get; set; } = string.Empty;
    public string CapturePayloadJson { get; set; } = "{}";
    public OperatorOfflineEventTimelineLinkageMetadata TimelineLinkage { get; set; } = new();
    public OperatorOfflineEventClarificationView Clarification { get; set; } = new();
}

public class OperatorOfflineEventRefinementRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public Guid OfflineEventId { get; set; }
    public string? Summary { get; set; }
    public string? RecordingReference { get; set; }
    public bool ClearRecordingReference { get; set; }
    public string? RefinementNote { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class OperatorOfflineEventTimelineLinkageStatuses
{
    public const string Unlinked = "unlinked";
    public const string Linked = "linked";
    public const string ReviewNeeded = "review_needed";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Unlinked,
        Linked,
        ReviewNeeded
    };

    public static string Normalize(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            ? Unlinked
            : status.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? status)
        => Supported.Contains(Normalize(status));
}

public class OperatorOfflineEventTimelineLinkageUpdateRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public Guid OfflineEventId { get; set; }
    public string LinkageStatus { get; set; } = OperatorOfflineEventTimelineLinkageStatuses.Unlinked;
    public string? TargetFamily { get; set; }
    public string? TargetRef { get; set; }
    public string? LinkageNote { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OperatorOfflineEventTimelineLinkageUpdateRecord
{
    public Guid AuditEventId { get; set; }
    public OperatorOfflineEventRecord OfflineEvent { get; set; } = new();
}
