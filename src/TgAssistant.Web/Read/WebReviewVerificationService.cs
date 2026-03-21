using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebReviewVerificationService
{
    private readonly IWebRouteRenderer _webRouteRenderer;
    private readonly IWebReviewService _webReviewService;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;

    public WebReviewVerificationService(
        IWebRouteRenderer webRouteRenderer,
        IWebReviewService webReviewService,
        IDomainReviewEventRepository domainReviewEventRepository,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IClarificationRepository clarificationRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository)
    {
        _webRouteRenderer = webRouteRenderer;
        _webReviewService = webReviewService;
        _domainReviewEventRepository = domainReviewEventRepository;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _clarificationRepository = clarificationRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var baseId = 9_400_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L);
        var caseId = baseId;
        var chatId = baseId;

        await SeedMessagesAndSessionsAsync(chatId, now, ct);

        var period = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "review_target_period",
            StartAt = now.AddDays(-12),
            EndAt = now.AddDays(-2),
            IsOpen = false,
            Summary = "initial period summary for review smoke",
            ReviewPriority = 5,
            InterpretationConfidence = 0.63f,
            BoundaryConfidence = 0.59f,
            SourceType = "smoke",
            SourceId = "web_review",
            EvidenceRefsJson = "[\"msg:101\",\"msg:102\"]"
        }, ct);

        var nextPeriod = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "review_target_period_next",
            StartAt = now.AddDays(-2),
            EndAt = null,
            IsOpen = true,
            Summary = "next period summary",
            ReviewPriority = 4,
            InterpretationConfidence = 0.57f,
            BoundaryConfidence = 0.52f,
            SourceType = "smoke",
            SourceId = "web_review",
            EvidenceRefsJson = "[\"msg:201\"]"
        }, ct);

        var transition = await _periodRepository.CreateTransitionAsync(new PeriodTransition
        {
            FromPeriodId = period.Id,
            ToPeriodId = nextPeriod.Id,
            TransitionType = "dynamic_shift",
            Summary = "transition pending review",
            IsResolved = false,
            Confidence = 0.47f,
            SourceType = "smoke",
            SourceId = "web_review"
        }, ct);

        var confirmQuestion = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "Confirm this clarification is now resolved?",
            QuestionType = "state",
            Priority = "important",
            Status = "open",
            WhyItMatters = "Used for confirm path",
            ExpectedGain = 0.72f,
            SourceType = "smoke",
            SourceId = "web_review"
        }, ct);

        var rejectQuestion = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "Reject this clarification for now?",
            QuestionType = "timeline",
            Priority = "blocking",
            Status = "open",
            WhyItMatters = "Used for reject path",
            ExpectedGain = 0.66f,
            SourceType = "smoke",
            SourceId = "web_review"
        }, ct);

        var conflict = await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
        {
            CaseId = caseId,
            ChatId = chatId,
            ConflictType = "answer_vs_state",
            ObjectAType = "clarification_answer",
            ObjectAId = confirmQuestion.Id.ToString(),
            ObjectBType = "state_snapshot",
            ObjectBId = "state-smoke",
            Summary = "conflict record for review defer path",
            Severity = "high",
            Status = "open",
            LastActor = "seed",
            LastReason = "seeded for review smoke"
        }, ct);

        var profile = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            SubjectType = "self",
            SubjectId = "1",
            CaseId = caseId,
            ChatId = chatId,
            Summary = "profile snapshot review target",
            Confidence = 0.61f,
            Stability = 0.44f,
            SourceMessageId = null,
            SourceSessionId = null
        }, ct);
        _ = await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = profile.Id,
            TraitKey = "initiative_balance",
            ValueLabel = "slightly_waiting",
            Confidence = 0.58f,
            Stability = 0.41f,
            IsSensitive = false,
            EvidenceRefsJson = "[\"msg:301\"]"
        }, ct);

        var strategy = await _strategyDraftRepository.CreateStrategyRecordAsync(new StrategyRecord
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = nextPeriod.Id,
            StrategyConfidence = 0.62f,
            RecommendedGoal = "maintain calm, low-pressure contact",
            WhyNotOthers = "higher-pressure options are risky under ambiguity",
            MicroStep = "send one calm check-in later today",
            HorizonJson = "[\"check-in\",\"observe tone\"]"
        }, ct);
        _ = await _strategyDraftRepository.CreateStrategyOptionAsync(new StrategyOption
        {
            StrategyRecordId = strategy.Id,
            ActionType = "check_in",
            Summary = "short calm check-in",
            Purpose = "keep thread warm without pressure",
            Risk = "{\"labels\":[\"timing_mismatch\"]}",
            WhenToUse = "when tone is stable",
            SuccessSigns = "short positive reply",
            FailureSigns = "cold/no reply",
            IsPrimary = true
        }, ct);
        _ = await _strategyDraftRepository.CreateDraftRecordAsync(new DraftRecord
        {
            StrategyRecordId = strategy.Id,
            MainDraft = "Hey, no rush to reply. Just wishing you a calm day.",
            AltDraft1 = "Quick check-in: hope your day is light.",
            AltDraft2 = "No pressure, just saying hi.",
            StyleNotes = "concise warm",
            Confidence = 0.60f
        }, ct);

        var request = new WebReadRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "web_review_smoke"
        };

        var reviewPage = await _webRouteRenderer.RenderAsync("/review", request, ct)
            ?? throw new InvalidOperationException("Web review smoke failed: /review route did not resolve.");
        if (string.IsNullOrWhiteSpace(reviewPage.Html) || !reviewPage.Html.Contains("Review Board", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: /review page is empty.");
        }

        var confirmResult = await _webReviewService.ApplyActionAsync(new WebReviewActionRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            ObjectType = "clarification_question",
            ObjectId = confirmQuestion.Id.ToString(),
            Action = "confirm",
            Actor = "web_review_smoke",
            Reason = "confirm path smoke"
        }, ct);
        if (!confirmResult.Success)
        {
            throw new InvalidOperationException("Web review smoke failed: confirm action did not succeed.");
        }

        var confirmUpdated = await _clarificationRepository.GetQuestionByIdAsync(confirmQuestion.Id, ct);
        if (confirmUpdated == null || !confirmUpdated.Status.Equals("resolved", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: confirm path did not update clarification status.");
        }

        var rejectResult = await _webReviewService.ApplyActionAsync(new WebReviewActionRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            ObjectType = "clarification_question",
            ObjectId = rejectQuestion.Id.ToString(),
            Action = "reject",
            Actor = "web_review_smoke",
            Reason = "reject path smoke"
        }, ct);
        if (!rejectResult.Success)
        {
            throw new InvalidOperationException("Web review smoke failed: reject action did not succeed.");
        }

        var rejectUpdated = await _clarificationRepository.GetQuestionByIdAsync(rejectQuestion.Id, ct);
        if (rejectUpdated == null || !rejectUpdated.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: reject path did not update clarification status.");
        }

        var deferResult = await _webReviewService.ApplyActionAsync(new WebReviewActionRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            ObjectType = "conflict_record",
            ObjectId = conflict.Id.ToString(),
            Action = "defer",
            Actor = "web_review_smoke",
            Reason = "defer path smoke"
        }, ct);
        if (!deferResult.Success)
        {
            throw new InvalidOperationException("Web review smoke failed: defer action did not succeed.");
        }

        var deferredConflict = await _inboxConflictRepository.GetConflictRecordByIdAsync(conflict.Id, ct);
        if (deferredConflict == null || !deferredConflict.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: defer path did not update conflict status.");
        }

        var editResult = await _webReviewService.EditPeriodAsync(new WebPeriodEditRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            Label = "review_target_period_edited",
            Summary = "edited summary from web review smoke",
            ReviewPriority = 2,
            Actor = "web_review_smoke",
            Reason = "edit path smoke"
        }, ct);
        if (!editResult.Success)
        {
            throw new InvalidOperationException("Web review smoke failed: period edit path did not succeed.");
        }

        var editedPeriod = await _periodRepository.GetPeriodByIdAsync(period.Id, ct);
        if (editedPeriod == null
            || !editedPeriod.Label.Equals("review_target_period_edited", StringComparison.Ordinal)
            || !editedPeriod.Summary.Contains("edited summary", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: period edit path did not persist changes.");
        }

        var actionRoute = await _webRouteRenderer.RenderAsync(
            $"/review-action?objectType=period_transition&objectId={transition.Id}&action=defer&reason=route-check",
            request,
            ct);
        if (actionRoute == null || !actionRoute.Html.Contains("Review Action", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: /review-action route did not resolve.");
        }

        var editRoute = await _webRouteRenderer.RenderAsync(
            $"/review-edit-period?periodId={period.Id}&label=route_edit_label&summary=route_edit_summary&reviewPriority=4&reason=route-check",
            request,
            ct);
        if (editRoute == null || !editRoute.Html.Contains("Review Action", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Web review smoke failed: /review-edit-period route did not resolve.");
        }

        var auditConfirm = await _domainReviewEventRepository.GetByObjectAsync("clarification_question", confirmQuestion.Id.ToString(), 20, ct);
        var auditReject = await _domainReviewEventRepository.GetByObjectAsync("clarification_question", rejectQuestion.Id.ToString(), 20, ct);
        var auditDefer = await _domainReviewEventRepository.GetByObjectAsync("conflict_record", conflict.Id.ToString(), 20, ct);
        var auditEdit = await _domainReviewEventRepository.GetByObjectAsync("period", period.Id.ToString(), 30, ct);

        if (!auditConfirm.Any(x => x.Action.Contains("web_review_confirm", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Web review smoke failed: confirm audit trail not found.");
        }

        if (!auditReject.Any(x => x.Action.Contains("web_review_reject", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Web review smoke failed: reject audit trail not found.");
        }

        if (!auditDefer.Any(x => x.Action.Contains("web_review_defer", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Web review smoke failed: defer audit trail not found.");
        }

        if (!auditEdit.Any(x => x.Action.Contains("web_review_edit", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Web review smoke failed: edit audit trail not found.");
        }
    }

    private async Task SeedMessagesAndSessionsAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var telegramId = 940_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var messages = new List<Message>();
        for (var i = 0; i < 24; i++)
        {
            messages.Add(new Message
            {
                TelegramMessageId = telegramId + i,
                ChatId = chatId,
                SenderId = i % 2 == 0 ? 1 : 2,
                SenderName = i % 2 == 0 ? "Self" : "Other",
                Timestamp = now.AddDays(-14).AddHours(i * 10),
                Text = i % 3 == 0 ? "calm check-in and low pressure" : "mixed rhythm but warming tone",
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = DateTime.UtcNow
            });
        }

        _ = await _messageRepository.SaveBatchAsync(messages, ct);

        var session = new ChatSession
        {
            ChatId = chatId,
            SessionIndex = (int)(Math.Abs(now.Ticks) % 100_000),
            StartDate = now.AddDays(-14),
            EndDate = now.AddDays(-10),
            LastMessageAt = now.AddDays(-10),
            Summary = "review smoke session",
            IsFinalized = true,
            IsAnalyzed = true
        };
        await _chatSessionRepository.UpsertAsync(session, ct);
    }
}
