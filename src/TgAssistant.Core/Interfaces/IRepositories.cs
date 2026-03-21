using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IMessageRepository
{
    Task<long> SaveBatchAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    Task<List<Message>> GetUnprocessedAsync(int limit = 100, CancellationToken ct = default);
    Task<List<Message>> GetByContactSinceAsync(long chatId, DateTime since, CancellationToken ct = default);
    Task<List<Message>> GetProcessedAfterIdAsync(long afterId, int limit, CancellationToken ct = default);
    Task<List<Message>> GetByIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default);
    Task<List<Message>> GetChatWindowBeforeAsync(long chatId, long beforeMessageId, int limit, CancellationToken ct = default);
    Task<Dictionary<long, List<Message>>> GetChatWindowsBeforeByMessageIdsAsync(
        IReadOnlyCollection<long> messageIds,
        int limit,
        CancellationToken ct = default);
    Task<List<Message>> GetChatWindowAroundAsync(long chatId, long centerMessageId, int beforeCount, int afterCount, CancellationToken ct = default);
    Task<List<Message>> GetByChatAndPeriodAsync(long chatId, DateTime fromUtc, DateTime toUtc, int limit, CancellationToken ct = default);
    Task<List<Message>> GetProcessedByChatAsync(long chatId, int limit, CancellationToken ct = default);
    Task<List<Message>> GetNeedsReanalysisAsync(int limit, CancellationToken ct = default);
    Task<List<EditDiffCandidate>> GetPendingEditDiffCandidatesAsync(int limit, CancellationToken ct = default);
    Task<Message?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<Dictionary<long, Message>> GetByTelegramMessageIdsAsync(
        long chatId,
        MessageSource source,
        IReadOnlyCollection<long> telegramMessageIds,
        CancellationToken ct = default);
    Task<Dictionary<long, List<long>>> ResolveChatsByTelegramMessageIdsAsync(
        IReadOnlyCollection<long> telegramMessageIds,
        MessageSource source,
        CancellationToken ct = default);
    Task MarkProcessedAsync(IEnumerable<long> messageIds, CancellationToken ct = default);
    Task MarkNeedsReanalysisAsync(IEnumerable<long> messageIds, string reasonCode = "unspecified", CancellationToken ct = default);
    Task MarkNeedsReanalysisDoneAsync(IEnumerable<long> messageIds, CancellationToken ct = default);
    Task SaveEditDiffAnalysisAsync(
        long messageId,
        string classification,
        string summary,
        bool shouldAffectMemory,
        bool addedImportant,
        bool removedImportant,
        float confidence,
        CancellationToken ct = default);
    Task<List<Message>> GetPendingArchiveMediaAsync(int limit, CancellationToken ct = default);
    Task<List<Message>> GetPendingVoiceParalinguisticsAsync(int limit, CancellationToken ct = default);
    Task UpdateMediaProcessingResultAsync(long messageId, MediaProcessingResult result, ProcessingStatus status, CancellationToken ct = default);
    Task UpdateMediaParalinguisticsAsync(long messageId, string jsonPayload, CancellationToken ct = default);
    Task UpdateVoiceProcessingResultAsync(
        long messageId,
        string? transcription,
        string? paralinguisticsJson,
        bool needsReanalysis,
        bool clearMediaPath,
        CancellationToken ct = default);
}

public class EditDiffCandidate
{
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public DateTime? EditedAtUtc { get; set; }
    public string BeforeText { get; set; } = string.Empty;
    public string AfterText { get; set; } = string.Empty;
}

public interface IArchiveImportRepository
{
    Task<ArchiveImportRun?> GetRunningRunAsync(string sourcePath, CancellationToken ct = default);
    Task<ArchiveImportRun?> GetLatestRunAsync(string sourcePath, CancellationToken ct = default);
    Task<ArchiveImportRun> CreateRunAsync(ArchiveImportRun run, CancellationToken ct = default);
    Task UpsertEstimateAsync(string sourcePath, ArchiveCostEstimate estimate, ArchiveImportRunStatus status, CancellationToken ct = default);
    Task UpdateProgressAsync(Guid runId, int lastMessageIndex, long importedMessages, long queuedMedia, CancellationToken ct = default);
    Task CompleteRunAsync(Guid runId, ArchiveImportRunStatus status, string? error, CancellationToken ct = default);
}

