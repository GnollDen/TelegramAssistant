using TgAssistant.Core.Legacy.Models;
using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

// Active baseline repository contracts only. Frozen Stage6/domain repository contracts live in
// TgAssistant.Core.Legacy.Interfaces and are not the canonical Phase B architecture surface.
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
    Task<List<Message>> GetProcessedByChatAndTimeRangeAsync(long chatId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<List<Message>> GetProcessedByChatAsync(long chatId, int limit, CancellationToken ct = default);
    Task<List<Message>> GetNeedsReanalysisAsync(int limit, CancellationToken ct = default);
    Task<long> CountNeedsReanalysisProcessedAsync(CancellationToken ct = default);
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
    Task<List<Message>> GetPendingVoiceParalinguisticsAsync(int limit, DateTime? minCreatedAtUtc = null, CancellationToken ct = default);
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

public interface IRealtimeMessageSubstrateRepository
{
    Task UpsertRealtimeBatchAsync(IReadOnlyCollection<Message> messages, CancellationToken ct = default);
}

public interface IArchiveMessageSubstrateRepository
{
    Task UpsertArchiveBatchAsync(
        IReadOnlyCollection<Message> messages,
        Guid archiveImportRunId,
        string sourcePath,
        CancellationToken ct = default);
}

public interface IModelOutputNormalizer
{
    ModelNormalizationResult Normalize(ModelNormalizationRequest request);
}

public interface IModelPassAuditStore
{
    Task<ModelPassAuditRecord> UpsertAsync(
        ModelPassEnvelope envelope,
        ModelNormalizationResult normalizationResult,
        CancellationToken ct = default);

    Task<ModelPassAuditRecord?> GetByModelPassRunIdAsync(Guid runId, CancellationToken ct = default);

    Task<int> GetConsecutiveNeedMoreDataCountAsync(
        string scopeKey,
        string stage,
        string passFamily,
        CancellationToken ct = default);
}

public interface IModelPassAuditService
{
    Task<ModelPassAuditRecord> NormalizeAndPersistAsync(ModelNormalizationRequest request, CancellationToken ct = default);
}

public interface IClarificationBranchStateRepository
{
    Task<ClarificationBranchStateRecord?> ApplyOutcomeAsync(
        ModelPassAuditRecord record,
        CancellationToken ct = default);

    Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAsync(
        string scopeKey,
        CancellationToken ct = default);

    Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAndFamilyAsync(
        string scopeKey,
        string branchFamily,
        CancellationToken ct = default);
}

public interface IIdentityMergeRepository
{
    Task<IdentityMergeRecord> ExecuteMergeAsync(
        IdentityMergeApplyRequest request,
        CancellationToken ct = default);

    Task<IdentityMergeRecord?> GetByIdAsync(
        Guid mergeId,
        CancellationToken ct = default);

    Task<IdentityMergeRecord> ReverseAsync(
        IdentityMergeReverseRequest request,
        CancellationToken ct = default);
}

public interface IStage6BootstrapRepository
{
    Task<Stage6BootstrapScopeResolution> ResolveScopeAsync(Stage6BootstrapRequest request, CancellationToken ct = default);

    Task<Stage6BootstrapGraphResult> UpsertGraphInitializationAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapScopeResolution resolution,
        CancellationToken ct = default);

    Task<List<Stage6BootstrapDiscoveryOutput>> UpsertDiscoveryOutputsAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapScopeResolution resolution,
        CancellationToken ct = default);

    Task<Stage6BootstrapPoolOutputSet> UpsertPoolOutputsAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapScopeResolution resolution,
        CancellationToken ct = default);
}

public interface IStage6BootstrapService
{
    Task<Stage6BootstrapGraphResult> RunGraphInitializationAsync(Stage6BootstrapRequest request, CancellationToken ct = default);
}

public interface IStage7DossierProfileRepository
{
    Task<Stage7DossierProfileFormationResult> UpsertAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        CancellationToken ct = default);
}

public interface IStage7DossierProfileService
{
    Task<Stage7DossierProfileFormationResult> FormAsync(
        Stage7DossierProfileFormationRequest request,
        CancellationToken ct = default);
}

public interface IStage7PairDynamicsRepository
{
    Task<Stage7PairDynamicsFormationResult> UpsertAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        CancellationToken ct = default);
}

public interface IStage7PairDynamicsService
{
    Task<Stage7PairDynamicsFormationResult> FormAsync(
        Stage7PairDynamicsFormationRequest request,
        CancellationToken ct = default);
}

