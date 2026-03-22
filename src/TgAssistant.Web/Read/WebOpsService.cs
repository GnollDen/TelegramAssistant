using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using System.Text.Json;

namespace TgAssistant.Web.Read;

public class WebOpsService : IWebOpsService
{
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly INetworkGraphService _networkGraphService;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IBudgetOpsRepository _budgetOpsRepository;
    private readonly IEvalRepository _evalRepository;

    public WebOpsService(
        IInboxConflictRepository inboxConflictRepository,
        IClarificationRepository clarificationRepository,
        IPeriodRepository periodRepository,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IOfflineEventRepository offlineEventRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        INetworkGraphService networkGraphService,
        IDomainReviewEventRepository domainReviewEventRepository,
        IBudgetOpsRepository budgetOpsRepository,
        IEvalRepository evalRepository)
    {
        _inboxConflictRepository = inboxConflictRepository;
        _clarificationRepository = clarificationRepository;
        _periodRepository = periodRepository;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _offlineEventRepository = offlineEventRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _networkGraphService = networkGraphService;
        _domainReviewEventRepository = domainReviewEventRepository;
        _budgetOpsRepository = budgetOpsRepository;
        _evalRepository = evalRepository;
    }

    public async Task<InboxReadModel> GetInboxAsync(
        WebReadRequest request,
        string? group = null,
        string? status = "open",
        string? priority = null,
        bool? blocking = null,
        CancellationToken ct = default)
    {
        var rows = (await _inboxConflictRepository.GetInboxItemsAsync(request.CaseId, string.IsNullOrWhiteSpace(status) ? null : status, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();

        if (!string.IsNullOrWhiteSpace(priority))
        {
            rows = rows.Where(x => x.Priority.Equals(priority, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (blocking.HasValue)
        {
            rows = rows.Where(x => x.IsBlocking == blocking.Value).ToList();
        }

        var mapped = rows
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new InboxItemReadModel
            {
                Id = x.Id,
                ItemType = x.ItemType,
                SourceObjectType = x.SourceObjectType,
                SourceObjectId = x.SourceObjectId,
                Priority = x.Priority,
                IsBlocking = x.IsBlocking,
                Summary = string.IsNullOrWhiteSpace(x.Summary) ? x.Title : x.Summary,
                Status = x.Status,
                UpdatedAt = x.UpdatedAt
            })
            .ToList();

        var blockingGroup = mapped.Where(x => x.IsBlocking || x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase)).ToList();
        var highImpactGroup = mapped.Where(x => !blockingGroup.Any(b => b.Id == x.Id) && IsHighImpact(x)).ToList();
        var restGroup = mapped.Where(x => !blockingGroup.Any(b => b.Id == x.Id) && !highImpactGroup.Any(h => h.Id == x.Id)).ToList();

        var normalizedGroup = NormalizeGroup(group);
        if (normalizedGroup == "blocking")
        {
            highImpactGroup = [];
            restGroup = [];
        }
        else if (normalizedGroup == "high_impact")
        {
            blockingGroup = [];
            restGroup = [];
        }
        else if (normalizedGroup == "everything_else")
        {
            blockingGroup = [];
            highImpactGroup = [];
        }

        return new InboxReadModel
        {
            GroupFilter = normalizedGroup,
            StatusFilter = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant(),
            PriorityFilter = priority,
            BlockingFilter = blocking,
            Blocking = blockingGroup,
            HighImpact = highImpactGroup,
            EverythingElse = restGroup,
            TotalVisible = blockingGroup.Count + highImpactGroup.Count + restGroup.Count
        };
    }

    public async Task<HistoryReadModel> GetHistoryAsync(
        WebReadRequest request,
        string? objectType = null,
        string? action = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var events = await CollectRecentEventsAsync(request, Math.Max(10, limit * 2), ct);
        if (!string.IsNullOrWhiteSpace(objectType))
        {
            events = events.Where(x => x.ObjectType.Equals(objectType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            events = events.Where(x => x.Action.Contains(action, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return new HistoryReadModel
        {
            ObjectTypeFilter = objectType,
            ActionFilter = action,
            Events = events
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Max(1, limit))
                .Select(ToActivity)
                .ToList()
        };
    }

    public async Task<ObjectHistoryReadModel> GetObjectHistoryAsync(
        WebReadRequest request,
        string objectType,
        string objectId,
        int limit = 30,
        CancellationToken ct = default)
    {
        var normalizedType = objectType.Trim().ToLowerInvariant();
        var objectModel = new ObjectHistoryReadModel
        {
            ObjectType = normalizedType,
            ObjectId = objectId.Trim(),
            ObjectSummary = "object not found"
        };

        switch (normalizedType)
        {
            case "clarification_question":
                if (Guid.TryParse(objectId, out var qid))
                {
                    var question = await _clarificationRepository.GetQuestionByIdAsync(qid, ct);
                    if (question != null && (question.ChatId == null || question.ChatId == request.ChatId) && question.CaseId == request.CaseId)
                    {
                        objectModel.ObjectSummary = question.QuestionText;
                        objectModel.Status = question.Status;
                        objectModel.Priority = question.Priority;
                    }
                }

                break;
            case "period":
                if (Guid.TryParse(objectId, out var pid))
                {
                    var period = await _periodRepository.GetPeriodByIdAsync(pid, ct);
                    if (period != null && (period.ChatId == null || period.ChatId == request.ChatId) && period.CaseId == request.CaseId)
                    {
                        objectModel.ObjectSummary = $"{period.Label}: {period.Summary}";
                        objectModel.Status = period.IsOpen ? "open" : "closed";
                        objectModel.Priority = period.ReviewPriority.ToString();
                    }
                }

                break;
            case "conflict_record":
                if (Guid.TryParse(objectId, out var cid))
                {
                    var conflict = await _inboxConflictRepository.GetConflictRecordByIdAsync(cid, ct);
                    if (conflict != null && (conflict.ChatId == null || conflict.ChatId == request.ChatId) && conflict.CaseId == request.CaseId)
                    {
                        objectModel.ObjectSummary = conflict.Summary;
                        objectModel.Status = conflict.Status;
                        objectModel.Priority = conflict.Severity;
                    }
                }

                break;
            case "inbox_item":
                if (Guid.TryParse(objectId, out var iid))
                {
                    var inbox = await _inboxConflictRepository.GetInboxItemByIdAsync(iid, ct);
                    if (inbox != null && (inbox.ChatId == null || inbox.ChatId == request.ChatId) && inbox.CaseId == request.CaseId)
                    {
                        objectModel.ObjectSummary = inbox.Summary;
                        objectModel.Status = inbox.Status;
                        objectModel.Priority = inbox.Priority;
                        objectModel.IsBlocking = inbox.IsBlocking;
                    }
                }

                break;
            case "draft_outcome":
                if (Guid.TryParse(objectId, out var oid))
                {
                    var outcomes = await _strategyDraftRepository.GetDraftOutcomesByCaseAsync(request.CaseId, ct);
                    var outcome = outcomes.FirstOrDefault(x => x.Id == oid);
                    if (outcome != null)
                    {
                        objectModel.ObjectSummary = $"{outcome.OutcomeLabel} (match={(outcome.MatchScore ?? 0f):0.00})";
                        objectModel.Status = outcome.SystemOutcomeLabel;
                        objectModel.Priority = outcome.UserOutcomeLabel;
                    }
                }

                break;
            case "eval_experiment_run":
                if (Guid.TryParse(objectId, out var experimentRunId))
                {
                    objectModel.ObjectSummary = $"experiment run {experimentRunId:D}";
                    objectModel.Status = "review_event";
                    objectModel.Priority = "high";
                }

                break;
        }

        var history = await _domainReviewEventRepository.GetByObjectAsync(normalizedType, objectId, Math.Max(1, limit), ct);
        if (normalizedType == "eval_experiment_run" && history.Count > 0)
        {
            var newest = history.OrderByDescending(x => x.CreatedAt).First();
            var parsed = ParseJsonMap(newest.NewValueRef);
            if (parsed.TryGetValue("experiment_key", out var experimentKey))
            {
                var scenarioPack = parsed.TryGetValue("scenario_pack", out var pack) ? pack : "-";
                var comparisonEvalRunId = parsed.TryGetValue("comparison_eval_run_id", out var runRef) ? runRef : "-";
                objectModel.ObjectSummary = $"experiment={experimentKey}, pack={scenarioPack}, comparison_run={comparisonEvalRunId}";
                objectModel.Status = newest.Action;
                objectModel.Priority = "high";
            }
        }

        objectModel.Events = history.Select(ToActivity).ToList();
        return objectModel;
    }

    public async Task<RecentChangesReadModel> GetRecentChangesAsync(WebReadRequest request, int limit = 8, CancellationToken ct = default)
    {
        var events = await CollectRecentEventsAsync(request, Math.Max(4, limit), ct);
        return new RecentChangesReadModel
        {
            Items = events
                .OrderByDescending(x => x.CreatedAt)
                .Take(Math.Max(1, limit))
                .Select(ToActivity)
                .ToList()
        };
    }

    public async Task<BudgetOperationalReadModel> GetBudgetOperationalStateAsync(CancellationToken ct = default)
    {
        var states = await _budgetOpsRepository.GetBudgetOperationalStatesAsync(ct);
        var mapped = states
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x =>
            {
                var normalizedState = x.State?.Trim().ToLowerInvariant() ?? string.Empty;
                var isQuotaBlocked = normalizedState == BudgetPathStates.QuotaBlocked;
                var isHardPaused = normalizedState == BudgetPathStates.HardPaused;
                var isPaused = isHardPaused || isQuotaBlocked;
                var isDegraded = normalizedState == BudgetPathStates.SoftLimited;
                return new BudgetOperationalStateReadModel
                {
                    PathKey = x.PathKey ?? string.Empty,
                    Modality = x.Modality ?? string.Empty,
                    State = x.State ?? string.Empty,
                    Reason = x.Reason ?? string.Empty,
                    UpdatedAt = x.UpdatedAt,
                    IsPaused = isPaused,
                    IsDegraded = isDegraded,
                    IsHardPaused = isHardPaused,
                    IsQuotaBlocked = isQuotaBlocked,
                    VisibilityMode = ResolveVisibilityMode(isQuotaBlocked, isHardPaused, isDegraded),
                    Details = ParseDetailsPairs(x.DetailsJson)
                };
            })
            .ToList();

        var pausedPaths = mapped.Count(x => x.IsPaused);
        var degradedPaths = mapped.Count(x => x.IsDegraded);
        var quotaBlockedPaths = mapped.Count(x => x.IsQuotaBlocked);
        var hardPausedPaths = mapped.Count(x => x.IsHardPaused);

        return new BudgetOperationalReadModel
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OperationalStatus = ResolveBudgetOperationalStatus(quotaBlockedPaths, pausedPaths, degradedPaths),
            TotalPaths = mapped.Count,
            PausedPaths = pausedPaths,
            HardPausedPaths = hardPausedPaths,
            QuotaBlockedPaths = quotaBlockedPaths,
            DegradedPaths = degradedPaths,
            ActivePaths = mapped.Count(x => !x.IsPaused && !x.IsDegraded),
            States = mapped
        };
    }

    public async Task<EvalRunsReadModel> GetEvalRunsAsync(
        string? runName = null,
        Guid? runId = null,
        int limit = 10,
        CancellationToken ct = default)
    {
        var normalizedLimit = Math.Max(1, Math.Min(limit, 40));
        var runs = await _evalRepository.GetRecentRunsAsync(normalizedLimit, ct);

        if (!string.IsNullOrWhiteSpace(runName))
        {
            var normalizedName = runName.Trim();
            runs = runs
                .Where(x => x.RunName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (runs.Count == 0)
            {
                var latestByName = await _evalRepository.GetLatestRunByNameAsync(normalizedName, ct);
                if (latestByName != null)
                {
                    runs = [latestByName];
                }
            }
        }

        if (runId.HasValue && runs.All(x => x.RunId != runId.Value))
        {
            var fetched = await _evalRepository.GetRunByIdAsync(runId.Value, ct);
            if (fetched != null)
            {
                fetched.Scenarios = await _evalRepository.GetScenarioResultsAsync(fetched.RunId, ct);
                runs.Add(fetched);
            }
        }

        var mappedRuns = runs
            .OrderByDescending(x => x.StartedAt)
            .Select(MapEvalRun)
            .ToList();

        var selected = runId.HasValue
            ? runs.FirstOrDefault(x => x.RunId == runId.Value)
            : runs.OrderByDescending(x => x.StartedAt).FirstOrDefault();

        var selectedRun = selected == null ? null : MapEvalRun(selected);
        var selectedScenarios = selected?.Scenarios
            .OrderBy(x => x.CreatedAt)
            .Select(MapScenario)
            .ToList() ?? [];

        return new EvalRunsReadModel
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OperationalStatus = ResolveEvalOperationalStatus(mappedRuns),
            TotalRuns = mappedRuns.Count,
            PassedRuns = mappedRuns.Count(x => x.Passed),
            FailedRuns = mappedRuns.Count(x => !x.Passed),
            Runs = mappedRuns,
            Comparisons = BuildRunComparisons(mappedRuns),
            SelectedRun = selectedRun,
            SelectedScenarios = selectedScenarios
        };
    }

    public async Task<AbScenarioCandidatePoolReadModel> GetAbScenarioCandidatesAsync(
        WebReadRequest request,
        int targetCount = 30,
        string? bucket = null,
        CancellationToken ct = default)
    {
        var normalizedTarget = Math.Clamp(targetCount, 20, 40);
        var bucketFilter = NormalizeCandidateBucket(bucket);

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .Take(80)
            .ToList();
        var periodById = periods.ToDictionary(x => x.Id, x => x);

        var transitionMap = new Dictionary<Guid, PeriodTransition>();
        foreach (var period in periods)
        {
            var rows = await _periodRepository.GetTransitionsByPeriodAsync(period.Id, ct);
            foreach (var row in rows)
            {
                transitionMap[row.Id] = row;
            }
        }

        var transitions = transitionMap.Values
            .OrderByDescending(x => x.CreatedAt)
            .Take(120)
            .ToList();
        var clarifications = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        var stateSnapshots = (await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 120, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        var strategies = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Take(120)
            .ToList();
        var outcomes = await _strategyDraftRepository.GetDraftOutcomesByCaseAsync(request.CaseId, ct);
        var offlineEvents = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();

        var strategyDraftMap = new Dictionary<Guid, List<DraftRecord>>();
        foreach (var strategy in strategies.Take(80))
        {
            strategyDraftMap[strategy.Id] = await _strategyDraftRepository.GetDraftRecordsByStrategyRecordIdAsync(strategy.Id, ct);
        }

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        sessionsByChat.TryGetValue(request.ChatId, out var chatSessions);
        chatSessions ??= [];
        var minFrom = periods.Count > 0
            ? periods.Min(x => x.StartAt).AddDays(-2)
            : DateTime.UtcNow.AddDays(-120);
        var maxTo = periods.Count > 0
            ? periods.Max(x => x.EndAt ?? x.UpdatedAt).AddDays(2)
            : DateTime.UtcNow;
        var messages = await _messageRepository.GetByChatAndPeriodAsync(request.ChatId, minFrom, maxTo, 20_000, ct);

        NetworkGraphResult network;
        try
        {
            network = await _networkGraphService.BuildAsync(new NetworkBuildRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Actor = request.Actor,
                AsOfUtc = request.AsOfUtc,
                MessageLimit = 700
            }, ct);
        }
        catch
        {
            network = new NetworkGraphResult();
        }

        var networkNodesByPeriod = network.Nodes
            .SelectMany(node => node.LinkedPeriodIds.Select(periodId => new { periodId, node.NodeId }))
            .GroupBy(x => x.periodId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        var networkEdgesByPeriod = network.InfluenceEdges
            .Where(x => x.LinkedPeriodId.HasValue)
            .Select(x => new { PeriodId = x.LinkedPeriodId!.Value, EdgeId = x.EdgeId })
            .Concat(network.InformationFlows
                .Where(x => x.LinkedPeriodId.HasValue)
                .Select(x => new { PeriodId = x.LinkedPeriodId!.Value, EdgeId = x.EdgeId }))
            .GroupBy(x => x.PeriodId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.EdgeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var outcomeByStrategy = outcomes
            .Where(x => x.StrategyRecordId.HasValue)
            .GroupBy(x => x.StrategyRecordId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());

        var drafts = new List<AbCandidateDraft>();
        foreach (var period in periods)
        {
            var range = BuildRange(period.StartAt, period.EndAt ?? period.UpdatedAt);
            var relatedTransitions = transitions
                .Where(x => x.FromPeriodId == period.Id || x.ToPeriodId == period.Id)
                .ToList();
            var unresolved = relatedTransitions.Where(x => !x.IsResolved).ToList();
            var relatedClarifications = clarifications
                .Where(x => x.PeriodId == period.Id || IsWithinRange(x.CreatedAt, range.From, range.To))
                .ToList();
            var relatedConflicts = conflicts
                .Where(x => IsWithinRange(x.CreatedAt, range.From, range.To))
                .ToList();
            var relatedSnapshots = stateSnapshots
                .Where(x => x.PeriodId == period.Id || IsWithinRange(x.AsOf, range.From, range.To))
                .ToList();
            var relatedStrategies = strategies
                .Where(x => x.PeriodId == period.Id || IsWithinRange(x.CreatedAt, range.From, range.To))
                .ToList();
            var relatedDrafts = relatedStrategies
                .SelectMany(x => strategyDraftMap.TryGetValue(x.Id, out var values) ? values : [])
                .DistinctBy(x => x.Id)
                .ToList();
            var relatedOutcomes = relatedStrategies
                .SelectMany(x => outcomeByStrategy.TryGetValue(x.Id, out var values) ? values : [])
                .DistinctBy(x => x.Id)
                .ToList();
            var relatedOffline = offlineEvents
                .Where(x => x.PeriodId == period.Id || RangesOverlap(range.From, range.To, x.TimestampStart, x.TimestampEnd ?? x.TimestampStart))
                .ToList();
            var nodeIds = networkNodesByPeriod.TryGetValue(period.Id, out var nodes) ? nodes : [];
            var edgeIds = networkEdgesByPeriod.TryGetValue(period.Id, out var edges) ? edges : [];
            var messageCount = CountMessages(messages, range.From, range.To);
            var sessionCount = CountSessions(chatSessions, range.From, range.To);

            var candidateBucket = ResolvePeriodBucket(period, messageCount, relatedStrategies.Count, relatedConflicts.Count, relatedClarifications.Count);
            var expectedState = ResolveSuggestedState(period, relatedSnapshots);
            var suggestedRisks = ResolveSuggestedRisks(
                unresolved.Count,
                relatedConflicts.Count,
                relatedClarifications.Count,
                messageCount,
                relatedSnapshots,
                candidateBucket);
            var sourceArtifacts = new AbScenarioSourceArtifactsReadModel
            {
                PeriodIds = [period.Id],
                TransitionIds = relatedTransitions.Select(x => x.Id).Take(8).ToList(),
                UnresolvedTransitionIds = unresolved.Select(x => x.Id).Take(8).ToList(),
                ConflictIds = relatedConflicts.Select(x => x.Id).Take(8).ToList(),
                ClarificationIds = relatedClarifications.Select(x => x.Id).Take(8).ToList(),
                StateSnapshotIds = relatedSnapshots.Select(x => x.Id).Take(8).ToList(),
                StrategyRecordIds = relatedStrategies.Select(x => x.Id).Take(8).ToList(),
                DraftRecordIds = relatedDrafts.Select(x => x.Id).Take(8).ToList(),
                OutcomeIds = relatedOutcomes.Select(x => x.Id).Take(8).ToList(),
                OfflineEventIds = relatedOffline.Select(x => x.Id).Take(8).ToList(),
                NetworkNodeIds = nodeIds.Take(8).ToList(),
                NetworkEdgeIds = edgeIds.Take(8).ToList()
            };

            drafts.Add(new AbCandidateDraft
            {
                CandidateId = $"cand_period_{period.Id:N}".ToLowerInvariant(),
                Title = BuildPeriodTitle(period),
                Bucket = candidateBucket,
                From = range.From,
                To = range.To,
                ChatIds = [request.ChatId],
                MessageCount = messageCount,
                SessionCount = sessionCount,
                SourceArtifacts = sourceArtifacts,
                WhySelected = BuildPeriodWhySelected(
                    unresolved.Count,
                    relatedConflicts.Count,
                    relatedClarifications.Count,
                    relatedStrategies.Count,
                    relatedDrafts.Count,
                    relatedOutcomes.Count,
                    relatedOffline.Count,
                    nodeIds.Count),
                RiskOfMisread = BuildMisreadRisk(unresolved.Count, relatedClarifications.Count, relatedConflicts.Count, messageCount, candidateBucket),
                SuggestedExpectedState = expectedState,
                SuggestedExpectedRisks = suggestedRisks,
                Score = ComputeScore(unresolved.Count, relatedConflicts.Count, relatedClarifications.Count, relatedStrategies.Count, relatedDrafts.Count, relatedOutcomes.Count, relatedOffline.Count, nodeIds.Count, messageCount, sessionCount),
                PriorityDate = range.To
            });
        }

        foreach (var transition in transitions.Take(60))
        {
            var fromPeriod = periodById.GetValueOrDefault(transition.FromPeriodId);
            var toPeriod = periodById.GetValueOrDefault(transition.ToPeriodId);
            if (fromPeriod == null && toPeriod == null)
            {
                continue;
            }

            var from = fromPeriod?.StartAt ?? transition.CreatedAt.AddDays(-2);
            var to = toPeriod?.EndAt ?? toPeriod?.UpdatedAt ?? transition.CreatedAt.AddDays(2);
            var range = BuildRange(from, to);
            var messageCount = CountMessages(messages, range.From, range.To);
            var sessionCount = CountSessions(chatSessions, range.From, range.To);
            var unresolvedWeight = transition.IsResolved ? 0 : 1;
            var transitionBucket = transition.IsResolved ? "state" : "counterexample";

            drafts.Add(new AbCandidateDraft
            {
                CandidateId = $"cand_transition_{transition.Id:N}".ToLowerInvariant(),
                Title = $"Transition {transition.TransitionType} ({(transition.IsResolved ? "resolved" : "unresolved")})",
                Bucket = transitionBucket,
                From = range.From,
                To = range.To,
                ChatIds = [request.ChatId],
                MessageCount = messageCount,
                SessionCount = sessionCount,
                SourceArtifacts = new AbScenarioSourceArtifactsReadModel
                {
                    PeriodIds = new[] { fromPeriod?.Id, toPeriod?.Id }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList(),
                    TransitionIds = [transition.Id],
                    UnresolvedTransitionIds = transition.IsResolved ? [] : [transition.Id]
                },
                WhySelected = transition.IsResolved
                    ? "Transition-centered candidate to compare stable boundary interpretation."
                    : "Unresolved transition candidate can expose ambiguity and boundary misclassification.",
                RiskOfMisread = transition.IsResolved
                    ? "May be simplified into linear trend without transition nuances."
                    : "High risk of forced closure on unresolved transition.",
                SuggestedExpectedState = transition.IsResolved ? "stabilizing" : "ambiguous",
                SuggestedExpectedRisks = transition.IsResolved ? ["trend_oversmoothing"] : ["forced_transition_resolution", "ambiguity_underweight"],
                Score = 4f + (transition.Confidence * 2f) + (unresolvedWeight * 5f) + (messageCount / 30f),
                PriorityDate = transition.CreatedAt
            });
        }

        foreach (var strategy in strategies.Take(80))
        {
            var linkedPeriod = strategy.PeriodId.HasValue && periodById.TryGetValue(strategy.PeriodId.Value, out var period)
                ? period
                : null;
            var from = linkedPeriod?.StartAt ?? strategy.CreatedAt.AddDays(-2);
            var to = linkedPeriod?.EndAt ?? linkedPeriod?.UpdatedAt ?? strategy.CreatedAt.AddDays(2);
            var range = BuildRange(from, to);
            var strategyDrafts = strategyDraftMap.TryGetValue(strategy.Id, out var values) ? values : [];
            var strategyOutcomes = outcomeByStrategy.TryGetValue(strategy.Id, out var outcomeRows) ? outcomeRows : [];
            var messageCount = CountMessages(messages, range.From, range.To);
            var sessionCount = CountSessions(chatSessions, range.From, range.To);
            var riskLabels = ParseRiskLabelsFromWhyNot(strategy.WhyNotOthers);

            drafts.Add(new AbCandidateDraft
            {
                CandidateId = $"cand_strategy_{strategy.Id:N}".ToLowerInvariant(),
                Title = $"Strategy chain: {BuildSnippet(strategy.MicroStep, 72)}",
                Bucket = "strategy_draft",
                From = range.From,
                To = range.To,
                ChatIds = [request.ChatId],
                MessageCount = messageCount,
                SessionCount = sessionCount,
                SourceArtifacts = new AbScenarioSourceArtifactsReadModel
                {
                    PeriodIds = linkedPeriod == null ? [] : [linkedPeriod.Id],
                    StrategyRecordIds = [strategy.Id],
                    DraftRecordIds = strategyDrafts.Select(x => x.Id).Take(8).ToList(),
                    OutcomeIds = strategyOutcomes.Select(x => x.Id).Take(8).ToList()
                },
                WhySelected = strategyOutcomes.Count > 0
                    ? "Strategy-to-draft-to-outcome artifacts are present and suitable for A/B chain comparison."
                    : "Strategy and draft artifacts are present and suitable for draft quality comparison.",
                RiskOfMisread = strategyOutcomes.Count > 0
                    ? "Outcome labels may be overfit to one short interaction."
                    : "Draft may be assessed without downstream outcome context.",
                SuggestedExpectedState = linkedPeriod?.DynamicSnapshot ?? linkedPeriod?.StatusSnapshot ?? "ambiguous",
                SuggestedExpectedRisks = riskLabels.Count > 0 ? riskLabels : ["strategy_to_draft_drift"],
                Score = 6f + (strategyOutcomes.Count * 2.5f) + (strategyDrafts.Count * 1.5f) + (messageCount / 35f),
                PriorityDate = strategy.CreatedAt
            });
        }

        var sortedMessages = messages
            .OrderBy(x => x.Timestamp)
            .ToList();
        if (sortedMessages.Count >= 16)
        {
            var windowSize = Math.Clamp(sortedMessages.Count / 6, 16, 48);
            var step = Math.Max(6, windowSize / 2);
            for (var i = 0; i + windowSize <= sortedMessages.Count; i += step)
            {
                var window = sortedMessages.Skip(i).Take(windowSize).ToList();
                var from = window.First().Timestamp;
                var to = window.Last().Timestamp;
                var range = BuildRange(from, to);
                var messageCount = window.Count;
                var sessionCount = CountSessions(chatSessions, range.From, range.To);
                var lowSignalRatio = window.Count(x => IsLowSignalText(x.Text)) / (float)window.Count;
                var fallbackBucket = lowSignalRatio >= 0.55f ? "counterexample" : "state";

                drafts.Add(new AbCandidateDraft
                {
                    CandidateId = $"cand_window_{from:yyyyMMddHHmm}_{to:yyyyMMddHHmm}_{i:D3}".ToLowerInvariant(),
                    Title = $"Window {from:yyyy-MM-dd} .. {to:yyyy-MM-dd}",
                    Bucket = fallbackBucket,
                    From = range.From,
                    To = range.To,
                    ChatIds = [request.ChatId],
                    MessageCount = messageCount,
                    SessionCount = sessionCount,
                    SourceArtifacts = new AbScenarioSourceArtifactsReadModel(),
                    WhySelected = "Deterministic fallback coverage slice from persisted message history to keep candidate pool broad.",
                    RiskOfMisread = lowSignalRatio >= 0.55f
                        ? "Low-signal/logistics window can be overread as emotional shift."
                        : "Medium-signal window may hide true boundary without artifact context.",
                    SuggestedExpectedState = lowSignalRatio >= 0.55f ? "neutral_low_signal" : "ambiguous",
                    SuggestedExpectedRisks = lowSignalRatio >= 0.55f
                        ? ["romantic_overread", "logistics_noise_overweight"]
                        : ["boundary_blur", "state_overconfidence"],
                    Score = 2f + ((1f - lowSignalRatio) * 2f) + (messageCount / 40f),
                    PriorityDate = range.To
                });
            }
        }

        var deduplicated = drafts
            .GroupBy(x => x.CandidateId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x
                .OrderByDescending(y => y.Score)
                .ThenByDescending(y => y.PriorityDate)
                .First())
            .Where(x => bucketFilter == null || x.Bucket.Equals(bucketFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.PriorityDate)
            .ThenBy(x => x.CandidateId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = SelectBalancedCandidates(deduplicated, normalizedTarget, bucketFilter);
        var mapped = selected
            .Select(x => new AbScenarioCandidateReadModel
            {
                CandidateId = x.CandidateId,
                Title = x.Title,
                Bucket = x.Bucket,
                DateRange = new AbDateRangeReadModel { From = x.From, To = x.To },
                ChatIds = x.ChatIds,
                MessageCount = x.MessageCount,
                SessionCount = x.SessionCount,
                SourceArtifacts = x.SourceArtifacts,
                WhySelected = x.WhySelected,
                RiskOfMisread = x.RiskOfMisread,
                SuggestedExpectedState = x.SuggestedExpectedState,
                SuggestedExpectedRisks = x.SuggestedExpectedRisks
            })
            .ToList();

        return new AbScenarioCandidatePoolReadModel
        {
            GeneratedAtUtc = DateTime.UtcNow,
            RequestedCount = normalizedTarget,
            BucketFilter = bucketFilter,
            TotalCandidates = mapped.Count,
            StateCandidates = mapped.Count(x => x.Bucket == "state"),
            StrategyDraftCandidates = mapped.Count(x => x.Bucket == "strategy_draft"),
            CounterexampleCandidates = mapped.Count(x => x.Bucket == "counterexample"),
            Candidates = mapped
        };
    }

    private async Task<List<DomainReviewEvent>> CollectRecentEventsAsync(WebReadRequest request, int limit, CancellationToken ct)
    {
        var objectRefs = new List<(string ObjectType, string ObjectId)>();

        var inbox = (await _inboxConflictRepository.GetInboxItemsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(40)
            .ToList();
        objectRefs.AddRange(inbox.Select(x => ("inbox_item", x.Id.ToString())));

        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(40)
            .ToList();
        objectRefs.AddRange(conflicts.Select(x => ("conflict_record", x.Id.ToString())));

        var questions = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(40)
            .ToList();
        objectRefs.AddRange(questions.Select(x => ("clarification_question", x.Id.ToString())));

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .Take(20)
            .ToList();
        objectRefs.AddRange(periods.Select(x => ("period", x.Id.ToString())));

        var outcomes = (await _strategyDraftRepository.GetDraftOutcomesByCaseAsync(request.CaseId, ct))
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .ToList();
        objectRefs.AddRange(outcomes.Select(x => ("draft_outcome", x.Id.ToString())));

        var distinctRefs = objectRefs
            .DistinctBy(x => $"{x.ObjectType}:{x.ObjectId}")
            .ToList();

        var merged = new List<DomainReviewEvent>();
        foreach (var (objectType, objectId) in distinctRefs)
        {
            var events = await _domainReviewEventRepository.GetByObjectAsync(objectType, objectId, 8, ct);
            merged.AddRange(events);
            if (merged.Count > limit * 4)
            {
                break;
            }
        }

        return merged
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private static ActivityEventReadModel ToActivity(DomainReviewEvent x)
    {
        var summary = string.IsNullOrWhiteSpace(x.Reason)
            ? x.Action
            : $"{x.Action}: {x.Reason}";

        return new ActivityEventReadModel
        {
            Id = x.Id,
            ObjectType = x.ObjectType,
            ObjectId = x.ObjectId,
            Action = x.Action,
            TimestampLabel = x.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            CreatedAt = x.CreatedAt,
            Summary = summary
        };
    }

    private static bool IsHighImpact(InboxItemReadModel item)
    {
        if (item.Priority.Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return item.SourceObjectType.Equals("conflict_record", StringComparison.OrdinalIgnoreCase)
               || item.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
               || item.SourceObjectType.Equals("period_transition", StringComparison.OrdinalIgnoreCase)
               || item.SourceObjectType.Equals("period", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGroup(string? group)
    {
        var normalized = group?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "blocking" => "blocking",
            "high" => "high_impact",
            "high_impact" => "high_impact",
            "everything" => "everything_else",
            "everything_else" => "everything_else",
            _ => "all"
        };
    }

    private static EvalRunReadModel MapEvalRun(EvalRunResult run)
    {
        var metrics = ParseJsonMap(run.MetricsJson);
        var scenarioCount = run.Scenarios.Count;
        var scenarioPassed = run.Scenarios.Count(x => x.Passed);
        return new EvalRunReadModel
        {
            RunId = run.RunId,
            RunName = run.RunName,
            Passed = run.Passed,
            RunKind = ResolveRunKind(run.RunName, metrics),
            LinkedExperimentRunId = TryParseGuidMetric(metrics, "experiment_run_id"),
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            Summary = run.Summary,
            Metrics = metrics,
            DurationSeconds = Math.Max(0, (long)(run.FinishedAt - run.StartedAt).TotalSeconds),
            ScenarioCount = scenarioCount,
            ScenarioPassed = scenarioPassed,
            ScenarioFailed = scenarioCount - scenarioPassed
        };
    }

    private static EvalScenarioReadModel MapScenario(EvalScenarioResult result)
    {
        return new EvalScenarioReadModel
        {
            Id = result.Id,
            RunId = result.RunId,
            ScenarioName = result.ScenarioName,
            Passed = result.Passed,
            Summary = result.Summary,
            Metrics = ParseJsonMap(result.MetricsJson),
            CreatedAt = result.CreatedAt
        };
    }

    private static List<KeyValuePair<string, string>> ParseDetailsPairs(string? json)
    {
        return ParseJsonMap(json)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static Dictionary<string, string> ParseJsonMap(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "null",
                    _ => property.Value.ToString()
                };
            }
        }
        catch (JsonException)
        {
            result["raw"] = json.Trim();
        }

        return result;
    }

    private static Guid? TryParseGuidMetric(IReadOnlyDictionary<string, string> metrics, string key)
    {
        if (metrics.TryGetValue(key, out var value) && Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string ResolveRunKind(string runName, IReadOnlyDictionary<string, string> metrics)
    {
        if (metrics.TryGetValue("type", out var type)
            && type.Equals("experiment_comparison", StringComparison.OrdinalIgnoreCase))
        {
            return "experiment_comparison";
        }

        if (runName.StartsWith("exp:", StringComparison.OrdinalIgnoreCase))
        {
            return "variant_run";
        }

        if (runName.StartsWith("experiment:", StringComparison.OrdinalIgnoreCase))
        {
            return "experiment_comparison";
        }

        return "standard";
    }

    private static string ResolveVisibilityMode(bool isQuotaBlocked, bool isHardPaused, bool isDegraded)
    {
        if (isQuotaBlocked)
        {
            return "quota_blocked";
        }

        if (isHardPaused)
        {
            return "hard_paused";
        }

        if (isDegraded)
        {
            return "degraded";
        }

        return "active";
    }

    private static string ResolveBudgetOperationalStatus(int quotaBlockedPaths, int pausedPaths, int degradedPaths)
    {
        if (quotaBlockedPaths > 0)
        {
            return "quota_blocked";
        }

        if (pausedPaths > 0)
        {
            return "paused";
        }

        return degradedPaths > 0 ? "degraded" : "active";
    }

    private static string ResolveEvalOperationalStatus(IReadOnlyList<EvalRunReadModel> runs)
    {
        if (runs.Count == 0)
        {
            return "no_data";
        }

        var latest = runs.OrderByDescending(x => x.StartedAt).First();
        return latest.Passed ? "healthy" : "attention_required";
    }

    private static List<EvalRunComparisonReadModel> BuildRunComparisons(IReadOnlyList<EvalRunReadModel> runs)
    {
        return runs
            .GroupBy(x => x.RunName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(x => x.StartedAt)
                    .Take(2)
                    .ToList();
                if (ordered.Count < 2)
                {
                    return null;
                }

                var current = ordered[0];
                var previous = ordered[1];
                var currentRate = current.ScenarioCount > 0
                    ? (double)current.ScenarioPassed / current.ScenarioCount
                    : (current.Passed ? 1d : 0d);
                var previousRate = previous.ScenarioCount > 0
                    ? (double)previous.ScenarioPassed / previous.ScenarioCount
                    : (previous.Passed ? 1d : 0d);

                return new EvalRunComparisonReadModel
                {
                    RunName = current.RunName,
                    CurrentRunId = current.RunId,
                    PreviousRunId = previous.RunId,
                    CurrentStartedAt = current.StartedAt,
                    PreviousStartedAt = previous.StartedAt,
                    CurrentPassed = current.Passed,
                    PreviousPassed = previous.Passed,
                    StatusChanged = current.Passed != previous.Passed,
                    StatusTransition = $"{(previous.Passed ? "PASS" : "FAIL")} -> {(current.Passed ? "PASS" : "FAIL")}",
                    CurrentScenarioPassRate = currentRate,
                    PreviousScenarioPassRate = previousRate,
                    ScenarioPassRateDelta = currentRate - previousRate,
                    CurrentDurationSeconds = current.DurationSeconds,
                    PreviousDurationSeconds = previous.DurationSeconds,
                    DurationDeltaSeconds = current.DurationSeconds - previous.DurationSeconds
                };
            })
            .Where(x => x != null)
            .Cast<EvalRunComparisonReadModel>()
            .OrderByDescending(x => x.CurrentStartedAt)
            .ToList();
    }

    private static (DateTime From, DateTime To) BuildRange(DateTime from, DateTime to)
    {
        return from <= to ? (from, to) : (to, from);
    }

    private static bool IsWithinRange(DateTime timestamp, DateTime from, DateTime to)
    {
        return timestamp >= from && timestamp <= to;
    }

    private static bool RangesOverlap(DateTime aFrom, DateTime aTo, DateTime bFrom, DateTime bTo)
    {
        return aFrom <= bTo && bFrom <= aTo;
    }

    private static int CountMessages(IReadOnlyList<Message> messages, DateTime from, DateTime to)
    {
        return messages.Count(x => x.Timestamp >= from && x.Timestamp <= to);
    }

    private static int CountSessions(IReadOnlyList<ChatSession> sessions, DateTime from, DateTime to)
    {
        return sessions.Count(x => RangesOverlap(from, to, x.StartDate, x.EndDate));
    }

    private static string ResolvePeriodBucket(Period period, int messageCount, int strategyCount, int conflictCount, int clarificationCount)
    {
        var label = $"{period.Label} {period.StatusSnapshot} {period.DynamicSnapshot} {period.Summary}".ToLowerInvariant();
        if (strategyCount > 0)
        {
            return "strategy_draft";
        }

        if (messageCount < 40 || label.Contains("logistics", StringComparison.OrdinalIgnoreCase))
        {
            return "counterexample";
        }

        if (conflictCount > 0 || clarificationCount > 0)
        {
            return "state";
        }

        if (label.Contains("ambig", StringComparison.OrdinalIgnoreCase)
            || label.Contains("fragile", StringComparison.OrdinalIgnoreCase)
            || label.Contains("cool", StringComparison.OrdinalIgnoreCase)
            || label.Contains("warm", StringComparison.OrdinalIgnoreCase))
        {
            return "state";
        }

        return "counterexample";
    }

    private static string ResolveSuggestedState(Period period, IReadOnlyList<StateSnapshot> snapshots)
    {
        var snapshot = snapshots
            .OrderByDescending(x => x.AsOf)
            .FirstOrDefault();
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.DynamicLabel))
        {
            return snapshot.DynamicLabel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(period.DynamicSnapshot))
        {
            return period.DynamicSnapshot.Trim();
        }

        if (!string.IsNullOrWhiteSpace(period.StatusSnapshot))
        {
            return period.StatusSnapshot.Trim();
        }

        return "ambiguous";
    }

    private static List<string> ResolveSuggestedRisks(
        int unresolvedTransitions,
        int conflictCount,
        int clarificationCount,
        int messageCount,
        IReadOnlyList<StateSnapshot> snapshots,
        string bucket)
    {
        var result = new List<string>();
        if (unresolvedTransitions > 0)
        {
            result.Add("transition_misread");
        }

        if (clarificationCount > 0)
        {
            result.Add("ambiguity_underweighted");
        }

        if (conflictCount > 0)
        {
            result.Add("conflict_underweighted");
        }

        if (messageCount < 35)
        {
            result.Add("low_signal_overread");
        }

        var highPressure = snapshots.Any(x => x.ExternalPressureScore >= 0.6f);
        if (highPressure)
        {
            result.Add("third_party_pressure_underweighted");
        }

        if (bucket == "counterexample")
        {
            result.Add("romantic_overread");
        }

        if (result.Count == 0)
        {
            result.Add("overconfident_state_assignment");
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToList();
    }

    private static string BuildPeriodTitle(Period period)
    {
        var label = string.IsNullOrWhiteSpace(period.Label) ? "period" : period.Label.Trim();
        var summarySnippet = BuildSnippet(period.Summary, 68);
        return string.IsNullOrWhiteSpace(summarySnippet)
            ? label
            : $"{label}: {summarySnippet}";
    }

    private static string BuildPeriodWhySelected(
        int unresolvedTransitions,
        int conflicts,
        int clarifications,
        int strategies,
        int drafts,
        int outcomes,
        int offlineEvents,
        int networkNodes)
    {
        var drivers = new List<string>();
        if (unresolvedTransitions > 0)
        {
            drivers.Add($"unresolved transitions={unresolvedTransitions}");
        }

        if (conflicts > 0)
        {
            drivers.Add($"conflicts={conflicts}");
        }

        if (clarifications > 0)
        {
            drivers.Add($"clarifications={clarifications}");
        }

        if (strategies > 0)
        {
            drivers.Add($"strategy records={strategies}");
        }

        if (drafts > 0)
        {
            drivers.Add($"draft records={drafts}");
        }

        if (outcomes > 0)
        {
            drivers.Add($"outcomes={outcomes}");
        }

        if (offlineEvents > 0)
        {
            drivers.Add($"offline events={offlineEvents}");
        }

        if (networkNodes > 0)
        {
            drivers.Add($"network context nodes={networkNodes}");
        }

        if (drivers.Count == 0)
        {
            return "Period selected as deterministic baseline slice for A/B bootstrap coverage.";
        }

        return $"Artifact-driven signals: {string.Join(", ", drivers)}.";
    }

    private static string BuildMisreadRisk(int unresolvedTransitions, int clarifications, int conflicts, int messageCount, string bucket)
    {
        if (unresolvedTransitions > 0 || clarifications > 0)
        {
            return "High risk of ambiguity collapse if transition/clarification signals are ignored.";
        }

        if (conflicts > 0)
        {
            return "Conflict dynamics can be underweighted and misread as a stable trend.";
        }

        if (bucket == "counterexample" || messageCount < 40)
        {
            return "Low-signal slice can be overread as meaningful romantic progression.";
        }

        return "Moderate risk of overconfident state labeling without full context.";
    }

    private static float ComputeScore(
        int unresolvedTransitions,
        int conflicts,
        int clarifications,
        int strategies,
        int drafts,
        int outcomes,
        int offlineEvents,
        int networkNodes,
        int messageCount,
        int sessionCount)
    {
        return (unresolvedTransitions * 5f)
               + (conflicts * 4f)
               + (clarifications * 3f)
               + (strategies * 2f)
               + (drafts * 1.5f)
               + (outcomes * 2f)
               + (offlineEvents * 1.5f)
               + (networkNodes * 1.25f)
               + (messageCount / 40f)
               + (sessionCount * 1.25f);
    }

    private static string BuildSnippet(string? text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = string.Join(" ", text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (normalized.Length <= maxLen)
        {
            return normalized;
        }

        return normalized[..Math.Max(1, maxLen - 1)].TrimEnd() + "…";
    }

    private static List<string> ParseRiskLabelsFromWhyNot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = value.Trim().ToLowerInvariant();
        var labels = new List<string>();
        if (normalized.Contains("pressure", StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("overpressure");
        }

        if (normalized.Contains("ambigu", StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("ambiguity_underweighted");
        }

        if (normalized.Contains("escalat", StringComparison.OrdinalIgnoreCase))
        {
            labels.Add("premature_escalation");
        }

        if (labels.Count == 0)
        {
            labels.Add("strategy_to_draft_drift");
        }

        return labels.Take(5).ToList();
    }

    private static bool IsLowSignalText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (normalized.Length <= 24)
        {
            return true;
        }

        var logisticsTokens = new[] { "ok", "thanks", "спс", "later", "tomorrow", "meeting", "where", "когда", "где", "завтра" };
        return logisticsTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeCandidateBucket(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "state" => "state",
            "strategy" => "strategy_draft",
            "strategy_draft" => "strategy_draft",
            "counterexample" => "counterexample",
            _ => null
        };
    }

    private static List<AbCandidateDraft> SelectBalancedCandidates(
        IReadOnlyList<AbCandidateDraft> candidates,
        int targetCount,
        string? bucketFilter)
    {
        if (bucketFilter != null)
        {
            return candidates.Take(targetCount).ToList();
        }

        var selected = new List<AbCandidateDraft>(targetCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stateMin = Math.Min(candidates.Count, Math.Max(6, (int)Math.Round(targetCount * 0.35)));
        var strategyMin = Math.Min(candidates.Count, Math.Max(5, (int)Math.Round(targetCount * 0.25)));
        var counterMin = Math.Min(candidates.Count, Math.Max(5, (int)Math.Round(targetCount * 0.20)));

        AddByBucket("state", stateMin);
        AddByBucket("strategy_draft", strategyMin);
        AddByBucket("counterexample", counterMin);

        foreach (var row in candidates)
        {
            if (selected.Count >= targetCount)
            {
                break;
            }

            if (seen.Add(row.CandidateId))
            {
                selected.Add(row);
            }
        }

        return selected;

        void AddByBucket(string bucket, int count)
        {
            foreach (var row in candidates.Where(x => x.Bucket == bucket))
            {
                if (selected.Count >= targetCount || count <= 0)
                {
                    break;
                }

                if (!seen.Add(row.CandidateId))
                {
                    continue;
                }

                selected.Add(row);
                count--;
            }
        }
    }

    private sealed class AbCandidateDraft
    {
        public string CandidateId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Bucket { get; set; } = "state";
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public List<long> ChatIds { get; set; } = [];
        public int MessageCount { get; set; }
        public int SessionCount { get; set; }
        public AbScenarioSourceArtifactsReadModel SourceArtifacts { get; set; } = new();
        public string WhySelected { get; set; } = string.Empty;
        public string RiskOfMisread { get; set; } = string.Empty;
        public string SuggestedExpectedState { get; set; } = string.Empty;
        public List<string> SuggestedExpectedRisks { get; set; } = [];
        public float Score { get; set; }
        public DateTime PriorityDate { get; set; }
    }
}
