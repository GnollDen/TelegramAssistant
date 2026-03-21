namespace TgAssistant.Core.Models;

public class CompetingContextImportEnvelope
{
    public string BatchId { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public List<CompetingContextImportRecord> Records { get; set; } = [];
}

public class CompetingContextImportRecord
{
    public string RecordId { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; }
    public string SubjectActorKey { get; set; } = string.Empty;
    public string CompetingActorKey { get; set; } = string.Empty;
    public string SignalType { get; set; } = string.Empty;
    public string SignalSubtype { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CompetingContextInterpretationRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public DateTime AsOfUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "system";
    public IReadOnlyList<CompetingContextImportRecord> Records { get; set; } = [];
}

public class CompetingContextRuntimeRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public DateTime AsOfUtc { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "stage6_runtime";
    public string SourceId { get; set; } = "competing_context";
}

public class CompetingContextRuntimeResult
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsAuthoritative { get; set; }
    public bool RequiresExplicitReview { get; set; } = true;
    public bool HasAnyEffect { get; set; }
    public IReadOnlyList<string> SourceRecordIds { get; set; } = [];
    public CompetingContextInterpretationResult Interpretation { get; set; } = new();
}

public class CompetingContextInterpretationResult
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsAuthoritative { get; set; }
    public bool RequiresExplicitReview { get; set; } = true;
    public List<CompetingGraphHint> GraphHints { get; set; } = [];
    public List<CompetingTimelineHint> TimelineHints { get; set; } = [];
    public CompetingStateModifiers StateModifiers { get; set; } = new();
    public List<CompetingStrategyConstraint> StrategyConstraints { get; set; } = [];
    public List<CompetingBlockedOverride> BlockedOverrideAttempts { get; set; } = [];
    public List<CompetingReviewAlert> ReviewAlerts { get; set; } = [];
}

public class CompetingContextImportValidationResult
{
    public List<CompetingContextImportRecord> Accepted { get; set; } = [];
    public List<CompetingImportRejection> Rejected { get; set; } = [];
}

public class CompetingImportRejection
{
    public CompetingContextImportRecord Record { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public class CompetingGraphHint
{
    public string HintId { get; set; } = string.Empty;
    public string FromActorKey { get; set; } = string.Empty;
    public string ToActorKey { get; set; } = string.Empty;
    public string InfluenceType { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool IsHypothesis { get; set; } = true;
    public string ApplyMode { get; set; } = "additive_hint";
    public List<string> EvidenceRefs { get; set; } = [];
}

public class CompetingTimelineHint
{
    public string HintId { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public DateTime? EffectiveAtUtc { get; set; }
    public string AnnotationType { get; set; } = "competing_context";
    public string Summary { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool RequiresReview { get; set; } = true;
    public bool AllowsBoundaryRewrite { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class CompetingStateModifiers
{
    public float ExternalPressureDelta { get; set; }
    public float AmbiguityDelta { get; set; }
    public float ConfidenceCap { get; set; } = 1f;
    public bool IsAdditiveOnly { get; set; } = true;
    public List<string> RationaleRefs { get; set; } = [];
}

public class CompetingStrategyConstraint
{
    public string ConstraintId { get; set; } = string.Empty;
    public string ConstraintType { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Summary { get; set; } = string.Empty;
    public bool RequiresReview { get; set; } = true;
    public List<string> EvidenceRefs { get; set; } = [];
}

public class CompetingBlockedOverride
{
    public string RecordId { get; set; } = string.Empty;
    public string AttemptedOperation { get; set; } = string.Empty;
    public string ReasonBlocked { get; set; } = string.Empty;
    public string RequiredReviewPath { get; set; } = "manual_review";
}

public class CompetingReviewAlert
{
    public string AlertId { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Message { get; set; } = string.Empty;
    public List<string> RelatedRecordIds { get; set; } = [];
}