public interface IStage7TimelineRepository
{
    Task<Stage7TimelineFormationResult> UpsertAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        CancellationToken ct = default);
}

public interface IStage7TimelineService
{
    Task<Stage7TimelineFormationResult> FormAsync(
        Stage7TimelineFormationRequest request,
        CancellationToken ct = default);
}

public interface IStage8RecomputeQueueRepository
{
    Task<Stage8RecomputeQueueItem> EnqueueAsync(
        Stage8RecomputeQueueRequest request,
        CancellationToken ct = default);

    Task<Stage8RecomputeQueueItem?> LeaseNextAsync(
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default);

    Task<Stage8RecomputeQueueItem?> LeaseNextBackfillAsync(
        DateTime nowUtc,
        TimeSpan leaseDuration,
        int maxConcurrentScopes,
        string workerId,
        CancellationToken ct = default);

    Task CompleteAsync(
        Guid queueItemId,
        Guid leaseToken,
        string resultStatus,
        Guid? modelPassRunId,
        CancellationToken ct = default);

    Task RescheduleAsync(
        Guid queueItemId,
        Guid leaseToken,
        string error,
        DateTime nextAvailableAtUtc,
        bool terminalFailure,
        Stage8BackfillRecoveryTelemetry? recoveryTelemetry = null,
        CancellationToken ct = default);

    Task<Stage8BackfillCheckpoint?> GetBackfillCheckpointAsync(
        string scopeKey,
        CancellationToken ct = default);
}

public interface IStage8OutcomeGateRepository
{
    Task<Stage8OutcomeGateResult> ApplyOutcomeGateAsync(
        Stage8OutcomeGateRequest request,
        CancellationToken ct = default);
}

public interface IStage8RelatedConflictRepository
{
    Task<Stage8RelatedConflictReevaluationResult> ReevaluateAsync(
        Stage8RelatedConflictReevaluationRequest request,
        CancellationToken ct = default);
}

public interface IRuntimeDefectRepository
{
    Task<RuntimeDefectRecord> UpsertAsync(
        RuntimeDefectUpsertRequest request,
        CancellationToken ct = default);

    Task<List<RuntimeDefectRecord>> GetOpenAsync(
        int limit = 200,
        CancellationToken ct = default);

    Task<int> ResolveOpenByDedupeKeyAsync(
        string dedupeKey,
        Guid? runId = null,
        CancellationToken ct = default);
}

public interface IRuntimeControlStateRepository
{
    Task<RuntimeControlStateRecord?> GetActiveAsync(CancellationToken ct = default);

    Task<RuntimeControlStateRecord> SetActiveAsync(
        string state,
        string reason,
        string source,
        string detailsJson,
        CancellationToken ct = default);
}

public interface IRuntimeControlStateService
{
    Task<RuntimeControlEnforcementDecision> EvaluateAndApplyFromDefectsAsync(CancellationToken ct = default);
}

public interface IStage8RecomputeQueueService
{
    Task<Stage8RecomputeQueueItem> EnqueueAsync(
        Stage8RecomputeQueueRequest request,
        CancellationToken ct = default);

    Task<Stage8RecomputeExecutionResult> ExecuteNextAsync(CancellationToken ct = default);

    Task<Stage8BackfillExecutionResult> ExecuteBackfillBatchAsync(
        Stage8BackfillExecutionRequest request,
        CancellationToken ct = default);
}

public interface IResolutionReadService
{
    Task<ResolutionQueueResult> GetQueueAsync(
        ResolutionQueueRequest request,
        CancellationToken ct = default);

    Task<ResolutionDetailResult> GetDetailAsync(
        ResolutionDetailRequest request,
        CancellationToken ct = default);
}

public interface IResolutionActionService
{
    Task<ResolutionActionResult> SubmitAsync(
        ResolutionActionRequest request,
        CancellationToken ct = default);
}

public interface IOperatorResolutionApplicationService
{
    Task<OperatorTrackedPersonQueryResult> QueryTrackedPersonsAsync(
        OperatorTrackedPersonQueryRequest request,
        CancellationToken ct = default);

    Task<OperatorTrackedPersonSelectionResult> SelectTrackedPersonAsync(
        OperatorTrackedPersonSelectionRequest request,
        CancellationToken ct = default);

    Task<OperatorResolutionQueueQueryResult> GetResolutionQueueAsync(
        OperatorResolutionQueueQueryRequest request,
        CancellationToken ct = default);

