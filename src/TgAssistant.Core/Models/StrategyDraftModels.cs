namespace TgAssistant.Core.Models;

public class StrategyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? StateSnapshotId { get; set; }
    public float StrategyConfidence { get; set; }
    public string RecommendedGoal { get; set; } = string.Empty;
    public string WhyNotOthers { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
}

public class StrategyOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StrategyRecordId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string WhenToUse { get; set; } = string.Empty;
    public string SuccessSigns { get; set; } = string.Empty;
    public string FailureSigns { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public class DraftRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StrategyRecordId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string MainDraft { get; set; } = string.Empty;
    public string? AltDraft1 { get; set; }
    public string? AltDraft2 { get; set; }
    public string? StyleNotes { get; set; }
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? SourceMessageId { get; set; }
}

public class DraftOutcome
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DraftId { get; set; }
    public long? ActualMessageId { get; set; }
    public float? MatchScore { get; set; }
    public string OutcomeLabel { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
}
