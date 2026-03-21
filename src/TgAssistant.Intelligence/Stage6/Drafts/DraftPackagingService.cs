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

    public DraftPackagingService(
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository,
        IDomainReviewEventRepository domainReviewEventRepository)
    {
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
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

        return record;
    }
}
