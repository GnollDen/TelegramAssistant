namespace TgAssistant.Core.Models;

public static class RuntimeControlStates
{
    public const string Normal = "normal";
    public const string Degraded = "degraded";
    public const string ReviewOnly = "review_only";
    public const string BudgetProtected = "budget_protected";
    public const string PromotionBlocked = "promotion_blocked";
    public const string SafeMode = "safe_mode";
}

public class RuntimeControlStateRecord
{
    public long Id { get; set; }
    public string State { get; set; } = RuntimeControlStates.Normal;
    public string Reason { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime? DeactivatedAtUtc { get; set; }
}

public class RuntimeControlEnforcementDecision
{
    public string State { get; set; } = RuntimeControlStates.Normal;
    public string Reason { get; set; } = string.Empty;
    public bool PauseAllExecution { get; set; }
    public bool RestrictToBootstrapOnly { get; set; }
    public bool ForcePromotionBlocked { get; set; }
    public bool DeferTimelineTargets { get; set; }
}
