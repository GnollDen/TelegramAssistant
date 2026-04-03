namespace TgAssistant.Core.Models;

public static class RuntimeDefectClasses
{
    public const string Ingestion = "ingestion";
    public const string Data = "data";
    public const string Identity = "identity";
    public const string Model = "model";
    public const string Normalization = "normalization";
    public const string ControlPlane = "control_plane";
    public const string Cost = "cost";
    public const string SemanticDrift = "semantic_drift";
}

public static class RuntimeDefectSeverities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

public static class RuntimeDefectStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public static class RuntimeDefectEscalationActions
{
    public const string Observe = "observe";
    public const string Ticket = "ticket";
    public const string ReviewOnly = "switch_review_only";
    public const string BudgetProtected = "switch_budget_protected";
    public const string SafeMode = "switch_safe_mode";
}

public class RuntimeDefectRecord
{
    public Guid Id { get; set; }
    public string DefectClass { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = RuntimeDefectStatuses.Open;
    public string ScopeKey { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public Guid? RunId { get; set; }
    public string? ObjectType { get; set; }
    public string? ObjectRef { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public int OccurrenceCount { get; set; }
    public string EscalationAction { get; set; } = RuntimeDefectEscalationActions.Observe;
    public string EscalationReason { get; set; } = string.Empty;
    public DateTime FirstSeenAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public class RuntimeDefectUpsertRequest
{
    public string DefectClass { get; set; } = RuntimeDefectClasses.ControlPlane;
    public string Severity { get; set; } = RuntimeDefectSeverities.Medium;
    public string ScopeKey { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public Guid? RunId { get; set; }
    public string? ObjectType { get; set; }
    public string? ObjectRef { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
}

public static class RuntimeDefectEscalationPolicy
{
    public static (string EscalationAction, string EscalationReason) Resolve(string defectClass, string severity, int occurrenceCount)
    {
        if (string.Equals(defectClass, RuntimeDefectClasses.Cost, StringComparison.Ordinal))
        {
            return (RuntimeDefectEscalationActions.BudgetProtected, "Cost anomaly requires budget-protected mode.");
        }

        if (string.Equals(defectClass, RuntimeDefectClasses.ControlPlane, StringComparison.Ordinal)
            && string.Equals(severity, RuntimeDefectSeverities.Critical, StringComparison.Ordinal))
        {
            return (RuntimeDefectEscalationActions.SafeMode, "Critical control-plane failure requires safe mode.");
        }

        if (string.Equals(defectClass, RuntimeDefectClasses.Normalization, StringComparison.Ordinal)
            || string.Equals(defectClass, RuntimeDefectClasses.Model, StringComparison.Ordinal))
        {
            if (string.Equals(severity, RuntimeDefectSeverities.High, StringComparison.Ordinal)
                || string.Equals(severity, RuntimeDefectSeverities.Critical, StringComparison.Ordinal)
                || occurrenceCount >= 3)
            {
                return (RuntimeDefectEscalationActions.ReviewOnly, "Repeated or high-risk model/normalization defects require review-only mode.");
            }
        }

        if ((string.Equals(defectClass, RuntimeDefectClasses.Identity, StringComparison.Ordinal)
                || string.Equals(defectClass, RuntimeDefectClasses.Data, StringComparison.Ordinal)
                || string.Equals(defectClass, RuntimeDefectClasses.SemanticDrift, StringComparison.Ordinal))
            && (string.Equals(severity, RuntimeDefectSeverities.High, StringComparison.Ordinal)
                || string.Equals(severity, RuntimeDefectSeverities.Critical, StringComparison.Ordinal)))
        {
            return (RuntimeDefectEscalationActions.Ticket, "High-risk data/identity/semantic defect requires manual review.");
        }

        return string.Equals(severity, RuntimeDefectSeverities.Low, StringComparison.Ordinal)
            ? (RuntimeDefectEscalationActions.Observe, "Low-severity defect remains under observation.")
            : (RuntimeDefectEscalationActions.Ticket, "Defect requires operator-visible review.");
    }
}
