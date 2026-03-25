namespace TgAssistant.Web.Read;

public class WebReadRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public string Actor { get; set; } = "web";
    public DateTime? AsOfUtc { get; set; }
}

public class DashboardReadModel
{
    public CurrentStateReadModel CurrentState { get; set; } = new();
    public StrategyReadModel Strategy { get; set; } = new();
    public ClarificationsReadModel Clarifications { get; set; } = new();
    public TimelineReadModel Timeline { get; set; } = new();
    public DraftsReviewsReadModel DraftsReviews { get; set; } = new();
    public List<string> Alerts { get; set; } = [];
}

public class CurrentStateReadModel
{
    public DateTime? AsOfUtc { get; set; }
    public string DynamicLabel { get; set; } = string.Empty;
    public string RelationshipStatus { get; set; } = string.Empty;
    public string? AlternativeStatus { get; set; }
    public float Confidence { get; set; }
    public Dictionary<string, float> Scores { get; set; } = new();
    public List<string> KeySignals { get; set; } = [];
    public List<string> MainRisks { get; set; } = [];
    public string NextMoveSummary { get; set; } = string.Empty;
    public List<StateInsightReadModel> ObservedFacts { get; set; } = [];
    public List<StateInsightReadModel> LikelyInterpretation { get; set; } = [];
    public List<StateInsightReadModel> Uncertainties { get; set; } = [];
    public List<StateInsightReadModel> MissingInformation { get; set; } = [];
    public string OverallSignalStrength { get; set; } = "weak";
}

public class StateInsightReadModel
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string SignalStrength { get; set; } = "weak";
    public string Evidence { get; set; } = string.Empty;
}

public class TimelineReadModel
{
    public TimelinePeriodReadModel? CurrentPeriod { get; set; }
    public List<TimelinePeriodReadModel> PriorPeriods { get; set; } = [];
    public List<TimelineTransitionReadModel> Transitions { get; set; } = [];
    public int UnresolvedTransitions { get; set; }
}

public class TimelinePeriodReadModel
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool IsOpen { get; set; }
    public string Summary { get; set; } = string.Empty;
    public float InterpretationConfidence { get; set; }
    public int OpenQuestionsCount { get; set; }
    public List<string> EvidenceHooks { get; set; } = [];
}

public class TimelineTransitionReadModel
{
    public Guid Id { get; set; }
    public Guid FromPeriodId { get; set; }
    public Guid ToPeriodId { get; set; }
    public string TransitionType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public float Confidence { get; set; }
}

public class NetworkReadModel
{
    public DateTime GeneratedAtUtc { get; set; }
    public List<NetworkNodeReadModel> Nodes { get; set; } = [];
    public List<NetworkInfluenceEdgeReadModel> InfluenceEdges { get; set; } = [];
    public List<NetworkInformationFlowReadModel> InformationFlows { get; set; } = [];
}

