using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class StateEngineVerificationService
{
    private static readonly HashSet<string> AllowedDynamicLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "warming",
        "stable",
        "cooling",
        "fragile",
        "uncertain_shift",
        "low_reciprocity",
        "testing_space",
        "reengaging"
    };

    private static readonly HashSet<string> AllowedRelationshipStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "platonic",
        "warm_platonic",
        "ambiguous",
        "reopening",
        "romantic_history_distanced",
        "fragile_contact",
        "detached"
    };

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly ICurrentStateEngine _currentStateEngine;
    private readonly ILogger<StateEngineVerificationService> _logger;

    public StateEngineVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IInboxConflictRepository inboxConflictRepository,
        IStateProfileRepository stateProfileRepository,
        ICurrentStateEngine currentStateEngine,
        ILogger<StateEngineVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _stateProfileRepository = stateProfileRepository;
        _currentStateEngine = currentStateEngine;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Keep case scope explicit: case_id is analysis scope, chat_id is canonical source anchor.
        var caseScope = CaseScopeFactory.CreateSmokeScope("current_state");
        var caseId = caseScope.CaseId;
        var chatId = caseScope.ChatId;
        var now = DateTime.UtcNow;

        await SeedPeriodsAsync(caseId, chatId, now, ct);
        await SeedMessagesAsync(chatId, now, ct);
        await SeedSessionsAsync(chatId, now, ct);
        await SeedClarificationsAsync(caseId, chatId, now, ct);
        await SeedOfflineAndConflictAsync(caseId, chatId, now, ct);
        await SeedHistoricalSnapshotsAsync(caseId, chatId, now, ct);

        var result = await _currentStateEngine.ComputeAsync(new CurrentStateRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "state_smoke",
            SourceType = "smoke",
            SourceId = "state-engine",
            Persist = true
        }, ct);

        var snapshot = result.Snapshot;
        if (snapshot.Id == Guid.Empty)
        {
            throw new InvalidOperationException("State smoke failed: snapshot was not persisted.");
        }

        if (!AllowedDynamicLabels.Contains(snapshot.DynamicLabel))
        {
            throw new InvalidOperationException($"State smoke failed: unexpected dynamic label '{snapshot.DynamicLabel}'.");
        }

        if (!AllowedRelationshipStatuses.Contains(snapshot.RelationshipStatus))
        {
            throw new InvalidOperationException($"State smoke failed: unexpected relationship status '{snapshot.RelationshipStatus}'.");
        }

        if (snapshot.Confidence <= 0f || snapshot.Confidence > 1f)
        {
            throw new InvalidOperationException("State smoke failed: confidence is out of [0,1] range.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.KeySignalRefsJson) || snapshot.KeySignalRefsJson == "[]")
        {
            throw new InvalidOperationException("State smoke failed: signal refs are empty.");
        }

        var reloaded = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshot.Id, ct);
        if (reloaded == null)
        {
            throw new InvalidOperationException("State smoke failed: persisted snapshot is not readable.");
        }

        var hasAmbiguityPath = reloaded.AlternativeStatus != null || result.Confidence.HighAmbiguity;
        var hasHistoryConflictPath = result.Confidence.HistoryConflictDetected || result.Scores.HistoryConflictDetected;
        if (!hasAmbiguityPath && !hasHistoryConflictPath)
        {
            throw new InvalidOperationException("State smoke failed: ambiguity/history-conflict path was not demonstrated.");
        }

        _logger.LogInformation(
            "State smoke passed. case_id={CaseId}, dynamic={DynamicLabel}, status={Status}, alt_status={AltStatus}, confidence={Confidence:0.00}, history_conflict={HistoryConflict}",
            caseId,
            reloaded.DynamicLabel,
            reloaded.RelationshipStatus,
            reloaded.AlternativeStatus,
            reloaded.Confidence,
            hasHistoryConflictPath);
    }

    private async Task SeedPeriodsAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "warm_phase",
            StartAt = now.AddDays(-50),
            EndAt = now.AddDays(-20),
            IsOpen = false,
            Summary = "Previously warm phase",
            BoundaryConfidence = 0.8f,
            InterpretationConfidence = 0.76f,
            ReviewPriority = 1,
            SourceType = "smoke",
            SourceId = "state"
        }, ct);

        await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "current_uncertain_phase",
            StartAt = now.AddDays(-12),
            EndAt = null,
            IsOpen = true,
            Summary = "Current uncertain phase",
            BoundaryConfidence = 0.6f,
            InterpretationConfidence = 0.48f,
            ReviewPriority = 4,
            SourceType = "smoke",
            SourceId = "state"
        }, ct);
    }

    private async Task SeedMessagesAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var baseTelegramId = 400000 + now.Minute * 1000;
        var messages = new List<Message>();
        var senderA = 1L;
        var senderB = 2L;

        void AddMessage(DateTime ts, long senderId, string text)
        {
            messages.Add(new Message
            {
                TelegramMessageId = baseTelegramId++,
                ChatId = chatId,
                SenderId = senderId,
                SenderName = senderId == senderA ? "A" : "B",
                Timestamp = ts,
                Text = text,
                Source = MessageSource.Archive,
                ProcessingStatus = ProcessingStatus.Processed,
                CreatedAt = DateTime.UtcNow
            });
        }

        AddMessage(now.AddDays(-11).AddHours(10), senderA, "thanks for staying in touch");
        AddMessage(now.AddDays(-11).AddHours(12), senderB, "busy today, talk later");
        AddMessage(now.AddDays(-9).AddHours(18), senderA, "I want to be honest about where we stand");
        AddMessage(now.AddDays(-9).AddHours(22), senderB, "not now, maybe later");
        AddMessage(now.AddDays(-7).AddHours(14), senderA, "I appreciate you, but this feels uncertain");
        AddMessage(now.AddDays(-5).AddHours(20), senderB, "can't talk long, work stress");
        AddMessage(now.AddDays(-3).AddHours(9), senderA, "can we reconnect soon?");
        AddMessage(now.AddDays(-2).AddHours(19), senderB, "maybe, still not sure");

        await _messageRepository.SaveBatchAsync(messages, ct);
    }

    private async Task SeedSessionsAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var baseIndex = (int)(Math.Abs(now.Ticks) % 10000) * 10;
        var sessions = new[]
        {
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 1,
                StartDate = now.AddDays(-11),
                EndDate = now.AddDays(-11).AddHours(2),
                LastMessageAt = now.AddDays(-11).AddHours(2),
                Summary = "mixed check-in",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 2,
                StartDate = now.AddDays(-9),
                EndDate = now.AddDays(-9).AddHours(4),
                LastMessageAt = now.AddDays(-9).AddHours(4),
                Summary = "uncertain tone",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 3,
                StartDate = now.AddDays(-3),
                EndDate = now.AddDays(-2),
                LastMessageAt = now.AddDays(-2),
                Summary = "attempted re-engagement",
                IsFinalized = true,
                IsAnalyzed = true
            }
        };

        foreach (var session in sessions)
        {
            await _chatSessionRepository.UpsertAsync(session, ct);
        }
    }

    private async Task SeedClarificationsAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        var blocking = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "Was the recent distance caused by external pressure?",
            QuestionType = "state_current",
            Priority = "blocking",
            Status = "open",
            WhyItMatters = "Affects current state interpretation",
            SourceType = "smoke",
            SourceId = "state"
        }, ct);

        var answered = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "Is reconnection still desired?",
            QuestionType = "state_intent",
            Priority = "important",
            Status = "resolved",
            WhyItMatters = "Affects readiness",
            SourceType = "smoke",
            SourceId = "state"
        }, ct);

        _ = await _clarificationRepository.ApplyAnswerAsync(
            answered.Id,
            new ClarificationAnswer
            {
                AnswerType = "boolean",
                AnswerValue = "yes",
                AnswerConfidence = 0.8f,
                SourceClass = "model_interpreted",
                SourceType = "model",
                SourceId = "state-smoke-model",
                CreatedAt = now.AddDays(-2)
            },
            markResolved: true,
            actor: "state_smoke",
            reason: "seeded model interpretation",
            ct: ct);

        _ = blocking;
    }

    private async Task SeedOfflineAndConflictAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            EventType = "work_stress",
            Title = "Heavy work week",
            UserSummary = "Work pressure reduced availability",
            TimestampStart = now.AddDays(-4),
            TimestampEnd = now.AddDays(-3),
            ReviewStatus = "pending",
            SourceType = "smoke",
            SourceId = "state"
        }, ct);

        await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
        {
            CaseId = caseId,
            ChatId = chatId,
            ConflictType = "interpretation_conflict",
            ObjectAType = "clarification_answer",
            ObjectAId = "seeded",
            ObjectBType = "state_assumption",
            ObjectBId = "historical_pattern",
            Summary = "Current signals conflict with prior warm pattern",
            Severity = "medium",
            Status = "open",
            LastActor = "state_smoke",
            LastReason = "seed conflict"
        }, ct);
    }

    private async Task SeedHistoricalSnapshotsAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        for (var i = 0; i < 3; i++)
        {
            await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
            {
                CaseId = caseId,
                ChatId = chatId,
                AsOf = now.AddDays(-35 + i * 6),
                DynamicLabel = "warming",
                RelationshipStatus = "warm_platonic",
                InitiativeScore = 0.72f,
                ResponsivenessScore = 0.78f,
                OpennessScore = 0.69f,
                WarmthScore = 0.82f,
                ReciprocityScore = 0.75f,
                AmbiguityScore = 0.28f,
                AvoidanceRiskScore = 0.24f,
                EscalationReadinessScore = 0.63f,
                ExternalPressureScore = 0.26f,
                Confidence = 0.82f,
                KeySignalRefsJson = "[\"seed:warm\"]",
                RiskRefsJson = "[]",
                CreatedAt = now.AddDays(-35 + i * 6)
            }, ct);
        }
    }
}
