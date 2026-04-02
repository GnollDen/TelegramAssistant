using TgAssistant.Core.Legacy.Models;
using TgAssistant.Core.Models;

namespace TgAssistant.Core.Legacy.Interfaces;

// Legacy repository contracts over frozen domain_* and stage6_* tables.
// These remain available for cleanup, diagnostics, and controlled migration work only.
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

public interface IStage6ArtifactRepository
{
    Task<Stage6ArtifactRecord> UpsertCurrentAsync(Stage6ArtifactRecord artifact, CancellationToken ct = default);
    Task<Stage6ArtifactRecord?> GetCurrentAsync(
        long caseId,
        long? chatId,
        string artifactType,
        string scopeKey,
        CancellationToken ct = default);
    Task<bool> MarkStaleAsync(Guid artifactId, string reason, DateTime staleAtUtc, CancellationToken ct = default);
    Task<bool> TouchReusedAsync(Guid artifactId, DateTime reusedAtUtc, CancellationToken ct = default);
}

public interface IStage6CaseRepository
{
    Task<Stage6CaseRecord> UpsertAsync(Stage6CaseRecord record, CancellationToken ct = default);
    Task<Stage6CaseRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Stage6CaseRecord?> GetBySourceAsync(
        long scopeCaseId,
        string caseType,
        string sourceObjectType,
        string sourceObjectId,
        CancellationToken ct = default);
    Task<List<Stage6CaseRecord>> GetCasesAsync(
        long scopeCaseId,
        string? status = null,
        string? caseType = null,
        CancellationToken ct = default);
    Task<List<Stage6ScopeCandidate>> GetScopeCandidatesAsync(int limit = 10, CancellationToken ct = default);
    Task<bool> UpdateStatusAsync(Guid id, string status, string actor, string? reason = null, CancellationToken ct = default);
    Task<Stage6CaseLink> UpsertLinkAsync(Stage6CaseLink link, CancellationToken ct = default);
    Task<List<Stage6CaseLink>> GetLinksAsync(Guid stage6CaseId, CancellationToken ct = default);
}

public interface IStage6UserContextRepository
{
    Task<Stage6UserContextEntry> CreateAsync(Stage6UserContextEntry entry, CancellationToken ct = default);
    Task<List<Stage6UserContextEntry>> GetByScopeCaseAsync(long scopeCaseId, int limit = 200, CancellationToken ct = default);
}

public interface IStage6FeedbackRepository
{
    Task<Stage6FeedbackEntry> AddAsync(Stage6FeedbackEntry entry, CancellationToken ct = default);
    Task<List<Stage6FeedbackEntry>> GetByCaseAsync(Guid stage6CaseId, int limit = 100, CancellationToken ct = default);
    Task<List<Stage6FeedbackEntry>> GetByArtifactAsync(
        long scopeCaseId,
        long? chatId,
        string artifactType,
        int limit = 100,
        CancellationToken ct = default);
}

public interface IStage6CaseOutcomeRepository
{
    Task<Stage6CaseOutcomeRecord> AddAsync(Stage6CaseOutcomeRecord record, CancellationToken ct = default);
    Task<List<Stage6CaseOutcomeRecord>> GetByCaseAsync(Guid stage6CaseId, int limit = 100, CancellationToken ct = default);
    Task<List<Stage6CaseOutcomeRecord>> GetByScopeAsync(long scopeCaseId, long? chatId, int limit = 300, CancellationToken ct = default);
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