public interface IEntityRepository
{
    Task<Entity> UpsertAsync(Entity entity, CancellationToken ct = default);
    Task<Entity?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Entity?> FindByActorKeyAsync(string actorKey, CancellationToken ct = default);
    Task<Entity?> FindByTelegramIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<Entity?> FindByNameOrAliasAsync(string name, CancellationToken ct = default);
    Task<Dictionary<string, Entity>> FindByNamesOrAliasesAsync(IReadOnlyCollection<string> names, CancellationToken ct = default);
    Task<Entity?> FindBestByNameAsync(string name, CancellationToken ct = default);
    Task MergeIntoAsync(Guid targetEntityId, Guid sourceEntityId, CancellationToken ct = default);
    Task<List<Entity>> GetAllAsync(CancellationToken ct = default);
    Task<List<Entity>> GetUpdatedSinceAsync(DateTime sinceUtc, int limit, CancellationToken ct = default);
}

public interface IEntityAliasRepository
{
    Task UpsertAliasAsync(Guid entityId, string alias, long? sourceMessageId = null, float confidence = 1.0f, CancellationToken ct = default);
}

public interface IEntityMergeRepository
{
    Task<int> RefreshAliasMergeCandidatesAsync(int maxCandidates, CancellationToken ct = default);
    Task<int> RecomputeScoresAsync(int limit, CancellationToken ct = default);
    Task<List<EntityMergeCandidate>> GetPendingAsync(int limit, CancellationToken ct = default);
    Task<List<EntityMergeReviewItem>> GetReviewQueueAsync(int limit, CancellationToken ct = default);
    Task<EntityMergeCandidate?> GetByIdAsync(long candidateId, CancellationToken ct = default);
    Task MarkDecisionAsync(long candidateId, MergeDecision decision, string? note = null, CancellationToken ct = default);
}

public interface IEntityMergeCommandRepository
{
    Task<List<EntityMergeCommand>> GetPendingAsync(int limit, CancellationToken ct = default);
    Task MarkDoneAsync(long commandId, CancellationToken ct = default);
    Task MarkFailedAsync(long commandId, string error, CancellationToken ct = default);
}

public interface IRelationshipRepository
{
    Task<Relationship> UpsertAsync(Relationship relationship, CancellationToken ct = default);
    Task<List<Relationship>> GetByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<List<EntityRelationshipInfo>> GetByEntityWithNamesAsync(Guid entityId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default);
}

