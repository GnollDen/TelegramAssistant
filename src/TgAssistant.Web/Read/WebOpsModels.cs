namespace TgAssistant.Web.Read;

public class InboxReadModel
{
    public string GroupFilter { get; set; } = "all";
    public string StatusFilter { get; set; } = "open";
    public string? PriorityFilter { get; set; }
    public bool? BlockingFilter { get; set; }
    public List<InboxItemReadModel> Blocking { get; set; } = [];
    public List<InboxItemReadModel> HighImpact { get; set; } = [];
    public List<InboxItemReadModel> EverythingElse { get; set; } = [];
    public int TotalVisible { get; set; }
}

public class InboxItemReadModel
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public float? Confidence { get; set; }
    public string ReasonSummary { get; set; } = string.Empty;
    public string? ClarificationKind { get; set; }
    public string? ResponseChannelHint { get; set; }
}

public class HistoryReadModel
{
    public string? ObjectTypeFilter { get; set; }
    public string? ActionFilter { get; set; }
    public List<ActivityEventReadModel> Events { get; set; } = [];
}

public class ActivityEventReadModel
{
    public Guid Id { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TimestampLabel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class ObjectHistoryReadModel
{
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string ObjectSummary { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public bool? IsBlocking { get; set; }
    public List<ActivityEventReadModel> Events { get; set; } = [];
}

public class RecentChangesReadModel
{
    public List<ActivityEventReadModel> Items { get; set; } = [];
}

public class BudgetOperationalStateReadModel
{
    public string PathKey { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool IsPaused { get; set; }
    public bool IsDegraded { get; set; }
    public bool IsHardPaused { get; set; }
    public bool IsQuotaBlocked { get; set; }
    public string VisibilityMode { get; set; } = "active";
    public List<KeyValuePair<string, string>> Details { get; set; } = [];
}

public class BudgetOperationalReadModel
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string OperationalStatus { get; set; } = "active";
    public int TotalPaths { get; set; }
    public int PausedPaths { get; set; }
    public int HardPausedPaths { get; set; }
    public int QuotaBlockedPaths { get; set; }
    public int DegradedPaths { get; set; }
    public int ActivePaths { get; set; }
    public List<BudgetOperationalStateReadModel> States { get; set; } = [];
}

public class EvalRunReadModel
{
    public Guid RunId { get; set; }
    public string RunName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string RunKind { get; set; } = "standard";
    public Guid? LinkedExperimentRunId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public Dictionary<string, string> Metrics { get; set; } = new();
    public long DurationSeconds { get; set; }
    public int ScenarioCount { get; set; }
    public int ScenarioPassed { get; set; }
    public int ScenarioFailed { get; set; }
}

public class EvalScenarioReadModel
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string ScenarioName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public Dictionary<string, string> Metrics { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class EvalRunsReadModel
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string OperationalStatus { get; set; } = "unknown";
    public int TotalRuns { get; set; }
    public int PassedRuns { get; set; }
    public int FailedRuns { get; set; }
    public List<EvalRunReadModel> Runs { get; set; } = [];
    public List<EvalRunComparisonReadModel> Comparisons { get; set; } = [];
    public EvalRunReadModel? SelectedRun { get; set; }
    public List<EvalScenarioReadModel> SelectedScenarios { get; set; } = [];
}

public class EvalRunComparisonReadModel
{
    public string RunName { get; set; } = string.Empty;
    public Guid CurrentRunId { get; set; }
    public Guid PreviousRunId { get; set; }
    public DateTime CurrentStartedAt { get; set; }
    public DateTime PreviousStartedAt { get; set; }
    public bool CurrentPassed { get; set; }
    public bool PreviousPassed { get; set; }
    public bool StatusChanged { get; set; }
    public string StatusTransition { get; set; } = string.Empty;
    public double CurrentScenarioPassRate { get; set; }
    public double PreviousScenarioPassRate { get; set; }
    public double ScenarioPassRateDelta { get; set; }
    public long CurrentDurationSeconds { get; set; }
    public long PreviousDurationSeconds { get; set; }
    public long DurationDeltaSeconds { get; set; }
}

public class AbScenarioCandidatePoolReadModel
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public int RequestedCount { get; set; }
    public string? BucketFilter { get; set; }
    public int TotalCandidates { get; set; }
    public int StateCandidates { get; set; }
    public int StrategyDraftCandidates { get; set; }
    public int CounterexampleCandidates { get; set; }
    public List<AbScenarioCandidateReadModel> Candidates { get; set; } = [];
}

public class AbScenarioCandidateReadModel
{
    public string CandidateId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Bucket { get; set; } = "state";
    public AbDateRangeReadModel DateRange { get; set; } = new();
    public List<long> ChatIds { get; set; } = [];
    public int MessageCount { get; set; }
    public int SessionCount { get; set; }
    public AbScenarioSourceArtifactsReadModel SourceArtifacts { get; set; } = new();
    public string WhySelected { get; set; } = string.Empty;
    public string RiskOfMisread { get; set; } = string.Empty;
    public string SuggestedExpectedState { get; set; } = string.Empty;
    public List<string> SuggestedExpectedRisks { get; set; } = [];
}

public class AbDateRangeReadModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class AbScenarioSourceArtifactsReadModel
{
    public List<Guid> PeriodIds { get; set; } = [];
    public List<Guid> TransitionIds { get; set; } = [];
    public List<Guid> UnresolvedTransitionIds { get; set; } = [];
    public List<Guid> ConflictIds { get; set; } = [];
    public List<Guid> ClarificationIds { get; set; } = [];
    public List<Guid> StateSnapshotIds { get; set; } = [];
    public List<Guid> StrategyRecordIds { get; set; } = [];
    public List<Guid> DraftRecordIds { get; set; } = [];
    public List<Guid> OutcomeIds { get; set; } = [];
    public List<Guid> OfflineEventIds { get; set; } = [];
    public List<string> NetworkNodeIds { get; set; } = [];
    public List<string> NetworkEdgeIds { get; set; } = [];
}

public class Stage6CaseQueueReadModel
{
    public string StatusFilter { get; set; } = "active";
    public string? PriorityFilter { get; set; }
    public string? CaseTypeFilter { get; set; }
    public string? ArtifactTypeFilter { get; set; }
    public string? Query { get; set; }
    public int TotalCases { get; set; }
    public int VisibleCases { get; set; }
    public int NeedsInputCases { get; set; }
    public int ReadyCases { get; set; }
    public int StaleCases { get; set; }
    public int ResolvedCases { get; set; }
    public List<Stage6CaseQueueItemReadModel> Cases { get; set; } = [];
}

public class Stage6CaseQueueItemReadModel
{
    public Guid Id { get; set; }
    public string CaseType { get; set; } = string.Empty;
    public string? CaseSubtype { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public string ReasonSummary { get; set; } = string.Empty;
    public string? QuestionText { get; set; }
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public bool NeedsAnswer { get; set; }
    public string? ResponseMode { get; set; }
    public int EvidenceCount { get; set; }
    public List<string> TargetArtifactTypes { get; set; } = [];
}

public class Stage6CaseDetailReadModel
{
    public bool Exists { get; set; }
    public Guid Id { get; set; }
    public string CaseType { get; set; } = string.Empty;
    public string? CaseSubtype { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public string ReasonSummary { get; set; } = string.Empty;
    public string? QuestionText { get; set; }
    public string? ClarificationKind { get; set; }
    public string? ResponseMode { get; set; }
    public string? ResponseChannelHint { get; set; }
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public string SourceSummary { get; set; } = string.Empty;
    public string? SourceLink { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime? StaleAt { get; set; }
    public DateTime? EarliestEvidenceAtUtc { get; set; }
    public DateTime? LatestEvidenceAtUtc { get; set; }
    public List<string> SubjectRefs { get; set; } = [];
    public List<string> ReopenTriggers { get; set; } = [];
    public List<Stage6EvidenceReadModel> Evidence { get; set; } = [];
    public List<Stage6EvidenceMessageReadModel> EvidenceMessages { get; set; } = [];
    public List<Stage6EvidenceParticipantReadModel> EvidenceParticipants { get; set; } = [];
    public List<Stage6ArtifactSummaryReadModel> Artifacts { get; set; } = [];
    public List<Stage6CaseQueueItemReadModel> LinkedCases { get; set; } = [];
    public List<Stage6LinkedObjectReadModel> LinkedObjects { get; set; } = [];
    public List<Stage6ContextEntryReadModel> ContextEntries { get; set; } = [];
    public List<Stage6FeedbackReadModel> Feedback { get; set; } = [];
    public List<Stage6CaseOutcomeReadModel> Outcomes { get; set; } = [];
    public List<ActivityEventReadModel> History { get; set; } = [];
    public ClarificationQuestionDetailReadModel? Clarification { get; set; }
}

public class ClarificationQuestionDetailReadModel
{
    public Guid QuestionId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string QuestionType { get; set; } = string.Empty;
    public List<string> AnswerOptions { get; set; } = [];
    public List<ClarificationAnswerDetailReadModel> Answers { get; set; } = [];
}

public class ClarificationAnswerDetailReadModel
{
    public Guid Id { get; set; }
    public string AnswerType { get; set; } = string.Empty;
    public string AnswerValue { get; set; } = string.Empty;
    public float AnswerConfidence { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class Stage6EvidenceReadModel
{
    public string Reference { get; set; } = string.Empty;
    public string SourceClass { get; set; } = "system_inference";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Link { get; set; }
    public DateTime? TimestampUtc { get; set; }
}

public class Stage6ArtifactSummaryReadModel
{
    public string ArtifactType { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ConfidenceLabel { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public DateTime? RefreshedAt { get; set; }
    public string? PayloadObjectType { get; set; }
    public string? PayloadObjectId { get; set; }
}

public class Stage6ContextEntryReadModel
{
    public Guid Id { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string ContentText { get; set; } = string.Empty;
    public string EnteredVia { get; set; } = string.Empty;
    public float UserReportedCertainty { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> AppliesToRefs { get; set; } = [];
    public Guid? SupersedesContextEntryId { get; set; }
    public List<string> ConflictsWithRefs { get; set; } = [];
    public string? StructuredPayloadJson { get; set; }
}

public class Stage6ArtifactDetailReadModel
{
    public string ArtifactType { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? ConfidenceLabel { get; set; }
    public string? PayloadObjectType { get; set; }
    public string? PayloadObjectId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string FreshnessBasisJson { get; set; } = "{}";
    public DateTime? GeneratedAt { get; set; }
    public DateTime? RefreshedAt { get; set; }
    public DateTime? LatestEvidenceAtUtc { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public List<Stage6EvidenceReadModel> Evidence { get; set; } = [];
    public List<Stage6FeedbackReadModel> Feedback { get; set; } = [];
    public List<Stage6CaseQueueItemReadModel> LinkedCases { get; set; } = [];
}

public class Stage6FeedbackReadModel
{
    public Guid Id { get; set; }
    public string FeedbackKind { get; set; } = string.Empty;
    public string FeedbackDimension { get; set; } = string.Empty;
    public bool? IsUseful { get; set; }
    public string? Note { get; set; }
    public string SourceChannel { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class Stage6LinkedObjectReadModel
{
    public string LinkRole { get; set; } = string.Empty;
    public string LinkedObjectType { get; set; } = string.Empty;
    public string LinkedObjectId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Link { get; set; }
}

public class Stage6EvidenceMessageReadModel
{
    public long MessageId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string TextSnippet { get; set; } = string.Empty;
    public bool IsDirectEvidence { get; set; }
}

public class Stage6EvidenceParticipantReadModel
{
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int EvidenceMessageCount { get; set; }
}

public class Stage6CaseOutcomeReadModel
{
    public Guid Id { get; set; }
    public string OutcomeType { get; set; } = string.Empty;
    public string CaseStatusAfter { get; set; } = string.Empty;
    public bool UserContextMaterial { get; set; }
    public string? Note { get; set; }
    public string SourceChannel { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class WebStage6CaseActionRequest
{
    public long ScopeCaseId { get; set; }
    public long ChatId { get; set; }
    public Guid Stage6CaseId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = "web";
    public string? Reason { get; set; }
    public string? Note { get; set; }
    public string? FeedbackKind { get; set; }
    public string? FeedbackDimension { get; set; }
    public bool? IsUseful { get; set; }
    public string? ContextSourceKind { get; set; }
    public string? ContextEntryMode { get; set; }
    public string? CorrectionTargetRef { get; set; }
    public string? CorrectionSummary { get; set; }
    public float? ContextCertainty { get; set; }
}

public class WebStage6CaseActionResult
{
    public bool Success { get; set; }
    public Guid Stage6CaseId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Status { get; set; }
    public List<string> RefreshedArtifactTypes { get; set; } = [];
}

public class WebStage6ClarificationAnswerRequest
{
    public long ScopeCaseId { get; set; }
    public long ChatId { get; set; }
    public Guid Stage6CaseId { get; set; }
    public string AnswerType { get; set; } = "text";
    public string AnswerValue { get; set; } = string.Empty;
    public float AnswerConfidence { get; set; } = 0.8f;
    public string SourceClass { get; set; } = "operator_web";
    public bool MarkResolved { get; set; } = true;
    public string Actor { get; set; } = "web";
    public string? Reason { get; set; }
    public bool? IsUseful { get; set; }
    public string? FeedbackDimension { get; set; }
}

public class WebStage6ClarificationAnswerResult
{
    public bool Success { get; set; }
    public Guid Stage6CaseId { get; set; }
    public Guid? QuestionId { get; set; }
    public Guid? AnswerId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string AnswerValue { get; set; } = string.Empty;
    public List<string> RefreshedArtifactTypes { get; set; } = [];
    public List<string> RecomputeTargets { get; set; } = [];
}

public class WebStage6ArtifactActionRequest
{
    public long ScopeCaseId { get; set; }
    public long ChatId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = "web";
    public string? Reason { get; set; }
}

public class WebStage6ArtifactActionResult
{
    public bool Success { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
