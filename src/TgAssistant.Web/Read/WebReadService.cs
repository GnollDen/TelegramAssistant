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
    private readonly ICurrentStateEngine _currentStateEngine;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IProfileEngine _profileEngine;
    private readonly IPeriodizationService _periodizationService;
    private readonly IDraftEngine _draftEngine;
    private readonly IDraftReviewEngine _draftReviewEngine;

    public WebReadService(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository,
        ICurrentStateEngine currentStateEngine,
        IStrategyEngine strategyEngine,
        IProfileEngine profileEngine,
        IPeriodizationService periodizationService,
        IDraftEngine draftEngine,
        IDraftReviewEngine draftReviewEngine)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _currentStateEngine = currentStateEngine;
        _strategyEngine = strategyEngine;
        _profileEngine = profileEngine;
        _periodizationService = periodizationService;
        _draftEngine = draftEngine;
        _draftReviewEngine = draftReviewEngine;
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
        var snapshot = (await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 30, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.AsOf)
            .FirstOrDefault();

        if (snapshot == null)
        {
            snapshot = (await _currentStateEngine.ComputeAsync(new CurrentStateRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "current_state_page",
                Persist = true,
                AsOfUtc = request.AsOfUtc
            }, ct)).Snapshot;
        }

        var strategy = await GetStrategyAsync(request, ct);

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
            KeySignals = ParseJsonStringList(snapshot.KeySignalRefsJson, 6),
            MainRisks = ParseJsonStringList(snapshot.RiskRefsJson, 6),
            NextMoveSummary = strategy.PrimarySummary
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

        return new ClarificationsReadModel
        {
            OpenCount = filtered.Count(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                || x.Status.Equals("in_progress", StringComparison.OrdinalIgnoreCase)),
            TopQuestions = open
        };
    }

    public async Task<StrategyReadModel> GetStrategyAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var record = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);

        if (record == null)
        {
            record = (await _strategyEngine.RunAsync(new StrategyEngineRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "strategy_page",
                Persist = true,
                AsOfUtc = request.AsOfUtc
            }, ct)).Record;
        }

        var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(record.Id, ct);
        var primary = options.FirstOrDefault(x => x.IsPrimary) ?? options.FirstOrDefault();

        return new StrategyReadModel
        {
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
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);

        if (strategy == null)
        {
            strategy = (await _strategyEngine.RunAsync(new StrategyEngineRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "drafts_page_strategy_seed",
                Persist = true,
                AsOfUtc = request.AsOfUtc
            }, ct)).Record;
        }

        var drafts = await _strategyDraftRepository.GetDraftRecordsByStrategyRecordIdAsync(strategy.Id, ct);
        var latestDraft = drafts.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (latestDraft == null)
        {
            latestDraft = (await _draftEngine.RunAsync(new DraftEngineRequest
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
        }

        DraftReviewReadModel? latestReview = null;
        if (latestDraft != null)
        {
            var review = await _draftReviewEngine.RunAsync(new DraftReviewRequest
            {
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                DraftRecordId = latestDraft.Id,
                Actor = request.Actor,
                SourceType = "web_read",
                SourceId = "drafts_review_page",
                Persist = false,
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
            LatestReview = latestReview
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
}