public interface ICommunicationEventRepository
{
    Task AddRangeAsync(IEnumerable<CommunicationEvent> events, CancellationToken ct = default);
    Task<List<CommunicationEvent>> GetByEntityAsync(Guid entityId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

public interface IFactRepository
{
    Task<Fact?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Fact> UpsertAsync(Fact fact, CancellationToken ct = default);
    Task<List<Fact>> GetCurrentByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<DossierFactPage> GetDossierFactsPageAsync(Guid entityId, int limit, string? categoryFilter, CancellationToken ct = default);
    Task<List<Fact>> GetAllByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task<List<Fact>> SearchSimilarFactsAsync(string model, float[] queryEmbedding, int limit = 10, CancellationToken ct = default);
    Task<List<Fact>> GetWithoutEmbeddingAsync(string model, int limit, CancellationToken ct = default);
    Task<long> CountWithoutEmbeddingAsync(string model, CancellationToken ct = default);
    Task SupersedeFactAsync(Guid oldFactId, Fact newFact, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default);
}

public interface ISummaryRepository
{
    Task SaveAsync(DailySummary summary, CancellationToken ct = default);
    Task<List<DailySummary>> GetByEntityAsync(Guid entityId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public interface IChatDialogSummaryRepository
{
    Task UpsertAsync(ChatDialogSummary summary, CancellationToken ct = default);
    Task UpsertAndFinalizeSessionsAsync(ChatDialogSummary summary, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default);
    Task<ChatDialogSummary?> GetByScopeAsync(
        long chatId,
        ChatDialogSummaryType summaryType,
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default);
    Task<List<ChatDialogSummary>> GetRecentByChatAsync(long chatId, int limit, CancellationToken ct = default);
}

public interface IChatSessionRepository
{
    Task UpsertAsync(ChatSession session, CancellationToken ct = default);
    Task<Dictionary<long, List<ChatSession>>> GetByChatsAsync(IReadOnlyCollection<long> chatIds, CancellationToken ct = default);
    Task<Dictionary<long, List<ChatSession>>> GetByPeriodAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<List<ChatSession>> GetPendingAnalysisSessionsAsync(DateTime staleBeforeUtc, int limit, CancellationToken ct = default);
    Task<Dictionary<long, List<ChatSession>>> GetPendingAggregationCandidatesAsync(DateTime staleBeforeUtc, CancellationToken ct = default);
    Task MarkAnalyzedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default);
    Task MarkFinalizedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default);
}

public interface IPromptTemplateRepository
{
    Task<PromptTemplate?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<PromptTemplate> UpsertAsync(PromptTemplate template, CancellationToken ct = default);
}

public interface IAnalysisStateRepository
{
    Task<long> GetWatermarkAsync(string key, CancellationToken ct = default);
    Task SetWatermarkAsync(string key, long value, CancellationToken ct = default);
    Task ResetWatermarksIfExistAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);
}

public interface IMessageExtractionRepository
{
    Task UpsertCheapAsync(long messageId, string cheapJson, bool needsExpensive, CancellationToken ct = default);
    Task QuarantineMessagesAsync(IReadOnlyCollection<long> messageIds, string reason, CancellationToken ct = default);
    Task<HashSet<long>> GetQuarantinedMessageIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default);
    Task<Dictionary<long, string>> GetCheapJsonByMessageIdsAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default);
    Task<List<MessageExtractionRecord>> GetExpensiveBacklogAsync(int limit, CancellationToken ct = default);
    Task<List<RefinementCandidate>> GetRefinementCandidatesAsync(
        long afterExtractionId,
        int limit,
        int minMessageLength,
        int staleAfterHours,
        DateTime cheapPromptUpdatedAtUtc,
        float lowConfidenceThreshold,
        CancellationToken ct = default);
    Task ResolveExpensiveAsync(long extractionId, string expensiveJson, CancellationToken ct = default);
    Task<ExpensiveRetryResult> MarkExpensiveFailedAsync(
        long extractionId,
        string? error,
        int maxRetries,
        int baseDelaySeconds,
        CancellationToken ct = default);
}

public interface IIntelligenceRepository
{
    Task ReplaceMessageIntelligenceAsync(
        long messageId,
        IReadOnlyCollection<IntelligenceObservation> observations,
        IReadOnlyCollection<IntelligenceClaim> claims,
        CancellationToken ct = default);
    Task<List<IntelligenceClaim>> GetClaimsByMessageAsync(long messageId, CancellationToken ct = default);
    Task<List<IntelligenceClaim>> GetClaimsByMessagesAsync(IReadOnlyCollection<long> messageIds, CancellationToken ct = default);
    Task<List<IntelligenceClaim>> GetClaimsByChatAndPeriodAsync(
        long chatId,
        DateTime fromUtc,
        DateTime toUtc,
        int limit,
        CancellationToken ct = default);
}

public interface IExtractionErrorRepository
{
    Task LogAsync(string stage, string reason, long? messageId = null, string? payload = null, CancellationToken ct = default);
}

public interface IStage5MetricsRepository
{
    Task SaveSnapshotAsync(Stage5MetricsSnapshot snapshot, CancellationToken ct = default);
    Task<Stage5MetricsSnapshot> CaptureAsync(CancellationToken ct = default);
}

public interface IAnalysisUsageRepository
{
    Task LogAsync(AnalysisUsageEvent evt, CancellationToken ct = default);
    Task<decimal> GetCostUsdSinceAsync(string phase, DateTime sinceUtc, CancellationToken ct = default);
    Task<Dictionary<string, decimal>> GetCostUsdByPhaseSinceAsync(DateTime sinceUtc, CancellationToken ct = default);
}

public interface IEmbeddingRepository
{
    Task UpsertAsync(TextEmbedding embedding, CancellationToken ct = default);
    Task<TextEmbedding?> GetByOwnerAsync(string ownerType, string ownerId, string? model = null, CancellationToken ct = default);
    Task<List<TextEmbedding>> FindNearestAsync(string ownerType, string model, float[] vector, int limit = 10, CancellationToken ct = default);
}

public interface IMaintenanceRepository
{
    Task<MaintenanceCleanupResult> CleanupAsync(MaintenanceCleanupRequest request, CancellationToken ct = default);
}

