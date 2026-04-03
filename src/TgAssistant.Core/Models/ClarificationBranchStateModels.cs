namespace TgAssistant.Core.Models;

public static class ClarificationBranchStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public class ClarificationBranchStateRecord
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string BranchFamily { get; set; } = string.Empty;
    public string BranchKey { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string PassFamily { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string Status { get; set; } = ClarificationBranchStatuses.Open;
    public string BlockReason { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTime FirstBlockedAtUtc { get; set; }
    public DateTime LastBlockedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}
