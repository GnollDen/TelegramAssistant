using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class ProfileEngineVerificationService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IProfileEngine _profileEngine;
    private readonly ILogger<ProfileEngineVerificationService> _logger;

    public ProfileEngineVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IStateProfileRepository stateProfileRepository,
        IProfileEngine profileEngine,
        ILogger<ProfileEngineVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _stateProfileRepository = stateProfileRepository;
        _profileEngine = profileEngine;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var caseId = 95000000 + (DateTime.UtcNow.Ticks % 1000000);
        var chatId = caseId;
        var selfSenderId = 101L;
        var otherSenderId = 202L;
        var now = DateTime.UtcNow;

        var periods = await SeedPeriodsAsync(caseId, chatId, now, ct);
        await SeedMessagesAsync(chatId, selfSenderId, otherSenderId, periods, now, ct);
        await SeedSessionsAsync(chatId, periods, now, ct);
        await SeedClarificationsAsync(caseId, chatId, periods, now, ct);
        await SeedOfflineEventsAsync(caseId, chatId, periods, now, ct);
        await SeedStateSnapshotAsync(caseId, chatId, periods.Last().Id, now, ct);

        var result = await _profileEngine.RunAsync(new ProfileEngineRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            SelfSenderId = selfSenderId,
            Actor = "profile_smoke",
            SourceType = "smoke",
            SourceId = "profile-engine",
            MaxPeriodSlices = 2,
            Persist = true
        }, ct);

        var selfSnapshots = result.Snapshots.Where(x => x.SubjectType == "self").ToList();
        var otherSnapshots = result.Snapshots.Where(x => x.SubjectType == "other").ToList();
        var pairSnapshots = result.Snapshots.Where(x => x.SubjectType == "pair").ToList();

        if (selfSnapshots.Count == 0)
        {
            throw new InvalidOperationException("Profile smoke failed: self profile was not generated.");
        }

        if (otherSnapshots.Count == 0)
        {
            throw new InvalidOperationException("Profile smoke failed: other profile was not generated.");
        }

        if (pairSnapshots.Count == 0)
        {
            throw new InvalidOperationException("Profile smoke failed: pair profile was not generated.");
        }

        var persistedSelf = await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(caseId, "self", selfSenderId.ToString(), ct);
        var persistedOther = await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(caseId, "other", otherSenderId.ToString(), ct);
        var persistedPair = await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(caseId, "pair", $"{selfSenderId}:{otherSenderId}", ct);

        if (persistedSelf.Count == 0 || persistedOther.Count == 0 || persistedPair.Count == 0)
        {
            throw new InvalidOperationException("Profile smoke failed: profile persistence is incomplete.");
        }

        var anyTrait = result.Traits.FirstOrDefault();
        if (anyTrait == null)
        {
            throw new InvalidOperationException("Profile smoke failed: traits were not persisted.");
        }

        if (result.Traits.Any(x => x.Confidence <= 0f || x.Stability <= 0f))
        {
            throw new InvalidOperationException("Profile smoke failed: trait confidence/stability has invalid values.");
        }

        var hasLowStabilityPath = result.Traits.Any(x => x.Stability < 0.45f)
                                  || result.Snapshots.Any(x => x.PeriodId != null);
        if (!hasLowStabilityPath)
        {
            throw new InvalidOperationException("Profile smoke failed: low-stability or period-specific path was not demonstrated.");
        }

        var hasWorks = result.Patterns.Any(x => x.PatternType == "what_works");
        var hasFails = result.Patterns.Any(x => x.PatternType == "what_fails");
        if (!hasWorks || !hasFails)
        {
            throw new InvalidOperationException("Profile smoke failed: what-works / what-fails patterns are missing.");
        }

        _logger.LogInformation(
            "Profile smoke passed. case_id={CaseId}, snapshots={SnapshotCount}, traits={TraitCount}, patterns={PatternCount}, low_stability={LowStability}",
            caseId,
            result.Snapshots.Count,
            result.Traits.Count,
            result.Patterns.Count,
            hasLowStabilityPath);
    }

    private async Task<List<Period>> SeedPeriodsAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        var period1 = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "warming_phase",
            StartAt = now.AddDays(-40),
            EndAt = now.AddDays(-20),
            IsOpen = false,
            Summary = "warmer interactions",
            BoundaryConfidence = 0.72f,
            InterpretationConfidence = 0.7f,
            SourceType = "smoke",
            SourceId = "profile"
        }, ct);

        var period2 = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = "uncertain_phase",
            StartAt = now.AddDays(-19),
            EndAt = null,
            IsOpen = true,
            Summary = "uncertain interactions",
            BoundaryConfidence = 0.61f,
            InterpretationConfidence = 0.53f,
            SourceType = "smoke",
            SourceId = "profile"
        }, ct);

        return [period1, period2];
    }

    private async Task SeedMessagesAsync(
        long chatId,
        long selfSenderId,
        long otherSenderId,
        IReadOnlyList<Period> periods,
        DateTime now,
        CancellationToken ct)
    {
        var seed = 950000 + now.Minute * 100;
        var messages = new List<Message>();

        void Add(DateTime ts, long senderId, string text)
        {
            messages.Add(new Message
            {
                TelegramMessageId = seed++,
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

        Add(periods[0].StartAt.AddDays(2), selfSenderId, "Thanks for yesterday, I appreciate your support.");
        Add(periods[0].StartAt.AddDays(2).AddHours(2), otherSenderId, "Glad to hear that, can we meet tomorrow?");
        Add(periods[0].StartAt.AddDays(6), selfSenderId, "Yes, that works for me.");
        Add(periods[0].StartAt.AddDays(10), otherSenderId, "Great, see you.");

        Add(periods[1].StartAt.AddDays(2), selfSenderId, "Can we clarify where we are now?");
        Add(periods[1].StartAt.AddDays(3), otherSenderId, "Maybe later, busy this week.");
        Add(periods[1].StartAt.AddDays(7), selfSenderId, "I understand, let me know when possible.");
        Add(periods[1].StartAt.AddDays(9), otherSenderId, "Not sure yet.");
        Add(periods[1].StartAt.AddDays(12), selfSenderId, "Thanks for being honest.");

        await _messageRepository.SaveBatchAsync(messages, ct);
    }

    private async Task SeedSessionsAsync(long chatId, IReadOnlyList<Period> periods, DateTime now, CancellationToken ct)
    {
        var sessions = new[]
        {
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 1,
                StartDate = periods[0].StartAt.AddDays(2),
                EndDate = periods[0].StartAt.AddDays(2).AddHours(2),
                LastMessageAt = periods[0].StartAt.AddDays(2).AddHours(2),
                Summary = "warm exchange",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 2,
                StartDate = periods[1].StartAt.AddDays(2),
                EndDate = periods[1].StartAt.AddDays(9),
                LastMessageAt = periods[1].StartAt.AddDays(9),
                Summary = "slower and ambiguous",
                IsFinalized = true,
                IsAnalyzed = true
            }
        };

        foreach (var session in sessions)
        {
            await _chatSessionRepository.UpsertAsync(session, ct);
        }
    }

    private async Task SeedClarificationsAsync(long caseId, long chatId, IReadOnlyList<Period> periods, DateTime now, CancellationToken ct)
    {
        var question = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = periods[1].Id,
            QuestionText = "Is current delay due to stress?",
            QuestionType = "profile_context",
            Priority = "important",
            Status = "resolved",
            WhyItMatters = "affects pair profile",
            SourceType = "smoke",
            SourceId = "profile"
        }, ct);

        _ = await _clarificationRepository.ApplyAnswerAsync(
            question.Id,
            new ClarificationAnswer
            {
                AnswerType = "boolean",
                AnswerValue = "yes",
                AnswerConfidence = 0.8f,
                SourceClass = "user_confirmed",
                SourceType = "user",
                SourceId = "profile-smoke",
                CreatedAt = now.AddDays(-5)
            },
            markResolved: true,
            actor: "profile_smoke",
            reason: "seed",
            ct: ct);
    }

    private async Task SeedOfflineEventsAsync(long caseId, long chatId, IReadOnlyList<Period> periods, DateTime now, CancellationToken ct)
    {
        await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            EventType = "supportive_meetup",
            Title = "Good meetup",
            UserSummary = "Warm in-person meetup",
            TimestampStart = periods[0].StartAt.AddDays(8),
            TimestampEnd = periods[0].StartAt.AddDays(8).AddHours(2),
            PeriodId = periods[0].Id,
            ReviewStatus = "reviewed",
            SourceType = "smoke",
            SourceId = "profile"
        }, ct);

        await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            EventType = "work_stress",
            Title = "Busy work sprint",
            UserSummary = "External pressure impacted rhythm",
            TimestampStart = periods[1].StartAt.AddDays(4),
            TimestampEnd = periods[1].StartAt.AddDays(6),
            PeriodId = periods[1].Id,
            ReviewStatus = "reviewed",
            SourceType = "smoke",
            SourceId = "profile"
        }, ct);

        await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            EventType = "schedule_pressure",
            Title = "Travel planning pressure",
            UserSummary = "More uncertainty in timing",
            TimestampStart = periods[1].StartAt.AddDays(10),
            TimestampEnd = periods[1].StartAt.AddDays(10).AddHours(4),
            PeriodId = periods[1].Id,
            ReviewStatus = "pending",
            SourceType = "smoke",
            SourceId = "profile"
        }, ct);
    }

    private async Task SeedStateSnapshotAsync(long caseId, long chatId, Guid currentPeriodId, DateTime now, CancellationToken ct)
    {
        await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
        {
            CaseId = caseId,
            ChatId = chatId,
            AsOf = now.AddDays(-2),
            DynamicLabel = "uncertain_shift",
            RelationshipStatus = "ambiguous",
            InitiativeScore = 0.52f,
            ResponsivenessScore = 0.41f,
            OpennessScore = 0.49f,
            WarmthScore = 0.55f,
            ReciprocityScore = 0.46f,
            AmbiguityScore = 0.66f,
            AvoidanceRiskScore = 0.57f,
            EscalationReadinessScore = 0.44f,
            ExternalPressureScore = 0.61f,
            Confidence = 0.58f,
            PeriodId = currentPeriodId,
            KeySignalRefsJson = "[]",
            RiskRefsJson = "[]",
            CreatedAt = now.AddDays(-2)
        }, ct);
    }
}
