namespace TgAssistant.Core.Models;

public class Period
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? CustomLabel { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsOpen { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string KeySignalsJson { get; set; } = "[]";
    public string WhatHelped { get; set; } = string.Empty;
    public string WhatHurt { get; set; } = string.Empty;
    public int OpenQuestionsCount { get; set; }
    public float BoundaryConfidence { get; set; }
    public float InterpretationConfidence { get; set; }
    public short ReviewPriority { get; set; }
    public bool IsSensitive { get; set; }
    public string StatusSnapshot { get; set; } = string.Empty;
    public string DynamicSnapshot { get; set; } = string.Empty;
    public string? Lessons { get; set; }
    public string? StrategicPatterns { get; set; }
    public string? ManualNotes { get; set; }
    public string? UserOverrideSummary { get; set; }
    public string SourceType { get; set; } = "system";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class PeriodTransition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromPeriodId { get; set; }
    public Guid ToPeriodId { get; set; }
    public string TransitionType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public float Confidence { get; set; }
    public Guid? GapId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string SourceType { get; set; } = "system";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Hypothesis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string HypothesisType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? PeriodId { get; set; }
    public string Statement { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string Status { get; set; } = "open";
    public string SourceType { get; set; } = "model";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string ConflictRefsJson { get; set; } = "[]";
    public string ValidationTargetsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
