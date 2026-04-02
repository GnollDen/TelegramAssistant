// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

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
        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, []).OrderBy(x => x.StartDate).ToList();
        var messages = await LoadMessagesAsync(request, sessions, ct);
        WarnOnTimestampCoverageMismatch(request, sessions, messages);
        var offlineEvents = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderBy(x => x.TimestampStart)
            .ToList();
        var clarificationQuestions = await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct);
        var existingPeriodsForScope = request.Persist
            ? (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
                .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
                .ToList()
            : [];

        if (request.Persist && existingPeriodsForScope.Count > 0)
        {
            // Stabilization policy: until timeline revision/supersede lands, periodization is append-only and review-driven.
            _logger.LogWarning(
                "Periodization append-only run on existing timeline scope: case_id={CaseId}, chat_id={ChatId}, existing_periods={ExistingPeriodCount}. Supersede/revision is not auto-applied.",
                request.CaseId,
                request.ChatId,
                existingPeriodsForScope.Count);

            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "timeline_scope",
                ObjectId = $"{request.CaseId}:{request.ChatId}",
                Action = "periodization_append_without_supersede",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    request.CaseId,
                    request.ChatId,
                    ExistingPeriodCount = existingPeriodsForScope.Count
                }, JsonOptions),
                Reason = "timeline_revision_policy_append_only",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        var answerByQuestion = new Dictionary<Guid, List<ClarificationAnswer>>();
        foreach (var question in clarificationQuestions)
        {
            var answers = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
            answerByQuestion[question.Id] = answers;
        }

        var conflicts = await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct);
        var boundaries = await _boundaryDetector.DetectAsync(request, messages, sessions, offlineEvents, clarificationQuestions, ct);
        var assembledPeriods = await _timelineAssembler.AssembleAsync(request, messages, sessions, boundaries, ct);

        var materializedPeriods = new List<Period>(assembledPeriods.Count);
        var suppressedNoSignalPeriods = 0;
        foreach (var basePeriod in assembledPeriods)
        {
            var periodMessages = messages
                .Where(x => x.Timestamp >= basePeriod.StartAt && x.Timestamp <= (basePeriod.EndAt ?? DateTime.MaxValue))
                .ToList();
            var periodOfflineEvents = offlineEvents
                .Where(x => x.TimestampStart >= basePeriod.StartAt && x.TimestampStart <= (basePeriod.EndAt ?? DateTime.MaxValue))
                .ToList();
            var periodAudioSnippets = await CollectAudioSnippetsAsync(periodOfflineEvents, ct);
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
                periodOfflineEvents,
                periodAudioSnippets,
                periodClarificationQuestions,
                periodClarificationAnswers,
                ct);

            basePeriod.KeySignalsJson = JsonSerializer.Serialize(evidence.KeySignals, JsonOptions);
            basePeriod.EvidenceRefsJson = JsonSerializer.Serialize(evidence.EvidenceRefs, JsonOptions);
            basePeriod.OpenQuestionsCount = evidence.OpenQuestionsCount;
            basePeriod.WhatHelped = evidence.WhatHelped;
            basePeriod.WhatHurt = evidence.WhatHurt;
            basePeriod.InterpretationConfidence = evidence.InterpretationConfidence;
            basePeriod.Summary = BuildPeriodSummary(basePeriod, periodMessages.Count, periodOfflineEvents, evidence);
            basePeriod.ReviewPriority = CalculateReviewPriority(basePeriod, conflicts);
            basePeriod.IsSensitive = IsSensitivePeriod(periodMessages, periodOfflineEvents, periodClarificationQuestions);
            basePeriod.StatusSnapshot = BuildStatusSnapshot(periodMessages);
            basePeriod.DynamicSnapshot = BuildDynamicSnapshot(periodMessages, periodClarificationAnswers);
            basePeriod.Lessons = BuildLessons(evidence);
            basePeriod.StrategicPatterns = BuildStrategicPatterns(periodMessages, periodClarificationAnswers);

            if (ShouldSuppressNoSignalPeriod(periodMessages, periodOfflineEvents, periodAudioSnippets, periodClarificationAnswers))
            {
                suppressedNoSignalPeriods++;
                _logger.LogInformation(
                    "Period suppressed by quality gate: case_id={CaseId}, chat_id={ChatId}, period_label={PeriodLabel}, start_at={StartAt}, end_at={EndAt}",
                    request.CaseId,
                    request.ChatId,
                    basePeriod.Label,
                    basePeriod.StartAt,
                    basePeriod.EndAt);
                continue;
            }

            materializedPeriods.Add(basePeriod);
        }

        NormalizePeriodSequence(materializedPeriods, request.ToUtc);

        var persistedPeriods = new List<Period>(materializedPeriods.Count);
        foreach (var period in materializedPeriods)
        {
            if (!request.Persist)
            {
                persistedPeriods.Add(period);
                continue;
            }

            var persisted = await _periodRepository.CreatePeriodAsync(period, ct);
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

            _ = await _offlineEventRepository.AssignPeriodByTimeRangeAsync(
                request.CaseId,
                request.ChatId,
                persisted.Id,
                persisted.StartAt,
                persisted.EndAt,
                ct);

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
            "Periodization run completed: case_id={CaseId}, periods={PeriodCount}, transitions={TransitionCount}, proposals={ProposalCount}, suppressed_no_signal_periods={SuppressedNoSignalPeriods}",
            request.CaseId,
            persistedPeriods.Count,
            persistedTransitions.Count,
            proposals.Count,
            suppressedNoSignalPeriods);

        return new PeriodizationRunResult
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            Periods = persistedPeriods,
            Transitions = persistedTransitions,
            Proposals = proposals.ToList()
        };
    }

    private async Task<List<Message>> LoadMessagesAsync(PeriodizationRunRequest request, IReadOnlyList<ChatSession> sessions, CancellationToken ct)
    {
        if (request.FromUtc.HasValue && request.ToUtc.HasValue)
        {
            var fromUtc = request.FromUtc.Value <= request.ToUtc.Value ? request.FromUtc.Value : request.ToUtc.Value;
            var toUtc = request.FromUtc.Value <= request.ToUtc.Value ? request.ToUtc.Value : request.FromUtc.Value;
            return await _messageRepository.GetProcessedByChatAndTimeRangeAsync(request.ChatId, fromUtc, toUtc, ct);
        }

        if (sessions.Count > 0)
        {
            var fromUtc = sessions.Min(x => x.StartDate);
            var toUtc = sessions.Max(x => x.LastMessageAt > x.EndDate ? x.LastMessageAt : x.EndDate);
            return await _messageRepository.GetProcessedByChatAndTimeRangeAsync(request.ChatId, fromUtc, toUtc, ct);
        }

        return await _messageRepository.GetProcessedByChatAndTimeRangeAsync(request.ChatId, DateTime.UnixEpoch, DateTime.UtcNow.AddDays(1), ct);
    }

    private static bool ShouldSuppressNoSignalPeriod(
        IReadOnlyList<Message> periodMessages,
        IReadOnlyList<OfflineEvent> periodOfflineEvents,
        IReadOnlyList<AudioSnippet> periodAudioSnippets,
        IReadOnlyList<ClarificationAnswer> periodClarificationAnswers)
    {
        return periodMessages.Count == 0
               && periodOfflineEvents.Count == 0
               && periodAudioSnippets.Count == 0
               && periodClarificationAnswers.Count == 0;
    }

    private static void NormalizePeriodSequence(List<Period> periods, DateTime? toUtc)
    {
        if (periods.Count == 0)
        {
            return;
        }

        periods.Sort((left, right) => left.StartAt.CompareTo(right.StartAt));
        var now = DateTime.UtcNow;
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var isLast = i == periods.Count - 1;
            period.Label = $"period_{i + 1:00}";
            period.IsOpen = !toUtc.HasValue && isLast;

            if (period.IsOpen)
            {
                period.EndAt = null;
            }
            else if (!isLast && period.EndAt == null)
            {
                period.EndAt = periods[i + 1].StartAt;
            }

            period.UpdatedAt = now;
        }
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

    private void WarnOnTimestampCoverageMismatch(
        PeriodizationRunRequest request,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<Message> messages)
    {
        if (sessions.Count == 0 || messages.Count == 0)
        {
            return;
        }

        var sessionStart = sessions.Min(x => x.StartDate);
        var sessionEnd = sessions.Max(x => x.LastMessageAt > x.EndDate ? x.LastMessageAt : x.EndDate);
        var loadedStart = messages.Min(x => x.Timestamp);
        var loadedEnd = messages.Max(x => x.Timestamp);
        if (loadedStart > sessionStart || loadedEnd < sessionEnd)
        {
            _logger.LogWarning(
                "Periodization message coverage is narrower than session range: case_id={CaseId}, chat_id={ChatId}, loaded_start={LoadedStart}, loaded_end={LoadedEnd}, session_start={SessionStart}, session_end={SessionEnd}, loaded_messages={LoadedMessageCount}, sessions={SessionCount}",
                request.CaseId,
                request.ChatId,
                loadedStart,
                loadedEnd,
                sessionStart,
                sessionEnd,
                messages.Count,
                sessions.Count);
        }
    }

    private static string BuildPeriodSummary(Period period, int messageCount, IReadOnlyList<OfflineEvent> offlineEvents, PeriodEvidencePack evidence)
    {
        var eventCount = offlineEvents.Count(x => x.TimestampStart >= period.StartAt && x.TimestampStart <= (period.EndAt ?? DateTime.MaxValue));
        return $"Period {period.Label}: messages={messageCount}, events={eventCount}, open_questions={evidence.OpenQuestionsCount}.";
    }

    private async Task<List<AudioSnippet>> CollectAudioSnippetsAsync(IReadOnlyList<OfflineEvent> periodEvents, CancellationToken ct)
    {
        var snippets = new List<AudioSnippet>();
        foreach (var evt in periodEvents)
        {
            var assets = await _offlineEventRepository.GetAudioAssetsByOfflineEventIdAsync(evt.Id, ct);
            foreach (var asset in assets)
            {
                var byAsset = await _offlineEventRepository.GetAudioSnippetsByAssetIdAsync(asset.Id, ct);
                snippets.AddRange(byAsset);
            }
        }

        return snippets
            .OrderByDescending(x => x.CreatedAt)
            .Take(16)
            .ToList();
    }

    private static bool IsSensitivePeriod(
        IReadOnlyList<Message> periodMessages,
        IReadOnlyList<OfflineEvent> periodEvents,
        IReadOnlyList<ClarificationQuestion> periodQuestions)
    {
        var sensitiveQuestion = periodQuestions.Any(x => x.QuestionType.Contains("sensitive", StringComparison.OrdinalIgnoreCase));
        if (sensitiveQuestion)
        {
            return true;
        }

        var sensitiveEvent = periodEvents.Any(x => x.EventType.Contains("conflict", StringComparison.OrdinalIgnoreCase) || x.EventType.Contains("health", StringComparison.OrdinalIgnoreCase));
        if (sensitiveEvent)
        {
            return true;
        }

        return periodMessages.Any(x =>
            (x.Text ?? string.Empty).Contains("private", StringComparison.OrdinalIgnoreCase) ||
            (x.Text ?? string.Empty).Contains("confidential", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildStatusSnapshot(IReadOnlyList<Message> periodMessages)
    {
        var signal = periodMessages.Select(x => x.Text ?? string.Empty).ToList();
        var positive = signal.Count(x => x.Contains("thanks", StringComparison.OrdinalIgnoreCase) || x.Contains("ok", StringComparison.OrdinalIgnoreCase));
        var negative = signal.Count(x => x.Contains("later", StringComparison.OrdinalIgnoreCase) || x.Contains("no", StringComparison.OrdinalIgnoreCase) || x.Contains("busy", StringComparison.OrdinalIgnoreCase));

        if (positive > negative + 2)
        {
            return "mostly_positive";
        }

        if (negative > positive + 2)
        {
            return "mostly_negative";
        }

        return "mixed";
    }

    private static string BuildDynamicSnapshot(IReadOnlyList<Message> periodMessages, IReadOnlyList<ClarificationAnswer> answers)
    {
        var senderVariety = periodMessages.Select(x => x.SenderId).Distinct().Count();
        var pace = periodMessages.Count <= 1
            ? 0
            : (periodMessages.Max(x => x.Timestamp) - periodMessages.Min(x => x.Timestamp)).TotalHours / periodMessages.Count;
        if (answers.Any(x => x.AnswerValue.Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            return "clarified_shift";
        }

        if (senderVariety > 1 && pace < 4)
        {
            return "active_exchange";
        }

        if (pace > 24)
        {
            return "slow_exchange";
        }

        return "stable";
    }

    private static string BuildLessons(PeriodEvidencePack evidence)
    {
        if (evidence.OpenQuestionsCount > 0)
        {
            return "Clarification gaps still affect interpretation quality; keep review loop active.";
        }

        if (evidence.KeySignals.Any(x => x.Contains("offline_event_count:") && !x.EndsWith(":0", StringComparison.Ordinal)))
        {
            return "Offline events can materially reshape timeline interpretation and should be captured promptly.";
        }

        return "Consistent interaction traces improve period interpretability.";
    }

    private static string BuildStrategicPatterns(IReadOnlyList<Message> periodMessages, IReadOnlyList<ClarificationAnswer> answers)
    {
        if (answers.Count >= 2)
        {
            return "Use clarification-confirmed milestones as anchors for downstream reasoning.";
        }

        if (periodMessages.Count >= 30)
        {
            return "Dense communication windows are suitable anchors for phase-level interpretation.";
        }

        return "Rely on conservative transition assumptions until more evidence appears.";
    }
}