    Task<OperatorResolutionDetailQueryResult> GetResolutionDetailAsync(
        OperatorResolutionDetailQueryRequest request,
        CancellationToken ct = default);

    Task<OperatorResolutionActionResultEnvelope> SubmitResolutionActionAsync(
        ResolutionActionRequest request,
        CancellationToken ct = default);
}

public interface IOperatorAssistantResponseGenerationService
{
    OperatorAssistantResponseEnvelope BuildResponse(
        OperatorAssistantResponseGenerationRequest request,
        DateTime? generatedAtUtc = null);

    IReadOnlyList<string> Validate(OperatorAssistantResponseEnvelope response);

    string RenderTelegram(OperatorAssistantResponseEnvelope response);
}

public interface IOperatorSessionAuditService
{
    Task<Guid> RecordSessionEventAsync(
        OperatorSessionAuditRequest request,
        CancellationToken ct = default);
}

public interface IStage8RecomputeTriggerService
{
    Task HandleSignalAsync(Stage8RecomputeTriggerSignal signal, CancellationToken ct = default);

    Task HandleDomainReviewEventAsync(DomainReviewEvent evt, CancellationToken ct = default);
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
    Task MarkNeedsAnalysisAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default);
    Task MarkFinalizedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default);
    Task<long> CountPendingAnalysisSessionsAsync(DateTime staleBeforeUtc, CancellationToken ct = default);
    Task<bool> TryUpdateSummaryIfShapeUnchangedAsync(
        Guid sessionId,
        long chatId,
        int sessionIndex,
        DateTime expectedStartDate,
        DateTime expectedEndDate,
        DateTime expectedLastMessageAt,
        string summary,
        CancellationToken ct = default);
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
    Task<int> ReleaseQuarantineForRetryAsync(
        string reason,
        DateTime quarantinedBeforeUtc,
        int limit,
        CancellationToken ct = default);
    Task<QuarantineMetrics> GetQuarantineMetricsAsync(DateTime stuckBeforeUtc, CancellationToken ct = default);
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
    Task<AnalysisUsageWindowSummary> SummarizeWindowAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
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

public interface IChatCoordinationService
{
    Task<Dictionary<long, ChatCoordinationState>> EnsureStatesAsync(
        IReadOnlyCollection<long> monitoredChatIds,
        IReadOnlyCollection<long> backfillChatIds,
        bool backfillEnabled,
        int handoverPendingExtractionThreshold,
        CancellationToken ct = default);
    Task<HashSet<long>> ResolveRealtimeEligibleChatIdsAsync(
        IReadOnlyCollection<long> monitoredChatIds,
        IReadOnlyCollection<long> backfillChatIds,
        bool backfillEnabled,
        int handoverPendingExtractionThreshold,
        CancellationToken ct = default);
    Task MarkBackfillStartedAsync(long chatId, CancellationToken ct = default);
    Task MarkBackfillCompletedAsync(
        long chatId,
        int handoverPendingExtractionThreshold,
        CancellationToken ct = default);
    Task MarkBackfillDegradedAsync(long chatId, string reason, CancellationToken ct = default);
    Task TouchRealtimeHeartbeatAsync(IReadOnlyCollection<long> realtimeChatIds, CancellationToken ct = default);
    Task<ChatPhaseGuardDecision> TryAcquirePhaseAsync(
        long chatId,
        string requestedPhase,
        string ownerId,
        string? reason = null,
        DateTime? reopenWindowFromUtc = null,
        DateTime? reopenWindowToUtc = null,
        string? reopenOperator = null,
        string? reopenAuditId = null,
        CancellationToken ct = default);
    Task<ChatPhaseLeaseRenewDecision> TryRenewPhaseLeaseAsync(
        long chatId,
        string phase,
        string ownerId,
        string? reason = null,
        CancellationToken ct = default);
    Task<ChatPhaseReleaseResult> ReleasePhaseAsync(
        long chatId,
        string phase,
        string ownerId,
        string? reason = null,
        CancellationToken ct = default);
    Task RecordBackupEvidenceAsync(
        BackupMetadataEvidence evidence,
        string recordedBy,
        CancellationToken ct = default);
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

public sealed class QuarantineMetrics
{
    public long Total { get; set; }
    public long Stuck { get; set; }
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
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AnalysisUsageWindowSummary
{
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int TotalRows { get; set; }
    public decimal TotalCostUsd { get; set; }
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public double AverageLatencyMs { get; set; }
    public Dictionary<string, decimal> CostByModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> RowsByPhaseModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> AvgLatencyByPhaseModel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
