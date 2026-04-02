namespace TgAssistant.Core.Legacy.Models;

public class ClarificationQuestionDraft
{
    public long? ChatId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = string.Empty;
    public string? Priority { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? RelatedHypothesisId { get; set; }
    public string AffectedOutputsJson { get; set; } = "[]";
    public string WhyItMatters { get; set; } = string.Empty;
    public float ExpectedGain { get; set; }
    public string AnswerOptionsJson { get; set; } = "[]";
    public string SourceType { get; set; } = "system";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
}

public class ClarificationQueueItem
{
    public ClarificationQuestion Question { get; set; } = new();
    public int QueueScore { get; set; }
    public bool IsBlockedByDependency { get; set; }
    public bool TimelineImpact { get; set; }
    public bool StateImpact { get; set; }
    public bool StrategyImpact { get; set; }
}

public class ClarificationApplyRequest
{
    public Guid QuestionId { get; set; }
    public string AnswerType { get; set; } = "text";
    public string AnswerValue { get; set; } = string.Empty;
    public float AnswerConfidence { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string AffectedObjectsJson { get; set; } = "[]";
    public string SourceType { get; set; } = "user";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public bool MarkResolved { get; set; } = true;
    public string Actor { get; set; } = "system";
    public string? Reason { get; set; }
}

public class ClarificationDependencyUpdate
{
    public Guid QuestionId { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public string OldPriority { get; set; } = string.Empty;
    public string NewPriority { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
}

public class RecomputeTarget
{
    public string Layer { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class RecomputeTargetPlan
{
    public Guid QuestionId { get; set; }
    public Guid AnswerId { get; set; }
    public List<RecomputeTarget> Targets { get; set; } = new();
}

public class ClarificationApplyResult
{
    public ClarificationQuestion Question { get; set; } = new();
    public ClarificationAnswer Answer { get; set; } = new();
    public List<ClarificationDependencyUpdate> DependencyUpdates { get; set; } = new();
    public List<ConflictRecord> Conflicts { get; set; } = new();
    public RecomputeTargetPlan RecomputePlan { get; set; } = new();
}