public interface IFactReviewCommandRepository
{
    Task<List<FactReviewCommand>> GetPendingAsync(int limit, CancellationToken ct = default);
    Task EnqueueAsync(Guid factId, string command, string? reason = null, CancellationToken ct = default);
    Task MarkDoneAsync(long commandId, CancellationToken ct = default);
    Task MarkFailedAsync(long commandId, string error, CancellationToken ct = default);
}

public class MessageExtractionRecord
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public string CheapJson { get; set; } = "{}";
    public string? ExpensiveJson { get; set; }
    public bool NeedsExpensive { get; set; }
    public int ExpensiveRetryCount { get; set; }
    public DateTime? ExpensiveNextRetryAt { get; set; }
    public string? ExpensiveLastError { get; set; }
}

public class ExpensiveRetryResult
{
    public bool Found { get; set; }
    public bool IsExhausted { get; set; }
    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
}

public interface IPeriodRepository
{
    Task<Period> CreatePeriodAsync(Period period, CancellationToken ct = default);
    Task<Period?> GetPeriodByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Period>> GetPeriodsByCaseAsync(long caseId, CancellationToken ct = default);
    Task<bool> UpdatePeriodLifecycleAsync(
        Guid id,
        string label,
        string summary,
        bool isOpen,
        DateTime? endAt,
        short reviewPriority,
        string actor,
        string? reason = null,
        CancellationToken ct = default);

    Task<PeriodTransition> CreateTransitionAsync(PeriodTransition transition, CancellationToken ct = default);
    Task<PeriodTransition?> GetTransitionByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<PeriodTransition>> GetTransitionsByPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<Hypothesis> CreateHypothesisAsync(Hypothesis hypothesis, CancellationToken ct = default);
    Task<Hypothesis?> GetHypothesisByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Hypothesis>> GetHypothesesByCaseAsync(long caseId, string? status = null, CancellationToken ct = default);
    Task<bool> UpdateHypothesisLifecycleAsync(
        Guid id,
        string status,
        float confidence,
        string actor,
        string? reason = null,
        CancellationToken ct = default);
}

public interface IClarificationRepository
{
    Task<ClarificationQuestion> CreateQuestionAsync(ClarificationQuestion question, CancellationToken ct = default);
    Task<ClarificationQuestion?> GetQuestionByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ClarificationQuestion>> GetQuestionsAsync(long caseId, Guid? periodId = null, string? status = null, CancellationToken ct = default);

    Task<ClarificationAnswer> CreateAnswerAsync(ClarificationAnswer answer, CancellationToken ct = default);
    Task<ClarificationAnswer?> GetAnswerByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ClarificationAnswer>> GetAnswersByQuestionIdAsync(Guid questionId, CancellationToken ct = default);
    Task<bool> UpdateQuestionWorkflowAsync(
        Guid id,
        string status,
        string priority,
        string actor,
        string? reason = null,
        CancellationToken ct = default);
    Task<ClarificationAnswer> ApplyAnswerAsync(
        Guid questionId,
        ClarificationAnswer answer,
        bool markResolved,
        string actor,
        string? reason = null,
        CancellationToken ct = default);
}

public interface IOfflineEventRepository
{
    Task<OfflineEvent> CreateOfflineEventAsync(OfflineEvent evt, CancellationToken ct = default);
    Task<OfflineEvent?> GetOfflineEventByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<OfflineEvent>> GetOfflineEventsByCaseAsync(long caseId, CancellationToken ct = default);
    Task<int> AssignPeriodByTimeRangeAsync(
        long caseId,
        long? chatId,
        Guid periodId,
        DateTime startAt,
        DateTime? endAt,
        CancellationToken ct = default);

    Task<AudioAsset> CreateAudioAssetAsync(AudioAsset asset, CancellationToken ct = default);
    Task<AudioAsset?> GetAudioAssetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<AudioAsset>> GetAudioAssetsByOfflineEventIdAsync(Guid offlineEventId, CancellationToken ct = default);

    Task<AudioSegment> CreateAudioSegmentAsync(AudioSegment segment, CancellationToken ct = default);
    Task<List<AudioSegment>> GetAudioSegmentsByAssetIdAsync(Guid audioAssetId, CancellationToken ct = default);

    Task<AudioSnippet> CreateAudioSnippetAsync(AudioSnippet snippet, CancellationToken ct = default);
    Task<List<AudioSnippet>> GetAudioSnippetsByAssetIdAsync(Guid audioAssetId, CancellationToken ct = default);
}

