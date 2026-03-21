namespace TgAssistant.Core.Models;

public class StateSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public DateTime AsOf { get; set; }
    public string DynamicLabel { get; set; } = string.Empty;
    public string RelationshipStatus { get; set; } = string.Empty;
    public string? AlternativeStatus { get; set; }
    public float InitiativeScore { get; set; }
    public float ResponsivenessScore { get; set; }
    public float OpennessScore { get; set; }
    public float WarmthScore { get; set; }
    public float ReciprocityScore { get; set; }
    public float AmbiguityScore { get; set; }
    public float AvoidanceRiskScore { get; set; }
    public float EscalationReadinessScore { get; set; }
    public float ExternalPressureScore { get; set; }
    public float Confidence { get; set; }
    public Guid? PeriodId { get; set; }
    public string KeySignalRefsJson { get; set; } = "[]";
    public string RiskRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
}

public class ProfileSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? PeriodId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
}

public class ProfileTrait
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileSnapshotId { get; set; }
    public string TraitKey { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public bool IsSensitive { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
}
