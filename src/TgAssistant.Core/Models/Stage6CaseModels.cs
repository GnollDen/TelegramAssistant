namespace TgAssistant.Core.Models;

public static class Stage6CaseTypes
{
    public const string NeedsInput = "needs_input";
    public const string NeedsReview = "needs_review";
    public const string Risk = "risk";
    public const string StateRefreshNeeded = "state_refresh_needed";
    public const string DossierCandidate = "dossier_candidate";
    public const string DraftCandidate = "draft_candidate";
    public const string ClarificationMissingData = "clarification_missing_data";
    public const string ClarificationAmbiguity = "clarification_ambiguity";
    public const string ClarificationEvidenceInterpretationConflict = "clarification_evidence_interpretation_conflict";
    public const string ClarificationNextStepBlocked = "clarification_next_step_blocked";
    public const string UserContextCorrection = "user_context_correction";
    public const string UserContextConflictReview = "user_context_conflict_review";
}

public static class Stage6CaseStatuses
{
    public const string New = "new";
    public const string Ready = "ready";
    public const string NeedsUserInput = "needs_user_input";
    public const string Resolved = "resolved";
    public const string Rejected = "rejected";
    public const string Stale = "stale";
}

public static class Stage6CaseLinkRoles
{
    public const string Source = "source";
    public const string ArtifactTarget = "artifact_target";
    public const string Evidence = "evidence";
    public const string Subject = "subject";
}

public class Stage6CaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ScopeCaseId { get; set; }
    public long? ChatId { get; set; }
    public string ScopeType { get; set; } = "chat";
    public string CaseType { get; set; } = Stage6CaseTypes.NeedsReview;
    public string? CaseSubtype { get; set; }
    public string Status { get; set; } = Stage6CaseStatuses.New;
    public string Priority { get; set; } = "important";
    public float? Confidence { get; set; }
    public string ReasonSummary { get; set; } = string.Empty;
    public string? ClarificationKind { get; set; }
    public string? QuestionText { get; set; }
    public string? ResponseMode { get; set; }
    public string? ResponseChannelHint { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string SubjectRefsJson { get; set; } = "[]";
    public string TargetArtifactTypesJson { get; set; } = "[]";
    public string ReopenTriggerRulesJson { get; set; } = "[]";
    public string ProvenanceJson { get; set; } = "{}";
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadyAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime? StaleAt { get; set; }
}

public class Stage6CaseLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Stage6CaseId { get; set; }
    public string LinkedObjectType { get; set; } = string.Empty;
    public string LinkedObjectId { get; set; } = string.Empty;
    public string LinkRole { get; set; } = Stage6CaseLinkRoles.Source;
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Stage6ScopeCandidate
{
    public long ScopeCaseId { get; set; }
    public long ChatId { get; set; }
    public int ActiveCaseCount { get; set; }
    public int TotalCaseCount { get; set; }
    public DateTime LastCaseUpdatedAtUtc { get; set; }
}
