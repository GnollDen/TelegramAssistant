using TgAssistant.Core.Models;

namespace TgAssistant.Host.OperatorApi;

public static class OperatorAlertsEscalationFilters
{
    public const string All = "all";
    public const string WebOnly = OperatorAlertEscalationBoundaries.WebOnly;
    public const string TelegramPushAcknowledge = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge;

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        All,
        WebOnly,
        TelegramPushAcknowledge
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return All;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return Supported.Contains(normalized) ? normalized : string.Empty;
    }

    public static bool IsSupported(string? value)
        => Supported.Contains(Normalize(value));
}

public sealed class OperatorAlertsQueryRequest
{
    public Guid? TrackedPersonId { get; set; }
    public string EscalationBoundary { get; set; } = OperatorAlertsEscalationFilters.All;
    public string? Search { get; set; }
    public int PersonLimit { get; set; } = 24;
    public int AlertsPerPersonLimit { get; set; } = 6;
}

public sealed class OperatorAlertsQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; }
    public OperatorAlertsSummaryView Summary { get; set; } = new();
    public List<OperatorAlertGroupView> Groups { get; set; } = [];
}

public sealed class OperatorAlertsSummaryView
{
    public int TrackedPersonCount { get; set; }
    public int GroupCount { get; set; }
    public int TotalAlerts { get; set; }
    public int TelegramPushCount { get; set; }
    public int WebOnlyCount { get; set; }
    public int RequiresAcknowledgementCount { get; set; }
    public int EnterResolutionCount { get; set; }
    public string EscalationBoundary { get; set; } = OperatorAlertsEscalationFilters.All;
    public List<OperatorAlertsFacetCountView> TopReasons { get; set; } = [];
    public List<OperatorAlertsFacetCountView> BoundaryBreakdown { get; set; } = [];
}

public sealed class OperatorAlertsFacetCountView
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public string AlertsUrl { get; set; } = string.Empty;
}

public sealed class OperatorAlertGroupView
{
    public OperatorTrackedPersonScopeSummary TrackedPerson { get; set; } = new();
    public string PersonWorkspaceUrl { get; set; } = string.Empty;
    public string ResolutionQueueUrl { get; set; } = string.Empty;
    public int AlertCount { get; set; }
    public int TelegramPushCount { get; set; }
    public int WebOnlyCount { get; set; }
    public List<OperatorAlertItemView> Alerts { get; set; } = [];
}

public sealed class OperatorAlertItemView
{
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string AffectedFamily { get; set; } = string.Empty;
    public string AffectedObjectRef { get; set; } = string.Empty;
    public float TrustFactor { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? RecommendedNextAction { get; set; }
    public string AlertRuleId { get; set; } = string.Empty;
    public string AlertReason { get; set; } = string.Empty;
    public string EscalationBoundary { get; set; } = string.Empty;
    public bool PushTelegram { get; set; }
    public bool RequiresAcknowledgement { get; set; }
    public bool EnterResolutionContext { get; set; }
    public string ResolutionUrl { get; set; } = string.Empty;
    public string PersonWorkspaceUrl { get; set; } = string.Empty;
}
