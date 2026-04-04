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
