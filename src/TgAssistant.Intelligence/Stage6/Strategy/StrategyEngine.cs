using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class StrategyEngine : IStrategyEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IStrategyOptionGenerator _optionGenerator;
    private readonly IStrategyRiskEvaluator _riskEvaluator;
    private readonly IStrategyRanker _ranker;
    private readonly IStrategyConfidenceEvaluator _confidenceEvaluator;
    private readonly IMicroStepPlanner _microStepPlanner;
    private readonly ILogger<StrategyEngine> _logger;

    public StrategyEngine(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IInboxConflictRepository inboxConflictRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        IStrategyOptionGenerator optionGenerator,
        IStrategyRiskEvaluator riskEvaluator,
        IStrategyRanker ranker,
        IStrategyConfidenceEvaluator confidenceEvaluator,
        IMicroStepPlanner microStepPlanner,
        ILogger<StrategyEngine> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _optionGenerator = optionGenerator;
        _riskEvaluator = riskEvaluator;
        _ranker = ranker;
        _confidenceEvaluator = confidenceEvaluator;
        _microStepPlanner = microStepPlanner;
        _logger = logger;
    }

    public async Task<StrategyEngineResult> RunAsync(StrategyEngineRequest request, CancellationToken ct = default)
    {
        var asOf = request.AsOfUtc ?? DateTime.UtcNow;
        var context = await LoadContextAsync(request, asOf, ct);

        var candidates = (await _optionGenerator.GenerateAsync(context, ct)).ToList();
        foreach (var candidate in candidates)
        {
            var risk = _riskEvaluator.Evaluate(context, candidate);
            candidate.RiskLabels = risk.Labels;
            candidate.RiskScore = risk.RiskScore;
        }

        var ranked = _ranker.Rank(context, candidates);
        var confidence = _confidenceEvaluator.Evaluate(context, ranked);
        var primary = ranked.First();
        var (microStep, horizon) = _microStepPlanner.Plan(context, primary, confidence);
        var whyNotNotes = BuildWhyNotNotes(primary, ranked);

        var sourceMessage = context.RecentMessages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var sourceSession = context.RecentSessions.OrderByDescending(x => x.EndDate).FirstOrDefault();
        var record = new StrategyRecord
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            PeriodId = context.CurrentPeriod?.Id,
            StateSnapshotId = context.CurrentState?.Id,
            StrategyConfidence = confidence.Confidence,
            RecommendedGoal = primary.Purpose,
            WhyNotOthers = whyNotNotes,
            MicroStep = microStep,
            HorizonJson = confidence.HorizonAllowed ? JsonSerializer.Serialize(horizon, JsonOptions) : null,
            SourceSessionId = sourceSession?.Id,
            SourceMessageId = sourceMessage?.Id,
            CreatedAt = DateTime.UtcNow
        };

        if (request.Persist)
        {
            var previousRecord = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
                .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);

            record = await _strategyDraftRepository.CreateStrategyRecordAsync(record, ct);

            foreach (var option in ranked)
            {
                await _strategyDraftRepository.CreateStrategyOptionAsync(new StrategyOption
                {
                    StrategyRecordId = record.Id,
                    ActionType = option.ActionType,
                    Summary = option.Summary,
                    Purpose = option.Purpose,
                    Risk = JsonSerializer.Serialize(new
                    {
                        labels = option.RiskLabels,
                        score = option.RiskScore
                    }, JsonOptions),
                    WhenToUse = option.WhenToUse,
                    SuccessSigns = option.SuccessSigns,
                    FailureSigns = option.FailureSigns,
                    IsPrimary = option.IsPrimary
                }, ct);
            }

            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "strategy_record",
                ObjectId = record.Id.ToString(),
                Action = "strategy_generated",
                OldValueRef = previousRecord == null
                    ? null
                    : JsonSerializer.Serialize(new
                    {
                        previousRecord.Id,
                        previousRecord.StrategyConfidence,
                        previousRecord.RecommendedGoal
                    }, JsonOptions),
                NewValueRef = JsonSerializer.Serialize(new
                {
                    record.CaseId,
                    record.ChatId,
                    record.StrategyConfidence,
                    record.RecommendedGoal,
                    record.MicroStep,
                    horizon_enabled = confidence.HorizonAllowed,
                    options = ranked.Select(x => new
                    {
                        x.ActionType,
                        x.FinalScore,
                        x.RiskScore,
                        x.IsPrimary
                    })
                }, JsonOptions),
                Reason = request.SourceId,
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        _logger.LogInformation(
            "Strategy run completed: case_id={CaseId}, chat_id={ChatId}, options={OptionCount}, primary={Primary}, confidence={Confidence:0.00}, high_uncertainty={HighUncertainty}",
            request.CaseId,
            request.ChatId,
            ranked.Count,
            primary.ActionType,
            confidence.Confidence,
            confidence.HighUncertainty);

        return new StrategyEngineResult
        {
            Record = record,
            Options = ranked.Select(x => new StrategyOption
            {
                StrategyRecordId = record.Id,
                ActionType = x.ActionType,
                Summary = x.Summary,
                Purpose = x.Purpose,
                Risk = JsonSerializer.Serialize(new
                {
                    labels = x.RiskLabels,
                    score = x.RiskScore
                }, JsonOptions),
                WhenToUse = x.WhenToUse,
                SuccessSigns = x.SuccessSigns,
                FailureSigns = x.FailureSigns,
                IsPrimary = x.IsPrimary
            }).ToList(),
            MicroStep = microStep,
            Horizon = horizon.ToList(),
            WhyNotNotes = whyNotNotes,
            Confidence = confidence
        };
    }

    private async Task<StrategyEvaluationContext> LoadContextAsync(StrategyEngineRequest request, DateTime asOf, CancellationToken ct)
    {
        // case_id is the analysis scope. chat_id anchors canonical source objects within the case.
        var fromUtc = asOf.AddDays(-120);
        var messages = await _messageRepository.GetByChatAndPeriodAsync(request.ChatId, fromUtc, asOf, 5000, ct);
        var orderedMessages = messages.OrderBy(x => x.Timestamp).ToList();

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, [])
            .Where(x => x.EndDate <= asOf)
            .OrderByDescending(x => x.EndDate)
            .Take(10)
            .ToList();

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.StartAt <= asOf)
            .OrderByDescending(x => x.StartAt)
            .ToList();
        var currentPeriod = periods.FirstOrDefault(x => x.IsOpen) ?? periods.FirstOrDefault();

        var states = await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 30, ct);
        var currentState = states
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.AsOf <= asOf)
            .OrderByDescending(x => x.AsOf)
            .FirstOrDefault();

        var questions = await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct);
        questions = questions
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.CreatedAt <= asOf)
            .OrderByDescending(x => x.CreatedAt)
            .Take(80)
            .ToList();

        var answers = new List<ClarificationAnswer>();
        foreach (var question in questions.Take(50))
        {
            var byQuestion = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
            answers.AddRange(byQuestion.Where(x => x.CreatedAt <= asOf));
        }

        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(50)
            .ToList();

        var senderCounts = orderedMessages
            .Where(x => x.SenderId > 0)
            .GroupBy(x => x.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var selfSenderId = request.SelfSenderId
            ?? senderCounts.FirstOrDefault()?.SenderId
            ?? 1L;
        var otherSenderId = senderCounts.Where(x => x.SenderId != selfSenderId).Select(x => x.SenderId).FirstOrDefault();
        if (otherSenderId <= 0)
        {
            otherSenderId = selfSenderId + 1;
        }

        var profileSnapshots = new List<ProfileSnapshot>();
        var profileTraits = new List<ProfileTrait>();
        await LoadLatestProfileBundleAsync(request.CaseId, "self", selfSenderId.ToString(), profileSnapshots, profileTraits, ct);
        await LoadLatestProfileBundleAsync(request.CaseId, "other", otherSenderId.ToString(), profileSnapshots, profileTraits, ct);
        await LoadLatestProfileBundleAsync(request.CaseId, "pair", $"{selfSenderId}:{otherSenderId}", profileSnapshots, profileTraits, ct);

        var styleHint = profileTraits
            .Where(x => x.TraitKey == "communication_style")
            .OrderByDescending(x => x.Confidence)
            .Select(x => x.ValueLabel)
            .FirstOrDefault() ?? "balanced_pragmatic";

        var highUncertainty = request.ForceHighUncertainty
            ?? DetectHighUncertainty(currentState, questions, conflicts);

        return new StrategyEvaluationContext
        {
            AsOfUtc = asOf,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = selfSenderId,
            OtherSenderId = otherSenderId,
            CurrentPeriod = currentPeriod,
            CurrentState = currentState,
            Periods = periods,
            ClarificationQuestions = questions,
            ClarificationAnswers = answers.OrderByDescending(x => x.CreatedAt).ToList(),
            Conflicts = conflicts,
            RecentMessages = orderedMessages,
            RecentSessions = sessions,
            ProfileSnapshots = profileSnapshots,
            ProfileTraits = profileTraits,
            SelfStyleHint = styleHint,
            HighUncertainty = highUncertainty
        };
    }

    private async Task LoadLatestProfileBundleAsync(
        long caseId,
        string subjectType,
        string subjectId,
        ICollection<ProfileSnapshot> snapshots,
        ICollection<ProfileTrait> traits,
        CancellationToken ct)
    {
        var rows = await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(caseId, subjectType, subjectId, ct);
        var latest = rows
            .OrderBy(x => x.PeriodId.HasValue ? 1 : 0)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (latest == null)
        {
            return;
        }

        snapshots.Add(latest);
        foreach (var trait in await _stateProfileRepository.GetProfileTraitsBySnapshotIdAsync(latest.Id, ct))
        {
            traits.Add(trait);
        }
    }

    private static bool DetectHighUncertainty(
        StateSnapshot? currentState,
        IReadOnlyList<ClarificationQuestion> questions,
        IReadOnlyList<ConflictRecord> conflicts)
    {
        var ambiguity = currentState?.AmbiguityScore ?? 0.75f;
        var stateConfidence = currentState?.Confidence ?? 0.45f;
        var hasBlockingQuestions = questions.Any(x =>
            x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
            && x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase));
        var hasOpenConflicts = conflicts.Any(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase));

        return ambiguity >= 0.62f || stateConfidence < 0.55f || hasBlockingQuestions || hasOpenConflicts;
    }

    private static string BuildWhyNotNotes(
        StrategyCandidateOption primary,
        IReadOnlyList<StrategyCandidateOption> ranked)
    {
        var alternatives = ranked
            .Where(x => !x.IsPrimary)
            .Take(2)
            .Select(x =>
            {
                var reasons = new List<string>();
                var delta = primary.FinalScore - x.FinalScore;
                if (delta > 0.12f)
                {
                    reasons.Add("weaker state/profile fit");
                }

                if (x.RiskScore > primary.RiskScore + 0.08f)
                {
                    reasons.Add("higher risk under current uncertainty");
                }

                if (x.ActionType is "invite" or "deepen" or "light_test")
                {
                    reasons.Add("more escalation pressure than needed now");
                }

                if (reasons.Count == 0)
                {
                    reasons.Add("less favorable ranking after risk-adjusted scoring");
                }

                return $"{x.ActionType}: {string.Join(", ", reasons)}";
            })
            .ToList();

        return alternatives.Count == 0
            ? "No meaningful alternatives were available beyond the primary safe option."
            : string.Join(" | ", alternatives);
    }
}
