using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using System.Text.Json;

namespace TgAssistant.Web.Read;

public class WebOpsService : IWebOpsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;
    private readonly IStage6UserContextRepository _stage6UserContextRepository;
    private readonly IStage6FeedbackRepository _stage6FeedbackRepository;
    private readonly IStage6CaseOutcomeRepository _stage6CaseOutcomeRepository;
    private readonly IClarificationOrchestrator _clarificationOrchestrator;

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
        IEvalRepository evalRepository,
        IStage6CaseRepository stage6CaseRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService,
        IStage6UserContextRepository stage6UserContextRepository,
        IStage6FeedbackRepository stage6FeedbackRepository,
        IStage6CaseOutcomeRepository stage6CaseOutcomeRepository,
        IClarificationOrchestrator clarificationOrchestrator)
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
        _stage6CaseRepository = stage6CaseRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
        _stage6UserContextRepository = stage6UserContextRepository;
        _stage6FeedbackRepository = stage6FeedbackRepository;
        _stage6CaseOutcomeRepository = stage6CaseOutcomeRepository;
        _clarificationOrchestrator = clarificationOrchestrator;
    }

    public async Task<Stage6CaseQueueReadModel> GetCaseQueueAsync(
        WebReadRequest request,
        string? status = "active",
        string? priority = null,
        string? caseType = null,
        string? artifactType = null,
        string? query = null,
        CancellationToken ct = default)
    {
        var allCases = (await _stage6CaseRepository.GetCasesAsync(request.CaseId, ct: ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();

        var normalizedStatus = NormalizeCaseQueueStatus(status);
        var normalizedPriority = EmptyToNull(priority);
        var normalizedCaseType = EmptyToNull(caseType);
        var normalizedArtifactType = EmptyToNull(artifactType);
        var normalizedQuery = EmptyToNull(query);

        var filtered = allCases
            .Where(x => MatchesCaseQueueStatus(x, normalizedStatus))
            .Where(x => string.IsNullOrWhiteSpace(normalizedPriority) || x.Priority.Equals(normalizedPriority, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(normalizedCaseType) || x.CaseType.Equals(normalizedCaseType, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(normalizedArtifactType)
                        || ParseJsonStringArray(x.TargetArtifactTypesJson).Contains(normalizedArtifactType, StringComparer.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(normalizedQuery) || MatchesCaseQuery(x, normalizedQuery))
            .OrderByDescending(x => PriorityWeight(x.Priority))
            .ThenByDescending(x => StatusWeight(x.Status))
            .ThenByDescending(x => x.UpdatedAt)
            .Select(x =>
            {
                var targetArtifacts = ParseJsonStringArray(x.TargetArtifactTypesJson);
                return new Stage6CaseQueueItemReadModel
                {
                    Id = x.Id,
                    CaseType = x.CaseType,
                    CaseSubtype = x.CaseSubtype,
                    Status = x.Status,
                    Priority = x.Priority,
                    Confidence = x.Confidence,
                    ReasonSummary = x.ReasonSummary,
                    QuestionText = x.QuestionText,
                    SourceObjectType = x.SourceObjectType,
                    SourceObjectId = x.SourceObjectId,
                    UpdatedAt = x.UpdatedAt,
                    NeedsAnswer = x.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase),
                    ResponseMode = x.ResponseMode,
                    EvidenceCount = ParseJsonStringArray(x.EvidenceRefsJson).Count,
                    TargetArtifactTypes = targetArtifacts
                };
            })
            .ToList();

        return new Stage6CaseQueueReadModel
        {
            StatusFilter = normalizedStatus,
            PriorityFilter = normalizedPriority,
            CaseTypeFilter = normalizedCaseType,
            ArtifactTypeFilter = normalizedArtifactType,
            Query = normalizedQuery,
            TotalCases = allCases.Count,
            VisibleCases = filtered.Count,
            NeedsInputCases = allCases.Count(x => x.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase)),
            ReadyCases = allCases.Count(x => x.Status.Equals(Stage6CaseStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                                             || x.Status.Equals(Stage6CaseStatuses.New, StringComparison.OrdinalIgnoreCase)),
            StaleCases = allCases.Count(x => x.Status.Equals(Stage6CaseStatuses.Stale, StringComparison.OrdinalIgnoreCase)),
            ResolvedCases = allCases.Count(x => x.Status.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase)),
            Cases = filtered
        };
    }

    public async Task<Stage6CaseDetailReadModel> GetCaseDetailAsync(
        WebReadRequest request,
        Guid stage6CaseId,
        CancellationToken ct = default)
    {
        var caseRecord = await _stage6CaseRepository.GetByIdAsync(stage6CaseId, ct);
        if (caseRecord == null || caseRecord.ScopeCaseId != request.CaseId || (caseRecord.ChatId.HasValue && caseRecord.ChatId != request.ChatId))
        {
            return new Stage6CaseDetailReadModel
            {
                Exists = false,
                Id = stage6CaseId,
                ReasonSummary = "Stage 6 case not found for this scope."
            };
        }

        var clarification = await LoadClarificationDetailAsync(caseRecord, ct);
        var sourceSummary = await BuildSourceSummaryAsync(caseRecord, clarification, ct);
        var history = await BuildStage6CaseHistoryAsync(caseRecord, ct);
        var evidence = await BuildStage6EvidenceAsync(caseRecord, clarification, ct);
        var artifacts = await BuildStage6ArtifactSummariesAsync(request, ParseJsonStringArray(caseRecord.TargetArtifactTypesJson), ct);
        var contextEntries = await BuildStage6ContextEntriesAsync(request, caseRecord, ct);
        var feedback = await BuildCaseFeedbackAsync(caseRecord.Id, ct);
        var outcomes = await BuildCaseOutcomesAsync(caseRecord.Id, ct);

        return new Stage6CaseDetailReadModel
        {
            Exists = true,
            Id = caseRecord.Id,
            CaseType = caseRecord.CaseType,
            CaseSubtype = caseRecord.CaseSubtype,
            Status = caseRecord.Status,
            Priority = caseRecord.Priority,
            Confidence = caseRecord.Confidence,
            ReasonSummary = caseRecord.ReasonSummary,
            QuestionText = caseRecord.QuestionText,
            ClarificationKind = caseRecord.ClarificationKind,
            ResponseMode = caseRecord.ResponseMode,
            ResponseChannelHint = caseRecord.ResponseChannelHint,
            SourceObjectType = caseRecord.SourceObjectType,
            SourceObjectId = caseRecord.SourceObjectId,
            SourceSummary = sourceSummary.Summary,
            SourceLink = sourceSummary.Link,
            CreatedAt = caseRecord.CreatedAt,
            UpdatedAt = caseRecord.UpdatedAt,
            SubjectRefs = ParseJsonStringArray(caseRecord.SubjectRefsJson),
            ReopenTriggers = ParseJsonStringArray(caseRecord.ReopenTriggerRulesJson),
            Evidence = evidence,
            Artifacts = artifacts,
            ContextEntries = contextEntries,
            Feedback = feedback,
            Outcomes = outcomes,
            History = history,
            Clarification = clarification
        };
    }

    public async Task<Stage6ArtifactDetailReadModel> GetArtifactDetailAsync(
        WebReadRequest request,
        string artifactType,
        CancellationToken ct = default)
    {
        var normalizedArtifactType = NormalizeArtifactType(artifactType);
        var scopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId);
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(request.CaseId, request.ChatId, normalizedArtifactType, scopeKey, ct);
        var evidenceStamp = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(request.CaseId, request.ChatId, normalizedArtifactType, ct);
        var linkedCases = await BuildLinkedCasesForArtifactAsync(request, normalizedArtifactType, ct);

        if (artifact == null)
        {
            return new Stage6ArtifactDetailReadModel
            {
                ArtifactType = normalizedArtifactType,
                Exists = false,
                Status = "missing",
                Reason = "No current artifact has been generated for this scope yet.",
                Summary = "Artifact pending or never generated.",
                FreshnessBasisJson = evidenceStamp.BasisJson,
                LatestEvidenceAtUtc = evidenceStamp.LatestEvidenceAtUtc,
                Evidence = BuildEvidenceFromBasis(evidenceStamp.BasisJson),
                Feedback = await BuildArtifactFeedbackAsync(request.CaseId, request.ChatId, normalizedArtifactType, ct),
                LinkedCases = linkedCases
            };
        }

        var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidenceStamp.LatestEvidenceAtUtc);
        var summary = SummarizeArtifact(artifact, freshness.Reason);

        return new Stage6ArtifactDetailReadModel
        {
            ArtifactType = normalizedArtifactType,
            Exists = true,
            Status = freshness.IsStale ? "stale" : "fresh",
            Reason = freshness.Reason,
            Summary = summary.Summary,
            ConfidenceLabel = summary.ConfidenceLabel,
            PayloadObjectType = artifact.PayloadObjectType,
            PayloadObjectId = artifact.PayloadObjectId,
            PayloadJson = artifact.PayloadJson,
            FreshnessBasisJson = artifact.FreshnessBasisJson,
            GeneratedAt = artifact.GeneratedAt,
            RefreshedAt = artifact.RefreshedAt,
            LatestEvidenceAtUtc = evidenceStamp.LatestEvidenceAtUtc,
            SourceType = artifact.SourceType,
            SourceId = artifact.SourceId,
            Evidence = BuildEvidenceFromBasis(artifact.FreshnessBasisJson),
            Feedback = await BuildArtifactFeedbackAsync(request.CaseId, request.ChatId, normalizedArtifactType, ct),
            LinkedCases = linkedCases
        };
    }

    public async Task<WebStage6CaseActionResult> ApplyCaseActionAsync(
        WebStage6CaseActionRequest request,
        CancellationToken ct = default)
    {
        var caseRecord = await LoadScopedStage6CaseAsync(request.ScopeCaseId, request.ChatId, request.Stage6CaseId, ct);
        if (caseRecord == null)
        {
            return new WebStage6CaseActionResult
            {
                Success = false,
                Stage6CaseId = request.Stage6CaseId,
                Action = request.Action,
                Message = "Stage 6 case not found for this scope."
            };
        }

        var action = NormalizeStage6Action(request.Action);
        return action switch
        {
            "resolve" => await ApplyStage6StatusActionAsync(caseRecord, Stage6CaseStatuses.Resolved, request, ct),
            "reject" => await ApplyStage6StatusActionAsync(caseRecord, Stage6CaseStatuses.Rejected, request, ct),
            "refresh" => await ApplyStage6RefreshActionAsync(caseRecord, request, ct),
            "annotate" => await ApplyStage6AnnotationAsync(caseRecord, request, ct),
            _ => new WebStage6CaseActionResult
            {
                Success = false,
                Stage6CaseId = request.Stage6CaseId,
                Action = action,
                Message = "Unsupported case action."
            }
        };
    }

    public async Task<WebStage6ClarificationAnswerResult> ApplyClarificationAnswerAsync(
        WebStage6ClarificationAnswerRequest request,
        CancellationToken ct = default)
    {
        var caseRecord = await LoadScopedStage6CaseAsync(request.ScopeCaseId, request.ChatId, request.Stage6CaseId, ct);
        if (caseRecord == null)
        {
            return new WebStage6ClarificationAnswerResult
            {
                Success = false,
                Stage6CaseId = request.Stage6CaseId,
                Message = "Stage 6 case not found for this scope."
            };
        }

        if (!caseRecord.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(caseRecord.SourceObjectId, out var questionId))
        {
            return new WebStage6ClarificationAnswerResult
            {
                Success = false,
                Stage6CaseId = request.Stage6CaseId,
                Message = "This case does not accept clarification answers."
            };
        }

        var trimmedAnswer = request.AnswerValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedAnswer))
        {
            return new WebStage6ClarificationAnswerResult
            {
                Success = false,
                Stage6CaseId = request.Stage6CaseId,
                Message = "Answer text is required."
            };
        }

        var targetArtifacts = ParseJsonStringArray(caseRecord.TargetArtifactTypesJson);
        var applyResult = await _clarificationOrchestrator.ApplyAnswerAsync(new ClarificationApplyRequest
        {
            QuestionId = questionId,
            AnswerType = string.IsNullOrWhiteSpace(request.AnswerType) ? "text" : request.AnswerType.Trim(),
            AnswerValue = trimmedAnswer,
            AnswerConfidence = Math.Clamp(request.AnswerConfidence, 0f, 1f),
            SourceClass = string.IsNullOrWhiteSpace(request.SourceClass) ? "operator_web" : request.SourceClass.Trim(),
            AffectedObjectsJson = JsonSerializer.Serialize(targetArtifacts, JsonOptions),
            SourceType = "web",
            SourceId = "web_operator_answer",
            MarkResolved = request.MarkResolved,
            Actor = request.Actor,
            Reason = request.Reason
        }, ct);

        var refreshedArtifactTypes = await MarkArtifactsStaleAsync(
            request.ScopeCaseId,
            request.ChatId,
            targetArtifacts,
            "clarification_answer_applied",
            ct);

        await RecordCaseOutcomeAsync(
            caseRecord,
            Stage6CaseOutcomeTypes.AnsweredByUser,
            request.MarkResolved ? Stage6CaseStatuses.Resolved : caseRecord.Status,
            request.Actor,
            request.Reason ?? "clarification_answer",
            sourceChannel: "web",
            userContextMaterial: true,
            ct: ct);

        await RecordCaseFeedbackAsync(
            caseRecord,
            request.IsUseful ?? true,
            request.Reason ?? request.AnswerValue,
            request.Actor,
            sourceChannel: "web",
            feedbackKind: Stage6FeedbackKinds.AcceptUseful,
            feedbackDimension: ResolveFeedbackDimension(caseRecord, request.FeedbackDimension),
            ct: ct);

        return new WebStage6ClarificationAnswerResult
        {
            Success = true,
            Stage6CaseId = caseRecord.Id,
            QuestionId = applyResult.Question.Id,
            AnswerId = applyResult.Answer.Id,
            Message = request.MarkResolved
                ? "Clarification answer recorded and case marked resolved."
                : "Clarification answer recorded.",
            QuestionText = applyResult.Question.QuestionText,
            AnswerValue = applyResult.Answer.AnswerValue,
            RefreshedArtifactTypes = refreshedArtifactTypes,
            RecomputeTargets = applyResult.RecomputePlan.Targets
                .Select(x => $"{x.Layer}:{x.TargetType}:{x.TargetId}")
                .ToList()
        };
    }

    public async Task<WebStage6ArtifactActionResult> ApplyArtifactActionAsync(
        WebStage6ArtifactActionRequest request,
        CancellationToken ct = default)
    {
        var action = NormalizeStage6Action(request.Action);
        var normalizedArtifactType = NormalizeArtifactType(request.ArtifactType);
        if (!action.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            return new WebStage6ArtifactActionResult
            {
                Success = false,
                ArtifactType = normalizedArtifactType,
                Action = action,
                Message = "Unsupported artifact action."
            };
        }

        var refreshedArtifactTypes = await MarkArtifactsStaleAsync(
            request.ScopeCaseId,
            request.ChatId,
            [normalizedArtifactType],
            request.Reason ?? "operator_refresh_requested",
            ct);

        if (refreshedArtifactTypes.Count > 0)
        {
            _ = await _stage6FeedbackRepository.AddAsync(new Stage6FeedbackEntry
            {
                ScopeCaseId = request.ScopeCaseId,
                ChatId = request.ChatId,
                ArtifactType = normalizedArtifactType,
                FeedbackKind = Stage6FeedbackKinds.RefreshRequested,
                FeedbackDimension = Stage6FeedbackDimensions.General,
                IsUseful = null,
                Note = request.Reason,
                SourceChannel = "web",
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        return new WebStage6ArtifactActionResult
        {
            Success = refreshedArtifactTypes.Count > 0,
            ArtifactType = normalizedArtifactType,
            Action = action,
            Message = refreshedArtifactTypes.Count > 0
                ? $"Artifact '{normalizedArtifactType}' marked stale for refresh."
                : $"No current '{normalizedArtifactType}' artifact was available to refresh."
        };
    }

    public async Task<InboxReadModel> GetInboxAsync(
        WebReadRequest request,
        string? group = null,
        string? status = "open",
        string? priority = null,
        bool? blocking = null,
        CancellationToken ct = default)
    {
        var queueStatus = NormalizeInboxStatusAsCaseStatus(status);
        var queue = await GetCaseQueueAsync(
            request,
            status: queueStatus,
            priority: priority,
            caseType: null,
            artifactType: null,
            query: null,
            ct: ct);

        var mapped = queue.Cases
            .Where(x => !blocking.HasValue
                        || blocking.Value == (x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase)
                                              || x.CaseType.Equals(Stage6CaseTypes.Risk, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new InboxItemReadModel
            {
                Id = x.Id,
                ItemType = x.CaseType,
                SourceObjectType = x.SourceObjectType,
                SourceObjectId = x.SourceObjectId,
                Priority = x.Priority,
                IsBlocking = x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase)
                             || x.CaseType.Equals(Stage6CaseTypes.Risk, StringComparison.OrdinalIgnoreCase),
                Summary = string.IsNullOrWhiteSpace(x.QuestionText) ? x.ReasonSummary : x.QuestionText,
                Status = x.Status,
                UpdatedAt = x.UpdatedAt,
                Confidence = x.Confidence,
                ReasonSummary = x.ReasonSummary
            })
            .ToList();

        var blockingGroup = mapped
            .Where(x => x.IsBlocking)
            .ToList();
        var highImpactGroup = mapped
            .Where(x => !blockingGroup.Any(b => b.Id == x.Id)
                        && x.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var restGroup = mapped
            .Where(x => !blockingGroup.Any(b => b.Id == x.Id) && !highImpactGroup.Any(h => h.Id == x.Id))
            .ToList();

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
            StatusFilter = queueStatus,
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
            case "stage6_case":
                if (Guid.TryParse(objectId, out var stage6CaseId))
                {
                    var stage6Case = await _stage6CaseRepository.GetByIdAsync(stage6CaseId, ct);
                    if (stage6Case != null && stage6Case.ScopeCaseId == request.CaseId && (stage6Case.ChatId == null || stage6Case.ChatId == request.ChatId))
                    {
                        objectModel.ObjectSummary = string.IsNullOrWhiteSpace(stage6Case.QuestionText)
                            ? stage6Case.ReasonSummary
                            : $"{stage6Case.QuestionText} | {stage6Case.ReasonSummary}";
                        objectModel.Status = stage6Case.Status;
                        objectModel.Priority = stage6Case.Priority;
                    }
                }

                break;
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

    private async Task<Stage6CaseRecord?> LoadScopedStage6CaseAsync(long scopeCaseId, long chatId, Guid stage6CaseId, CancellationToken ct)
    {
        var caseRecord = await _stage6CaseRepository.GetByIdAsync(stage6CaseId, ct);
        if (caseRecord == null || caseRecord.ScopeCaseId != scopeCaseId || (caseRecord.ChatId.HasValue && caseRecord.ChatId != chatId))
        {
            return null;
        }

        return caseRecord;
    }

    private async Task<WebStage6CaseActionResult> ApplyStage6StatusActionAsync(
        Stage6CaseRecord caseRecord,
        string targetStatus,
        WebStage6CaseActionRequest request,
        CancellationToken ct)
    {
        var success = caseRecord.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
                      && Guid.TryParse(caseRecord.SourceObjectId, out var questionId)
            ? await ApplyClarificationWorkflowStatusAsync(questionId, targetStatus, caseRecord.Priority, request.Actor, request.Reason ?? request.Note, ct)
            : await _stage6CaseRepository.UpdateStatusAsync(caseRecord.Id, targetStatus, request.Actor, request.Reason ?? request.Note, ct);

        if (success)
        {
            var contextMaterial = await ResolveUserContextMaterialAsync(caseRecord, ct);
            var outcomeType = targetStatus.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase)
                ? Stage6CaseOutcomeTypes.Resolved
                : targetStatus.Equals(Stage6CaseStatuses.Rejected, StringComparison.OrdinalIgnoreCase)
                    ? Stage6CaseOutcomeTypes.Rejected
                    : Stage6CaseOutcomeTypes.Stale;

            await RecordCaseOutcomeAsync(
                caseRecord,
                outcomeType,
                targetStatus,
                request.Actor,
                request.Reason ?? request.Note,
                sourceChannel: "web",
                userContextMaterial: contextMaterial,
                ct: ct);

            var feedbackKind = ResolveFeedbackKindFromAction(targetStatus.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase) ? "resolve" : "reject", request.FeedbackKind);
            var isUseful = request.IsUseful ?? targetStatus.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase);
            await RecordCaseFeedbackAsync(
                caseRecord,
                isUseful,
                request.Note ?? request.Reason,
                request.Actor,
                sourceChannel: "web",
                feedbackKind: feedbackKind,
                feedbackDimension: ResolveFeedbackDimension(caseRecord, request.FeedbackDimension),
                ct: ct);
        }

        return new WebStage6CaseActionResult
        {
            Success = success,
            Stage6CaseId = caseRecord.Id,
            Action = NormalizeStage6Action(request.Action),
            Status = targetStatus,
            Message = success
                ? $"Case moved to '{targetStatus}'."
                : "Case status change was not applied."
        };
    }

    private async Task<WebStage6CaseActionResult> ApplyStage6RefreshActionAsync(
        Stage6CaseRecord caseRecord,
        WebStage6CaseActionRequest request,
        CancellationToken ct)
    {
        var targetArtifacts = ParseJsonStringArray(caseRecord.TargetArtifactTypesJson);
        var refreshedArtifactTypes = await MarkArtifactsStaleAsync(
            request.ScopeCaseId,
            request.ChatId,
            targetArtifacts,
            request.Reason ?? "operator_refresh_requested",
            ct);

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "stage6_case",
            ObjectId = caseRecord.Id.ToString(),
            Action = "refresh_requested",
            NewValueRef = JsonSerializer.Serialize(new { artifacts = refreshedArtifactTypes }, JsonOptions),
            Reason = request.Reason,
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        if (refreshedArtifactTypes.Count > 0
            && (caseRecord.Status.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase)
                || caseRecord.Status.Equals(Stage6CaseStatuses.Rejected, StringComparison.OrdinalIgnoreCase)
                || caseRecord.Status.Equals(Stage6CaseStatuses.Stale, StringComparison.OrdinalIgnoreCase)))
        {
            _ = await _stage6CaseRepository.UpdateStatusAsync(caseRecord.Id, Stage6CaseStatuses.Ready, request.Actor, request.Reason, ct);
        }

        await RecordCaseOutcomeAsync(
            caseRecord,
            Stage6CaseOutcomeTypes.Refreshed,
            refreshedArtifactTypes.Count > 0 ? Stage6CaseStatuses.Ready : caseRecord.Status,
            request.Actor,
            request.Reason ?? "refresh_requested",
            sourceChannel: "web",
            userContextMaterial: false,
            ct: ct);

        await RecordCaseFeedbackAsync(
            caseRecord,
            request.IsUseful,
            request.Note ?? request.Reason,
            request.Actor,
            sourceChannel: "web",
            feedbackKind: ResolveFeedbackKindFromAction("refresh", request.FeedbackKind),
            feedbackDimension: ResolveFeedbackDimension(caseRecord, request.FeedbackDimension),
            ct: ct);

        return new WebStage6CaseActionResult
        {
            Success = true,
            Stage6CaseId = caseRecord.Id,
            Action = "refresh",
            Status = refreshedArtifactTypes.Count > 0 ? Stage6CaseStatuses.Ready : caseRecord.Status,
            Message = refreshedArtifactTypes.Count > 0
                ? $"Refresh requested for: {string.Join(", ", refreshedArtifactTypes)}."
                : "No current linked artifacts were available to refresh.",
            RefreshedArtifactTypes = refreshedArtifactTypes
        };
    }

    private async Task<WebStage6CaseActionResult> ApplyStage6AnnotationAsync(
        Stage6CaseRecord caseRecord,
        WebStage6CaseActionRequest request,
        CancellationToken ct)
    {
        var note = string.IsNullOrWhiteSpace(request.Note) ? request.Reason : request.Note;
        if (string.IsNullOrWhiteSpace(note))
        {
            return new WebStage6CaseActionResult
            {
                Success = false,
                Stage6CaseId = caseRecord.Id,
                Action = "annotate",
                Message = "Annotation text is required."
            };
        }

        var appliesToRefs = ParseJsonStringArray(caseRecord.TargetArtifactTypesJson)
            .Select(x => $"artifact_type:{x}")
            .Concat(ParseJsonStringArray(caseRecord.SubjectRefsJson))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _ = await _stage6UserContextRepository.CreateAsync(new Stage6UserContextEntry
        {
            Stage6CaseId = caseRecord.Id,
            ScopeCaseId = request.ScopeCaseId,
            ChatId = request.ChatId,
            SourceKind = UserContextSourceKinds.LongFormContext,
            ClarificationQuestionId = caseRecord.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
                                     && Guid.TryParse(caseRecord.SourceObjectId, out var questionId)
                ? questionId
                : null,
            ContentText = note.Trim(),
            AppliesToRefsJson = JsonSerializer.Serialize(appliesToRefs, JsonOptions),
            EnteredVia = "web",
            UserReportedCertainty = 0.75f,
            SourceType = "web",
            SourceId = $"stage6_case:{caseRecord.Id:D}",
            ConflictsWithRefsJson = "[]",
            CreatedAt = DateTime.UtcNow
        }, ct);

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "stage6_case",
            ObjectId = caseRecord.Id.ToString(),
            Action = "annotation_added",
            NewValueRef = JsonSerializer.Serialize(new { note }, JsonOptions),
            Reason = request.Reason,
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        await RecordCaseFeedbackAsync(
            caseRecord,
            request.IsUseful,
            note,
            request.Actor,
            sourceChannel: "web",
            feedbackKind: ResolveFeedbackKindFromAction("annotate", request.FeedbackKind),
            feedbackDimension: ResolveFeedbackDimension(caseRecord, request.FeedbackDimension),
            ct: ct);

        return new WebStage6CaseActionResult
        {
            Success = true,
            Stage6CaseId = caseRecord.Id,
            Action = "annotate",
            Status = caseRecord.Status,
            Message = "Annotation saved as user-reported context."
        };
    }

    private async Task<bool> ApplyClarificationWorkflowStatusAsync(
        Guid questionId,
        string targetStatus,
        string priority,
        string actor,
        string? reason,
        CancellationToken ct)
    {
        var questionStatus = targetStatus switch
        {
            Stage6CaseStatuses.Resolved => "resolved",
            Stage6CaseStatuses.Rejected => "rejected",
            Stage6CaseStatuses.Stale => "stale",
            Stage6CaseStatuses.Ready => "answered",
            _ => "open"
        };

        return await _clarificationRepository.UpdateQuestionWorkflowAsync(questionId, questionStatus, priority, actor, reason, ct);
    }

    private async Task<List<string>> MarkArtifactsStaleAsync(
        long scopeCaseId,
        long chatId,
        IReadOnlyCollection<string> artifactTypes,
        string reason,
        CancellationToken ct)
    {
        var refreshed = new List<string>();
        var scopeKey = Stage6ArtifactTypes.ChatScope(chatId);
        foreach (var artifactType in artifactTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = await _stage6ArtifactRepository.GetCurrentAsync(scopeCaseId, chatId, artifactType, scopeKey, ct);
            if (current == null)
            {
                continue;
            }

            if (await _stage6ArtifactRepository.MarkStaleAsync(current.Id, reason, DateTime.UtcNow, ct))
            {
                refreshed.Add(artifactType);
            }
        }

        return refreshed;
    }

    private async Task<List<Stage6FeedbackReadModel>> BuildCaseFeedbackAsync(Guid stage6CaseId, CancellationToken ct)
    {
        var rows = await _stage6FeedbackRepository.GetByCaseAsync(stage6CaseId, 40, ct);
        return rows
            .OrderByDescending(x => x.CreatedAt)
            .Select(MapFeedback)
            .ToList();
    }

    private async Task<List<Stage6FeedbackReadModel>> BuildArtifactFeedbackAsync(long scopeCaseId, long chatId, string artifactType, CancellationToken ct)
    {
        var rows = await _stage6FeedbackRepository.GetByArtifactAsync(scopeCaseId, chatId, artifactType, 40, ct);
        return rows
            .OrderByDescending(x => x.CreatedAt)
            .Select(MapFeedback)
            .ToList();
    }

    private async Task<List<Stage6CaseOutcomeReadModel>> BuildCaseOutcomesAsync(Guid stage6CaseId, CancellationToken ct)
    {
        var rows = await _stage6CaseOutcomeRepository.GetByCaseAsync(stage6CaseId, 40, ct);
        return rows
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new Stage6CaseOutcomeReadModel
            {
                Id = x.Id,
                OutcomeType = x.OutcomeType,
                CaseStatusAfter = x.CaseStatusAfter,
                UserContextMaterial = x.UserContextMaterial,
                Note = x.Note,
                SourceChannel = x.SourceChannel,
                Actor = x.Actor,
                CreatedAt = x.CreatedAt
            })
            .ToList();
    }

    private async Task RecordCaseFeedbackAsync(
        Stage6CaseRecord caseRecord,
        bool? isUseful,
        string? note,
        string actor,
        string sourceChannel,
        string feedbackKind,
        string feedbackDimension,
        CancellationToken ct)
    {
        _ = await _stage6FeedbackRepository.AddAsync(new Stage6FeedbackEntry
        {
            ScopeCaseId = caseRecord.ScopeCaseId,
            ChatId = caseRecord.ChatId,
            Stage6CaseId = caseRecord.Id,
            FeedbackKind = feedbackKind,
            FeedbackDimension = feedbackDimension,
            IsUseful = isUseful,
            Note = note,
            SourceChannel = sourceChannel,
            Actor = actor,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private async Task RecordCaseOutcomeAsync(
        Stage6CaseRecord caseRecord,
        string outcomeType,
        string caseStatusAfter,
        string actor,
        string? note,
        string sourceChannel,
        bool userContextMaterial,
        CancellationToken ct)
    {
        _ = await _stage6CaseOutcomeRepository.AddAsync(new Stage6CaseOutcomeRecord
        {
            Stage6CaseId = caseRecord.Id,
            ScopeCaseId = caseRecord.ScopeCaseId,
            ChatId = caseRecord.ChatId,
            OutcomeType = outcomeType,
            CaseStatusAfter = caseStatusAfter,
            UserContextMaterial = userContextMaterial,
            Note = note,
            SourceChannel = sourceChannel,
            Actor = actor,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private async Task<bool> ResolveUserContextMaterialAsync(Stage6CaseRecord caseRecord, CancellationToken ct)
    {
        var rows = await _stage6UserContextRepository.GetByScopeCaseAsync(caseRecord.ScopeCaseId, 400, ct);
        return rows.Any(x => x.Stage6CaseId == caseRecord.Id && x.CreatedAt >= caseRecord.CreatedAt);
    }

    private static Stage6FeedbackReadModel MapFeedback(Stage6FeedbackEntry row) => new()
    {
        Id = row.Id,
        FeedbackKind = row.FeedbackKind,
        FeedbackDimension = row.FeedbackDimension,
        IsUseful = row.IsUseful,
        Note = row.Note,
        SourceChannel = row.SourceChannel,
        Actor = row.Actor,
        CreatedAt = row.CreatedAt
    };

    private async Task<ClarificationQuestionDetailReadModel?> LoadClarificationDetailAsync(Stage6CaseRecord caseRecord, CancellationToken ct)
    {
        if (!caseRecord.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(caseRecord.SourceObjectId, out var questionId))
        {
            return null;
        }

        var question = await _clarificationRepository.GetQuestionByIdAsync(questionId, ct);
        if (question == null)
        {
            return null;
        }

        var answers = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
        return new ClarificationQuestionDetailReadModel
        {
            QuestionId = question.Id,
            QuestionText = question.QuestionText,
            WhyItMatters = question.WhyItMatters,
            Status = question.Status,
            Priority = question.Priority,
            QuestionType = question.QuestionType,
            AnswerOptions = ParseJsonStringArray(question.AnswerOptionsJson),
            Answers = answers
                .OrderByDescending(x => x.CreatedAt)
                .Take(8)
                .Select(x => new ClarificationAnswerDetailReadModel
                {
                    Id = x.Id,
                    AnswerType = x.AnswerType,
                    AnswerValue = x.AnswerValue,
                    AnswerConfidence = x.AnswerConfidence,
                    SourceClass = x.SourceClass,
                    CreatedAt = x.CreatedAt
                })
                .ToList()
        };
    }

    private async Task<(string Summary, string? Link)> BuildSourceSummaryAsync(
        Stage6CaseRecord caseRecord,
        ClarificationQuestionDetailReadModel? clarification,
        CancellationToken ct)
    {
        if (clarification != null)
        {
            return (clarification.QuestionText, $"/history-object?objectType=clarification_question&objectId={clarification.QuestionId}");
        }

        if (caseRecord.SourceObjectType.Equals("strategy_record", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(caseRecord.SourceObjectId, out var strategyId))
        {
            var strategy = await _strategyDraftRepository.GetStrategyRecordByIdAsync(strategyId, ct);
            if (strategy != null)
            {
                return ($"{strategy.RecommendedGoal} | {BuildSnippet(strategy.MicroStep, 96)}", $"/history-object?objectType=strategy_record&objectId={strategy.Id}");
            }
        }

        if (caseRecord.SourceObjectType.Equals("period", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(caseRecord.SourceObjectId, out var periodId))
        {
            var period = await _periodRepository.GetPeriodByIdAsync(periodId, ct);
            if (period != null)
            {
                return ($"{period.Label}: {BuildSnippet(period.Summary, 96)}", $"/history-object?objectType=period&objectId={period.Id}");
            }
        }

        if (caseRecord.SourceObjectType.Equals("state_snapshot", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(caseRecord.SourceObjectId, out var snapshotId))
        {
            var snapshot = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshotId, ct);
            if (snapshot != null)
            {
                return ($"{snapshot.DynamicLabel} / {snapshot.RelationshipStatus}", $"/history-object?objectType=state_snapshot&objectId={snapshot.Id}");
            }
        }

        return (string.IsNullOrWhiteSpace(caseRecord.QuestionText) ? caseRecord.ReasonSummary : caseRecord.QuestionText, BuildObjectLink(caseRecord.SourceObjectType, caseRecord.SourceObjectId));
    }

    private async Task<List<ActivityEventReadModel>> BuildStage6CaseHistoryAsync(Stage6CaseRecord caseRecord, CancellationToken ct)
    {
        var history = await _domainReviewEventRepository.GetByObjectAsync("stage6_case", caseRecord.Id.ToString(), 12, ct);
        if (caseRecord.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase))
        {
            history.AddRange(await _domainReviewEventRepository.GetByObjectAsync("clarification_question", caseRecord.SourceObjectId, 12, ct));
        }

        return history
            .OrderByDescending(x => x.CreatedAt)
            .Take(12)
            .Select(ToActivity)
            .ToList();
    }

    private async Task<List<Stage6EvidenceReadModel>> BuildStage6EvidenceAsync(
        Stage6CaseRecord caseRecord,
        ClarificationQuestionDetailReadModel? clarification,
        CancellationToken ct)
    {
        var refs = ParseJsonStringArray(caseRecord.EvidenceRefsJson);
        var evidence = new List<Stage6EvidenceReadModel>();

        if (clarification != null)
        {
            evidence.Add(new Stage6EvidenceReadModel
            {
                Reference = $"clarification_question:{clarification.QuestionId}",
                SourceClass = "system_inference",
                Title = "Clarification prompt",
                Summary = clarification.WhyItMatters,
                Link = $"/history-object?objectType=clarification_question&objectId={clarification.QuestionId}",
                TimestampUtc = null
            });
        }

        foreach (var reference in refs.Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
        {
            evidence.Add(await BuildEvidenceItemAsync(reference, ct));
        }

        return evidence;
    }

    private async Task<Stage6EvidenceReadModel> BuildEvidenceItemAsync(string reference, CancellationToken ct)
    {
        if (!TryParseReference(reference, out var objectType, out var objectId))
        {
            return new Stage6EvidenceReadModel
            {
                Reference = reference,
                SourceClass = "system_inference",
                Title = "Reference",
                Summary = reference
            };
        }

        switch (objectType)
        {
            case "message" when long.TryParse(objectId, out var messageId):
                var message = await _messageRepository.GetByIdAsync(messageId, ct);
                if (message != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "observed_evidence",
                        Title = $"Message #{message.Id}",
                        Summary = BuildSnippet(message.Text, 160),
                        Link = $"/search?objectType=message&q={Uri.EscapeDataString(message.Id.ToString())}",
                        TimestampUtc = message.Timestamp
                    };
                }

                break;

            case "clarification_question" when Guid.TryParse(objectId, out var questionId):
                var question = await _clarificationRepository.GetQuestionByIdAsync(questionId, ct);
                if (question != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "system_inference",
                        Title = "Clarification question",
                        Summary = question.QuestionText,
                        Link = $"/history-object?objectType=clarification_question&objectId={question.Id}",
                        TimestampUtc = question.UpdatedAt
                    };
                }

                break;

            case "clarification_answer" when Guid.TryParse(objectId, out var answerId):
                var answer = await _clarificationRepository.GetAnswerByIdAsync(answerId, ct);
                if (answer != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "user_reported_context",
                        Title = "Clarification answer",
                        Summary = answer.AnswerValue,
                        Link = $"/search?objectType=clarification_answer&q={Uri.EscapeDataString(answer.Id.ToString())}",
                        TimestampUtc = answer.CreatedAt
                    };
                }

                break;

            case "state_snapshot" when Guid.TryParse(objectId, out var snapshotId):
                var snapshot = await _stateProfileRepository.GetStateSnapshotByIdAsync(snapshotId, ct);
                if (snapshot != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "system_inference",
                        Title = "Current-state snapshot",
                        Summary = $"{snapshot.DynamicLabel} / {snapshot.RelationshipStatus} (conf {snapshot.Confidence:0.00})",
                        Link = $"/history-object?objectType=state_snapshot&objectId={snapshot.Id}",
                        TimestampUtc = snapshot.AsOf
                    };
                }

                break;

            case "strategy_record" when Guid.TryParse(objectId, out var strategyId):
                var strategy = await _strategyDraftRepository.GetStrategyRecordByIdAsync(strategyId, ct);
                if (strategy != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "system_inference",
                        Title = "Strategy record",
                        Summary = $"{strategy.RecommendedGoal} | {BuildSnippet(strategy.MicroStep, 140)}",
                        Link = $"/history-object?objectType=strategy_record&objectId={strategy.Id}",
                        TimestampUtc = strategy.CreatedAt
                    };
                }

                break;

            case "draft_record" when Guid.TryParse(objectId, out var draftId):
                var draft = await _strategyDraftRepository.GetDraftRecordByIdAsync(draftId, ct);
                if (draft != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "system_inference",
                        Title = "Draft record",
                        Summary = BuildSnippet(draft.MainDraft, 160),
                        Link = $"/history-object?objectType=draft_record&objectId={draft.Id}",
                        TimestampUtc = draft.CreatedAt
                    };
                }

                break;

            case "period" when Guid.TryParse(objectId, out var periodId):
                var period = await _periodRepository.GetPeriodByIdAsync(periodId, ct);
                if (period != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "system_inference",
                        Title = "Timeline period",
                        Summary = $"{period.Label}: {BuildSnippet(period.Summary, 140)}",
                        Link = $"/history-object?objectType=period&objectId={period.Id}",
                        TimestampUtc = period.UpdatedAt
                    };
                }

                break;

            case "conflict_record" when Guid.TryParse(objectId, out var conflictId):
                var conflict = await _inboxConflictRepository.GetConflictRecordByIdAsync(conflictId, ct);
                if (conflict != null)
                {
                    return new Stage6EvidenceReadModel
                    {
                        Reference = reference,
                        SourceClass = "system_inference",
                        Title = "Conflict record",
                        Summary = conflict.Summary,
                        Link = $"/history-object?objectType=conflict_record&objectId={conflict.Id}",
                        TimestampUtc = conflict.UpdatedAt
                    };
                }

                break;
        }

        return new Stage6EvidenceReadModel
        {
            Reference = reference,
            SourceClass = SourceClassForReference(objectType),
            Title = objectType.Replace('_', ' '),
            Summary = objectId,
            Link = BuildObjectLink(objectType, objectId)
        };
    }

    private async Task<List<Stage6ArtifactSummaryReadModel>> BuildStage6ArtifactSummariesAsync(
        WebReadRequest request,
        IReadOnlyCollection<string> artifactTypes,
        CancellationToken ct)
    {
        var results = new List<Stage6ArtifactSummaryReadModel>();
        var scopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId);
        foreach (var artifactType in artifactTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = await _stage6ArtifactRepository.GetCurrentAsync(request.CaseId, request.ChatId, artifactType, scopeKey, ct);
            if (current == null)
            {
                results.Add(new Stage6ArtifactSummaryReadModel
                {
                    ArtifactType = artifactType,
                    Exists = false,
                    Status = "missing",
                    Summary = "No current artifact.",
                    Reason = "pending_generation"
                });
                continue;
            }

            var evidenceStamp = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(request.CaseId, request.ChatId, artifactType, ct);
            var freshness = Stage6ArtifactFreshness.Evaluate(current, DateTime.UtcNow, evidenceStamp.LatestEvidenceAtUtc);
            var summary = SummarizeArtifact(current, freshness.Reason);
            results.Add(new Stage6ArtifactSummaryReadModel
            {
                ArtifactType = artifactType,
                Exists = true,
                Status = freshness.IsStale ? "stale" : "fresh",
                Summary = summary.Summary,
                Reason = freshness.Reason,
                ConfidenceLabel = summary.ConfidenceLabel,
                GeneratedAt = current.GeneratedAt,
                RefreshedAt = current.RefreshedAt,
                PayloadObjectType = current.PayloadObjectType,
                PayloadObjectId = current.PayloadObjectId
            });
        }

        return results;
    }

    private async Task<List<Stage6ContextEntryReadModel>> BuildStage6ContextEntriesAsync(
        WebReadRequest request,
        Stage6CaseRecord caseRecord,
        CancellationToken ct)
    {
        var questionId = caseRecord.SourceObjectType.Equals("clarification_question", StringComparison.OrdinalIgnoreCase)
                         && Guid.TryParse(caseRecord.SourceObjectId, out var parsedQuestionId)
            ? parsedQuestionId
            : (Guid?)null;

        var contextEntries = await _stage6UserContextRepository.GetByScopeCaseAsync(request.CaseId, 120, ct);
        return contextEntries
            .Where(x => x.ChatId == request.ChatId)
            .Where(x => x.Stage6CaseId == caseRecord.Id || (questionId.HasValue && x.ClarificationQuestionId == questionId.Value))
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new Stage6ContextEntryReadModel
            {
                Id = x.Id,
                SourceKind = x.SourceKind,
                ContentText = x.ContentText,
                EnteredVia = x.EnteredVia,
                UserReportedCertainty = x.UserReportedCertainty,
                CreatedAt = x.CreatedAt,
                AppliesToRefs = ParseJsonStringArray(x.AppliesToRefsJson)
            })
            .ToList();
    }

    private async Task<List<Stage6CaseQueueItemReadModel>> BuildLinkedCasesForArtifactAsync(
        WebReadRequest request,
        string artifactType,
        CancellationToken ct)
    {
        var cases = await _stage6CaseRepository.GetCasesAsync(request.CaseId, ct: ct);
        return cases
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => ParseJsonStringArray(x.TargetArtifactTypesJson).Contains(artifactType, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => PriorityWeight(x.Priority))
            .ThenByDescending(x => x.UpdatedAt)
            .Take(8)
            .Select(x => new Stage6CaseQueueItemReadModel
            {
                Id = x.Id,
                CaseType = x.CaseType,
                CaseSubtype = x.CaseSubtype,
                Status = x.Status,
                Priority = x.Priority,
                Confidence = x.Confidence,
                ReasonSummary = x.ReasonSummary,
                QuestionText = x.QuestionText,
                SourceObjectType = x.SourceObjectType,
                SourceObjectId = x.SourceObjectId,
                UpdatedAt = x.UpdatedAt,
                NeedsAnswer = x.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase),
                ResponseMode = x.ResponseMode,
                EvidenceCount = ParseJsonStringArray(x.EvidenceRefsJson).Count,
                TargetArtifactTypes = ParseJsonStringArray(x.TargetArtifactTypesJson)
            })
            .ToList();
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        return NormalizeArtifactType(artifact.ArtifactType) switch
        {
            Stage6ArtifactTypes.CurrentState => SummarizeCurrentStateArtifact(artifact, freshnessReason),
            Stage6ArtifactTypes.Strategy => SummarizeStrategyArtifact(artifact, freshnessReason),
            Stage6ArtifactTypes.Draft => SummarizeDraftArtifact(artifact, freshnessReason),
            Stage6ArtifactTypes.Review => SummarizeReviewArtifact(artifact, freshnessReason),
            Stage6ArtifactTypes.ClarificationState => SummarizeClarificationStateArtifact(artifact, freshnessReason),
            Stage6ArtifactTypes.Dossier => SummarizeDossierArtifact(artifact, freshnessReason),
            _ => (BuildSnippet(artifact.PayloadJson, 180), null)
        };
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeCurrentStateArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        var model = Deserialize<CurrentStateReadModel>(artifact.PayloadJson);
        if (model != null)
        {
            return ($"{model.DynamicLabel} / {model.RelationshipStatus}{RenderFreshnessSuffix(freshnessReason)}", model.Confidence.ToString("0.00"));
        }

        return (BuildSnippet(artifact.PayloadJson, 180), null);
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeStrategyArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        var model = Deserialize<StrategyReadModel>(artifact.PayloadJson);
        if (model != null && !string.IsNullOrWhiteSpace(model.PrimarySummary))
        {
            return ($"{BuildSnippet(model.PrimarySummary, 140)}{RenderFreshnessSuffix(freshnessReason)}", model.Confidence.ToString("0.00"));
        }

        var payload = ParseJsonMap(artifact.PayloadJson);
        var goal = payload.GetValueOrDefault("recommendedGoal");
        var step = payload.GetValueOrDefault("microStep");
        var confidence = payload.GetValueOrDefault("strategyConfidence");
        var summary = string.IsNullOrWhiteSpace(goal)
            ? BuildSnippet(step, 160)
            : $"{goal} | {BuildSnippet(step, 120)}";
        return ($"{summary}{RenderFreshnessSuffix(freshnessReason)}", string.IsNullOrWhiteSpace(confidence) ? null : confidence);
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeDraftArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        var model = Deserialize<DraftReadModel>(artifact.PayloadJson);
        if (model != null && !string.IsNullOrWhiteSpace(model.MainDraft))
        {
            return ($"{BuildSnippet(model.MainDraft, 140)}{RenderFreshnessSuffix(freshnessReason)}", model.Confidence.ToString("0.00"));
        }

        var payload = ParseJsonMap(artifact.PayloadJson);
        return ($"{BuildSnippet(payload.GetValueOrDefault("mainDraft"), 160)}{RenderFreshnessSuffix(freshnessReason)}", payload.GetValueOrDefault("confidence"));
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeReviewArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        var model = Deserialize<DraftReviewResult>(artifact.PayloadJson);
        if (model != null)
        {
            var summary = string.IsNullOrWhiteSpace(model.Assessment)
                ? BuildSnippet(model.StrategyConflictNote, 140)
                : BuildSnippet(model.Assessment, 140);
            return ($"{summary}{RenderFreshnessSuffix(freshnessReason)}", model.MainRisks.Count.ToString());
        }

        return (BuildSnippet(artifact.PayloadJson, 180), null);
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeClarificationStateArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        var model = Deserialize<ClarificationsReadModel>(artifact.PayloadJson);
        if (model != null)
        {
            return ($"open_clarifications={model.OpenCount}, top_questions={model.TopQuestions.Count}{RenderFreshnessSuffix(freshnessReason)}", null);
        }

        return (BuildSnippet(artifact.PayloadJson, 180), null);
    }

    private static (string Summary, string? ConfidenceLabel) SummarizeDossierArtifact(Stage6ArtifactRecord artifact, string? freshnessReason)
    {
        var model = Deserialize<DossierReadModel>(artifact.PayloadJson);
        if (model != null)
        {
            var summary = string.IsNullOrWhiteSpace(model.Summary)
                ? $"observed={model.ObservedFacts.Count}, confirmed={model.Confirmed.Count}, conflicts={model.Conflicts.Count}"
                : BuildSnippet(model.Summary, 160);
            return ($"{summary}{RenderFreshnessSuffix(freshnessReason)}", null);
        }

        return (BuildSnippet(artifact.PayloadJson, 180), null);
    }

    private static List<Stage6EvidenceReadModel> BuildEvidenceFromBasis(string? basisJson)
    {
        return ParseJsonMap(basisJson)
            .Select(x => new Stage6EvidenceReadModel
            {
                Reference = x.Key,
                SourceClass = "observed_evidence",
                Title = x.Key.Replace('_', ' '),
                Summary = x.Value
            })
            .ToList();
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
        if (!string.IsNullOrWhiteSpace(run.ScenarioPackKey))
        {
            metrics["scenario_pack_key"] = run.ScenarioPackKey;
        }
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
        var metrics = ParseJsonMap(result.MetricsJson);
        metrics["scenario_type"] = result.ScenarioType;
        metrics["latency_ms"] = result.LatencyMs.ToString();
        metrics["cost_usd"] = result.CostUsd.ToString("0.######");
        var modelSummary = ParseJsonMap(result.ModelSummaryJson);
        if (modelSummary.Count > 0)
        {
            metrics["models"] = string.Join(" | ", modelSummary.Select(x => $"{x.Key}:{x.Value}"));
        }

        var feedbackSummary = ParseJsonMap(result.FeedbackSummaryJson);
        if (feedbackSummary.Count > 0)
        {
            metrics["feedback"] = string.Join(" | ", feedbackSummary.Select(x => $"{x.Key}:{x.Value}"));
        }

        return new EvalScenarioReadModel
        {
            Id = result.Id,
            RunId = result.RunId,
            ScenarioName = result.ScenarioName,
            Passed = result.Passed,
            Summary = result.Summary,
            Metrics = metrics,
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

    private static List<string> ParseJsonStringArray(string? json)
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
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static T? Deserialize<T>(string? json)
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

    private static string NormalizeCaseQueueStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "new" => Stage6CaseStatuses.New,
            "ready" => Stage6CaseStatuses.Ready,
            "needs_user_input" => Stage6CaseStatuses.NeedsUserInput,
            "resolved" => Stage6CaseStatuses.Resolved,
            "rejected" => Stage6CaseStatuses.Rejected,
            "stale" => Stage6CaseStatuses.Stale,
            _ => "active"
        };
    }

    private static string NormalizeInboxStatusAsCaseStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            null or "" or "open" => "active",
            "active" => "active",
            "all" => "all",
            "ready" => Stage6CaseStatuses.Ready,
            "needs_user_input" => Stage6CaseStatuses.NeedsUserInput,
            "resolved" => Stage6CaseStatuses.Resolved,
            "rejected" => Stage6CaseStatuses.Rejected,
            "stale" => Stage6CaseStatuses.Stale,
            _ => "active"
        };
    }

    private static bool MatchesCaseQueueStatus(Stage6CaseRecord caseRecord, string normalizedStatus)
    {
        return normalizedStatus switch
        {
            "all" => true,
            "active" => caseRecord.Status is Stage6CaseStatuses.New or Stage6CaseStatuses.Ready or Stage6CaseStatuses.NeedsUserInput or Stage6CaseStatuses.Stale,
            _ => caseRecord.Status.Equals(normalizedStatus, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesCaseQuery(Stage6CaseRecord caseRecord, string query)
    {
        return (caseRecord.CaseType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.CaseSubtype?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.Status?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.Priority?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.ReasonSummary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.QuestionText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.SourceObjectType?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || (caseRecord.SourceObjectId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
               || ParseJsonStringArray(caseRecord.TargetArtifactTypesJson).Any(x => x.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static int PriorityWeight(string priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            "blocking" => 4,
            "important" => 3,
            "high" => 3,
            "optional" => 1,
            _ => 2
        };
    }

    private static int StatusWeight(string status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            Stage6CaseStatuses.NeedsUserInput => 5,
            Stage6CaseStatuses.Ready => 4,
            Stage6CaseStatuses.New => 3,
            Stage6CaseStatuses.Stale => 2,
            Stage6CaseStatuses.Resolved => 1,
            Stage6CaseStatuses.Rejected => 0,
            _ => 0
        };
    }

    private static string NormalizeArtifactType(string artifactType)
    {
        return string.IsNullOrWhiteSpace(artifactType)
            ? string.Empty
            : artifactType.Trim().ToLowerInvariant();
    }

    private static string NormalizeStage6Action(string action)
    {
        return action?.Trim().ToLowerInvariant() switch
        {
            "resolve" => "resolve",
            "reject" => "reject",
            "refresh" => "refresh",
            "annotate" => "annotate",
            _ => string.Empty
        };
    }

    private static string ResolveFeedbackKindFromAction(string action, string? explicitKind)
    {
        var normalizedExplicit = EmptyToNull(explicitKind)?.ToLowerInvariant();
        if (normalizedExplicit is Stage6FeedbackKinds.AcceptUseful
            or Stage6FeedbackKinds.RejectNotUseful
            or Stage6FeedbackKinds.CorrectionNote
            or Stage6FeedbackKinds.RefreshRequested)
        {
            return normalizedExplicit;
        }

        return action.Trim().ToLowerInvariant() switch
        {
            "resolve" => Stage6FeedbackKinds.AcceptUseful,
            "reject" => Stage6FeedbackKinds.RejectNotUseful,
            "refresh" => Stage6FeedbackKinds.RefreshRequested,
            _ => Stage6FeedbackKinds.CorrectionNote
        };
    }

    private static string ResolveFeedbackDimension(Stage6CaseRecord caseRecord, string? explicitDimension)
    {
        var normalizedExplicit = EmptyToNull(explicitDimension)?.ToLowerInvariant();
        if (normalizedExplicit is Stage6FeedbackDimensions.General
            or Stage6FeedbackDimensions.ClarificationUsefulness
            or Stage6FeedbackDimensions.BehavioralUsefulness)
        {
            return normalizedExplicit;
        }

        if (caseRecord.CaseType.StartsWith("clarification_", StringComparison.OrdinalIgnoreCase))
        {
            return Stage6FeedbackDimensions.ClarificationUsefulness;
        }

        return caseRecord.CaseSubtype?.Contains("behavior", StringComparison.OrdinalIgnoreCase) == true
            ? Stage6FeedbackDimensions.BehavioralUsefulness
            : Stage6FeedbackDimensions.General;
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryParseReference(string reference, out string objectType, out string objectId)
    {
        objectType = string.Empty;
        objectId = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var parts = reference.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        objectType = parts[0].Trim().ToLowerInvariant();
        objectId = parts[1].Trim();
        return true;
    }

    private static string SourceClassForReference(string objectType)
    {
        return objectType switch
        {
            "message" => "observed_evidence",
            "clarification_answer" => "user_reported_context",
            _ => "system_inference"
        };
    }

    private static string? BuildObjectLink(string objectType, string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        return objectType.Trim().ToLowerInvariant() switch
        {
            "stage6_artifact_type" => $"/artifact-detail?artifactType={Uri.EscapeDataString(objectId)}",
            _ => $"/history-object?objectType={Uri.EscapeDataString(objectType)}&objectId={Uri.EscapeDataString(objectId)}"
        };
    }

    private static string RenderFreshnessSuffix(string? freshnessReason)
    {
        return string.IsNullOrWhiteSpace(freshnessReason) ? string.Empty : $" [{freshnessReason}]";
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