public interface IStateProfileRepository
{
    Task<StateSnapshot> CreateStateSnapshotAsync(StateSnapshot snapshot, CancellationToken ct = default);
    Task<StateSnapshot?> GetStateSnapshotByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<StateSnapshot>> GetStateSnapshotsByCaseAsync(long caseId, int limit = 20, CancellationToken ct = default);

    Task<ProfileSnapshot> CreateProfileSnapshotAsync(ProfileSnapshot snapshot, CancellationToken ct = default);
    Task<ProfileSnapshot?> GetProfileSnapshotByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ProfileSnapshot>> GetProfileSnapshotsByCaseAsync(long caseId, string subjectType, string subjectId, CancellationToken ct = default);

    Task<ProfileTrait> CreateProfileTraitAsync(ProfileTrait trait, CancellationToken ct = default);
    Task<List<ProfileTrait>> GetProfileTraitsBySnapshotIdAsync(Guid profileSnapshotId, CancellationToken ct = default);
}

public interface IStrategyDraftRepository
{
    Task<StrategyRecord> CreateStrategyRecordAsync(StrategyRecord record, CancellationToken ct = default);
    Task<StrategyRecord?> GetStrategyRecordByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<StrategyRecord>> GetStrategyRecordsByCaseAsync(long caseId, CancellationToken ct = default);

    Task<StrategyOption> CreateStrategyOptionAsync(StrategyOption option, CancellationToken ct = default);
    Task<List<StrategyOption>> GetStrategyOptionsByRecordIdAsync(Guid strategyRecordId, CancellationToken ct = default);

    Task<DraftRecord> CreateDraftRecordAsync(DraftRecord draft, CancellationToken ct = default);
    Task<DraftRecord?> GetDraftRecordByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<DraftRecord>> GetDraftRecordsByStrategyRecordIdAsync(Guid strategyRecordId, CancellationToken ct = default);

    Task<DraftOutcome> CreateDraftOutcomeAsync(DraftOutcome outcome, CancellationToken ct = default);
    Task<List<DraftOutcome>> GetDraftOutcomesByDraftIdAsync(Guid draftId, CancellationToken ct = default);
    Task<List<DraftOutcome>> GetDraftOutcomesByStrategyRecordIdAsync(Guid strategyRecordId, CancellationToken ct = default);
    Task<List<DraftOutcome>> GetDraftOutcomesByCaseAsync(long caseId, CancellationToken ct = default);
}

public interface IInboxConflictRepository
{
    Task<InboxItem> CreateInboxItemAsync(InboxItem item, CancellationToken ct = default);
    Task<InboxItem?> GetInboxItemByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<InboxItem>> GetInboxItemsAsync(long caseId, string? status = null, CancellationToken ct = default);

    Task<ConflictRecord> CreateConflictRecordAsync(ConflictRecord record, CancellationToken ct = default);
    Task<ConflictRecord?> GetConflictRecordByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ConflictRecord>> GetConflictRecordsAsync(long caseId, string? status = null, CancellationToken ct = default);
    Task<bool> UpdateInboxItemStatusAsync(Guid id, string status, string actor, string? reason = null, CancellationToken ct = default);
    Task<bool> UpdateConflictStatusAsync(Guid id, string status, string actor, string? reason = null, CancellationToken ct = default);
}

public interface IDependencyLinkRepository
{
    Task<DependencyLink> CreateDependencyLinkAsync(DependencyLink link, CancellationToken ct = default);
    Task<List<DependencyLink>> GetByUpstreamAsync(string upstreamType, string upstreamId, CancellationToken ct = default);
    Task<List<DependencyLink>> GetByDownstreamAsync(string downstreamType, string downstreamId, CancellationToken ct = default);
}

public interface IDomainReviewEventRepository
{
    Task<DomainReviewEvent> AddAsync(DomainReviewEvent evt, CancellationToken ct = default);
    Task<List<DomainReviewEvent>> GetByObjectAsync(string objectType, string objectId, int limit = 100, CancellationToken ct = default);
}

