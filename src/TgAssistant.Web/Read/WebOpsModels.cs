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
