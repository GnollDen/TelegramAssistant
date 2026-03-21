using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class StrategyEngineVerificationService
{
    private static readonly HashSet<string> AggressiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "invite",
        "deepen",
        "light_test"
    };

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IStrategyEngine _strategyEngine;
    private readonly ILogger<StrategyEngineVerificationService> _logger;

    public StrategyEngineVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IInboxConflictRepository inboxConflictRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IStrategyEngine strategyEngine,
        ILogger<StrategyEngineVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _strategyEngine = strategyEngine;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var highUncertaintyScope = CaseScopeFactory.CreateSmokeScope("strategy_high_uncertainty");
        var highSelfSenderId = 1001L;
        var highOtherSenderId = 2002L;
        await SeedScenarioAsync(
            highUncertaintyScope.CaseId,
            highUncertaintyScope.ChatId,
            highSelfSenderId,
            highOtherSenderId,
            highUncertainty: true,
            ct);

        var highResult = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = highUncertaintyScope.CaseId,
            ChatId = highUncertaintyScope.ChatId,
            SelfSenderId = highSelfSenderId,
            Actor = "strategy_smoke",
            SourceType = "smoke",
            SourceId = "strategy-high-uncertainty",
            Persist = true
        }, ct);

        ValidateResult(highResult, expectHorizon: false, expectSoftenedOptions: true, scenario: "high_uncertainty");
        await ValidatePersistenceAsync(highResult.Record.Id, minOptions: 2, ct);

        var confidentScope = CaseScopeFactory.CreateSmokeScope("strategy_confident");
        var confidentSelfSenderId = 3003L;
        var confidentOtherSenderId = 4004L;
        await SeedScenarioAsync(
            confidentScope.CaseId,
            confidentScope.ChatId,
            confidentSelfSenderId,
            confidentOtherSenderId,
            highUncertainty: false,
            ct);

        var confidentResult = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = confidentScope.CaseId,
            ChatId = confidentScope.ChatId,
            SelfSenderId = confidentSelfSenderId,
            Actor = "strategy_smoke",
            SourceType = "smoke",
            SourceId = "strategy-confident",
            Persist = true
        }, ct);

        ValidateResult(confidentResult, expectHorizon: true, expectSoftenedOptions: false, scenario: "confident");
        await ValidatePersistenceAsync(confidentResult.Record.Id, minOptions: 3, ct);

        _logger.LogInformation(
            "Strategy smoke passed. high_case_id={HighCaseId}, high_options={HighOptionCount}, high_primary={HighPrimary}, confident_case_id={ConfidentCaseId}, confident_options={ConfidentOptionCount}, confident_primary={ConfidentPrimary}",
            highUncertaintyScope.CaseId,
            highResult.Options.Count,
            highResult.Options.First(x => x.IsPrimary).ActionType,
            confidentScope.CaseId,
            confidentResult.Options.Count,
            confidentResult.Options.First(x => x.IsPrimary).ActionType);
    }

    private async Task SeedScenarioAsync(
        long caseId,
        long chatId,
        long selfSenderId,
        long otherSenderId,
        bool highUncertainty,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var firstPeriod = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = highUncertainty ? "uncertain_phase" : "reengaging_phase",
            StartAt = now.AddDays(-20),
            EndAt = null,
            IsOpen = true,
            Summary = highUncertainty ? "mixed and unresolved signals" : "stable warming signals",
            BoundaryConfidence = highUncertainty ? 0.58f : 0.78f,
            InterpretationConfidence = highUncertainty ? 0.52f : 0.74f,
            SourceType = "smoke",
            SourceId = "strategy"
        }, ct);

        await SeedMessagesAndSessionsAsync(chatId, selfSenderId, otherSenderId, highUncertainty, now, ct);
        await SeedClarificationAndConflictsAsync(caseId, chatId, firstPeriod.Id, highUncertainty, now, ct);
        await SeedStateSnapshotAsync(caseId, chatId, firstPeriod.Id, highUncertainty, now, ct);
        await SeedProfileArtifactsAsync(caseId, chatId, selfSenderId, otherSenderId, firstPeriod.Id, highUncertainty, now, ct);
    }

    private async Task SeedMessagesAndSessionsAsync(
        long chatId,
        long selfSenderId,
        long otherSenderId,
        bool highUncertainty,
        DateTime now,
        CancellationToken ct)
    {
        var telegramSeed = 7_000_000 + now.Second * 100 + (highUncertainty ? 11 : 21);
        var messages = new List<Message>();
        void AddMessage(DateTime ts, long senderId, string text)
        {
            messages.Add(new Message
            {
                TelegramMessageId = telegramSeed++,
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

        if (highUncertainty)
        {
            AddMessage(now.AddDays(-9), selfSenderId, "Can we clarify where we stand?");
            AddMessage(now.AddDays(-8), otherSenderId, "Not sure, maybe later.");
            AddMessage(now.AddDays(-6), selfSenderId, "I don't want to pressure you.");
            AddMessage(now.AddDays(-4), otherSenderId, "Still unclear right now.");
        }
        else
        {
            AddMessage(now.AddDays(-9), selfSenderId, "Loved our last conversation, how is your week?");
            AddMessage(now.AddDays(-8), otherSenderId, "Good, and yes I'd like to catch up soon.");
            AddMessage(now.AddDays(-6), selfSenderId, "Great, I can do Friday or Saturday.");
            AddMessage(now.AddDays(-4), otherSenderId, "Friday works, let's do it.");
        }

        await _messageRepository.SaveBatchAsync(messages, ct);

        var baseIndex = (int)(Math.Abs(now.Ticks) % 10000) * 10 + (highUncertainty ? 0 : 4);
        await _chatSessionRepository.UpsertAsync(new ChatSession
        {
            ChatId = chatId,
            SessionIndex = baseIndex + 1,
            StartDate = now.AddDays(-9),
            EndDate = now.AddDays(-8),
            LastMessageAt = now.AddDays(-8),
            Summary = highUncertainty ? "uncertain exchange" : "warm exchange",
            IsFinalized = true,
            IsAnalyzed = true
        }, ct);
    }

    private async Task SeedClarificationAndConflictsAsync(
        long caseId,
        long chatId,
        Guid periodId,
        bool highUncertainty,
        DateTime now,
        CancellationToken ct)
    {
        if (highUncertainty)
        {
            await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
            {
                CaseId = caseId,
                ChatId = chatId,
                PeriodId = periodId,
                QuestionText = "Is delay caused by external pressure or disengagement?",
                QuestionType = "strategy_uncertainty",
                Priority = "blocking",
                Status = "open",
                WhyItMatters = "Affects safe action intensity",
                SourceType = "smoke",
                SourceId = "strategy"
            }, ct);

            await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
            {
                CaseId = caseId,
                ChatId = chatId,
                PeriodId = periodId,
                ConflictType = "answer_vs_state",
                ObjectAType = "clarification_answer",
                ObjectAId = "seeded_high",
                ObjectBType = "state_snapshot",
                ObjectBId = "seeded_high",
                Summary = "Recent answer conflicts with current interpretation",
                Severity = "high",
                Status = "open",
                LastActor = "strategy_smoke",
                LastReason = "seed high uncertainty"
            }, ct);
        }
        else
        {
            var question = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
            {
                CaseId = caseId,
                ChatId = chatId,
                PeriodId = periodId,
                QuestionText = "Is meeting this week realistic?",
                QuestionType = "strategy_readiness",
                Priority = "important",
                Status = "resolved",
                WhyItMatters = "Affects invite confidence",
                SourceType = "smoke",
                SourceId = "strategy"
            }, ct);

            _ = await _clarificationRepository.ApplyAnswerAsync(
                question.Id,
                new ClarificationAnswer
                {
                    AnswerType = "boolean",
                    AnswerValue = "yes",
                    AnswerConfidence = 0.9f,
                    SourceClass = "user_confirmed",
                    SourceType = "user",
                    SourceId = "strategy-smoke",
                    CreatedAt = now.AddDays(-2)
                },
                markResolved: true,
                actor: "strategy_smoke",
                reason: "seed confident case",
                ct: ct);
        }
    }

    private async Task SeedStateSnapshotAsync(
        long caseId,
        long chatId,
        Guid periodId,
        bool highUncertainty,
        DateTime now,
        CancellationToken ct)
    {
        await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            AsOf = now.AddHours(-8),
            DynamicLabel = highUncertainty ? "uncertain_shift" : "reengaging",
            RelationshipStatus = highUncertainty ? "ambiguous" : "reopening",
            AlternativeStatus = highUncertainty ? "fragile_contact" : null,
            InitiativeScore = highUncertainty ? 0.44f : 0.63f,
            ResponsivenessScore = highUncertainty ? 0.36f : 0.7f,
            OpennessScore = highUncertainty ? 0.41f : 0.64f,
            WarmthScore = highUncertainty ? 0.49f : 0.72f,
            ReciprocityScore = highUncertainty ? 0.39f : 0.69f,
            AmbiguityScore = highUncertainty ? 0.77f : 0.31f,
            AvoidanceRiskScore = highUncertainty ? 0.64f : 0.33f,
            EscalationReadinessScore = highUncertainty ? 0.34f : 0.74f,
            ExternalPressureScore = highUncertainty ? 0.58f : 0.34f,
            Confidence = highUncertainty ? 0.48f : 0.83f,
            PeriodId = periodId,
            KeySignalRefsJson = JsonSerializer.Serialize(new[] { "msg:recent_tone", "session:latest" }),
            RiskRefsJson = JsonSerializer.Serialize(new[] { "clarification:blocking", "conflict:open" }),
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private async Task SeedProfileArtifactsAsync(
        long caseId,
        long chatId,
        long selfSenderId,
        long otherSenderId,
        Guid periodId,
        bool highUncertainty,
        DateTime now,
        CancellationToken ct)
    {
        var selfSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "self",
            SubjectId = selfSenderId.ToString(),
            PeriodId = null,
            Summary = "self profile",
            Confidence = highUncertainty ? 0.58f : 0.74f,
            Stability = highUncertainty ? 0.42f : 0.72f,
            CreatedAt = now
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = selfSnapshot.Id,
            TraitKey = "communication_style",
            ValueLabel = highUncertainty ? "brief_guarded" : "balanced_pragmatic",
            Confidence = 0.7f,
            Stability = 0.62f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = selfSnapshot.Id,
            TraitKey = "conflict_repair_behavior",
            ValueLabel = highUncertainty ? "repair_fragile" : "repair_capable",
            Confidence = 0.64f,
            Stability = 0.55f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        var otherSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "other",
            SubjectId = otherSenderId.ToString(),
            PeriodId = periodId,
            Summary = "other profile",
            Confidence = highUncertainty ? 0.52f : 0.73f,
            Stability = highUncertainty ? 0.4f : 0.68f,
            CreatedAt = now
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = otherSnapshot.Id,
            TraitKey = "communication_style",
            ValueLabel = highUncertainty ? "brief_guarded" : "detailed_expressive",
            Confidence = 0.66f,
            Stability = 0.5f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        var pairSnapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            SubjectType = "pair",
            SubjectId = $"{selfSenderId}:{otherSenderId}",
            PeriodId = periodId,
            Summary = "pair profile",
            Confidence = highUncertainty ? 0.5f : 0.78f,
            Stability = highUncertainty ? 0.39f : 0.7f,
            CreatedAt = now
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = pairSnapshot.Id,
            TraitKey = "repair_capacity",
            ValueLabel = highUncertainty ? "repair_fragile" : "repair_capable",
            Confidence = 0.67f,
            Stability = 0.59f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = pairSnapshot.Id,
            TraitKey = "distance_recovery",
            ValueLabel = highUncertainty ? "slow_recovery" : "recovers_after_distance",
            Confidence = 0.68f,
            Stability = 0.6f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = pairSnapshot.Id,
            TraitKey = "what_fails",
            ValueLabel = highUncertainty ? "high pressure asks fail" : "pressure spikes reduce quality",
            Confidence = 0.63f,
            Stability = 0.46f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);
    }

    private static void ValidateResult(
        StrategyEngineResult result,
        bool expectHorizon,
        bool expectSoftenedOptions,
        string scenario)
    {
        if (result.Record.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: strategy record not created.");
        }

        if (result.Options.Count < 2)
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: expected multiple options.");
        }

        var primaryOptions = result.Options.Where(x => x.IsPrimary).ToList();
        if (primaryOptions.Count != 1)
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: expected exactly one primary option.");
        }

        if (string.IsNullOrWhiteSpace(result.MicroStep))
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: micro-step is missing.");
        }

        if (result.Options.Any(x => string.IsNullOrWhiteSpace(x.Purpose)
                                    || string.IsNullOrWhiteSpace(x.WhenToUse)
                                    || string.IsNullOrWhiteSpace(x.SuccessSigns)
                                    || string.IsNullOrWhiteSpace(x.FailureSigns)
                                    || string.IsNullOrWhiteSpace(x.Risk)))
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: option content is incomplete.");
        }

        if (result.Options.Any(x => !x.Risk.Contains("labels", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: risk tags are missing.");
        }

        if (expectHorizon && result.Horizon.Count == 0)
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: horizon expected but missing.");
        }

        if (!expectHorizon && result.Horizon.Count > 0)
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: horizon should be absent under low confidence/high uncertainty.");
        }

        if (expectSoftenedOptions)
        {
            if (result.Options.Count > 3)
            {
                throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: high-uncertainty option set was not narrowed.");
            }

            if (result.Options.Any(x => AggressiveActions.Contains(x.ActionType)))
            {
                throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: aggressive options were not softened out.");
            }
        }

        if (string.IsNullOrWhiteSpace(result.WhyNotNotes))
        {
            throw new InvalidOperationException($"Strategy smoke ({scenario}) failed: why-not notes are missing.");
        }
    }

    private async Task ValidatePersistenceAsync(Guid recordId, int minOptions, CancellationToken ct)
    {
        var record = await _strategyDraftRepository.GetStrategyRecordByIdAsync(recordId, ct);
        if (record == null)
        {
            throw new InvalidOperationException("Strategy smoke failed: persisted record is not readable.");
        }

        var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(recordId, ct);
        if (options.Count < minOptions)
        {
            throw new InvalidOperationException("Strategy smoke failed: persisted options count is below expected minimum.");
        }

        if (string.IsNullOrWhiteSpace(record.MicroStep))
        {
            throw new InvalidOperationException("Strategy smoke failed: persisted micro-step is empty.");
        }

        if (record.StrategyConfidence <= 0f || record.StrategyConfidence > 1f)
        {
            throw new InvalidOperationException("Strategy smoke failed: persisted confidence is out of range.");
        }
    }
}
