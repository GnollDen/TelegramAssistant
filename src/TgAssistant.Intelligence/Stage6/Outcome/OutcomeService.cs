using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Outcome;

public class OutcomeService : IOutcomeService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IDraftActionMatcher _draftActionMatcher;
    private readonly IObservedOutcomeRecorder _observedOutcomeRecorder;
    private readonly ILearningSignalBuilder _learningSignalBuilder;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<OutcomeService> _logger;

    public OutcomeService(
        IStrategyDraftRepository strategyDraftRepository,
        IMessageRepository messageRepository,
        IDraftActionMatcher draftActionMatcher,
        IObservedOutcomeRecorder observedOutcomeRecorder,
        ILearningSignalBuilder learningSignalBuilder,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<OutcomeService> logger)
    {
        _strategyDraftRepository = strategyDraftRepository;
        _messageRepository = messageRepository;
        _draftActionMatcher = draftActionMatcher;
        _observedOutcomeRecorder = observedOutcomeRecorder;
        _learningSignalBuilder = learningSignalBuilder;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task<OutcomeRecordResult> RecordAsync(OutcomeRecordRequest request, CancellationToken ct = default)
    {
        var strategy = await _strategyDraftRepository.GetStrategyRecordByIdAsync(request.StrategyRecordId, ct)
            ?? throw new InvalidOperationException($"Outcome recording failed: strategy record '{request.StrategyRecordId}' not found.");
        if (strategy.CaseId != request.CaseId)
        {
            throw new InvalidOperationException($"Outcome recording failed: strategy record '{request.StrategyRecordId}' does not belong to case '{request.CaseId}'.");
        }

        var draft = await _strategyDraftRepository.GetDraftRecordByIdAsync(request.DraftRecordId, ct)
            ?? throw new InvalidOperationException($"Outcome recording failed: draft record '{request.DraftRecordId}' not found.");
        if (draft.StrategyRecordId != strategy.Id)
        {
            throw new InvalidOperationException("Outcome recording failed: draft is not linked to the provided strategy record.");
        }

        var strategyOptions = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(strategy.Id, ct);
        var primaryOption = strategyOptions
            .FirstOrDefault(x => x.IsPrimary)
            ?? strategyOptions.FirstOrDefault();

        var match = await _draftActionMatcher.MatchAsync(
            draft,
            request.ChatId,
            request.ActualMessageId,
            request.ActualActionText,
            ct);

        var followUpText = request.FollowUpText;
        if (string.IsNullOrWhiteSpace(followUpText) && request.FollowUpMessageId.HasValue)
        {
            followUpText = (await _messageRepository.GetByIdAsync(request.FollowUpMessageId.Value, ct))?.Text;
        }

        var observed = _observedOutcomeRecorder.Assess(followUpText, request.UserOutcomeLabel);
        var learningSignals = _learningSignalBuilder.Build(strategy, primaryOption, draft, match, observed, request.UserOutcomeLabel);

        var normalizedUser = ObservedOutcomeRecorder.NormalizeLabel(request.UserOutcomeLabel);
        var finalOutcome = ResolveFinalOutcomeLabel(normalizedUser, observed.Label);

        var outcome = new DraftOutcome
        {
            DraftId = draft.Id,
            StrategyRecordId = strategy.Id,
            ActualMessageId = match.MatchedMessageId ?? request.ActualMessageId,
            FollowUpMessageId = request.FollowUpMessageId,
            MatchedBy = match.MatchMethod,
            MatchScore = match.MatchScore,
            OutcomeLabel = finalOutcome,
            UserOutcomeLabel = normalizedUser == "unclear" ? null : normalizedUser,
            SystemOutcomeLabel = observed.Label,
            OutcomeConfidence = Math.Clamp((observed.Confidence * 0.7f) + (match.MatchScore * 0.3f), 0f, 1f),
            LearningSignalsJson = JsonSerializer.Serialize(learningSignals, JsonOptions),
            Notes = request.Notes,
            SourceMessageId = request.FollowUpMessageId ?? (match.MatchedMessageId ?? request.ActualMessageId),
            SourceSessionId = draft.SourceSessionId,
            CreatedAt = DateTime.UtcNow
        };

        if (request.Persist)
        {
            outcome = await _strategyDraftRepository.CreateDraftOutcomeAsync(outcome, ct);
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "draft_outcome",
                ObjectId = outcome.Id.ToString(),
                Action = "outcome_recorded",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    request.CaseId,
                    request.ChatId,
                    strategy_record_id = strategy.Id,
                    draft_record_id = draft.Id,
                    outcome.OutcomeLabel,
                    outcome.UserOutcomeLabel,
                    outcome.SystemOutcomeLabel,
                    outcome.MatchScore,
                    outcome.OutcomeConfidence,
                    learning_signals = learningSignals.Select(x => new { x.SignalKey, x.Value, x.Confidence })
                }, JsonOptions),
                Reason = request.SourceId,
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        _logger.LogInformation(
            "Outcome recorded: case_id={CaseId}, chat_id={ChatId}, strategy={StrategyRecordId}, draft={DraftRecordId}, outcome={OutcomeLabel}, match={MatchScore:0.00}",
            request.CaseId,
            request.ChatId,
            strategy.Id,
            draft.Id,
            outcome.OutcomeLabel,
            outcome.MatchScore);

        return new OutcomeRecordResult
        {
            Outcome = outcome,
            Match = match,
            ObservedOutcome = observed,
            LearningSignals = learningSignals
        };
    }

    private static string ResolveFinalOutcomeLabel(string userOutcomeLabel, string systemOutcomeLabel)
    {
        if (userOutcomeLabel != "unclear" && systemOutcomeLabel != "unclear" && userOutcomeLabel != systemOutcomeLabel)
        {
            return "mixed";
        }

        if (userOutcomeLabel != "unclear")
        {
            return userOutcomeLabel;
        }

        return systemOutcomeLabel;
    }
}
