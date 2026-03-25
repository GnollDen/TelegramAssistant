using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Drafts;

public class DraftEngineVerificationService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IDraftEngine _draftEngine;
    private readonly ILogger<DraftEngineVerificationService> _logger;

    public DraftEngineVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository,
        IStrategyEngine strategyEngine,
        IDraftEngine draftEngine,
        ILogger<DraftEngineVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _strategyEngine = strategyEngine;
        _draftEngine = draftEngine;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var safeScope = CaseScopeFactory.CreateSmokeScope("draft_safe");
        var safeSelfSender = 501L;
        var safeOtherSender = 601L;
        await SeedScenarioAsync(safeScope.CaseId, safeScope.ChatId, safeSelfSender, safeOtherSender, "brief_guarded", highAmbiguity: false, ct);
        var safeStrategy = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = safeScope.CaseId,
            ChatId = safeScope.ChatId,
            SelfSenderId = safeSelfSender,
            Actor = "draft_smoke",
            SourceType = "smoke",
            SourceId = "draft-safe-strategy",
            Persist = true
        }, ct);

        var safeDraft = await _draftEngine.RunAsync(new DraftEngineRequest
        {
            CaseId = safeScope.CaseId,
            ChatId = safeScope.ChatId,
            StrategyRecordId = safeStrategy.Record.Id,
            SelfSenderId = safeSelfSender,
            UserNotes = "keep it calm and concise",
            Actor = "draft_smoke",
            SourceType = "smoke",
            SourceId = "draft-safe",
            Persist = true
        }, ct);

        await ValidateDraftRecord(safeDraft.Record, safeStrategy.Record.Id, "safe");
        if (safeDraft.HasIntentConflict)
        {
            throw new InvalidOperationException("Draft smoke failed: safe scenario unexpectedly flagged intent conflict.");
        }

        var conflictScope = CaseScopeFactory.CreateSmokeScope("draft_conflict");
        var conflictSelfSender = 701L;
        var conflictOtherSender = 801L;
        await SeedScenarioAsync(conflictScope.CaseId, conflictScope.ChatId, conflictSelfSender, conflictOtherSender, "detailed_expressive", highAmbiguity: true, ct);
        var conflictStrategy = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = conflictScope.CaseId,
            ChatId = conflictScope.ChatId,
            SelfSenderId = conflictSelfSender,
            Actor = "draft_smoke",
            SourceType = "smoke",
            SourceId = "draft-conflict-strategy",
            Persist = true
        }, ct);

        var conflictBefore = (await _inboxConflictRepository.GetConflictRecordsAsync(conflictScope.CaseId, null, ct)).Count;
        var conflictDraft = await _draftEngine.RunAsync(new DraftEngineRequest
        {
            CaseId = conflictScope.CaseId,
            ChatId = conflictScope.ChatId,
            StrategyRecordId = conflictStrategy.Record.Id,
            SelfSenderId = conflictSelfSender,
            UserNotes = "дожми, надави и потребуй ответ сегодня",
            DesiredTone = "forceful",
            Actor = "draft_smoke",
            SourceType = "smoke",
            SourceId = "draft-conflict",
            Persist = true
        }, ct);
        var conflictAfter = (await _inboxConflictRepository.GetConflictRecordsAsync(conflictScope.CaseId, null, ct)).Count;

        await ValidateDraftRecord(conflictDraft.Record, conflictStrategy.Record.Id, "conflict");
        if (!conflictDraft.HasIntentConflict || conflictAfter <= conflictBefore)
        {
            throw new InvalidOperationException("Draft smoke failed: conflict-handling path was not demonstrated.");
        }

        if (conflictDraft.Record.MainDraft == conflictDraft.Record.AltDraft2)
        {
            throw new InvalidOperationException("Draft smoke failed: conflict alternative is not differentiated from safer main draft.");
        }

        if (string.IsNullOrWhiteSpace(safeDraft.Record.StyleNotes) || string.IsNullOrWhiteSpace(conflictDraft.Record.StyleNotes))
        {
            throw new InvalidOperationException("Draft smoke failed: style notes are missing.");
        }

        if (!safeDraft.Record.StyleNotes.Contains("communication_style=brief_guarded", StringComparison.OrdinalIgnoreCase)
            || !conflictDraft.Record.StyleNotes.Contains("communication_style=detailed_expressive", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Draft smoke failed: style shaping markers were not preserved in style notes.");
        }

        if (!safeDraft.Record.StyleNotes.Contains("style_contract=avoid_preachy_avoid_service_tone_avoid_anxious_overexplaining_avoid_emotional_overfilling", StringComparison.OrdinalIgnoreCase)
            || !conflictDraft.Record.StyleNotes.Contains("style_contract=avoid_preachy_avoid_service_tone_avoid_anxious_overexplaining_avoid_emotional_overfilling", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Draft smoke failed: personal-style contract guardrails were not recorded.");
        }

        if (!conflictDraft.Record.StyleNotes.Contains("desired_tone=more_direct", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Draft smoke failed: desired tone was not applied to draft alternatives.");
        }

        if (conflictDraft.Record.MainDraft.Length <= safeDraft.Record.MainDraft.Length)
        {
            throw new InvalidOperationException("Draft smoke failed: style shaping effect on draft length was not demonstrated.");
        }

        _logger.LogInformation(
            "Draft smoke passed. safe_case={SafeCaseId}, safe_draft={SafeDraftId}, conflict_case={ConflictCaseId}, conflict_draft={ConflictDraftId}, conflict_created={ConflictCreated}",
            safeScope.CaseId,
            safeDraft.Record.Id,
            conflictScope.CaseId,
            conflictDraft.Record.Id,
            conflictAfter - conflictBefore);
    }

    private async Task SeedScenarioAsync(
        long caseId,
        long chatId,
        long selfSenderId,
        long otherSenderId,
        string communicationStyle,
        bool highAmbiguity,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var period = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = highAmbiguity ? "uncertain_period" : "stable_period",
            StartAt = now.AddDays(-12),
            EndAt = null,
            IsOpen = true,
            Summary = highAmbiguity ? "mixed signal period" : "stable warm period",
            BoundaryConfidence = highAmbiguity ? 0.6f : 0.79f,
            InterpretationConfidence = highAmbiguity ? 0.52f : 0.78f,
            SourceType = "smoke",
            SourceId = "draft"
        }, ct);

        var tgId = 8_000_000 + now.Second * 100 + (highAmbiguity ? 2 : 1);
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
            Add(now.AddDays(-7), selfSenderId, "Хочу понять, как нам лучше общаться дальше.");
            Add(now.AddDays(-6), otherSenderId, "Пока не знаю, у меня много напряжения.");
            Add(now.AddDays(-4), selfSenderId, "Ок, без давления.");
            Add(now.AddDays(-2), otherSenderId, "Спасибо за понимание.");
        }
        else
        {
            Add(now.AddDays(-7), selfSenderId, "Спасибо за вчерашний разговор, было тепло.");
            Add(now.AddDays(-6), otherSenderId, "Мне тоже было хорошо, рада, что на связи.");
            Add(now.AddDays(-4), selfSenderId, "Если хочешь, можем спокойно продолжить.");
            Add(now.AddDays(-2), otherSenderId, "Да, с удовольствием.");
        }

        await _messageRepository.SaveBatchAsync(messages, ct);

        var sessionIndex = (int)(Math.Abs(now.Ticks) % 10000) + (highAmbiguity ? 20000 : 10000);
        await _chatSessionRepository.UpsertAsync(new ChatSession
        {
            ChatId = chatId,
            SessionIndex = sessionIndex,
            StartDate = now.AddDays(-7),
            EndDate = now.AddDays(-2),
            LastMessageAt = now.AddDays(-2),
            Summary = highAmbiguity ? "uncertain tempo" : "stable warm pace",
            IsFinalized = true,
            IsAnalyzed = true
        }, ct);

        await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            AsOf = now.AddHours(-6),
            DynamicLabel = highAmbiguity ? "uncertain_shift" : "stable",
            RelationshipStatus = highAmbiguity ? "ambiguous" : "warm_platonic",
            InitiativeScore = highAmbiguity ? 0.45f : 0.61f,
            ResponsivenessScore = highAmbiguity ? 0.42f : 0.69f,
            OpennessScore = highAmbiguity ? 0.4f : 0.67f,
            WarmthScore = highAmbiguity ? 0.5f : 0.73f,
            ReciprocityScore = highAmbiguity ? 0.43f : 0.68f,
            AmbiguityScore = highAmbiguity ? 0.72f : 0.31f,
            AvoidanceRiskScore = highAmbiguity ? 0.61f : 0.32f,
            EscalationReadinessScore = highAmbiguity ? 0.36f : 0.74f,
            ExternalPressureScore = highAmbiguity ? 0.58f : 0.38f,
            Confidence = highAmbiguity ? 0.51f : 0.82f,
            PeriodId = period.Id,
            KeySignalRefsJson = "[\"message:latest\",\"session:latest\"]",
            RiskRefsJson = highAmbiguity ? "[\"ambiguity:high\"]" : "[]"
        }, ct);

        var selfSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "self",
            SubjectId = selfSenderId.ToString(),
            Summary = "self profile for draft smoke",
            Confidence = 0.7f,
            Stability = 0.62f,
            PeriodId = period.Id
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = selfSnapshot.Id,
            TraitKey = "communication_style",
            ValueLabel = communicationStyle,
            Confidence = 0.78f,
            Stability = 0.66f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        var pairSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "pair",
            SubjectId = $"{selfSenderId}:{otherSenderId}",
            Summary = "pair profile for draft smoke",
            Confidence = 0.68f,
            Stability = 0.59f,
            PeriodId = period.Id
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = pairSnapshot.Id,
            TraitKey = "initiative_balance",
            ValueLabel = highAmbiguity ? "asymmetric_initiative" : "balanced",
            Confidence = 0.66f,
            Stability = 0.55f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);
    }

    private async Task ValidateDraftRecord(DraftRecord record, Guid strategyRecordId, string scenario)
    {
        if (record.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"Draft smoke failed ({scenario}): draft record was not created.");
        }

        if (record.StrategyRecordId != strategyRecordId)
        {
            throw new InvalidOperationException($"Draft smoke failed ({scenario}): strategy linkage is broken.");
        }

        if (string.IsNullOrWhiteSpace(record.MainDraft)
            || string.IsNullOrWhiteSpace(record.AltDraft1)
            || string.IsNullOrWhiteSpace(record.AltDraft2))
        {
            throw new InvalidOperationException($"Draft smoke failed ({scenario}): main/alternatives are incomplete.");
        }

        if (record.MainDraft == record.AltDraft1 || record.MainDraft == record.AltDraft2 || record.AltDraft1 == record.AltDraft2)
        {
            throw new InvalidOperationException($"Draft smoke failed ({scenario}): drafts are boilerplate-identical.");
        }

        if (record.Confidence <= 0f || record.Confidence > 1f)
        {
            throw new InvalidOperationException($"Draft smoke failed ({scenario}): confidence out of range.");
        }

        var reloaded = await _strategyDraftRepository.GetDraftRecordByIdAsync(record.Id);
        if (reloaded == null)
        {
            throw new InvalidOperationException($"Draft smoke failed ({scenario}): persisted draft is not readable.");
        }
    }
}
