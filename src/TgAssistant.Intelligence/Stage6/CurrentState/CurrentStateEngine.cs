// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class CurrentStateEngine : ICurrentStateEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;
    private readonly ICompetingContextRuntimeService _competingContextRuntimeService;
    private readonly IStateScoreCalculator _scoreCalculator;
    private readonly IStateConfidenceEvaluator _confidenceEvaluator;
    private readonly IDynamicLabelMapper _dynamicLabelMapper;
    private readonly IRelationshipStatusMapper _relationshipStatusMapper;
    private readonly ILogger<CurrentStateEngine> _logger;

    public CurrentStateEngine(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IInboxConflictRepository inboxConflictRepository,
        IStateProfileRepository stateProfileRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService,
        ICompetingContextRuntimeService competingContextRuntimeService,
        IStateScoreCalculator scoreCalculator,
        IStateConfidenceEvaluator confidenceEvaluator,
        IDynamicLabelMapper dynamicLabelMapper,
        IRelationshipStatusMapper relationshipStatusMapper,
        ILogger<CurrentStateEngine> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _stateProfileRepository = stateProfileRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
        _competingContextRuntimeService = competingContextRuntimeService;
        _scoreCalculator = scoreCalculator;
        _confidenceEvaluator = confidenceEvaluator;
        _dynamicLabelMapper = dynamicLabelMapper;
        _relationshipStatusMapper = relationshipStatusMapper;
        _logger = logger;
    }

    public async Task<CurrentStateResult> ComputeAsync(CurrentStateRequest request, CancellationToken ct = default)
    {
        var asOf = request.AsOfUtc ?? DateTime.UtcNow;
        var context = await LoadContextAsync(request, asOf, ct);

        var scores = await _scoreCalculator.CalculateAsync(context, ct);
        ApplyModelInterpretationHints(scores, context.ClarificationAnswers);

        var competingRuntime = await _competingContextRuntimeService.RunAsync(new CompetingContextRuntimeRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            AsOfUtc = asOf,
            Actor = request.Actor,
            SourceType = request.SourceType,
            SourceId = request.SourceId
        }, ct);
        ApplyCompetingStateModifiers(scores, competingRuntime.Interpretation.StateModifiers);

        var confidence = _confidenceEvaluator.Evaluate(scores, context);
        ApplyCompetingConfidenceCap(confidence, competingRuntime.Interpretation.StateModifiers);
        var previousSnapshot = context.HistoricalSnapshots.OrderByDescending(x => x.AsOf).FirstOrDefault();
        var dynamicLabel = _dynamicLabelMapper.Map(scores, context, confidence, previousSnapshot);
        var (relationshipStatus, alternativeStatus) = _relationshipStatusMapper.Map(scores, context, confidence, previousSnapshot);

        var sourceMessage = context.RecentMessages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var sourceSession = context.RecentSessions.OrderByDescending(x => x.EndDate).FirstOrDefault();

        var snapshot = new StateSnapshot
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            AsOf = asOf,
            DynamicLabel = dynamicLabel,
            RelationshipStatus = relationshipStatus,
            AlternativeStatus = alternativeStatus,
            InitiativeScore = scores.Initiative,
            ResponsivenessScore = scores.Responsiveness,
            OpennessScore = scores.Openness,
            WarmthScore = scores.Warmth,
            ReciprocityScore = scores.Reciprocity,
            AmbiguityScore = scores.Ambiguity,
            AvoidanceRiskScore = scores.AvoidanceRisk,
            EscalationReadinessScore = scores.EscalationReadiness,
            ExternalPressureScore = scores.ExternalPressure,
            Confidence = confidence.Confidence,
            PeriodId = context.CurrentPeriod?.Id,
            KeySignalRefsJson = JsonSerializer.Serialize(scores.SignalRefs, JsonOptions),
            RiskRefsJson = JsonSerializer.Serialize(scores.RiskRefs, JsonOptions),
            SourceSessionId = sourceSession?.Id,
            SourceMessageId = sourceMessage?.Id,
            CreatedAt = DateTime.UtcNow
        };

        if (request.Persist)
        {
            snapshot = await _stateProfileRepository.CreateStateSnapshotAsync(snapshot, ct);
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "state_snapshot",
                ObjectId = snapshot.Id.ToString(),
                Action = "state_computed",
                OldValueRef = previousSnapshot == null
                    ? null
                    : JsonSerializer.Serialize(new
                    {
                        previousSnapshot.DynamicLabel,
                        previousSnapshot.RelationshipStatus,
                        previousSnapshot.Confidence,
                        previousSnapshot.AmbiguityScore
                    }, JsonOptions),
                NewValueRef = JsonSerializer.Serialize(new
                {
                    snapshot.DynamicLabel,
                    snapshot.RelationshipStatus,
                    snapshot.AlternativeStatus,
                    snapshot.Confidence,
                    snapshot.AmbiguityScore,
                    competing_source_records = competingRuntime.SourceRecordIds.Count,
                    competing_state_modifier_refs = competingRuntime.Interpretation.StateModifiers.RationaleRefs.Count,
                    confidence.HistoryConflictDetected,
                    scores.HistoricalModulationWeight
                }, JsonOptions),
                Reason = "current_state_engine",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
                request.CaseId,
                request.ChatId,
                Stage6ArtifactTypes.CurrentState,
                ct);
            _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
            {
                ArtifactType = Stage6ArtifactTypes.CurrentState,
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                ScopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId),
                PayloadObjectType = "state_snapshot",
                PayloadObjectId = snapshot.Id.ToString(),
                PayloadJson = JsonSerializer.Serialize(new
                {
                    snapshot.Id,
                    snapshot.DynamicLabel,
                    snapshot.RelationshipStatus,
                    snapshot.AlternativeStatus,
                    snapshot.Confidence,
                    snapshot.AsOf
                }, JsonOptions),
                FreshnessBasisHash = evidence.BasisHash,
                FreshnessBasisJson = evidence.BasisJson,
                GeneratedAt = snapshot.CreatedAt,
                RefreshedAt = snapshot.CreatedAt,
                StaleAt = snapshot.CreatedAt.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.CurrentState)),
                IsStale = false,
                SourceType = request.SourceType,
                SourceId = request.SourceId,
                SourceMessageId = snapshot.SourceMessageId,
                SourceSessionId = snapshot.SourceSessionId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct);
        }

        _logger.LogInformation(
            "Current state computed: case_id={CaseId}, chat_id={ChatId}, dynamic={DynamicLabel}, status={Status}, confidence={Confidence:0.00}, ambiguity={Ambiguity:0.00}",
            request.CaseId,
            request.ChatId,
            snapshot.DynamicLabel,
            snapshot.RelationshipStatus,
            snapshot.Confidence,
            snapshot.AmbiguityScore);

        return new CurrentStateResult
        {
            Snapshot = snapshot,
            Scores = scores,
            Confidence = confidence
        };
    }

    private async Task<CurrentStateContext> LoadContextAsync(CurrentStateRequest request, DateTime asOf, CancellationToken ct)
    {
        var fromUtc = asOf.AddDays(-90);
        var messages = await _messageRepository.GetByChatAndPeriodAsync(request.ChatId, fromUtc, asOf, 4000, ct);

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, [])
            .Where(x => x.EndDate <= asOf)
            .OrderByDescending(x => x.EndDate)
            .Take(8)
            .ToList();

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .ToList();

        var currentPeriod = periods
            .Where(x => x.StartAt <= asOf && (x.EndAt == null || x.EndAt >= asOf))
            .OrderByDescending(x => x.StartAt)
            .FirstOrDefault()
            ?? periods.OrderByDescending(x => x.StartAt).FirstOrDefault();

        var questions = await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct);
        questions = questions
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.CreatedAt <= asOf)
            .ToList();

        var answers = new List<ClarificationAnswer>();
        foreach (var question in questions.Take(80))
        {
            var byQuestion = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
            answers.AddRange(byQuestion.Where(x => x.CreatedAt <= asOf));
        }

        var offlineEvents = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.TimestampStart <= asOf)
            .OrderByDescending(x => x.TimestampStart)
            .Take(40)
            .ToList();

        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(30)
            .ToList();

        var historicalSnapshots = await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 12, ct);

        return new CurrentStateContext
        {
            AsOfUtc = asOf,
            CurrentPeriod = currentPeriod,
            Periods = periods,
            RecentMessages = messages.OrderBy(x => x.Timestamp).ToList(),
            RecentSessions = sessions,
            ClarificationQuestions = questions,
            ClarificationAnswers = answers.OrderByDescending(x => x.CreatedAt).ToList(),
            OfflineEvents = offlineEvents,
            Conflicts = conflicts,
            HistoricalSnapshots = historicalSnapshots.Where(x => x.AsOf <= asOf).ToList()
        };
    }

    private static void ApplyModelInterpretationHints(StateScoreResult scores, IReadOnlyList<ClarificationAnswer> answers)
    {
        var modelAnswers = answers
            .Where(x => x.SourceClass.Contains("model", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();

        if (modelAnswers.Count == 0)
        {
            return;
        }

        var positive = modelAnswers.Count(x => x.AnswerValue.Contains("yes", StringComparison.OrdinalIgnoreCase)
                                               || x.AnswerValue.Contains("closer", StringComparison.OrdinalIgnoreCase)
                                               || x.AnswerValue.Contains("reconnect", StringComparison.OrdinalIgnoreCase));
        var negative = modelAnswers.Count(x => x.AnswerValue.Contains("no", StringComparison.OrdinalIgnoreCase)
                                               || x.AnswerValue.Contains("distance", StringComparison.OrdinalIgnoreCase)
                                               || x.AnswerValue.Contains("avoid", StringComparison.OrdinalIgnoreCase));

        if (positive > 0 && negative == 0)
        {
            scores.EscalationReadiness = Math.Clamp(scores.EscalationReadiness + 0.05f, 0f, 1f);
            scores.Ambiguity = Math.Clamp(scores.Ambiguity - 0.04f, 0f, 1f);
            return;
        }

        if (negative > 0 && positive == 0)
        {
            scores.EscalationReadiness = Math.Clamp(scores.EscalationReadiness - 0.05f, 0f, 1f);
            scores.AvoidanceRisk = Math.Clamp(scores.AvoidanceRisk + 0.05f, 0f, 1f);
            return;
        }

        scores.Ambiguity = Math.Clamp(scores.Ambiguity + 0.05f, 0f, 1f);
    }

    private static void ApplyCompetingStateModifiers(StateScoreResult scores, CompetingStateModifiers modifiers)
    {
        if (!modifiers.IsAdditiveOnly || modifiers.RationaleRefs.Count == 0)
        {
            return;
        }

        scores.ExternalPressure = Math.Clamp(scores.ExternalPressure + modifiers.ExternalPressureDelta, 0f, 1f);
        scores.Ambiguity = Math.Clamp(scores.Ambiguity + modifiers.AmbiguityDelta, 0f, 1f);

        scores.SignalRefs.Add($"competing_context:state_modifier_refs={modifiers.RationaleRefs.Count}");
        scores.RiskRefs.Add("competing_context:review_required");
        if (modifiers.ExternalPressureDelta > 0f)
        {
            scores.RiskRefs.Add("competing_context:external_pressure");
        }

        if (modifiers.AmbiguityDelta > 0f)
        {
            scores.RiskRefs.Add("competing_context:ambiguity_increase");
        }
    }

    private static void ApplyCompetingConfidenceCap(StateConfidenceResult confidence, CompetingStateModifiers modifiers)
    {
        if (modifiers.RationaleRefs.Count == 0)
        {
            return;
        }

        confidence.Confidence = Math.Min(confidence.Confidence, modifiers.ConfidenceCap);
        confidence.HighAmbiguity |= modifiers.AmbiguityDelta > 0f;
    }
}
