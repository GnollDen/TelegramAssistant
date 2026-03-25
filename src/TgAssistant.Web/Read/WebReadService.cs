using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebReadService : IWebReadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly INetworkGraphService _networkGraphService;
    private readonly ICurrentStateEngine _currentStateEngine;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IProfileEngine _profileEngine;
    private readonly IPeriodizationService _periodizationService;
    private readonly IDraftEngine _draftEngine;
    private readonly IDraftReviewEngine _draftReviewEngine;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;

    public WebReadService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository,
        INetworkGraphService networkGraphService,
        ICurrentStateEngine currentStateEngine,
        IStrategyEngine strategyEngine,
        IProfileEngine profileEngine,
        IPeriodizationService periodizationService,
        IDraftEngine draftEngine,
        IDraftReviewEngine draftReviewEngine,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _networkGraphService = networkGraphService;
        _currentStateEngine = currentStateEngine;
        _strategyEngine = strategyEngine;
        _profileEngine = profileEngine;
        _periodizationService = periodizationService;
        _draftEngine = draftEngine;
        _draftReviewEngine = draftReviewEngine;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
    }

    public async Task<DashboardReadModel> GetDashboardAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var currentState = await GetCurrentStateAsync(request, ct);
        var strategy = await GetStrategyAsync(request, ct);
        var clarifications = await GetClarificationsAsync(request, ct);
        var timeline = await GetTimelineAsync(request, ct);
        var draftsReviews = await GetDraftsReviewsAsync(request, ct);

        var alerts = new List<string>();
        var openConflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, "open", ct))
            .Count(x => x.ChatId == null || x.ChatId == request.ChatId);
        if (openConflicts > 0)
        {
            alerts.Add($"open conflicts: {openConflicts}");
        }

        if (timeline.UnresolvedTransitions > 0)
        {
            alerts.Add($"unresolved transitions: {timeline.UnresolvedTransitions}");
        }

        if (currentState.Confidence < 0.55f)
        {
            alerts.Add("state confidence is low");
        }

        if (clarifications.OpenCount > 0)
        {
            alerts.Add($"open clarifications: {clarifications.OpenCount}");
        }

        return new DashboardReadModel
        {
            CurrentState = currentState,
            Strategy = strategy,
            Clarifications = clarifications,
            Timeline = timeline,
            DraftsReviews = draftsReviews,
            Alerts = alerts
        };
    }

    public async Task<CurrentStateReadModel> GetCurrentStateAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var scopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId);
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.CurrentState,
            scopeKey,
            ct);
        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.CurrentState,
            ct);

        StateSnapshot? snapshot = null;
        if (artifact != null)
        {
            var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
            if (!freshness.IsStale
                && Guid.TryParse(artifact.PayloadObjectId, out var snapshotId))
            {
                snapshot = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshotId, ct);
                if (snapshot != null)
                {
                    _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
                }
            }

            if (snapshot == null)
            {
                _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            }
        }

        snapshot ??= (await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 30, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.AsOf)
            .FirstOrDefault();

        snapshot ??= (await _currentStateEngine.ComputeAsync(new CurrentStateRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            Actor = request.Actor,
            SourceType = "web_read",
            SourceId = "current_state_page",
            Persist = true,
            AsOfUtc = request.AsOfUtc
        }, ct)).Snapshot;

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.CurrentState,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ScopeKey = scopeKey,
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
            RefreshedAt = DateTime.UtcNow,
            StaleAt = snapshot.CreatedAt.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.CurrentState)),
            IsStale = false,
            SourceType = "web_read",
            SourceId = "current_state_page",
            SourceMessageId = snapshot.SourceMessageId,
            SourceSessionId = snapshot.SourceSessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        var strategy = await GetStrategyAsync(request, ct);
        var openQuestions = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => IsOpenWorkflowStatus(x.Status))
            .OrderByDescending(x => PriorityWeight(x.Priority))
            .ThenByDescending(x => x.UpdatedAt)
            .Take(4)
            .ToList();
        var openConflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, "open", ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(4)
            .ToList();
        var keySignals = ParseJsonStringList(snapshot.KeySignalRefsJson, 6);
        var mainRisks = ParseJsonStringList(snapshot.RiskRefsJson, 6);
        var overallSignal = BuildOverallSignalStrength(snapshot.Confidence, snapshot.AmbiguityScore, openConflicts.Count);
        var observedFacts = BuildStateObservedFacts(snapshot, keySignals, openQuestions.Count, openConflicts.Count, overallSignal);
        var likelyInterpretation = BuildStateLikelyInterpretation(snapshot, strategy, overallSignal);
        var uncertainties = BuildStateUncertainties(snapshot, openQuestions, openConflicts);
        var missingInformation = BuildStateMissingInformation(openQuestions);

        return new CurrentStateReadModel
        {
            AsOfUtc = snapshot.AsOf,
            DynamicLabel = snapshot.DynamicLabel,
            RelationshipStatus = snapshot.RelationshipStatus,
            AlternativeStatus = snapshot.AlternativeStatus,
            Confidence = snapshot.Confidence,
            Scores = new Dictionary<string, float>
            {
                ["initiative"] = snapshot.InitiativeScore,
                ["responsiveness"] = snapshot.ResponsivenessScore,
                ["openness"] = snapshot.OpennessScore,
                ["warmth"] = snapshot.WarmthScore,
                ["reciprocity"] = snapshot.ReciprocityScore,
                ["ambiguity"] = snapshot.AmbiguityScore,
                ["avoidance_risk"] = snapshot.AvoidanceRiskScore,
                ["escalation_readiness"] = snapshot.EscalationReadinessScore,
                ["external_pressure"] = snapshot.ExternalPressureScore
            },
            KeySignals = keySignals,
            MainRisks = mainRisks,
            NextMoveSummary = strategy.PrimarySummary,
            ObservedFacts = observedFacts,
            LikelyInterpretation = likelyInterpretation,
            Uncertainties = uncertainties,
            MissingInformation = missingInformation,
            OverallSignalStrength = overallSignal
        };
    }

    public async Task<TimelineReadModel> GetTimelineAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .ToList();

        if (periods.Count == 0)
        {
            var run = await _periodizationService.RunAsync(new PeriodizationRunRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "timeline_page",
                Persist = true
            }, ct);

            periods = run.Periods
                .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
                .OrderByDescending(x => x.StartAt)
                .ToList();
        }

        var transitions = new List<PeriodTransition>();
        foreach (var period in periods.Take(8))
        {
            transitions.AddRange(await _periodRepository.GetTransitionsByPeriodAsync(period.Id, ct));
        }

        var distinctTransitions = transitions
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var current = periods.FirstOrDefault(x => x.IsOpen) ?? periods.FirstOrDefault();
        var prior = periods.Where(x => current == null || x.Id != current.Id).Take(4).ToList();

        return new TimelineReadModel
        {
            CurrentPeriod = current == null ? null : ToTimelinePeriod(current),
            PriorPeriods = prior.Select(ToTimelinePeriod).ToList(),
            Transitions = distinctTransitions
                .Take(8)
                .Select(x => new TimelineTransitionReadModel
                {
                    Id = x.Id,
                    FromPeriodId = x.FromPeriodId,
                    ToPeriodId = x.ToPeriodId,
                    TransitionType = x.TransitionType,
                    Summary = x.Summary,
                    IsResolved = x.IsResolved,
                    Confidence = x.Confidence
                })
                .ToList(),
            UnresolvedTransitions = distinctTransitions.Count(x => !x.IsResolved)
        };
    }

    public async Task<NetworkReadModel> GetNetworkAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var graph = await _networkGraphService.BuildAsync(new NetworkBuildRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            Actor = request.Actor,
            AsOfUtc = request.AsOfUtc,
            MessageLimit = 700
        }, ct);

        return new NetworkReadModel
        {
            GeneratedAtUtc = graph.GeneratedAtUtc,
            Nodes = graph.Nodes
                .Select(x => new NetworkNodeReadModel
                {
                    NodeId = x.NodeId,
                    NodeType = x.NodeType,
                    DisplayName = x.DisplayName,
                    PrimaryRole = x.PrimaryRole,
                    AdditionalRoles = x.AdditionalRoles,
                    GlobalRole = x.GlobalRole,
                    IsFocalActor = x.IsFocalActor,
                    ImportanceScore = x.ImportanceScore,
                    Confidence = x.Confidence,
                    LinkedPeriods = x.LinkedPeriodIds,
                    LinkedEvents = x.LinkedOfflineEventIds,
                    LinkedClarifications = x.LinkedClarificationIds,
                    EvidenceRefs = x.EvidenceRefs
                })
                .ToList(),
            InfluenceEdges = graph.InfluenceEdges
                .Select(x => new NetworkInfluenceEdgeReadModel
                {
                    EdgeId = x.EdgeId,
                    FromNodeId = x.FromNodeId,
                    ToNodeId = x.ToNodeId,
                    InfluenceType = x.InfluenceType,
                    Confidence = x.Confidence,
                    IsHypothesis = x.IsHypothesis,
                    LinkedPeriodId = x.LinkedPeriodId,
                    EvidenceRefs = x.EvidenceRefs
                })
                .ToList(),
            InformationFlows = graph.InformationFlows
                .Select(x => new NetworkInformationFlowReadModel
                {
                    EdgeId = x.EdgeId,
                    FromNodeId = x.FromNodeId,
                    ToNodeId = x.ToNodeId,
                    FlowType = x.FlowType,
                    Direction = x.Direction,
                    Confidence = x.Confidence,
                    LinkedPeriodId = x.LinkedPeriodId,
                    EvidenceRefs = x.EvidenceRefs
                })
                .ToList()
        };
    }

    public async Task<ProfilesReadModel> GetProfilesAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var (selfSenderId, otherSenderId) = await ResolveSelfOtherSendersAsync(request.ChatId, ct);
        await EnsureProfilesAsync(request, selfSenderId, ct);

        var self = await LoadProfileSubjectAsync(request, "self", selfSenderId.ToString(), ct);
        var other = await LoadProfileSubjectAsync(request, "other", otherSenderId.ToString(), ct);
        var pair = await LoadProfileSubjectAsync(request, "pair", $"{selfSenderId}:{otherSenderId}", ct);

        return new ProfilesReadModel { Self = self, Other = other, Pair = pair };
    }

    public async Task<ClarificationsReadModel> GetClarificationsAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var scopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId);
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.ClarificationState,
            scopeKey,
            ct);
        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.ClarificationState,
            ct);
        if (artifact != null)
        {
            var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
            if (!freshness.IsStale)
            {
                var persisted = Deserialize<ClarificationsReadModel>(artifact.PayloadJson);
                if (persisted != null)
                {
                    _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
                    return persisted;
                }
            }

            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
        }

        var all = await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct);
        var filtered = all
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();

        var open = filtered
            .Where(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                        || x.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => PriorityWeight(x.Priority))
            .ThenByDescending(x => x.ExpectedGain)
            .ThenByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new ClarificationQuestionReadModel
            {
                Id = x.Id,
                QuestionText = x.QuestionText,
                WhyItMatters = x.WhyItMatters,
                Priority = x.Priority,
                Status = x.Status,
                CreatedAt = x.CreatedAt
            })
            .ToList();

        var model = new ClarificationsReadModel
        {
            OpenCount = filtered.Count(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                || x.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase)),
            TopQuestions = open
        };

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.ClarificationState,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ScopeKey = scopeKey,
            PayloadObjectType = "clarification_state",
            PayloadObjectId = $"{request.CaseId}:{request.ChatId}",
            PayloadJson = JsonSerializer.Serialize(model, JsonOptions),
            FreshnessBasisHash = evidence.BasisHash,
            FreshnessBasisJson = evidence.BasisJson,
            GeneratedAt = DateTime.UtcNow,
            RefreshedAt = DateTime.UtcNow,
            StaleAt = DateTime.UtcNow.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.ClarificationState)),
            IsStale = false,
            SourceType = "web_read",
            SourceId = "clarification_state_page",
            SourceMessageId = null,
            SourceSessionId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return model;
    }

    public async Task<StrategyReadModel> GetStrategyAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var scopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId);
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Strategy,
            scopeKey,
            ct);
        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Strategy,
            ct);

        StrategyRecord? record = null;
        if (artifact != null)
        {
            var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
            if (!freshness.IsStale && Guid.TryParse(artifact.PayloadObjectId, out var strategyId))
            {
                record = await _strategyDraftRepository.GetStrategyRecordByIdAsync(strategyId, ct);
                if (record != null)
                {
                    _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
                }
            }

            if (record == null)
            {
                _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            }
        }

        record ??= (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);

        record ??= (await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            Actor = request.Actor,
            SourceType = "web_read",
            SourceId = "strategy_page",
            Persist = true,
            AsOfUtc = request.AsOfUtc
        }, ct)).Record;

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.Strategy,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ScopeKey = scopeKey,
            PayloadObjectType = "strategy_record",
            PayloadObjectId = record.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new
            {
                record.Id,
                record.StrategyConfidence,
                record.RecommendedGoal,
                record.MicroStep
            }, JsonOptions),
            FreshnessBasisHash = evidence.BasisHash,
            FreshnessBasisJson = evidence.BasisJson,
            GeneratedAt = record.CreatedAt,
            RefreshedAt = DateTime.UtcNow,
            StaleAt = record.CreatedAt.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.Strategy)),
            IsStale = false,
            SourceType = "web_read",
            SourceId = "strategy_page",
            SourceMessageId = record.SourceMessageId,
            SourceSessionId = record.SourceSessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(record.Id, ct);
        var primary = options.FirstOrDefault(x => x.IsPrimary) ?? options.FirstOrDefault();

        return new StrategyReadModel
        {
            RecordId = record.Id,
            Confidence = record.StrategyConfidence,
            PrimarySummary = primary?.Summary ?? "No primary option",
            PrimaryPurpose = primary?.Purpose ?? string.Empty,
            PrimaryRisks = primary == null ? [] : ParseRiskLabels(primary.Risk),
            Alternatives = options
                .Where(x => !x.IsPrimary)
                .Take(4)
                .Select(x => new StrategyOptionReadModel
                {
                    ActionType = x.ActionType,
                    Summary = x.Summary,
                    Purpose = x.Purpose,
                    Risks = ParseRiskLabels(x.Risk)
                })
                .ToList(),
            MicroStep = record.MicroStep,
            Horizon = ParseJsonStringList(record.HorizonJson, 4),
            WhyNotNotes = record.WhyNotOthers
        };
    }

    public async Task<DraftsReviewsReadModel> GetDraftsReviewsAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var strategy = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId)
            ?? (await _strategyEngine.RunAsync(new StrategyEngineRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "drafts_page_strategy_seed",
                Persist = true,
                AsOfUtc = request.AsOfUtc
            }, ct)).Record;

        DraftRecord? latestDraft = null;
        var draftArtifact = await _stage6ArtifactRepository.GetCurrentAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Draft,
            Stage6ArtifactTypes.ChatScope(request.ChatId),
            ct);
        var draftEvidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Draft,
            ct);
        if (draftArtifact != null)
        {
            var freshness = Stage6ArtifactFreshness.Evaluate(draftArtifact, DateTime.UtcNow, draftEvidence.LatestEvidenceAtUtc);
            if (!freshness.IsStale && Guid.TryParse(draftArtifact.PayloadObjectId, out var draftId))
            {
                latestDraft = await _strategyDraftRepository.GetDraftRecordByIdAsync(draftId, ct);
                if (latestDraft != null)
                {
                    _ = await _stage6ArtifactRepository.TouchReusedAsync(draftArtifact.Id, DateTime.UtcNow, ct);
                }
            }

            if (latestDraft == null)
            {
                _ = await _stage6ArtifactRepository.MarkStaleAsync(draftArtifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
            }
        }

        latestDraft ??= (await _strategyDraftRepository.GetDraftRecordsByStrategyRecordIdAsync(strategy.Id, ct))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        latestDraft ??= (await _draftEngine.RunAsync(new DraftEngineRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            StrategyRecordId = strategy.Id,
            Actor = request.Actor,
            SourceType = "web_read",
            SourceId = "drafts_page",
            Persist = true,
            AsOfUtc = request.AsOfUtc
        }, ct)).Record;

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.Draft,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId),
            PayloadObjectType = "draft_record",
            PayloadObjectId = latestDraft.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new
            {
                latestDraft.Id,
                latestDraft.MainDraft,
                latestDraft.AltDraft1,
                latestDraft.AltDraft2,
                latestDraft.Confidence
            }, JsonOptions),
            FreshnessBasisHash = draftEvidence.BasisHash,
            FreshnessBasisJson = draftEvidence.BasisJson,
            GeneratedAt = latestDraft.CreatedAt,
            RefreshedAt = DateTime.UtcNow,
            StaleAt = latestDraft.CreatedAt.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.Draft)),
            IsStale = false,
            SourceType = "web_read",
            SourceId = "drafts_page",
            SourceMessageId = latestDraft.SourceMessageId,
            SourceSessionId = latestDraft.SourceSessionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        DraftReviewReadModel? latestReview = null;
        if (latestDraft != null)
        {
            var reviewArtifact = await _stage6ArtifactRepository.GetCurrentAsync(
                request.CaseId,
                request.ChatId,
                Stage6ArtifactTypes.Review,
                Stage6ArtifactTypes.ChatScope(request.ChatId),
                ct);
            var reviewEvidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
                request.CaseId,
                request.ChatId,
                Stage6ArtifactTypes.Review,
                ct);
            DraftReviewResult? review = null;
            if (reviewArtifact != null)
            {
                var freshness = Stage6ArtifactFreshness.Evaluate(reviewArtifact, DateTime.UtcNow, reviewEvidence.LatestEvidenceAtUtc);
                if (!freshness.IsStale)
                {
                    review = Deserialize<DraftReviewResult>(reviewArtifact.PayloadJson);
                    if (review != null)
                    {
                        _ = await _stage6ArtifactRepository.TouchReusedAsync(reviewArtifact.Id, DateTime.UtcNow, ct);
                    }
                }

                if (review == null)
                {
                    _ = await _stage6ArtifactRepository.MarkStaleAsync(reviewArtifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
                }
            }

            review ??= await _draftReviewEngine.RunAsync(new DraftReviewRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                DraftRecordId = latestDraft.Id,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "drafts_review_page",
                Persist = true,
                AsOfUtc = request.AsOfUtc
            }, ct);

            latestReview = new DraftReviewReadModel
            {
                Assessment = review.Assessment,
                MainRisks = review.MainRisks,
                RiskLabels = review.RiskLabels,
                SaferRewrite = review.SaferRewrite,
                NaturalRewrite = review.NaturalRewrite,
                StrategyConflictDetected = review.StrategyConflictDetected
            };
        }

        return new DraftsReviewsReadModel
        {
            LatestDraft = latestDraft == null ? null : new DraftReadModel
            {
                Id = latestDraft.Id,
                CreatedAt = latestDraft.CreatedAt,
                MainDraft = latestDraft.MainDraft,
                AltDraft1 = latestDraft.AltDraft1,
                AltDraft2 = latestDraft.AltDraft2,
                StyleNotes = latestDraft.StyleNotes,
                Confidence = latestDraft.Confidence
            },
            LatestReview = latestReview,
            LatestOutcome = latestDraft == null
                ? null
                : await LoadLatestOutcomeAsync(latestDraft, strategy, ct)
        };
    }

    public async Task<OutcomeTrailReadModel> GetOutcomeTrailAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var strategies = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var strategyById = strategies.ToDictionary(x => x.Id, x => x);
        var outcomes = await _strategyDraftRepository.GetDraftOutcomesByCaseAsync(request.CaseId, ct);
        var items = new List<OutcomeChainItemReadModel>();
        var cacheByMessageId = new Dictionary<long, Message>();
        var missingDraftCount = 0;
        var missingStrategyCount = 0;
        var scannedOutcomes = outcomes.Count;

        foreach (var outcome in outcomes.OrderByDescending(x => x.CreatedAt).Take(24))
        {
            ct.ThrowIfCancellationRequested();

            var draft = await _strategyDraftRepository.GetDraftRecordByIdAsync(outcome.DraftId, ct);
            if (draft == null)
            {
                missingDraftCount++;
                continue;
            }

            var strategyId = outcome.StrategyRecordId ?? draft.StrategyRecordId;
            if (!strategyById.TryGetValue(strategyId, out var strategy))
            {
                missingStrategyCount++;
                continue;
            }

            var actualMessage = await TryGetMessageAsync(outcome.ActualMessageId, cacheByMessageId, ct);
            var followUpMessage = await TryGetMessageAsync(outcome.FollowUpMessageId, cacheByMessageId, ct);

            items.Add(new OutcomeChainItemReadModel
            {
                OutcomeId = outcome.Id,
                OutcomeCreatedAt = outcome.CreatedAt,
                StrategyRecordId = strategy.Id,
                StrategyCreatedAt = strategy.CreatedAt,
                StrategySummary = strategy.MicroStep,
                DraftId = draft.Id,
                DraftCreatedAt = draft.CreatedAt,
                DraftSnippet = BuildTextSnippet(draft.MainDraft, 220),
                ActualMessageId = outcome.ActualMessageId,
                ActualMessageSnippet = BuildTextSnippet(actualMessage?.Text, 220),
                FollowUpMessageId = outcome.FollowUpMessageId,
                FollowUpMessageSnippet = BuildTextSnippet(followUpMessage?.Text, 220),
                MatchScore = outcome.MatchScore,
                MatchedBy = outcome.MatchedBy ?? string.Empty,
                OutcomeLabel = outcome.OutcomeLabel,
                UserOutcomeLabel = outcome.UserOutcomeLabel,
                SystemOutcomeLabel = outcome.SystemOutcomeLabel,
                OutcomeConfidence = outcome.OutcomeConfidence,
                LearningSignals = ParseLearningSignalLabels(outcome.LearningSignalsJson)
            });
        }

        return new OutcomeTrailReadModel
        {
            TotalOutcomesScanned = scannedOutcomes,
            MissingDraftCount = missingDraftCount,
            MissingStrategyCount = missingStrategyCount,
            Items = items
        };
    }

    public async Task<OfflineEventsReadModel> GetOfflineEventsAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var events = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.TimestampStart)
            .Take(20)
            .ToList();

        var rows = new List<OfflineEventReadModel>(events.Count);
        foreach (var evt in events)
        {
            var assets = await _offlineEventRepository.GetAudioAssetsByOfflineEventIdAsync(evt.Id, ct);
            var snippetsCount = 0;
            foreach (var asset in assets.Take(5))
            {
                snippetsCount += (await _offlineEventRepository.GetAudioSnippetsByAssetIdAsync(asset.Id, ct)).Count;
            }

            var evidenceHooks = ParseJsonStringList(evt.EvidenceRefsJson, 3);
            var evidenceSummary = evidenceHooks.Count == 0
                ? $"assets={assets.Count}, snippets={snippetsCount}"
                : string.Join("; ", evidenceHooks);

            rows.Add(new OfflineEventReadModel
            {
                Id = evt.Id,
                TimestampStart = evt.TimestampStart,
                EventType = evt.EventType,
                Title = evt.Title,
                UserSummary = evt.UserSummary,
                LinkedPeriodId = evt.PeriodId,
                EvidenceSummary = evidenceSummary
            });
        }

        return new OfflineEventsReadModel { Events = rows };
    }

    private async Task EnsureProfilesAsync(WebReadRequest request, long selfSenderId, CancellationToken ct)
    {
        var (self, other) = await ResolveSelfOtherSendersAsync(request.ChatId, ct);
        var pairId = $"{self}:{other}";

        var hasSelf = (await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(request.CaseId, "self", self.ToString(), ct))
            .Any(x => x.ChatId == null || x.ChatId == request.ChatId);
        var hasOther = (await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(request.CaseId, "other", other.ToString(), ct))
            .Any(x => x.ChatId == null || x.ChatId == request.ChatId);
        var hasPair = (await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(request.CaseId, "pair", pairId, ct))
            .Any(x => x.ChatId == null || x.ChatId == request.ChatId);

        if (hasSelf && hasOther && hasPair)
        {
            return;
        }

        _ = await _profileEngine.RunAsync(new ProfileEngineRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = selfSenderId,
            Actor = request.Actor,
            SourceType = "web_read",
            SourceId = "profiles_page",
            Persist = true,
            AsOfUtc = request.AsOfUtc
        }, ct);
    }

    private async Task<ProfileSubjectReadModel> LoadProfileSubjectAsync(WebReadRequest request, string subjectType, string subjectId, CancellationToken ct)
    {
        var snapshots = (await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(request.CaseId, subjectType, subjectId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

        var snapshot = snapshots.FirstOrDefault();
        if (snapshot == null)
        {
            return new ProfileSubjectReadModel
            {
                SubjectType = subjectType,
                SubjectId = subjectId,
                Summary = "No profile data",
                Confidence = 0,
                Stability = 0
            };
        }

        var traits = await _stateProfileRepository.GetProfileTraitsBySnapshotIdAsync(snapshot.Id, ct);
        var topTraits = traits
            .Where(x => x.TraitKey is not "what_works" and not "what_fails")
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.Stability)
            .Take(6)
            .Select(x => new ProfileTraitReadModel
            {
                TraitKey = x.TraitKey,
                ValueLabel = x.ValueLabel,
                Confidence = x.Confidence,
                Stability = x.Stability
            })
            .ToList();

        var whatWorks = traits.FirstOrDefault(x => x.TraitKey == "what_works")?.ValueLabel
            ?? "No clear pattern yet.";
        var whatFails = traits.FirstOrDefault(x => x.TraitKey == "what_fails")?.ValueLabel
            ?? "No clear anti-pattern yet.";

        return new ProfileSubjectReadModel
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            Summary = snapshot.Summary,
            Confidence = snapshot.Confidence,
            Stability = snapshot.Stability,
            TopTraits = topTraits,
            WhatWorks = whatWorks,
            WhatFails = whatFails
        };
    }

    private async Task<(long SelfSenderId, long OtherSenderId)> ResolveSelfOtherSendersAsync(long chatId, CancellationToken ct)
    {
        var messages = await _messageRepository.GetByChatAndPeriodAsync(chatId, DateTime.UtcNow.AddDays(-120), DateTime.UtcNow, 5000, ct);
        var senderCounts = messages
            .Where(x => x.SenderId > 0)
            .GroupBy(x => x.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var selfSenderId = senderCounts.FirstOrDefault()?.SenderId ?? 1L;
        var otherSenderId = senderCounts.FirstOrDefault(x => x.SenderId != selfSenderId)?.SenderId ?? (selfSenderId + 1);
        return (selfSenderId, otherSenderId);
    }

    private static TimelinePeriodReadModel ToTimelinePeriod(Period x)
    {
        var hooks = ParseJsonStringList(x.EvidenceRefsJson, 4);
        if (hooks.Count == 0)
        {
            hooks = ParseJsonStringList(x.KeySignalsJson, 4);
        }

        return new TimelinePeriodReadModel
        {
            Id = x.Id,
            Label = x.Label,
            StartAt = x.StartAt,
            EndAt = x.EndAt,
            IsOpen = x.IsOpen,
            Summary = x.Summary,
            InterpretationConfidence = x.InterpretationConfidence,
            OpenQuestionsCount = x.OpenQuestionsCount,
            EvidenceHooks = hooks
        };
    }

    private static int PriorityWeight(string priority)
    {
        return priority.Trim().ToLowerInvariant() switch
        {
            "blocking" => 3,
            "important" => 2,
            _ => 1
        };
    }

    private static bool IsOpenWorkflowStatus(string status)
    {
        return status.Equals("open", StringComparison.OrdinalIgnoreCase)
               || status.Equals("ready", StringComparison.OrdinalIgnoreCase)
               || status.Equals("needs_user_input", StringComparison.OrdinalIgnoreCase)
               || status.Equals("review", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOverallSignalStrength(float confidence, float ambiguity, int openConflicts)
    {
        if (openConflicts > 0 && ambiguity >= 0.7f)
        {
            return "contradictory";
        }

        if (confidence >= 0.74f && ambiguity <= 0.45f)
        {
            return "strong";
        }

        if (confidence >= 0.55f)
        {
            return "medium";
        }

        return "weak";
    }

    private static List<StateInsightReadModel> BuildStateObservedFacts(
        StateSnapshot snapshot,
        IReadOnlyCollection<string> keySignals,
        int openQuestionCount,
        int openConflictCount,
        string overallSignal)
    {
        var items = new List<StateInsightReadModel>
        {
            new()
            {
                Title = "Current relationship label",
                Detail = $"{snapshot.DynamicLabel} / {snapshot.RelationshipStatus}",
                SignalStrength = overallSignal,
                Evidence = $"state_snapshot:{snapshot.Id}"
            },
            new()
            {
                Title = "Communication responsiveness",
                Detail = $"responsiveness={snapshot.ResponsivenessScore:0.00}, reciprocity={snapshot.ReciprocityScore:0.00}",
                SignalStrength = ScoreBand(snapshot.ResponsivenessScore),
                Evidence = "state_score:responsiveness,reciprocity"
            },
            new()
            {
                Title = "Open clarification/conflict load",
                Detail = $"open_clarifications={openQuestionCount}, open_conflicts={openConflictCount}",
                SignalStrength = openConflictCount > 0 ? "contradictory" : openQuestionCount > 0 ? "medium" : "weak",
                Evidence = "clarification_and_conflict_counts"
            }
        };

        if (keySignals.Count > 0)
        {
            items.Add(new StateInsightReadModel
            {
                Title = "Top observed signal references",
                Detail = string.Join("; ", keySignals.Take(3)),
                SignalStrength = overallSignal,
                Evidence = "state_snapshot:key_signal_refs"
            });
        }

        return items.Take(5).ToList();
    }

    private static List<StateInsightReadModel> BuildStateLikelyInterpretation(
        StateSnapshot snapshot,
        StrategyReadModel strategy,
        string overallSignal)
    {
        var items = new List<StateInsightReadModel>
        {
            new()
            {
                Title = "Likely current posture",
                Detail = $"High ambiguity ({snapshot.AmbiguityScore:0.00}) with confidence {snapshot.Confidence:0.00} suggests cautious interpretation, not certainty.",
                SignalStrength = overallSignal,
                Evidence = "state_snapshot:ambiguity,confidence"
            }
        };

        if (!string.IsNullOrWhiteSpace(strategy.PrimarySummary))
        {
            items.Add(new StateInsightReadModel
            {
                Title = "Strategy alignment",
                Detail = strategy.PrimarySummary,
                SignalStrength = strategy.Confidence >= 0.72f ? "strong" : strategy.Confidence >= 0.55f ? "medium" : "weak",
                Evidence = $"strategy_record:{strategy.RecordId}"
            });
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AlternativeStatus))
        {
            items.Add(new StateInsightReadModel
            {
                Title = "Alternative reading remains plausible",
                Detail = $"Alternative status: {snapshot.AlternativeStatus}.",
                SignalStrength = "medium",
                Evidence = "state_snapshot:alternative_status"
            });
        }

        return items.Take(4).ToList();
    }

    private static List<StateInsightReadModel> BuildStateUncertainties(
        StateSnapshot snapshot,
        IReadOnlyCollection<ClarificationQuestion> openQuestions,
        IReadOnlyCollection<ConflictRecord> openConflicts)
    {
        var items = new List<StateInsightReadModel>();

        if (snapshot.AmbiguityScore >= 0.62f)
        {
            items.Add(new StateInsightReadModel
            {
                Title = "High ambiguity",
                Detail = $"Ambiguity score is {snapshot.AmbiguityScore:0.00}; interpretation confidence should be capped.",
                SignalStrength = snapshot.AmbiguityScore >= 0.75f ? "contradictory" : "medium",
                Evidence = "state_score:ambiguity"
            });
        }

        items.AddRange(openConflicts.Take(2).Select(x => new StateInsightReadModel
        {
            Title = $"Open conflict: {x.ConflictType}",
            Detail = x.Summary,
            SignalStrength = x.Severity.Equals("high", StringComparison.OrdinalIgnoreCase)
                || x.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase)
                ? "contradictory"
                : "medium",
            Evidence = $"conflict_record:{x.Id}"
        }));

        items.AddRange(openQuestions.Take(2).Select(x => new StateInsightReadModel
        {
            Title = $"Unresolved question ({x.Priority})",
            Detail = x.QuestionText,
            SignalStrength = x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase) ? "contradictory" : "weak",
            Evidence = $"clarification_question:{x.Id}"
        }));

        return items.Take(5).ToList();
    }

    private static List<StateInsightReadModel> BuildStateMissingInformation(
        IReadOnlyCollection<ClarificationQuestion> openQuestions)
    {
        return openQuestions
            .Take(3)
            .Select(x => new StateInsightReadModel
            {
                Title = "Missing input",
                Detail = x.QuestionText,
                SignalStrength = x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase) ? "medium" : "weak",
                Evidence = string.IsNullOrWhiteSpace(x.WhyItMatters)
                    ? $"clarification_question:{x.Id}"
                    : x.WhyItMatters
            })
            .ToList();
    }

    private static string ScoreBand(float score)
    {
        if (score >= 0.75f)
        {
            return "strong";
        }

        if (score >= 0.55f)
        {
            return "medium";
        }

        return "weak";
    }

    private static List<string> ParseRiskLabels(string? riskJson)
    {
        if (string.IsNullOrWhiteSpace(riskJson))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(riskJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (!doc.RootElement.TryGetProperty("labels", out var labelsNode) || labelsNode.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return labelsNode.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(6)
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> ParseJsonStringList(string? json, int limit)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return doc.RootElement.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(Math.Max(1, limit))
                .Cast<string>()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static T? Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private async Task<DraftOutcomeReadModel?> LoadLatestOutcomeAsync(
        DraftRecord latestDraft,
        StrategyRecord strategy,
        CancellationToken ct)
    {
        var outcomes = await _strategyDraftRepository.GetDraftOutcomesByDraftIdAsync(latestDraft.Id, ct);
        if (outcomes.Count == 0)
        {
            outcomes = await _strategyDraftRepository.GetDraftOutcomesByStrategyRecordIdAsync(strategy.Id, ct);
        }

        var latest = outcomes
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (latest == null)
        {
            return null;
        }

        return new DraftOutcomeReadModel
        {
            Id = latest.Id,
            DraftId = latest.DraftId,
            StrategyRecordId = latest.StrategyRecordId,
            ActualMessageId = latest.ActualMessageId,
            FollowUpMessageId = latest.FollowUpMessageId,
            MatchScore = latest.MatchScore,
            MatchedBy = latest.MatchedBy ?? string.Empty,
            OutcomeLabel = latest.OutcomeLabel,
            UserOutcomeLabel = latest.UserOutcomeLabel,
            SystemOutcomeLabel = latest.SystemOutcomeLabel,
            OutcomeConfidence = latest.OutcomeConfidence,
            LearningSignals = ParseLearningSignalLabels(latest.LearningSignalsJson),
            Notes = latest.Notes,
            CreatedAt = latest.CreatedAt
        };
    }

    private async Task<Message?> TryGetMessageAsync(long? messageId, Dictionary<long, Message> cache, CancellationToken ct)
    {
        if (!messageId.HasValue || messageId.Value <= 0)
        {
            return null;
        }

        if (cache.TryGetValue(messageId.Value, out var cached))
        {
            return cached;
        }

        var row = await _messageRepository.GetByIdAsync(messageId.Value, ct);
        if (row != null)
        {
            cache[messageId.Value] = row;
        }

        return row;
    }

    private static string BuildTextSnippet(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Trim().Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..Math.Max(1, maxLength - 3)] + "...";
    }

    private static List<string> ParseLearningSignalLabels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var labels = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!item.TryGetProperty("signalKey", out var keyNode) || keyNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = item.TryGetProperty("value", out var valueNode) && valueNode.ValueKind == JsonValueKind.String
                    ? valueNode.GetString()
                    : string.Empty;
                var key = keyNode.GetString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                labels.Add(string.IsNullOrWhiteSpace(value) ? key : $"{key}:{value}");
                if (labels.Count >= 6)
                {
                    break;
                }
            }

            return labels;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
