namespace TgAssistant.Core.Models;

public static class ResolutionQueueSortFields
{
    public const string Priority = "priority";
    public const string UpdatedAt = "updated_at";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Priority,
        UpdatedAt
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? Priority
            : sortBy.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? sortBy)
        => Supported.Contains(Normalize(sortBy));
}

public static class ResolutionEvidenceSortFields
{
    public const string ObservedAt = "observed_at";
    public const string TrustFactor = "trust_factor";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        ObservedAt,
        TrustFactor
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? ObservedAt
            : sortBy.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? sortBy)
        => Supported.Contains(Normalize(sortBy));
}

public static class ResolutionSortDirections
{
    public const string Asc = "asc";
    public const string Desc = "desc";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Asc,
        Desc
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? direction)
    {
        return string.IsNullOrWhiteSpace(direction)
            ? Desc
            : direction.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? direction)
        => Supported.Contains(Normalize(direction));
}

public class ResolutionFacetCount
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public abstract class OperatorContractRequestBase
{
    public OperatorIdentityContext OperatorIdentity { get; set; } = new();
    public OperatorSessionContext Session { get; set; } = new();
}

public class OperatorTrackedPersonQueryRequest : OperatorContractRequestBase
{
    public Guid? PreferredTrackedPersonId { get; set; }
    public int Limit { get; set; } = 25;
}

public class OperatorTrackedPersonSelectionRequest : OperatorContractRequestBase
{
    public Guid TrackedPersonId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OperatorTrackedPersonScopeSummary
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public int UnresolvedCount { get; set; }
    public bool HasUnresolved { get; set; }
    public DateTime RecentUpdateAtUtc { get; set; }
    public DateTime? LastUnresolvedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class OperatorTrackedPersonQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public bool AutoSelected { get; set; }
    public string SelectionSource { get; set; } = "none";
    public Guid? ActiveTrackedPersonId { get; set; }
    public OperatorTrackedPersonScopeSummary? ActiveTrackedPerson { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public List<OperatorTrackedPersonScopeSummary> TrackedPersons { get; set; } = [];
}

public class OperatorTrackedPersonSelectionResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public bool ScopeChanged { get; set; }
    public Guid? AuditEventId { get; set; }
    public OperatorTrackedPersonScopeSummary? ActiveTrackedPerson { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
}

public class OperatorPersonWorkspaceListQueryRequest : OperatorContractRequestBase
{
    public string? Search { get; set; }
    public int Limit { get; set; } = 50;
}

public class OperatorPersonWorkspaceListQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public Guid? ActiveTrackedPersonId { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public List<OperatorTrackedPersonScopeSummary> Persons { get; set; } = [];
}

public class OperatorResolutionQueueQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public List<string> ItemTypes { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<string> Priorities { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
    public string SortBy { get; set; } = ResolutionQueueSortFields.Priority;
    public string SortDirection { get; set; } = ResolutionSortDirections.Desc;
    public int Limit { get; set; } = 50;
}

public class OperatorResolutionQueueQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public ResolutionQueueResult Queue { get; set; } = new();
}

public class OperatorResolutionDetailQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public int EvidenceLimit { get; set; } = 5;
    public string EvidenceSortBy { get; set; } = ResolutionEvidenceSortFields.ObservedAt;
    public string EvidenceSortDirection { get; set; } = ResolutionSortDirections.Desc;
}

public class OperatorResolutionDetailQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public ResolutionDetailResult Detail { get; set; } = new();
}

public class OperatorResolutionActionResultEnvelope
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public ResolutionActionResult Action { get; set; } = new();
}

public class OperatorOfflineEventQueryApiRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public List<string> Statuses { get; set; } = [];
    public string SortBy { get; set; } = OperatorOfflineEventSortFields.UpdatedAt;
    public string SortDirection { get; set; } = ResolutionSortDirections.Desc;
    public int Limit { get; set; } = 50;
}

public class OperatorOfflineEventQueryApiResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventQueryResult OfflineEvents { get; set; } = new();
}

public class OperatorOfflineEventDetailQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public Guid OfflineEventId { get; set; }
}

public class OperatorOfflineEventDetailQueryResultEnvelope
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventDetailView OfflineEvent { get; set; } = new();
}

public class OperatorOfflineEventRefinementResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventDetailView OfflineEvent { get; set; } = new();
}

public class OperatorOfflineEventTimelineLinkageUpdateResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public Guid? AuditEventId { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventDetailView OfflineEvent { get; set; } = new();
}
