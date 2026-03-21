namespace TgAssistant.Core.Models;

public class ClarificationQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = string.Empty;
    public string Priority { get; set; } = "important";
    public string Status { get; set; } = "open";
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
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ClarificationAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public string AnswerType { get; set; } = string.Empty;
    public string AnswerValue { get; set; } = string.Empty;
    public float AnswerConfidence { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string AffectedObjectsJson { get; set; } = "[]";
    public string SourceType { get; set; } = "user";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
