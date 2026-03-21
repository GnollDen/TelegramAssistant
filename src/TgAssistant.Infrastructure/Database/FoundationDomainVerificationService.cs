using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public class FoundationDomainVerificationService
{
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<FoundationDomainVerificationService> _logger;

    public FoundationDomainVerificationService(
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IStateProfileRepository stateProfileRepository,
        IOfflineEventRepository offlineEventRepository,
        IInboxConflictRepository inboxConflictRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<FoundationDomainVerificationService> logger)
    {
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _stateProfileRepository = stateProfileRepository;
        _offlineEventRepository = offlineEventRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Keep case scope explicit: case_id is analysis scope, chat_id is canonical source anchor.
        var caseScope = CaseScopeFactory.CreateSmokeScope("foundation");
        var caseId = caseScope.CaseId;
        var chatId = caseScope.ChatId;
        var now = DateTime.UtcNow;

        var period = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "baseline",
            StartAt = now,
            IsOpen = true,
            Summary = "foundation smoke period",
            WhatHelped = string.Empty,
            WhatHurt = string.Empty,
            StatusSnapshot = "unknown",
            DynamicSnapshot = "neutral",
            SourceType = "smoke",
            SourceId = "foundation-domain"
        }, ct);

        var question = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            QuestionText = "smoke question",
            QuestionType = "confirmation",
            Priority = "important",
            Status = "open",
            WhyItMatters = "smoke verification",
            SourceType = "smoke",
            SourceId = "foundation-domain"
        }, ct);

        var snapshot = await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            AsOf = now,
            DynamicLabel = "steady",
            RelationshipStatus = "undetermined",
            InitiativeScore = 0.5f,
            ResponsivenessScore = 0.5f,
            OpennessScore = 0.5f,
            WarmthScore = 0.5f,
            ReciprocityScore = 0.5f,
            AmbiguityScore = 0.5f,
            AvoidanceRiskScore = 0.5f,
            EscalationReadinessScore = 0.5f,
            ExternalPressureScore = 0.5f,
            Confidence = 0.5f
        }, ct);

        var offlineEvent = await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            EventType = "smoke",
            Title = "foundation smoke offline event",
            UserSummary = "offline smoke summary",
            TimestampStart = now,
            PeriodId = period.Id,
            ReviewStatus = "pending",
            SourceType = "smoke",
            SourceId = "foundation-domain"
        }, ct);

        var hypothesis = await _periodRepository.CreateHypothesisAsync(new Hypothesis
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            HypothesisType = "signal_interpretation",
            SubjectType = "chat",
            SubjectId = caseId.ToString(),
            Statement = "smoke hypothesis",
            Confidence = 0.4f,
            Status = "open",
            SourceType = "smoke",
            SourceId = "foundation-domain"
        }, ct);

        var inboxItem = await _inboxConflictRepository.CreateInboxItemAsync(new InboxItem
        {
            CaseId = caseId,
            ChatId = chatId,
            ItemType = "clarification",
            SourceObjectType = "clarification_question",
            SourceObjectId = question.Id.ToString(),
            Title = "smoke inbox item",
            Summary = "smoke inbox summary",
            Status = "open"
        }, ct);

        var conflictRecord = await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
        {
            CaseId = caseId,
            ChatId = chatId,
            ConflictType = "contradiction",
            ObjectAType = "hypothesis",
            ObjectAId = hypothesis.Id.ToString(),
            ObjectBType = "clarification_answer",
            ObjectBId = "smoke",
            Summary = "smoke conflict",
            Status = "open"
        }, ct);

        var periodUpdated = await _periodRepository.UpdatePeriodLifecycleAsync(
            period.Id,
            "baseline-updated",
            "foundation smoke period updated",
            false,
            now,
            1,
            "smoke-runner",
            "period lifecycle update",
            ct);
        var hypothesisUpdated = await _periodRepository.UpdateHypothesisLifecycleAsync(
            hypothesis.Id,
            "supported",
            0.75f,
            "smoke-runner",
            "hypothesis lifecycle update",
            ct);
        var questionUpdated = await _clarificationRepository.UpdateQuestionWorkflowAsync(
            question.Id,
            "in_progress",
            "blocking",
            "smoke-runner",
            "question workflow update",
            ct);
        var answerApplied = await _clarificationRepository.ApplyAnswerAsync(
            question.Id,
            new ClarificationAnswer
            {
                AnswerType = "text",
                AnswerValue = "smoke answer",
                AnswerConfidence = 0.8f,
                SourceClass = "manual",
                SourceType = "smoke",
                SourceId = "foundation-domain"
            },
            markResolved: true,
            actor: "smoke-runner",
            reason: "answer application",
            ct: ct);
        var inboxUpdated = await _inboxConflictRepository.UpdateInboxItemStatusAsync(
            inboxItem.Id,
            "resolved",
            "smoke-runner",
            "inbox resolved",
            ct);
        var conflictUpdated = await _inboxConflictRepository.UpdateConflictStatusAsync(
            conflictRecord.Id,
            "resolved",
            "smoke-runner",
            "conflict resolved",
            ct);

        var periodRead = await _periodRepository.GetPeriodByIdAsync(period.Id, ct);
        var questionRead = await _clarificationRepository.GetQuestionByIdAsync(question.Id, ct);
        var snapshotRead = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshot.Id, ct);
        var offlineRead = await _offlineEventRepository.GetOfflineEventByIdAsync(offlineEvent.Id, ct);
        var reviewEvents = await _domainReviewEventRepository.GetByObjectAsync("clarification_question", question.Id.ToString(), 10, ct);

        if (periodRead == null ||
            questionRead == null ||
            snapshotRead == null ||
            offlineRead == null ||
            !periodUpdated ||
            !hypothesisUpdated ||
            !questionUpdated ||
            !inboxUpdated ||
            !conflictUpdated ||
            answerApplied.Id == Guid.Empty ||
            reviewEvents.Count == 0)
        {
            throw new InvalidOperationException("Foundation domain smoke verification failed: create/read/update or review trail check did not pass.");
        }

        _logger.LogInformation(
            "Foundation domain smoke passed. period={PeriodId}, question={QuestionId}, snapshot={SnapshotId}, offline_event={OfflineEventId}, review_events={ReviewEventCount}",
            periodRead.Id,
            questionRead.Id,
            snapshotRead.Id,
            offlineRead.Id,
            reviewEvents.Count);
    }
}
