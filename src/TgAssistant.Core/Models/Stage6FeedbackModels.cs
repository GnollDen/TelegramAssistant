namespace TgAssistant.Core.Legacy.Models;

public static class Stage6FeedbackKinds
{
    public const string AcceptUseful = "accept_useful";
    public const string RejectNotUseful = "reject_not_useful";
    public const string CorrectionNote = "correction_note";
    public const string RefreshRequested = "refresh_requested";
}

public static class Stage6FeedbackDimensions
{
    public const string General = "general";
    public const string ClarificationUsefulness = "clarification_usefulness";
    public const string BehavioralUsefulness = "behavioral_usefulness";
}

public static class Stage6CaseOutcomeTypes
{
    public const string Resolved = "resolved";
    public const string Rejected = "rejected";
    public const string Stale = "stale";
    public const string Refreshed = "refreshed";
    public const string AnsweredByUser = "answered_by_user";
}

public class Stage6FeedbackEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ScopeCaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? Stage6CaseId { get; set; }
    public string? ArtifactType { get; set; }
    public string FeedbackKind { get; set; } = Stage6FeedbackKinds.CorrectionNote;
    public string FeedbackDimension { get; set; } = Stage6FeedbackDimensions.General;
    public bool? IsUseful { get; set; }
    public string? Note { get; set; }
    public string SourceChannel { get; set; } = "web";
    public string Actor { get; set; } = "operator";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Stage6CaseOutcomeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Stage6CaseId { get; set; }
    public long ScopeCaseId { get; set; }
    public long? ChatId { get; set; }
    public string OutcomeType { get; set; } = Stage6CaseOutcomeTypes.Resolved;
    public string CaseStatusAfter { get; set; } = Stage6CaseStatuses.Resolved;
    public bool UserContextMaterial { get; set; }
    public string? Note { get; set; }
    public string SourceChannel { get; set; } = "web";
    public string Actor { get; set; } = "operator";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
