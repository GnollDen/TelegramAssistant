using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class PeriodizationService : IPeriodizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IPeriodBoundaryDetector _boundaryDetector;
    private readonly ITimelineAssembler _timelineAssembler;
    private readonly ITransitionBuilder _transitionBuilder;
    private readonly IPeriodEvidenceAssembler _evidenceAssembler;
    private readonly IPeriodProposalService _proposalService;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<PeriodizationService> _logger;

    public PeriodizationService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IOfflineEventRepository offlineEventRepository,
        IClarificationRepository clarificationRepository,
        IInboxConflictRepository inboxConflictRepository,
        IPeriodRepository periodRepository,
        IPeriodBoundaryDetector boundaryDetector,
        ITimelineAssembler timelineAssembler,
        ITransitionBuilder transitionBuilder,
        IPeriodEvidenceAssembler evidenceAssembler,
        IPeriodProposalService proposalService,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<PeriodizationService> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _offlineEventRepository = offlineEventRepository;
        _clarificationRepository = clarificationRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _periodRepository = periodRepository;
        _boundaryDetector = boundaryDetector;
        _timelineAssembler = timelineAssembler;
        _transitionBuilder = transitionBuilder;
        _evidenceAssembler = evidenceAssembler;
        _proposalService = proposalService;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task<PeriodizationRunResult> RunAsync(PeriodizationRunRequest request, CancellationToken ct = default)
    {
        var messages = await LoadMessagesAsync(request, ct);
        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, []).OrderBy(x => x.StartDate).ToList();
        var offlineEvents = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderBy(x => x.TimestampStart)
            .ToList();
        var clarificationQuestions = await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct);

        var answerByQuestion = new Dictionary<Guid, List<ClarificationAnswer>>();
        foreach (var question in clarificationQuestions)
        {
            var answers = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
            answerByQuestion[question.Id] = answers;
        }

        var conflicts = await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct);
        var boundaries = await _boundaryDetector.DetectAsync(request, messages, sessions, offlineEvents, clarificationQuestions, ct);
        var assembledPeriods = await _timelineAssembler.AssembleAsync(request, messages, sessions, boundaries, ct);

        var persistedPeriods = new List<Period>(assembledPeriods.Count);
        foreach (var basePeriod in assembledPeriods)
        {
            var periodMessages = messages
                .Where(x => x.Timestamp >= basePeriod.StartAt && x.Timestamp <= (basePeriod.EndAt ?? DateTime.MaxValue))
                .ToList();
            var periodClarificationQuestions = clarificationQuestions
                .Where(x =>
                    x.PeriodId == basePeriod.Id ||
                    (x.CreatedAt >= basePeriod.StartAt && x.CreatedAt <= (basePeriod.EndAt ?? DateTime.MaxValue)))
                .ToList();
            var periodClarificationAnswers = periodClarificationQuestions
                .SelectMany(x => answerByQuestion.GetValueOrDefault(x.Id, []))
                .ToList();

            var evidence = await _evidenceAssembler.BuildEvidenceAsync(
                request,
                basePeriod,
                periodMessages,
                sessions,
                offlineEvents,
                periodClarificationQuestions,
                periodClarificationAnswers,
                ct);

            basePeriod.KeySignalsJson = JsonSerializer.Serialize(evidence.KeySignals, JsonOptions);
            basePeriod.EvidenceRefsJson = JsonSerializer.Serialize(evidence.EvidenceRefs, JsonOptions);
            basePeriod.OpenQuestionsCount = evidence.OpenQuestionsCount;
            basePeriod.WhatHelped = evidence.WhatHelped;
            basePeriod.WhatHurt = evidence.WhatHurt;
            basePeriod.InterpretationConfidence = evidence.InterpretationConfidence;
            basePeriod.Summary = BuildPeriodSummary(basePeriod, periodMessages.Count, offlineEvents, evidence);
            basePeriod.ReviewPriority = CalculateReviewPriority(basePeriod, conflicts);

            if (!request.Persist)
            {
                persistedPeriods.Add(basePeriod);
                continue;
            }

            var persisted = await _periodRepository.CreatePeriodAsync(basePeriod, ct);
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "period",
                ObjectId = persisted.Id.ToString(),
                Action = "periodization_created",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    persisted.StartAt,
                    persisted.EndAt,
                    persisted.BoundaryConfidence,
                    persisted.InterpretationConfidence,
                    persisted.ReviewPriority
                }, JsonOptions),
                Reason = "periodization_mvp",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            persistedPeriods.Add(persisted);
        }

        var transitions = await _transitionBuilder.BuildAsync(request, persistedPeriods, boundaries, ct);
        var persistedTransitions = new List<PeriodTransition>(transitions.Count);
        foreach (var transition in transitions)
        {
            if (!request.Persist)
            {
                persistedTransitions.Add(transition);
                continue;
            }

            var persisted = await _periodRepository.CreateTransitionAsync(transition, ct);
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "period_transition",
                ObjectId = persisted.Id.ToString(),
                Action = persisted.IsResolved ? "transition_created" : "unresolved_transition_created",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    persisted.FromPeriodId,
                    persisted.ToPeriodId,
                    persisted.TransitionType,
                    persisted.Confidence,
                    persisted.IsResolved,
                    persisted.GapId
                }, JsonOptions),
                Reason = "periodization_mvp",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
            persistedTransitions.Add(persisted);
        }

        var proposals = await _proposalService.BuildAndPersistProposalsAsync(request, persistedPeriods, persistedTransitions, conflicts, ct);

        _logger.LogInformation(
            "Periodization run completed: case_id={CaseId}, periods={PeriodCount}, transitions={TransitionCount}, proposals={ProposalCount}",
            request.CaseId,
            persistedPeriods.Count,
            persistedTransitions.Count,
            proposals.Count);

        return new PeriodizationRunResult
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            Periods = persistedPeriods,
            Transitions = persistedTransitions,
            Proposals = proposals.ToList()
        };
    }

    private async Task<List<Message>> LoadMessagesAsync(PeriodizationRunRequest request, CancellationToken ct)
    {
        if (request.FromUtc.HasValue && request.ToUtc.HasValue)
        {
            return await _messageRepository.GetByChatAndPeriodAsync(request.ChatId, request.FromUtc.Value, request.ToUtc.Value, 50000, ct);
        }

        return await _messageRepository.GetProcessedByChatAsync(request.ChatId, 50000, ct);
    }

    private static short CalculateReviewPriority(Period period, IReadOnlyCollection<ConflictRecord> conflicts)
    {
        var priority = 0;
        if (period.BoundaryConfidence < 0.55f || period.InterpretationConfidence < 0.55f)
        {
            priority += 2;
        }

        if (period.IsOpen || period.EndAt == null || period.EndAt.Value >= DateTime.UtcNow.AddDays(-7))
        {
            priority += 2;
        }

        var periodConflictCount = conflicts.Count(x => x.PeriodId == period.Id || x.Status.Equals("open", StringComparison.OrdinalIgnoreCase));
        if (periodConflictCount > 0)
        {
            priority += 2;
        }

        return (short)Math.Clamp(priority, 0, 5);
    }

    private static string BuildPeriodSummary(Period period, int messageCount, IReadOnlyList<OfflineEvent> offlineEvents, PeriodEvidencePack evidence)
    {
        var eventCount = offlineEvents.Count(x => x.TimestampStart >= period.StartAt && x.TimestampStart <= (period.EndAt ?? DateTime.MaxValue));
        return $"Period {period.Label}: messages={messageCount}, events={eventCount}, open_questions={evidence.OpenQuestionsCount}.";
    }
}
