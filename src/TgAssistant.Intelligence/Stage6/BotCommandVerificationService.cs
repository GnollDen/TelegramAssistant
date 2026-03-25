using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6;

public class BotCommandVerificationService
{
    private readonly IBotChatService _botChatService;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IClarificationOrchestrator _clarificationOrchestrator;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly ILogger<BotCommandVerificationService> _logger;

    public BotCommandVerificationService(
        IBotChatService botChatService,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IClarificationOrchestrator clarificationOrchestrator,
        IClarificationRepository clarificationRepository,
        ILogger<BotCommandVerificationService> logger)
    {
        _botChatService = botChatService;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _clarificationOrchestrator = clarificationOrchestrator;
        _clarificationRepository = clarificationRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var baseId = 9_100_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L);
        // Bot command default policy fallback is case_id == chat_id when explicit default case is not configured.
        var caseId = baseId;
        var chatId = baseId;

        await SeedMessagesAsync(chatId, now, ct);
        await SeedSessionsAsync(chatId, now, ct);

        var created = await _clarificationOrchestrator.EnqueueQuestionsAsync(
            caseId,
            [
                new ClarificationQuestionDraft
                {
                    ChatId = chatId,
                    QuestionText = "Did tone shift after the offline meeting?",
                    QuestionType = "timeline_state_strategy",
                    Priority = "blocking",
                    WhyItMatters = "Changes state confidence and next move",
                    ExpectedGain = 0.88f,
                    AffectedOutputsJson = "[\"periods\",\"state\",\"strategy\"]",
                    AnswerOptionsJson = "[\"yes\",\"no\",\"unclear\"]",
                    SourceType = "smoke",
                    SourceId = "bot"
                }
            ],
            actor: "bot_smoke",
            ct: ct);

        if (created.Count == 0)
        {
            throw new InvalidOperationException("Bot smoke failed to seed clarification question.");
        }

        var question = created[0];

        var stateReply = await _botChatService.GenerateReplyAsync($"/state case={caseId} chat={chatId}", chatId, null, 1, ct);
        AssertContains(stateReply, "state:", "/state");
        AssertContains(stateReply, "status:", "/state");
        AssertContains(stateReply, "next:", "/state");

        var nextReply = await _botChatService.GenerateReplyAsync($"/next case={caseId} chat={chatId}", chatId, null, 1, ct);
        AssertContains(nextReply, "next:", "/next");
        AssertContains(nextReply, "micro-step:", "/next");
        AssertContains(nextReply, "ethics:", "/next");

        var draftReply = await _botChatService.GenerateReplyAsync($"/draft case={caseId} chat={chatId} tone=warm concise", chatId, null, 1, ct);
        AssertContains(draftReply, "main:", "/draft");
        AssertContains(draftReply, "softer alternative:", "/draft");
        AssertContains(draftReply, "more direct alternative:", "/draft");

        var reviewReply = await _botChatService.GenerateReplyAsync($"/review case={caseId} chat={chatId} Thanks for yesterday, I liked being with you", chatId, null, 1, ct);
        AssertContains(reviewReply, "assessment:", "/review");
        AssertContains(reviewReply, "safer:", "/review");
        AssertContains(reviewReply, "more natural:", "/review");

        var gapsReply = await _botChatService.GenerateReplyAsync($"/gaps case={caseId} chat={chatId}", chatId, null, 1, ct);
        AssertContains(gapsReply, "question:", "/gaps");
        AssertContains(gapsReply, "answer path:", "/gaps");

        var answerReply = await _botChatService.GenerateReplyAsync($"/answer case={caseId} chat={chatId} {question.Id} | yes, clearly warmer and calmer", chatId, 777001, 1, ct);
        AssertContains(answerReply, "saved answer for:", "/answer");
        AssertContains(answerReply, "recompute:", "/answer");

        var answeredQuestion = await _clarificationRepository.GetQuestionByIdAsync(question.Id, ct)
            ?? throw new InvalidOperationException("Bot smoke cannot load answered clarification question.");
        if (!answeredQuestion.Status.Equals("resolved", StringComparison.OrdinalIgnoreCase)
            && !answeredQuestion.Status.Equals("answered", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Bot smoke failed: /answer did not update question workflow. status={answeredQuestion.Status}");
        }

        var timelineReply = await _botChatService.GenerateReplyAsync($"/timeline case={caseId} chat={chatId}", chatId, null, 1, ct);
        AssertContains(timelineReply, "current:", "/timeline");

        var offlineReply = await _botChatService.GenerateReplyAsync($"/offline case={caseId} chat={chatId} We met offline and agreed to reduce pressure this week.", chatId, 777002, 1, ct);
        AssertContains(offlineReply, "Offline event logged:", "/offline");

        _logger.LogInformation(
            "Bot smoke passed. case_id={CaseId}, chat_id={ChatId}, state_len={StateLen}, next_len={NextLen}, draft_len={DraftLen}, review_len={ReviewLen}, gaps_len={GapsLen}, answer_len={AnswerLen}, timeline_len={TimelineLen}, offline_len={OfflineLen}",
            caseId,
            chatId,
            stateReply.Length,
            nextReply.Length,
            draftReply.Length,
            reviewReply.Length,
            gapsReply.Length,
            answerReply.Length,
            timelineReply.Length,
            offlineReply.Length);
    }

    private async Task SeedMessagesAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var telegramId = 900_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var rows = new List<Message>();

        for (var i = 0; i < 36; i++)
        {
            var ts = now.AddDays(-18).AddHours(i * 9);
            rows.Add(new Message
            {
                TelegramMessageId = telegramId + i,
                ChatId = chatId,
                SenderId = i % 2 == 0 ? 1 : 2,
                SenderName = i % 2 == 0 ? "Self" : "Other",
                Timestamp = ts,
                Text = i % 5 == 0
                    ? "Thanks, this feels calmer and warmer than before."
                    : (i % 3 == 0 ? "I need some space this week, but still want contact." : "Let's keep it light and steady."),
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = DateTime.UtcNow
            });
        }

        _ = await _messageRepository.SaveBatchAsync(rows, ct);
    }

    private async Task SeedSessionsAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var baseIndex = (int)(Math.Abs(now.Ticks) % 100_000);
        var sessions = new[]
        {
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 1,
                StartDate = now.AddDays(-18),
                EndDate = now.AddDays(-16),
                LastMessageAt = now.AddDays(-16),
                Summary = "slow restart",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 2,
                StartDate = now.AddDays(-13),
                EndDate = now.AddDays(-10),
                LastMessageAt = now.AddDays(-10),
                Summary = "mixed warmth and distance",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 3,
                StartDate = now.AddDays(-7),
                EndDate = now.AddDays(-3),
                LastMessageAt = now.AddDays(-3),
                Summary = "stabilizing exchange",
                IsFinalized = true,
                IsAnalyzed = true
            }
        };

        foreach (var session in sessions)
        {
            await _chatSessionRepository.UpsertAsync(session, ct);
        }
    }

    private static void AssertContains(string output, string token, string command)
    {
        if (string.IsNullOrWhiteSpace(output) || !output.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Bot smoke failed for {command}: expected token '{token}' in output.");
        }
    }
}
