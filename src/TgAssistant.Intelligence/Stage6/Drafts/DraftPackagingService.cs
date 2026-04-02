// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Drafts;

public class DraftPackagingService : IDraftPackagingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;

    public DraftPackagingService(
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService)
    {
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
    }

    public async Task<DraftRecord> PersistAsync(
        DraftGenerationContext context,
        DraftStyledContent styled,
        DraftConflictAssessment conflict,
        CancellationToken ct = default)
    {
        var sourceMessage = context.RecentMessages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var sourceSession = context.RecentSessions.OrderByDescending(x => x.EndDate).FirstOrDefault();
        var styleNotes = conflict.HasConflict
            ? $"{styled.StyleNotes}; conflict={conflict.Reason}"
            : styled.StyleNotes;

        var alt2 = conflict.HasConflict && !string.IsNullOrWhiteSpace(conflict.RiskyIntentAlternative)
            ? conflict.RiskyIntentAlternative
            : styled.AltDraft2;

        var confidence = conflict.HasConflict
            ? Math.Clamp(styled.Confidence - 0.1f, 0f, 1f)
            : styled.Confidence;

        var record = await _strategyDraftRepository.CreateDraftRecordAsync(new DraftRecord
        {
            StrategyRecordId = context.StrategyRecord.Id,
            SourceSessionId = sourceSession?.Id,
            SourceMessageId = sourceMessage?.Id,
            MainDraft = styled.MainDraft,
            AltDraft1 = styled.AltDraft1,
            AltDraft2 = alt2,
            StyleNotes = styleNotes,
            Confidence = confidence,
            CreatedAt = DateTime.UtcNow
        }, ct);

        Guid? conflictRecordId = null;
        if (conflict.HasConflict)
        {
            var conflictRecord = await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
            {
                CaseId = context.CaseId,
                ChatId = context.ChatId,
                PeriodId = context.CurrentPeriod?.Id,
                ConflictType = "draft_intent_vs_strategy",
                ObjectAType = "user_notes",
                ObjectAId = $"case:{context.CaseId}:strategy:{context.StrategyRecord.Id}",
                ObjectBType = "strategy_record",
                ObjectBId = context.StrategyRecord.Id.ToString(),
                Summary = conflict.Reason,
                Severity = "medium",
                Status = "open",
                LastActor = "draft_engine",
                LastReason = "conflict detected during draft generation"
            }, ct);
            conflictRecordId = conflictRecord.Id;
        }

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "draft_record",
            ObjectId = record.Id.ToString(),
            Action = "draft_generated",
            NewValueRef = JsonSerializer.Serialize(new
            {
                record.StrategyRecordId,
                record.Confidence,
                has_alt_1 = !string.IsNullOrWhiteSpace(record.AltDraft1),
                has_alt_2 = !string.IsNullOrWhiteSpace(record.AltDraft2),
                conflict = conflict.HasConflict,
                conflict_record_id = conflictRecordId
            }, JsonOptions),
            Reason = "draft_engine",
            Actor = "draft_engine",
            CreatedAt = DateTime.UtcNow
        }, ct);

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            context.CaseId,
            context.ChatId,
            Stage6ArtifactTypes.Draft,
            ct);
        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.Draft,
            CaseId = context.CaseId,
            ChatId = context.ChatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(context.ChatId),
            PayloadObjectType = "draft_record",
            PayloadObjectId = record.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new
            {
                record.Id,
                record.MainDraft,
                record.AltDraft1,
                record.AltDraft2,
                softer_alternative = record.AltDraft1,
                more_direct_alternative = record.AltDraft2,
                record.Confidence,
                strategy_record_id = record.StrategyRecordId
            }, JsonOptions),
            FreshnessBasisHash = evidence.BasisHash,
            FreshnessBasisJson = evidence.BasisJson,
            GeneratedAt = record.CreatedAt,
            RefreshedAt = record.CreatedAt,
            StaleAt = record.CreatedAt.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.Draft)),
            IsStale = false,
            SourceType = "draft_engine",
            SourceId = "draft_generated",
            SourceMessageId = record.SourceMessageId,
            SourceSessionId = record.SourceSessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return record;
    }
}