public class NetworkNodeReadModel
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PrimaryRole { get; set; } = string.Empty;
    public List<string> AdditionalRoles { get; set; } = [];
    public string GlobalRole { get; set; } = string.Empty;
    public bool IsFocalActor { get; set; }
    public float ImportanceScore { get; set; }
    public float Confidence { get; set; }
    public List<Guid> LinkedPeriods { get; set; } = [];
    public List<Guid> LinkedEvents { get; set; } = [];
    public List<Guid> LinkedClarifications { get; set; } = [];
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NetworkInfluenceEdgeReadModel
{
    public string EdgeId { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string InfluenceType { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public bool IsHypothesis { get; set; }
    public Guid? LinkedPeriodId { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class NetworkInformationFlowReadModel
{
    public string EdgeId { get; set; } = string.Empty;
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public string FlowType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public Guid? LinkedPeriodId { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class ProfilesReadModel
{
    public ProfileSubjectReadModel Self { get; set; } = new();
    public ProfileSubjectReadModel Other { get; set; } = new();
    public ProfileSubjectReadModel Pair { get; set; } = new();
}

public class ProfileSubjectReadModel
{
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public List<ProfileTraitReadModel> TopTraits { get; set; } = [];
    public string WhatWorks { get; set; } = string.Empty;
    public string WhatFails { get; set; } = string.Empty;
}

public class ProfileTraitReadModel
{
    public string TraitKey { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
}

public class ClarificationsReadModel
{
    public int OpenCount { get; set; }
    public List<ClarificationQuestionReadModel> TopQuestions { get; set; } = [];
}

public class ClarificationQuestionReadModel
{
    public Guid Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class StrategyReadModel
{
    public Guid RecordId { get; set; }
    public float Confidence { get; set; }
    public string PrimarySummary { get; set; } = string.Empty;
    public string PrimaryPurpose { get; set; } = string.Empty;
    public List<string> PrimaryRisks { get; set; } = [];
    public List<StrategyOptionReadModel> Alternatives { get; set; } = [];
    public string MicroStep { get; set; } = string.Empty;
    public List<string> Horizon { get; set; } = [];
    public string WhyNotNotes { get; set; } = string.Empty;
}

public class StrategyOptionReadModel
{
    public string ActionType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<string> Risks { get; set; } = [];
}

public class DraftsReviewsReadModel
{
    public DraftReadModel? LatestDraft { get; set; }
    public DraftReviewReadModel? LatestReview { get; set; }
    public DraftOutcomeReadModel? LatestOutcome { get; set; }
}

public class DraftReadModel
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string MainDraft { get; set; } = string.Empty;
    public string? AltDraft1 { get; set; }
    public string? AltDraft2 { get; set; }
    public string? StyleNotes { get; set; }
    public float Confidence { get; set; }
}

public class DraftReviewReadModel
{
    public string Assessment { get; set; } = string.Empty;
    public List<string> MainRisks { get; set; } = [];
    public List<string> RiskLabels { get; set; } = [];
    public string SaferRewrite { get; set; } = string.Empty;
    public string NaturalRewrite { get; set; } = string.Empty;
    public bool StrategyConflictDetected { get; set; }
}

public class DraftOutcomeReadModel
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public Guid? StrategyRecordId { get; set; }
    public long? ActualMessageId { get; set; }
    public long? FollowUpMessageId { get; set; }
    public float? MatchScore { get; set; }
    public string MatchedBy { get; set; } = string.Empty;
    public string OutcomeLabel { get; set; } = string.Empty;
    public string? UserOutcomeLabel { get; set; }
    public string? SystemOutcomeLabel { get; set; }
    public float? OutcomeConfidence { get; set; }
    public List<string> LearningSignals { get; set; } = [];
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OutcomeTrailReadModel
{
    public int TotalOutcomesScanned { get; set; }
    public int MissingDraftCount { get; set; }
    public int MissingStrategyCount { get; set; }
    public List<OutcomeChainItemReadModel> Items { get; set; } = [];
}

public class OutcomeChainItemReadModel
{
    public Guid OutcomeId { get; set; }
    public DateTime OutcomeCreatedAt { get; set; }
    public Guid? StrategyRecordId { get; set; }
    public DateTime? StrategyCreatedAt { get; set; }
    public string StrategySummary { get; set; } = string.Empty;
    public Guid DraftId { get; set; }
    public DateTime? DraftCreatedAt { get; set; }
    public string DraftSnippet { get; set; } = string.Empty;
    public long? ActualMessageId { get; set; }
    public string? ActualMessageSnippet { get; set; }
    public long? FollowUpMessageId { get; set; }
    public string? FollowUpMessageSnippet { get; set; }
    public float? MatchScore { get; set; }
    public string MatchedBy { get; set; } = string.Empty;
    public string OutcomeLabel { get; set; } = string.Empty;
    public string? UserOutcomeLabel { get; set; }
    public string? SystemOutcomeLabel { get; set; }
    public float? OutcomeConfidence { get; set; }
    public List<string> LearningSignals { get; set; } = [];
}

public class OfflineEventsReadModel
{
    public List<OfflineEventReadModel> Events { get; set; } = [];
}

public class OfflineEventReadModel
{
    public Guid Id { get; set; }
    public DateTime TimestampStart { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UserSummary { get; set; } = string.Empty;
    public Guid? LinkedPeriodId { get; set; }
    public string EvidenceSummary { get; set; } = string.Empty;
}

public class WebRenderResult
{
    public string Route { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
}
