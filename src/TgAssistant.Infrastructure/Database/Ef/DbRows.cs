using System.Text.Json;

namespace TgAssistant.Infrastructure.Database.Ef;

public class DbMessage
{
    public long Id { get; set; }
    public long TelegramMessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Text { get; set; }
    public short MediaType { get; set; }
    public string? MediaPath { get; set; }
    public string? MediaDescription { get; set; }
    public string? MediaTranscription { get; set; }
    public string? MediaParalinguisticsJson { get; set; }
    public long? ReplyToMessageId { get; set; }
    public DateTime? EditTimestamp { get; set; }
    public string? ReactionsJson { get; set; }
    public string? ForwardJson { get; set; }
    public short ProcessingStatus { get; set; }
    public short Source { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public bool NeedsReanalysis { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbArchiveImportRun
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public short Status { get; set; }
    public int LastMessageIndex { get; set; }
    public long ImportedMessages { get; set; }
    public long QueuedMedia { get; set; }
    public long TotalMessages { get; set; }
    public long TotalMedia { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntity
{
    public Guid Id { get; set; }
    public short Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string? ActorKey { get; set; }
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}");
    public bool IsUserConfirmed { get; set; }
    public float TrustFactor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntityAlias
{
    public long Id { get; set; }
    public Guid EntityId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string AliasNorm { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntityMergeCandidate
{
    public long Id { get; set; }
    public Guid EntityLowId { get; set; }
    public Guid EntityHighId { get; set; }
    public string AliasNorm { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public float Score { get; set; }
    public short ReviewPriority { get; set; }
    public short Status { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntityMergeDecision
{
    public long Id { get; set; }
    public long? CandidateId { get; set; }
    public Guid EntityLowId { get; set; }
    public Guid EntityHighId { get; set; }
    public string AliasNorm { get; set; } = string.Empty;
    public short Decision { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbEntityMergeCommand
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public short Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class DbFactReviewCommand
{
    public long Id { get; set; }
    public Guid FactId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public short Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class DbFact
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public short Status { get; set; }
    public float Confidence { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsCurrent { get; set; }
    public string DecayClass { get; set; } = "slow";
    public bool IsUserConfirmed { get; set; }
    public float TrustFactor { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbRelationship
{
    public Guid Id { get; set; }
    public Guid FromEntityId { get; set; }
    public Guid ToEntityId { get; set; }
    public string Type { get; set; } = string.Empty;
    public short Status { get; set; }
    public float Confidence { get; set; }
    public string? ContextText { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbDailySummary
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime Date { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int MediaCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbChatDialogSummary
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public short SummaryType { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public long StartMessageId { get; set; }
    public long EndMessageId { get; set; }
    public int MessageCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbChatSession
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public int SessionIndex { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public bool IsAnalyzed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbPromptTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Version { get; set; } = "v1";
    public string Checksum { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbAnalysisState
{
    public string Key { get; set; } = string.Empty;
    public long Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbMessageExtraction
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public string CheapJson { get; set; } = "{}";
    public string? ExpensiveJson { get; set; }
    public bool NeedsExpensive { get; set; }
    public bool IsQuarantined { get; set; }
    public string? QuarantineReason { get; set; }
    public DateTime? QuarantinedAt { get; set; }
    public int ExpensiveRetryCount { get; set; }
    public DateTime? ExpensiveNextRetryAt { get; set; }
    public string? ExpensiveLastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbIntelligenceObservation
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public Guid? EntityId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string ObservationType { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? Value { get; set; }
    public string? Evidence { get; set; }
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbIntelligenceClaim
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public Guid? EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string ClaimType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public short Status { get; set; }
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbCommunicationEvent
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public Guid? EntityId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? Sentiment { get; set; }
    public string? Summary { get; set; }
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbExtractionError
{
    public long Id { get; set; }
    public string Stage { get; set; } = string.Empty;
    public long? MessageId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbStage5MetricsSnapshot
{
    public long Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public long ProcessedMessages { get; set; }
    public long ExtractionsTotal { get; set; }
    public long ExpensiveBacklog { get; set; }
    public long MergeCandidatesPending { get; set; }
    public long FactReviewsPending { get; set; }
    public long ExtractionErrors1h { get; set; }
    public long AnalysisRequests1h { get; set; }
    public long AnalysisTokens1h { get; set; }
    public decimal AnalysisCostUsd1h { get; set; }
    public long PendingSessionsQueue { get; set; }
    public long ReanalysisBacklog { get; set; }
    public long QuarantineTotal { get; set; }
    public long QuarantineStuck { get; set; }
    public long DuplicateMessageBusinessKeyGroups { get; set; }
    public long DuplicateMessageBusinessKeyRows { get; set; }
    public decimal DuplicateMessageBusinessKeyRowRate { get; set; }
    public long ProcessedWithoutExtraction { get; set; }
    public long ProcessedWithoutApplyEvidenceCount { get; set; }
    public decimal ProcessedWithoutApplyEvidenceRate { get; set; }
    public long WatermarkRegressionBlocked1h { get; set; }
    public long WatermarkMonotonicRegressionCount { get; set; }
}

public class DbAnalysisUsageEvent
{
    public long Id { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbTextEmbedding
{
    public long Id { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public DateTime CreatedAt { get; set; }
}

public class DbStickerCache
{
    public string ContentHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long HitCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
}

public class DbPeriod
{
    public Guid Id { get; set; }
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
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbPeriodTransition
{
    public Guid Id { get; set; }
    public Guid FromPeriodId { get; set; }
    public Guid ToPeriodId { get; set; }
    public string TransitionType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public float Confidence { get; set; }
    public Guid? GapId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbHypothesis
{
    public Guid Id { get; set; }
    public string HypothesisType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? PeriodId { get; set; }
    public string Statement { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public string ConflictRefsJson { get; set; } = "[]";
    public string ValidationTargetsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbClarificationQuestion
{
    public Guid Id { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string QuestionType { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public Guid? RelatedHypothesisId { get; set; }
    public string AffectedOutputsJson { get; set; } = "[]";
    public string WhyItMatters { get; set; } = string.Empty;
    public float ExpectedGain { get; set; }
    public string AnswerOptionsJson { get; set; } = "[]";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbClarificationAnswer
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public string AnswerType { get; set; } = string.Empty;
    public string AnswerValue { get; set; } = string.Empty;
    public float AnswerConfidence { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string AffectedObjectsJson { get; set; } = "[]";
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbOfflineEvent
{
    public Guid Id { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UserSummary { get; set; } = string.Empty;
    public string? AutoSummary { get; set; }
    public DateTime TimestampStart { get; set; }
    public DateTime? TimestampEnd { get; set; }
    public Guid? PeriodId { get; set; }
    public string ReviewStatus { get; set; } = string.Empty;
    public string? ImpactSummary { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbAudioAsset
{
    public Guid Id { get; set; }
    public Guid OfflineEventId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int? DurationSeconds { get; set; }
    public string TranscriptStatus { get; set; } = string.Empty;
    public string? TranscriptText { get; set; }
    public string SpeakerReviewStatus { get; set; } = string.Empty;
    public string ProcessingStatus { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbAudioSegment
{
    public Guid Id { get; set; }
    public Guid AudioAssetId { get; set; }
    public int SegmentIndex { get; set; }
    public decimal StartSeconds { get; set; }
    public decimal EndSeconds { get; set; }
    public string? SpeakerLabel { get; set; }
    public string TranscriptText { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbAudioSnippet
{
    public Guid Id { get; set; }
    public Guid AudioAssetId { get; set; }
    public Guid? AudioSegmentId { get; set; }
    public string SnippetType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}

public class DbStateSnapshot
{
    public Guid Id { get; set; }
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
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbProfileSnapshot
{
    public Guid Id { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? PeriodId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbProfileTrait
{
    public Guid Id { get; set; }
    public Guid ProfileSnapshotId { get; set; }
    public string TraitKey { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public bool IsSensitive { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbStrategyRecord
{
    public Guid Id { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? StateSnapshotId { get; set; }
    public float StrategyConfidence { get; set; }
    public string RecommendedGoal { get; set; } = string.Empty;
    public string WhyNotOthers { get; set; } = string.Empty;
    public string MicroStep { get; set; } = string.Empty;
    public string? HorizonJson { get; set; }
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbStrategyOption
{
    public Guid Id { get; set; }
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

public class DbDraftRecord
{
    public Guid Id { get; set; }
    public Guid StrategyRecordId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string MainDraft { get; set; } = string.Empty;
    public string? AltDraft1 { get; set; }
    public string? AltDraft2 { get; set; }
    public string? StyleNotes { get; set; }
    public float Confidence { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbDraftOutcome
{
    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public Guid? StrategyRecordId { get; set; }
    public long? ActualMessageId { get; set; }
    public long? FollowUpMessageId { get; set; }
    public string? MatchedBy { get; set; }
    public float? MatchScore { get; set; }
    public string OutcomeLabel { get; set; } = string.Empty;
    public string? UserOutcomeLabel { get; set; }
    public string? SystemOutcomeLabel { get; set; }
    public float? OutcomeConfidence { get; set; }
    public string? LearningSignalsJson { get; set; }
    public string? Notes { get; set; }
    public Guid? SourceSessionId { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbInboxItem
{
    public Guid Id { get; set; }
    public string ItemType { get; set; } = string.Empty;
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LastActor { get; set; }
    public string? LastReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbConflictRecord
{
    public Guid Id { get; set; }
    public string ConflictType { get; set; } = string.Empty;
    public string ObjectAType { get; set; } = string.Empty;
    public string ObjectAId { get; set; } = string.Empty;
    public string ObjectBType { get; set; } = string.Empty;
    public string ObjectBId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string? LastActor { get; set; }
    public string? LastReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbDependencyLink
{
    public Guid Id { get; set; }
    public string UpstreamType { get; set; } = string.Empty;
    public string UpstreamId { get; set; } = string.Empty;
    public string DownstreamType { get; set; } = string.Empty;
    public string DownstreamId { get; set; } = string.Empty;
    public string LinkType { get; set; } = string.Empty;
    public string? LinkReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbDomainReviewEvent
{
    public Guid Id { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValueRef { get; set; }
    public string? NewValueRef { get; set; }
    public string? Reason { get; set; }
    public string Actor { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DbBudgetOperationalState
{
    public string PathKey { get; set; } = string.Empty;
    public string Modality { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}

public class DbChatCoordinationState
{
    public long ChatId { get; set; }
    public string State { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime? LastBackfillStartedAt { get; set; }
    public DateTime? LastBackfillCompletedAt { get; set; }
    public DateTime? HandoverReadyAt { get; set; }
    public DateTime? RealtimeActivatedAt { get; set; }
    public DateTime? LastListenerSeenAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbChatPhaseGuard
{
    public long ChatId { get; set; }
    public string? ActivePhase { get; set; }
    public string? OwnerId { get; set; }
    public string? PhaseReason { get; set; }
    public DateTime? ActiveSince { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? LastRequestedPhase { get; set; }
    public string? LastObservedPhase { get; set; }
    public string? LastDenyCode { get; set; }
    public string? LastDenyReason { get; set; }
    public DateTime? LastDeniedAt { get; set; }
    public DateTime? LastRecoveryAt { get; set; }
    public string? LastRecoveryFromOwnerId { get; set; }
    public string? LastRecoveryCode { get; set; }
    public string? LastRecoveryReason { get; set; }
    public DateTime? TailReopenWindowFromUtc { get; set; }
    public DateTime? TailReopenWindowToUtc { get; set; }
    public string? TailReopenOperator { get; set; }
    public string? TailReopenAuditId { get; set; }
}

public class DbBackupEvidenceRecord
{
    public string BackupId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string ArtifactUri { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}

public class DbEvalRun
{
    public Guid Id { get; set; }
    public string RunName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = "{}";
}

public class DbEvalScenarioResult
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string ScenarioName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class DbExternalArchiveImportBatch
{
    public Guid RunId { get; set; }
    public long CaseId { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ImportBatchId { get; set; } = string.Empty;
    public string RequestPayloadHash { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; }
    public string Actor { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int AcceptedCount { get; set; }
    public int ReplayedCount { get; set; }
    public int RejectedCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbExternalArchiveImportRecord
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public long CaseId { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ImportBatchId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? SubjectActorKey { get; set; }
    public string? TargetActorKey { get; set; }
    public long? ChatId { get; set; }
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public float Confidence { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public string EvidenceRefsJson { get; set; } = "[]";
    public string TruthLayer { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public float BaseWeight { get; set; }
    public float ConfidenceMultiplier { get; set; }
    public float CorroborationMultiplier { get; set; }
    public float FinalWeight { get; set; }
    public bool NeedsClarification { get; set; }
    public string WeightingReason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class DbExternalArchiveLinkageArtifact
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid RecordRowId { get; set; }
    public long CaseId { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public float LinkConfidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = string.Empty;
    public bool AutoApplyAllowed { get; set; }
    public DateTime CreatedAt { get; set; }
}
