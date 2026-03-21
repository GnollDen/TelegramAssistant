using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.DraftReview;

public class DraftReviewVerificationService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IDraftEngine _draftEngine;
    private readonly IDraftReviewEngine _draftReviewEngine;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<DraftReviewVerificationService> _logger;

    public DraftReviewVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyEngine strategyEngine,
        IDraftEngine draftEngine,
        IDraftReviewEngine draftReviewEngine,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<DraftReviewVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyEngine = strategyEngine;
        _draftEngine = draftEngine;
        _draftReviewEngine = draftReviewEngine;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var alignedScope = CaseScopeFactory.CreateSmokeScope("review_aligned");
        var alignedSelfSender = 5101L;
        var alignedOtherSender = 6101L;
        await SeedScenarioAsync(alignedScope.CaseId, alignedScope.ChatId, alignedSelfSender, alignedOtherSender, highAmbiguity: false, "brief_guarded", ct);

        var alignedStrategy = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = alignedScope.CaseId,
            ChatId = alignedScope.ChatId,
            SelfSenderId = alignedSelfSender,
            Actor = "review_smoke",
            SourceType = "smoke",
            SourceId = "review-aligned-strategy",
            Persist = true
        }, ct);

        var alignedDraft = await _draftEngine.RunAsync(new DraftEngineRequest
        {
            CaseId = alignedScope.CaseId,
            ChatId = alignedScope.ChatId,
            StrategyRecordId = alignedStrategy.Record.Id,
            SelfSenderId = alignedSelfSender,
            UserNotes = "keep it calm and concise",
            Actor = "review_smoke",
            SourceType = "smoke",
            SourceId = "review-aligned-draft",
            Persist = true
        }, ct);

        var alignedReview = await _draftReviewEngine.RunAsync(new DraftReviewRequest
        {
            CaseId = alignedScope.CaseId,
            ChatId = alignedScope.ChatId,
            DraftRecordId = alignedDraft.Record.Id,
            Actor = "review_smoke",
            SourceType = "smoke",
            SourceId = "review-aligned",
            Persist = true
        }, ct);

        ValidateReviewResult(alignedReview, expectConflict: false, "aligned");
        await ValidatePersistenceAsync(alignedReview.ReviewId, ct);

        var conflictScope = CaseScopeFactory.CreateSmokeScope("review_conflict");
        var conflictSelfSender = 7101L;
        var conflictOtherSender = 8101L;
        await SeedScenarioAsync(conflictScope.CaseId, conflictScope.ChatId, conflictSelfSender, conflictOtherSender, highAmbiguity: true, "detailed_expressive", ct);

        var conflictStrategy = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = conflictScope.CaseId,
            ChatId = conflictScope.ChatId,
            SelfSenderId = conflictSelfSender,
            Actor = "review_smoke",
            SourceType = "smoke",
            SourceId = "review-conflict-strategy",
            Persist = true
        }, ct);

        var conflictReview = await _draftReviewEngine.RunAsync(new DraftReviewRequest
        {
            CaseId = conflictScope.CaseId,
            ChatId = conflictScope.ChatId,
            StrategyRecordId = conflictStrategy.Record.Id,
            CandidateText = "Ответь сегодня и давай уже определимся с отношениями прямо сейчас.",
            Actor = "review_smoke",
            SourceType = "smoke",
            SourceId = "review-conflict",
            Persist = true
        }, ct);

        ValidateReviewResult(conflictReview, expectConflict: true, "conflict");
        await ValidatePersistenceAsync(conflictReview.ReviewId, ct);

        _logger.LogInformation(
            "Draft review smoke passed. aligned_case={AlignedCaseId}, aligned_review={AlignedReviewId}, conflict_case={ConflictCaseId}, conflict_review={ConflictReviewId}, conflict_labels={ConflictLabels}",
            alignedScope.CaseId,
            alignedReview.ReviewId,
            conflictScope.CaseId,
            conflictReview.ReviewId,
            string.Join(",", conflictReview.RiskLabels));
    }

    private static void ValidateReviewResult(DraftReviewResult result, bool expectConflict, string scenario)
    {
        if (result.ReviewId == Guid.Empty)
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): review id was not generated.");
        }

        if (string.IsNullOrWhiteSpace(result.Assessment))
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): assessment is missing.");
        }

        if (result.MainRisks.Count is < 1 or > 2)
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): expected 1-2 main risks.");
        }

        if (result.RiskLabels.Count == 0)
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): risk labels are missing.");
        }

        if (string.IsNullOrWhiteSpace(result.SaferRewrite) || string.IsNullOrWhiteSpace(result.NaturalRewrite))
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): rewrites are incomplete.");
        }

        if (result.SaferRewrite.Equals(result.NaturalRewrite, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): safer and natural rewrites are identical.");
        }

        if (expectConflict && !result.StrategyConflictDetected)
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): strategy-conflict path was not demonstrated.");
        }

        if (!expectConflict && result.StrategyConflictDetected)
        {
            throw new InvalidOperationException($"Draft review smoke failed ({scenario}): unexpected strategy conflict.");
        }
    }

    private async Task ValidatePersistenceAsync(Guid reviewId, CancellationToken ct)
    {
        var events = await _domainReviewEventRepository.GetByObjectAsync("draft_review", reviewId.ToString(), limit: 10, ct);
        if (events.Count == 0)
        {
            throw new InvalidOperationException("Draft review smoke failed: persisted review artifact not found in domain review history.");
        }
    }

    private async Task SeedScenarioAsync(
        long caseId,
        long chatId,
        long selfSenderId,
        long otherSenderId,
        bool highAmbiguity,
        string communicationStyle,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var period = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = highAmbiguity ? "uncertain_review_period" : "stable_review_period",
            StartAt = now.AddDays(-14),
            EndAt = null,
            IsOpen = true,
            Summary = highAmbiguity ? "mixed and fragile period" : "calm warm period",
            BoundaryConfidence = highAmbiguity ? 0.59f : 0.8f,
            InterpretationConfidence = highAmbiguity ? 0.51f : 0.79f,
            SourceType = "smoke",
            SourceId = "review"
        }, ct);

        var tgId = 9_200_000 + now.Second * 100 + (highAmbiguity ? 7 : 3);
        var messages = new List<Message>();
        void Add(DateTime ts, long senderId, string text)
        {
            messages.Add(new Message
            {
                TelegramMessageId = tgId++,
                ChatId = chatId,
                SenderId = senderId,
                SenderName = senderId == selfSenderId ? "Self" : "Other",
                Timestamp = ts,
                Text = text,
                Source = MessageSource.Archive,
                ProcessingStatus = ProcessingStatus.Processed,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (highAmbiguity)
        {
            Add(now.AddDays(-10), selfSenderId, "Мне важно понять, как лучше общаться дальше.");
            Add(now.AddDays(-9), otherSenderId, "Сейчас сложно, не уверена.");
            Add(now.AddDays(-6), selfSenderId, "Ок, без давления, когда будет ресурс.");
            Add(now.AddDays(-3), otherSenderId, "Спасибо, позже вернусь к этому.");
        }
        else
        {
            Add(now.AddDays(-10), selfSenderId, "Рад(а), что мы на связи, как ты?");
            Add(now.AddDays(-9), otherSenderId, "Все хорошо, спасибо. Тепло с тобой общаться.");
            Add(now.AddDays(-6), selfSenderId, "Если ок, можем спокойно продолжить на неделе.");
            Add(now.AddDays(-3), otherSenderId, "Да, с удовольствием.");
        }

        await _messageRepository.SaveBatchAsync(messages, ct);

        var sessionIndex = (int)(Math.Abs(now.Ticks) % 9000) + (highAmbiguity ? 30000 : 20000);
        await _chatSessionRepository.UpsertAsync(new ChatSession
        {
            ChatId = chatId,
            SessionIndex = sessionIndex,
            StartDate = now.AddDays(-10),
            EndDate = now.AddDays(-3),
            LastMessageAt = now.AddDays(-3),
            Summary = highAmbiguity ? "mixed pace" : "warm pace",
            IsFinalized = true,
            IsAnalyzed = true
        }, ct);

        await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            AsOf = now.AddHours(-5),
            DynamicLabel = highAmbiguity ? "uncertain_shift" : "stable",
            RelationshipStatus = highAmbiguity ? "ambiguous" : "warm_platonic",
            InitiativeScore = highAmbiguity ? 0.47f : 0.62f,
            ResponsivenessScore = highAmbiguity ? 0.41f : 0.68f,
            OpennessScore = highAmbiguity ? 0.43f : 0.66f,
            WarmthScore = highAmbiguity ? 0.5f : 0.74f,
            ReciprocityScore = highAmbiguity ? 0.44f : 0.69f,
            AmbiguityScore = highAmbiguity ? 0.73f : 0.32f,
            AvoidanceRiskScore = highAmbiguity ? 0.6f : 0.34f,
            EscalationReadinessScore = highAmbiguity ? 0.35f : 0.75f,
            ExternalPressureScore = highAmbiguity ? 0.57f : 0.35f,
            Confidence = highAmbiguity ? 0.5f : 0.84f,
            PeriodId = period.Id,
            KeySignalRefsJson = "[\"message:recent\",\"session:recent\"]",
            RiskRefsJson = highAmbiguity ? "[\"ambiguity:high\"]" : "[]"
        }, ct);

        var selfSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "self",
            SubjectId = selfSenderId.ToString(),
            Summary = "review smoke self profile",
            Confidence = 0.71f,
            Stability = 0.63f,
            PeriodId = period.Id
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = selfSnapshot.Id,
            TraitKey = "communication_style",
            ValueLabel = communicationStyle,
            Confidence = 0.77f,
            Stability = 0.65f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        var pairSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "pair",
            SubjectId = $"{selfSenderId}:{otherSenderId}",
            Summary = "review smoke pair profile",
            Confidence = 0.68f,
            Stability = 0.6f,
            PeriodId = period.Id
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = pairSnapshot.Id,
            TraitKey = "initiative_balance",
            ValueLabel = highAmbiguity ? "asymmetric_initiative" : "balanced",
            Confidence = 0.67f,
            Stability = 0.58f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);
    }
}
