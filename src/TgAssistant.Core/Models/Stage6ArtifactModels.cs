namespace TgAssistant.Core.Legacy.Models;

public static class Stage6ArtifactTypes
{
    public const string Dossier = "dossier";
    public const string CurrentState = "current_state";
    public const string Strategy = "strategy";
    public const string Draft = "draft";
    public const string Review = "review";
    public const string ClarificationState = "clarification_state";

    public static string ChatScope(long chatId) => $"chat:{chatId}";
}

public class Stage6ArtifactRecord
{
    public Guid Id { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string? PayloadObjectType { get; set; }
    public string? PayloadObjectId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string FreshnessBasisHash { get; set; } = string.Empty;
    public string FreshnessBasisJson { get; set; } = "{}";
    public DateTime GeneratedAt { get; set; }
    public DateTime? RefreshedAt { get; set; }
    public DateTime? StaleAt { get; set; }
    public bool IsStale { get; set; }
    public string? StaleReason { get; set; }
    public int ReuseCount { get; set; }
    public bool IsCurrent { get; set; } = true;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class Stage6ArtifactFreshness
{
    public static (bool IsStale, string? Reason) Evaluate(
        Stage6ArtifactRecord artifact,
        DateTime nowUtc,
        DateTime? latestEvidenceAtUtc)
    {
        if (artifact.IsStale)
        {
            return (true, string.IsNullOrWhiteSpace(artifact.StaleReason) ? "marked_stale" : artifact.StaleReason);
        }

        if (latestEvidenceAtUtc.HasValue && latestEvidenceAtUtc.Value > artifact.GeneratedAt)
        {
            return (true, "evidence_newer_than_artifact");
        }

        if (artifact.StaleAt.HasValue && artifact.StaleAt.Value <= nowUtc)
        {
            return (true, "stale_ttl_exceeded");
        }

        return (false, null);
    }
}

public class Stage6ArtifactEvidenceStamp
{
    public DateTime? LatestEvidenceAtUtc { get; set; }
    public string BasisJson { get; set; } = "{}";
    public string BasisHash { get; set; } = string.Empty;
}