public interface IExternalArchiveIngestionRepository
{
    Task<ExternalArchiveImportBatch?> GetBatchByDedupKeyAsync(
        long caseId,
        string sourceClass,
        string sourceRef,
        string requestPayloadHash,
        CancellationToken ct = default);
    Task<ExternalArchiveImportBatch> CreateBatchAsync(ExternalArchiveImportBatch batch, CancellationToken ct = default);
    Task UpdateBatchStatusAsync(
        Guid runId,
        int acceptedCount,
        int replayedCount,
        int rejectedCount,
        string status,
        CancellationToken ct = default);
    Task<ExternalArchivePersistedRecord?> GetRecordByNaturalKeyAsync(
        long caseId,
        string sourceClass,
        string sourceRef,
        string recordId,
        CancellationToken ct = default);
    Task<ExternalArchivePersistedRecord> CreateRecordAsync(ExternalArchivePersistedRecord record, CancellationToken ct = default);
    Task<ExternalArchiveLinkageArtifact> CreateLinkageArtifactAsync(ExternalArchiveLinkageArtifact artifact, CancellationToken ct = default);
    Task<List<ExternalArchivePersistedRecord>> GetRecordsByRunIdAsync(Guid runId, CancellationToken ct = default);
    Task<List<ExternalArchivePersistedRecord>> GetRecentRecordsByCaseSourceAsync(
        long caseId,
        string sourceClass,
        long? chatId,
        DateTime asOfUtc,
        int limit = 200,
        CancellationToken ct = default);
    Task<List<ExternalArchiveLinkageArtifact>> GetLinkageArtifactsByRunIdAsync(Guid runId, CancellationToken ct = default);
}

public interface IBudgetOpsRepository
{
    Task UpsertBudgetOperationalStateAsync(BudgetOperationalState state, CancellationToken ct = default);
    Task<BudgetOperationalState?> GetBudgetOperationalStateAsync(string pathKey, CancellationToken ct = default);
    Task<List<BudgetOperationalState>> GetBudgetOperationalStatesAsync(CancellationToken ct = default);
}

public interface IEvalRepository
{
    Task<EvalRunResult> CreateRunAsync(EvalRunResult run, CancellationToken ct = default);
    Task<EvalScenarioResult> AddScenarioResultAsync(EvalScenarioResult result, CancellationToken ct = default);
    Task CompleteRunAsync(Guid runId, bool passed, string summary, string metricsJson, DateTime finishedAt, CancellationToken ct = default);
    Task<EvalRunResult?> GetRunByIdAsync(Guid runId, CancellationToken ct = default);
    Task<List<EvalScenarioResult>> GetScenarioResultsAsync(Guid runId, CancellationToken ct = default);
    Task<EvalRunResult?> GetLatestRunByNameAsync(string runName, CancellationToken ct = default);
    Task<List<EvalRunResult>> GetRecentRunsAsync(int limit = 20, CancellationToken ct = default);
}

public class EntityMergeCandidate
{
    public long Id { get; set; }
    public Guid EntityLowId { get; set; }
    public Guid EntityHighId { get; set; }
    public string AliasNorm { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public float Score { get; set; }
    public short ReviewPriority { get; set; }
    public short Status { get; set; }
}

public class EntityMergeReviewItem
{
    public long CandidateId { get; set; }
    public Guid EntityLowId { get; set; }
    public Guid EntityHighId { get; set; }
    public string EntityLowName { get; set; } = string.Empty;
    public string EntityHighName { get; set; } = string.Empty;
    public string AliasNorm { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public float Score { get; set; }
    public short ReviewPriority { get; set; }
}

public class EntityMergeCommand
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public enum MergeDecision : short
{
    Pending = 0,
    Merged = 1,
    Rejected = 2
}

public class Stage5MetricsSnapshot
{
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
}

public class MaintenanceCleanupRequest
{
    public int ExtractionErrorsRetentionDays { get; set; }
    public int Stage5MetricsRetentionDays { get; set; }
    public int MergeDecisionsRetentionDays { get; set; }
    public int FactReviewCommandsRetentionDays { get; set; }
    public int FactReviewPendingTimeoutDays { get; set; }
    public bool FactDecayEnabled { get; set; } = true;
}

public class MaintenanceCleanupResult
{
    public int ExtractionErrorsDeleted { get; set; }
    public int Stage5MetricsDeleted { get; set; }
    public int MergeDecisionsDeleted { get; set; }
    public int FactReviewCommandsDeleted { get; set; }
    public int FactReviewCommandsTimedOut { get; set; }
    public int FactsExpired { get; set; }
}

public class FactReviewCommand
{
    public long Id { get; set; }
    public Guid FactId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class AnalysisUsageEvent
{
    public string Phase { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
