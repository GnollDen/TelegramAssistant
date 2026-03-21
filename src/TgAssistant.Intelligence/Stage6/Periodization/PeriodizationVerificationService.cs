using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class PeriodizationVerificationService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodizationService _periodizationService;
    private readonly ILogger<PeriodizationVerificationService> _logger;

    public PeriodizationVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IOfflineEventRepository offlineEventRepository,
        IClarificationRepository clarificationRepository,
        IPeriodizationService periodizationService,
        ILogger<PeriodizationVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _offlineEventRepository = offlineEventRepository;
        _clarificationRepository = clarificationRepository;
        _periodizationService = periodizationService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var caseId = 93030000 + DateTime.UtcNow.Minute;
        var chatId = caseId;
        var now = DateTime.UtcNow;

        var seededMessages = BuildSeedMessages(chatId, now);
        await _messageRepository.SaveBatchAsync(seededMessages, ct);

        foreach (var session in BuildSeedSessions(chatId, now))
        {
            await _chatSessionRepository.UpsertAsync(session, ct);
        }

        _ = await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            EventType = "meeting",
            Title = "Important offline meeting",
            UserSummary = "Discussion changed communication tone",
            TimestampStart = now.AddDays(-12),
            TimestampEnd = now.AddDays(-12).AddHours(2),
            ReviewStatus = "pending",
            SourceType = "smoke",
            SourceId = "periodization"
        }, ct);

        var q1 = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "Did communication shift after the meeting?",
            QuestionType = "timeline_transition",
            Priority = "blocking",
            Status = "open",
            WhyItMatters = "Defines timeline transition",
            SourceType = "smoke",
            SourceId = "periodization"
        }, ct);

        _ = await _clarificationRepository.ApplyAnswerAsync(
            q1.Id,
            new ClarificationAnswer
            {
                AnswerType = "boolean",
                AnswerValue = "yes",
                AnswerConfidence = 0.9f,
                SourceClass = "user_confirmed",
                SourceType = "user",
                SourceId = "smoke-user"
            },
            markResolved: true,
            actor: "periodization_smoke",
            reason: "seed clarification",
            ct: ct);

        await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "What exactly triggered the post-gap transition?",
            QuestionType = "timeline_followup",
            Priority = "important",
            Status = "open",
            WhyItMatters = "Unknown cause should remain unresolved",
            SourceType = "smoke",
            SourceId = "periodization"
        }, ct);

        var result = await _periodizationService.RunAsync(new PeriodizationRunRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "periodization_smoke",
            SourceType = "smoke",
            SourceId = "periodization-mvp",
            Persist = true
        }, ct);

        if (result.Periods.Count < 2)
        {
            throw new InvalidOperationException("Periodization smoke failed: expected multiple periods.");
        }

        if (result.Transitions.Count == 0)
        {
            throw new InvalidOperationException("Periodization smoke failed: no transitions created.");
        }

        if (!result.Transitions.Any(x => !x.IsResolved && x.GapId.HasValue))
        {
            throw new InvalidOperationException("Periodization smoke failed: unresolved transition was not demonstrated.");
        }

        if (!result.Periods.Any(x => !string.IsNullOrWhiteSpace(x.EvidenceRefsJson) && x.EvidenceRefsJson != "[]"))
        {
            throw new InvalidOperationException("Periodization smoke failed: evidence pack is empty.");
        }

        if (result.Proposals.Count == 0)
        {
            throw new InvalidOperationException("Periodization smoke failed: merge/split proposal path not demonstrated.");
        }

        _logger.LogInformation(
            "Periodization smoke passed. case_id={CaseId}, periods={PeriodCount}, transitions={TransitionCount}, unresolved={UnresolvedCount}, proposals={ProposalCount}",
            caseId,
            result.Periods.Count,
            result.Transitions.Count,
            result.Transitions.Count(x => !x.IsResolved),
            result.Proposals.Count);
    }

    private static List<Message> BuildSeedMessages(long chatId, DateTime now)
    {
        var rows = new List<Message>();
        var telegramId = 100000L + now.Minute * 100;

        void AddWindow(DateTime start, int days, int perDay, long senderA, long senderB, string theme)
        {
            var cursor = start;
            for (var d = 0; d < days; d++)
            {
                for (var i = 0; i < perDay; i++)
                {
                    var sender = i % 2 == 0 ? senderA : senderB;
                    rows.Add(new Message
                    {
                        TelegramMessageId = telegramId++,
                        ChatId = chatId,
                        SenderId = sender,
                        SenderName = sender == senderA ? "A" : "B",
                        Timestamp = cursor.AddMinutes(i * 40),
                        Text = i % 3 == 0 ? $"{theme} thanks, let's continue" : $"{theme} update {i}",
                        ProcessingStatus = ProcessingStatus.Processed,
                        Source = MessageSource.Archive,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                cursor = cursor.AddDays(1);
            }
        }

        AddWindow(now.AddDays(-30), days: 7, perDay: 5, senderA: 1, senderB: 2, theme: "warm");
        AddWindow(now.AddDays(-14), days: 1, perDay: 2, senderA: 1, senderB: 2, theme: "short");
        AddWindow(now.AddDays(-11), days: 7, perDay: 6, senderA: 1, senderB: 2, theme: "tense busy");

        return rows;
    }

    private static List<ChatSession> BuildSeedSessions(long chatId, DateTime now)
    {
        return
        [
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 1,
                StartDate = now.AddDays(-30),
                EndDate = now.AddDays(-28),
                LastMessageAt = now.AddDays(-28),
                Summary = "steady warm sessions",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 2,
                StartDate = now.AddDays(-24),
                EndDate = now.AddDays(-23),
                LastMessageAt = now.AddDays(-23),
                Summary = "continued stable",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 3,
                StartDate = now.AddDays(-14),
                EndDate = now.AddDays(-14).AddHours(5),
                LastMessageAt = now.AddDays(-14).AddHours(5),
                Summary = "brief uncertain interaction",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 4,
                StartDate = now.AddDays(-11),
                EndDate = now.AddDays(-10),
                LastMessageAt = now.AddDays(-10),
                Summary = "dynamic changed",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = 5,
                StartDate = now.AddDays(-6),
                EndDate = now.AddDays(-4),
                LastMessageAt = now.AddDays(-4),
                Summary = "tense but active",
                IsFinalized = true,
                IsAnalyzed = true
            }
        ];
    }
}
