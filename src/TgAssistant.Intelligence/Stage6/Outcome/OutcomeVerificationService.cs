// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Outcome;

public class OutcomeVerificationService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IDraftEngine _draftEngine;
    private readonly IOutcomeService _outcomeService;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<OutcomeVerificationService> _logger;

    public OutcomeVerificationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IStrategyEngine strategyEngine,
        IDraftEngine draftEngine,
        IOutcomeService outcomeService,
        IStrategyDraftRepository strategyDraftRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<OutcomeVerificationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _strategyEngine = strategyEngine;
        _draftEngine = draftEngine;
        _outcomeService = outcomeService;
        _strategyDraftRepository = strategyDraftRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var scope = CaseScopeFactory.CreateSmokeScope("outcome");
        var now = DateTime.UtcNow;

        await SeedMessagesAsync(scope.ChatId, now, ct);
        await SeedSessionsAsync(scope.ChatId, now, ct);

        var strategy = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            Actor = "outcome_smoke",
            SourceType = "smoke",
            SourceId = "outcome-strategy",
            Persist = true
        }, ct);

        var draft = await _draftEngine.RunAsync(new DraftEngineRequest
        {
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            StrategyRecordId = strategy.Record.Id,
            DesiredTone = "warm concise",
            UserNotes = "keep it low-pressure and clear",
            Actor = "outcome_smoke",
            SourceType = "smoke",
            SourceId = "outcome-draft",
            Persist = true
        }, ct);

        var baseTelegram = 995_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var actualMessage = new Message
        {
            TelegramMessageId = baseTelegram + 1,
            ChatId = scope.ChatId,
            SenderId = 1,
            SenderName = "Self",
            Timestamp = now.AddMinutes(1),
            Text = draft.Record.MainDraft + " Let me know when comfortable.",
            ProcessingStatus = ProcessingStatus.Processed,
            Source = MessageSource.Realtime,
            CreatedAt = DateTime.UtcNow
        };
        var followUpMessage = new Message
        {
            TelegramMessageId = baseTelegram + 2,
            ChatId = scope.ChatId,
            SenderId = 2,
            SenderName = "Other",
            Timestamp = now.AddMinutes(25),
            Text = "Thanks, that tone works for me. Let's do a short check-in tomorrow.",
            ReplyToMessageId = baseTelegram + 1,
            ProcessingStatus = ProcessingStatus.Processed,
            Source = MessageSource.Realtime,
            CreatedAt = DateTime.UtcNow
        };

        _ = await _messageRepository.SaveBatchAsync([actualMessage, followUpMessage], ct);
        var savedActual = (await _messageRepository.GetByTelegramMessageIdsAsync(
            scope.ChatId,
            MessageSource.Realtime,
            [actualMessage.TelegramMessageId],
            ct)).Values.FirstOrDefault();
        var savedFollowUp = (await _messageRepository.GetByTelegramMessageIdsAsync(
            scope.ChatId,
            MessageSource.Realtime,
            [followUpMessage.TelegramMessageId],
            ct)).Values.FirstOrDefault();

        if (savedActual == null || savedFollowUp == null)
        {
            throw new InvalidOperationException("Outcome smoke failed: cannot resolve persisted actual/follow-up messages.");
        }

        var result = await _outcomeService.RecordAsync(new OutcomeRecordRequest
        {
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            StrategyRecordId = strategy.Record.Id,
            DraftRecordId = draft.Record.Id,
            ActualMessageId = savedActual.Id,
            FollowUpMessageId = savedFollowUp.Id,
            UserOutcomeLabel = "positive",
            Notes = "smoke chain outcome",
            Actor = "outcome_smoke",
            SourceType = "smoke",
            SourceId = "outcome-smoke"
        }, ct);

        if (result.Outcome.Id == Guid.Empty)
        {
            throw new InvalidOperationException("Outcome smoke failed: outcome record was not created.");
        }

        if (result.Outcome.StrategyRecordId != strategy.Record.Id || result.Outcome.DraftId != draft.Record.Id)
        {
            throw new InvalidOperationException("Outcome smoke failed: strategy/draft linkage is broken.");
        }

        if (result.Outcome.ActualMessageId == null)
        {
            throw new InvalidOperationException("Outcome smoke failed: draft to actual-action linkage is missing.");
        }

        if (result.Match.MatchScore <= 0f || string.IsNullOrWhiteSpace(result.Match.MatchMethod))
        {
            throw new InvalidOperationException("Outcome smoke failed: matching path did not produce a usable result.");
        }

        if (result.LearningSignals.Count == 0)
        {
            throw new InvalidOperationException("Outcome smoke failed: learning signals are empty.");
        }

        var reloaded = await _strategyDraftRepository.GetDraftOutcomesByDraftIdAsync(draft.Record.Id, ct);
        var persisted = reloaded.FirstOrDefault(x => x.Id == result.Outcome.Id);
        if (persisted == null)
        {
            throw new InvalidOperationException("Outcome smoke failed: persisted outcome is not readable.");
        }

        var parsedSignals = ParseSignals(persisted.LearningSignalsJson);
        if (parsedSignals.Count == 0)
        {
            throw new InvalidOperationException("Outcome smoke failed: persisted learning signals are not inspectable.");
        }

        var trail = await _domainReviewEventRepository.GetByObjectAsync("draft_outcome", persisted.Id.ToString(), 5, ct);
        if (!trail.Any(x => x.Action.Equals("outcome_recorded", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Outcome smoke failed: persisted outcome chain is not inspectable via review trail.");
        }

        _logger.LogInformation(
            "Outcome smoke passed. case_id={CaseId}, strategy={StrategyId}, draft={DraftId}, outcome={OutcomeId}, match={Match:0.00}, signals={Signals}",
            scope.CaseId,
            strategy.Record.Id,
            draft.Record.Id,
            result.Outcome.Id,
            result.Match.MatchScore,
            parsedSignals.Count);
    }

    private async Task SeedMessagesAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var baseTelegram = 994_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var rows = new List<Message>();
        for (var i = 0; i < 20; i++)
        {
            rows.Add(new Message
            {
                TelegramMessageId = baseTelegram + i,
                ChatId = chatId,
                SenderId = i % 2 == 0 ? 1 : 2,
                SenderName = i % 2 == 0 ? "Self" : "Other",
                Timestamp = now.AddDays(-7).AddHours(i * 5),
                Text = i % 3 == 0
                    ? "Thanks, this calmer pace is better."
                    : "Let's keep contact light and clear this week.",
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
        await _chatSessionRepository.UpsertAsync(new ChatSession
        {
            ChatId = chatId,
            SessionIndex = baseIndex + 1,
            StartDate = now.AddDays(-8),
            EndDate = now.AddDays(-5),
            LastMessageAt = now.AddDays(-5),
            Summary = "reconnect with low pressure",
            IsFinalized = true,
            IsAnalyzed = true
        }, ct);

        await _chatSessionRepository.UpsertAsync(new ChatSession
        {
            ChatId = chatId,
            SessionIndex = baseIndex + 2,
            StartDate = now.AddDays(-4),
            EndDate = now.AddDays(-1),
            LastMessageAt = now.AddDays(-1),
            Summary = "stable exchange and improving tone",
            IsFinalized = true,
            IsAnalyzed = true
        }, ct);
    }

    private static List<LearningSignal> ParseSignals(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<LearningSignal>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
