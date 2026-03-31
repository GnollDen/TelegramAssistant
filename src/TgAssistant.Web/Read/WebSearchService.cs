using System.Text.Json;
using System.Globalization;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebSearchService : IWebSearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;

    public WebSearchService(
        IMessageRepository messageRepository,
        IInboxConflictRepository inboxConflictRepository,
        IClarificationRepository clarificationRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService)
    {
        _messageRepository = messageRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _clarificationRepository = clarificationRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
    }

    public async Task<SearchReadModel> SearchAsync(
        WebReadRequest request,
        string? query,
        string? objectType = null,
        string? status = null,
        string? priority = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var q = query?.Trim() ?? string.Empty;
        var all = await CollectSearchItemsAsync(request, ct);

        if (!string.IsNullOrWhiteSpace(objectType))
        {
            all = all.Where(x => x.ObjectType.Equals(objectType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            all = all.Where(x => !string.IsNullOrWhiteSpace(x.Status)
                && x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            all = all.Where(x => !string.IsNullOrWhiteSpace(x.Priority)
                && x.Priority.Equals(priority, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            all = all.Where(x => ContainsInvariant(x.Title, q)
                                 || ContainsInvariant(x.Summary, q)
                                 || ContainsInvariant(x.ObjectId, q)
                                 || ContainsInvariant(x.ObjectType, q))
                .ToList();
        }

        return new SearchReadModel
        {
            Query = q,
            ObjectTypeFilter = objectType,
            StatusFilter = status,
            PriorityFilter = priority,
            Results = all
                .OrderByDescending(x => x.UpdatedAt)
                .Take(Math.Max(1, limit))
                .ToList()
        };
    }

    public async Task<SavedViewReadModel> GetSavedViewAsync(
        WebReadRequest request,
        string viewKey,
        int limit = 50,
        CancellationToken ct = default)
    {
        var normalized = viewKey.Trim().ToLowerInvariant();
        return normalized switch
        {
            "blocking" => await BuildBlockingViewAsync(request, limit, ct),
            "current-period" => await BuildCurrentPeriodViewAsync(request, ct),
            "conflicts" => await BuildConflictsViewAsync(request, limit, ct),
            _ => new SavedViewReadModel
            {
                ViewKey = normalized,
                Title = "Сохраненный вид: неизвестный",
                Description = "Доступные виды: blocking, current-period, conflicts",
                Items = []
            }
        };
    }

    public async Task<DossierReadModel> GetDossierAsync(WebReadRequest request, int limit = 50, CancellationToken ct = default)
    {
        var scopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId);
        var artifact = await _stage6ArtifactRepository.GetCurrentAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Dossier,
            scopeKey,
            ct);
        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Dossier,
            ct);

        if (artifact != null)
        {
            var freshness = Stage6ArtifactFreshness.Evaluate(artifact, DateTime.UtcNow, evidence.LatestEvidenceAtUtc);
            if (!freshness.IsStale)
            {
                var persisted = Deserialize<DossierReadModel>(artifact.PayloadJson);
                if (persisted != null && IsStructuredDossier(persisted))
                {
                    _ = await _stage6ArtifactRepository.TouchReusedAsync(artifact.Id, DateTime.UtcNow, ct);
                    return persisted;
                }
            }

            _ = await _stage6ArtifactRepository.MarkStaleAsync(artifact.Id, freshness.Reason ?? "stale", DateTime.UtcNow, ct);
        }

        var questions = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(Math.Max(limit, 80))
            .ToList();

        var questionAnswers = new List<(ClarificationQuestion Question, ClarificationAnswer? Answer)>();
        foreach (var question in questions)
        {
            var latestAnswer = (await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct))
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
            questionAnswers.Add((question, latestAnswer));
        }

        var resolvedAnswers = questionAnswers
            .Where(x => x.Answer != null && x.Question.Status.Equals("resolved", StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.Question, Answer = x.Answer! })
            .OrderByDescending(x => x.Answer.CreatedAt)
            .ToList();

        var openQuestions = questionAnswers
            .Where(x => x.Answer == null || IsOpenWorkflowStatus(x.Question.Status))
            .Select(x => x.Question)
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();

        var confirmed = resolvedAnswers
            .Take(limit)
            .Select(x => new DossierItemReadModel
            {
                ObjectType = "clarification_answer",
                ObjectId = x.Answer.Id.ToString(),
                Title = x.Question.QuestionText,
                Summary = x.Answer.AnswerValue,
                ConfidenceLabel = x.Answer.AnswerConfidence.ToString("0.00"),
                Link = $"/history-object?objectType=clarification_question&objectId={x.Question.Id}",
                UpdatedAt = x.Answer.CreatedAt
            })
            .ToList();

        var hypotheses = (await _periodRepository.GetHypothesesByCaseAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .Select(x => new DossierItemReadModel
            {
                ObjectType = "hypothesis",
                ObjectId = x.Id.ToString(),
                Title = x.HypothesisType,
                Summary = x.Statement,
                ConfidenceLabel = $"{x.Status}:{x.Confidence:0.00}",
                Link = $"/search?objectType=hypothesis&q={Uri.EscapeDataString(x.Statement)}",
                UpdatedAt = x.UpdatedAt
            })
            .ToList();

        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .Select(x => new DossierItemReadModel
            {
                ObjectType = "conflict_record",
                ObjectId = x.Id.ToString(),
                Title = x.ConflictType,
                Summary = x.Summary,
                ConfidenceLabel = $"{x.Severity}:{x.Status}",
                Link = $"/history-object?objectType=conflict_record&objectId={x.Id}",
                UpdatedAt = x.UpdatedAt
            })
            .ToList();

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .Take(Math.Max(limit, 20))
            .ToList();

        var messages = (await _messageRepository.GetByChatAndPeriodAsync(
                request.ChatId,
                DateTime.UtcNow.AddDays(-45),
                DateTime.UtcNow,
                400,
                ct))
            .OrderByDescending(x => x.Timestamp)
            .ToList();

        var observedFacts = resolvedAnswers
            .Take(5)
            .Select(x => new DossierInsightReadModel
            {
                Title = x.Question.QuestionText,
                Detail = x.Answer.AnswerValue,
                SignalStrength = SignalFromConfidence(x.Answer.AnswerConfidence),
                Evidence = $"clarification_answer:{x.Answer.Id}",
                SourceObjectType = "clarification_answer",
                SourceObjectId = x.Answer.Id.ToString(),
                Link = $"/history-object?objectType=clarification_question&objectId={x.Question.Id}",
                UpdatedAt = x.Answer.CreatedAt
            })
            .ToList();

        if (messages.Count > 0)
        {
            var latestMessageAt = messages.Max(x => x.Timestamp);
            observedFacts.Add(new DossierInsightReadModel
            {
                Title = "Recent chat activity window",
                Detail = $"Observed {messages.Count} messages in the last 45 days; latest message at {latestMessageAt:yyyy-MM-dd HH:mm} UTC.",
                SignalStrength = messages.Count >= 30 ? "strong" : messages.Count >= 12 ? "medium" : "weak",
                Evidence = $"message_count:{messages.Count}",
                SourceObjectType = "message",
                SourceObjectId = messages.First().Id.ToString(),
                Link = $"/search?objectType=period&q={Uri.EscapeDataString("current")}",
                UpdatedAt = latestMessageAt
            });
        }

        var relationshipRead = periods
            .Take(4)
            .Select(x => new DossierInsightReadModel
            {
                Title = x.CustomLabel ?? x.Label,
                Detail = string.IsNullOrWhiteSpace(x.Summary)
                    ? "Period exists but summary is sparse."
                    : x.Summary,
                SignalStrength = SignalFromConfidence(x.InterpretationConfidence),
                Evidence = $"period:{x.Id}",
                SourceObjectType = "period",
                SourceObjectId = x.Id.ToString(),
                Link = $"/history-object?objectType=period&objectId={x.Id}",
                UpdatedAt = x.UpdatedAt
            })
            .ToList();

        var notableEvents = new List<DossierInsightReadModel>();
        notableEvents.AddRange(conflicts.Take(3).Select(x => new DossierInsightReadModel
        {
            Title = x.Title,
            Detail = x.Summary,
            SignalStrength = SignalFromConflictSeverity(x.ConfidenceLabel),
            Evidence = $"conflict:{x.ObjectId}",
            SourceObjectType = x.ObjectType,
            SourceObjectId = x.ObjectId,
            Link = x.Link,
            UpdatedAt = x.UpdatedAt
        }));
        notableEvents.AddRange(resolvedAnswers.Take(2).Select(x => new DossierInsightReadModel
        {
            Title = "Уточнение закрыто",
            Detail = $"{x.Question.QuestionText}: {x.Answer.AnswerValue}",
            SignalStrength = SignalFromConfidence(x.Answer.AnswerConfidence),
            Evidence = $"clarification_answer:{x.Answer.Id}",
            SourceObjectType = "clarification_answer",
            SourceObjectId = x.Answer.Id.ToString(),
            Link = $"/history-object?objectType=clarification_question&objectId={x.Question.Id}",
            UpdatedAt = x.Answer.CreatedAt
        }));

        var likelyInterpretation = hypotheses
            .Take(4)
            .Select(x => new DossierInsightReadModel
            {
                Title = x.Title,
                Detail = x.Summary,
                SignalStrength = SignalFromHypothesisStatus(x.ConfidenceLabel),
                Evidence = $"hypothesis:{x.ObjectId}",
                SourceObjectType = x.ObjectType,
                SourceObjectId = x.ObjectId,
                Link = x.Link,
                UpdatedAt = x.UpdatedAt
            })
            .ToList();

        if (likelyInterpretation.Count == 0)
        {
            likelyInterpretation.Add(new DossierInsightReadModel
            {
                Title = "Интерпретация пока неполная",
                Detail = "Явных гипотез мало, поэтому рабочая картина остается предварительной.",
                SignalStrength = "weak",
                Evidence = "hypothesis:none",
                Link = "/search?objectType=hypothesis",
                UpdatedAt = DateTime.UtcNow
            });
        }

        var uncertainties = new List<DossierInsightReadModel>();
        uncertainties.AddRange(openQuestions.Take(4).Select(x => new DossierInsightReadModel
        {
            Title = x.QuestionText,
            Detail = string.IsNullOrWhiteSpace(x.WhyItMatters)
                ? "Вопрос открыт и влияет на качество следующего решения."
                : x.WhyItMatters,
            SignalStrength = x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase) ? "contradictory" : "weak",
            Evidence = $"clarification_question:{x.Id}",
            SourceObjectType = "clarification_question",
            SourceObjectId = x.Id.ToString(),
            Link = $"/history-object?objectType=clarification_question&objectId={x.Id}",
            UpdatedAt = x.UpdatedAt
        }));
        uncertainties.AddRange(conflicts
            .Where(x => x.ConfidenceLabel.Contains("open", StringComparison.OrdinalIgnoreCase)
                        || x.ConfidenceLabel.Contains("review", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .Select(x => new DossierInsightReadModel
            {
                Title = $"Conflict: {x.Title}",
                Detail = x.Summary,
                SignalStrength = "contradictory",
                Evidence = $"conflict:{x.ObjectId}",
                SourceObjectType = x.ObjectType,
                SourceObjectId = x.ObjectId,
                Link = x.Link,
                UpdatedAt = x.UpdatedAt
            }));

        var missingInformation = openQuestions
            .Take(4)
            .Select(x => new DossierInsightReadModel
            {
                Title = "Missing clarification input",
                Detail = x.QuestionText,
                SignalStrength = x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase) ? "medium" : "weak",
                Evidence = $"clarification_question:{x.Id}",
                SourceObjectType = "clarification_question",
                SourceObjectId = x.Id.ToString(),
                Link = $"/history-object?objectType=clarification_question&objectId={x.Id}",
                UpdatedAt = x.UpdatedAt
            })
            .ToList();

        var practicalInterpretation = new List<DossierInsightReadModel>();
        if (uncertainties.Any(x => x.SignalStrength == "contradictory"))
        {
            practicalInterpretation.Add(new DossierInsightReadModel
            {
                Title = "Stabilize before escalation",
                Detail = "Есть противоречия: сильные выводы пока считаем предварительными до сверки ключевых конфликтов.",
                SignalStrength = "medium",
                Evidence = "conflict_and_uncertainty_present",
                Link = "/view/conflicts",
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (openQuestions.Count > 0)
        {
            practicalInterpretation.Add(new DossierInsightReadModel
            {
                Title = "Priority next step",
                Detail = "Сначала закройте уточнение с максимальным приоритетом, затем обновляйте стратегию и черновик.",
                SignalStrength = openQuestions.Any(x => x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase))
                    ? "strong"
                    : "medium",
                Evidence = $"open_clarifications:{openQuestions.Count}",
                Link = "/clarifications",
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (practicalInterpretation.Count == 0)
        {
            practicalInterpretation.Add(new DossierInsightReadModel
            {
                Title = "Low immediate risk",
                Detail = "Критичных противоречий не видно; можно продолжать обычный мониторинг.",
                SignalStrength = "weak",
                Evidence = "no_blocking_signal",
                Link = "/dashboard",
                UpdatedAt = DateTime.UtcNow
            });
        }

        var summary = BuildDossierSummary(observedFacts, likelyInterpretation, uncertainties, missingInformation);

        var model = new DossierReadModel
        {
            Summary = summary,
            ObservedFacts = CapByRecency(observedFacts, 6),
            RelationshipRead = CapByRecency(relationshipRead, 5),
            NotableEvents = CapByRecency(notableEvents, 5),
            LikelyInterpretation = CapByRecency(likelyInterpretation, 5),
            Uncertainties = CapByRecency(uncertainties, 5),
            MissingInformation = CapByRecency(missingInformation, 5),
            PracticalInterpretation = CapByRecency(practicalInterpretation, 4),
            Confirmed = confirmed,
            Hypotheses = hypotheses,
            Conflicts = conflicts
        };

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.Dossier,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ScopeKey = scopeKey,
            PayloadObjectType = "dossier",
            PayloadObjectId = $"{request.CaseId}:{request.ChatId}",
            PayloadJson = JsonSerializer.Serialize(model, JsonOptions),
            FreshnessBasisHash = evidence.BasisHash,
            FreshnessBasisJson = evidence.BasisJson,
            GeneratedAt = DateTime.UtcNow,
            RefreshedAt = DateTime.UtcNow,
            StaleAt = DateTime.UtcNow.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.Dossier)),
            IsStale = false,
            SourceType = "web_read",
            SourceId = "dossier_page",
            SourceMessageId = null,
            SourceSessionId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return model;
    }

    private static bool IsStructuredDossier(DossierReadModel model)
    {
        return !string.IsNullOrWhiteSpace(model.Summary)
               || model.ObservedFacts.Count > 0
               || model.LikelyInterpretation.Count > 0
               || model.Uncertainties.Count > 0
               || model.MissingInformation.Count > 0;
    }

    private static string SignalFromConfidence(float confidence)
    {
        if (confidence >= 0.78f)
        {
            return "strong";
        }

        if (confidence >= 0.55f)
        {
            return "medium";
        }

        return "weak";
    }

    private static string SignalFromHypothesisStatus(string confidenceLabel)
    {
        if (confidenceLabel.Contains("rejected", StringComparison.OrdinalIgnoreCase)
            || confidenceLabel.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            return "contradictory";
        }

        var marker = confidenceLabel.Split(':').LastOrDefault();
        if (marker != null
            && float.TryParse(marker, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return SignalFromConfidence(numeric);
        }

        return "weak";
    }

    private static string SignalFromConflictSeverity(string confidenceLabel)
    {
        if (confidenceLabel.Contains("critical", StringComparison.OrdinalIgnoreCase)
            || confidenceLabel.Contains("high", StringComparison.OrdinalIgnoreCase))
        {
            return "contradictory";
        }

        if (confidenceLabel.Contains("medium", StringComparison.OrdinalIgnoreCase))
        {
            return "medium";
        }

        return "weak";
    }

    private static bool IsOpenWorkflowStatus(string status)
    {
        return status.Equals("open", StringComparison.OrdinalIgnoreCase)
               || status.Equals("ready", StringComparison.OrdinalIgnoreCase)
               || status.Equals("needs_user_input", StringComparison.OrdinalIgnoreCase)
               || status.Equals("review", StringComparison.OrdinalIgnoreCase);
    }

    private static List<DossierInsightReadModel> CapByRecency(
        IEnumerable<DossierInsightReadModel> rows,
        int max)
    {
        return rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) && !string.IsNullOrWhiteSpace(x.Detail))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(max)
            .ToList();
    }

    private static string BuildDossierSummary(
        IReadOnlyCollection<DossierInsightReadModel> observedFacts,
        IReadOnlyCollection<DossierInsightReadModel> likelyInterpretation,
        IReadOnlyCollection<DossierInsightReadModel> uncertainties,
        IReadOnlyCollection<DossierInsightReadModel> missingInformation)
    {
        var known = SelectTopSummaryLine(observedFacts, "Подтвержденных наблюдений пока мало.");
        var matters = SelectTopSummaryLine(likelyInterpretation, "Стабильная интерпретация пока не сформирована.");
        var contradictionCount = uncertainties.Count(x => x.SignalStrength == "contradictory");
        var uncertain = contradictionCount > 0
            ? $"Есть {contradictionCount} противоречивых сигналов, их нужно закрыть перед сильными выводами."
            : SelectTopSummaryLine(uncertainties, "Критичных неопределенностей сейчас не видно.");
        var missing = missingInformation.Count > 0
            ? $"Не хватает данных по {missingInformation.Count} открытым вопросам."
            : "Ключевых пробелов в данных не зафиксировано.";
        return $"Что известно: {known} Что важно: {matters} Что неясно: {uncertain} {missing}";
    }

    private static string SelectTopSummaryLine(
        IEnumerable<DossierInsightReadModel> rows,
        string fallback)
    {
        var candidate = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Detail))
            .OrderByDescending(x => SignalWeight(x.SignalStrength))
            .ThenByDescending(x => x.UpdatedAt)
            .Select(x => x.Detail.Trim())
            .FirstOrDefault();

        return candidate ?? fallback;
    }

    private static int SignalWeight(string? signalStrength)
    {
        return (signalStrength ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "contradictory" => 4,
            "strong" => 3,
            "medium" => 2,
            _ => 1
        };
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
        catch
        {
            return default;
        }
    }

    private async Task<List<SearchResultReadModel>> CollectSearchItemsAsync(WebReadRequest request, CancellationToken ct)
    {
        var items = new List<SearchResultReadModel>();

        var inbox = (await _inboxConflictRepository.GetInboxItemsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        items.AddRange(inbox.Select(x => new SearchResultReadModel
        {
            ObjectType = "inbox_item",
            ObjectId = x.Id.ToString(),
            Title = x.Title,
            Summary = x.Summary,
            Status = x.Status,
            Priority = x.Priority,
            UpdatedAt = x.UpdatedAt,
            Link = $"/history-object?objectType=inbox_item&objectId={x.Id}"
        }));

        var clarifications = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        items.AddRange(clarifications.Select(x => new SearchResultReadModel
        {
            ObjectType = "clarification_question",
            ObjectId = x.Id.ToString(),
            Title = x.QuestionText,
            Summary = x.WhyItMatters,
            Status = x.Status,
            Priority = x.Priority,
            UpdatedAt = x.UpdatedAt,
            Link = $"/history-object?objectType=clarification_question&objectId={x.Id}"
        }));

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        items.AddRange(periods.Select(x => new SearchResultReadModel
        {
            ObjectType = "period",
            ObjectId = x.Id.ToString(),
            Title = x.Label,
            Summary = x.Summary,
            Status = x.IsOpen ? "open" : "closed",
            Priority = x.ReviewPriority.ToString(),
            UpdatedAt = x.UpdatedAt,
            Link = $"/history-object?objectType=period&objectId={x.Id}"
        }));

        var hypotheses = (await _periodRepository.GetHypothesesByCaseAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        items.AddRange(hypotheses.Select(x => new SearchResultReadModel
        {
            ObjectType = "hypothesis",
            ObjectId = x.Id.ToString(),
            Title = x.HypothesisType,
            Summary = x.Statement,
            Status = x.Status,
            Priority = null,
            UpdatedAt = x.UpdatedAt,
            Link = $"/search?objectType=hypothesis&q={Uri.EscapeDataString(x.Statement)}"
        }));

        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        items.AddRange(conflicts.Select(x => new SearchResultReadModel
        {
            ObjectType = "conflict_record",
            ObjectId = x.Id.ToString(),
            Title = x.ConflictType,
            Summary = x.Summary,
            Status = x.Status,
            Priority = x.Severity,
            UpdatedAt = x.UpdatedAt,
            Link = $"/history-object?objectType=conflict_record&objectId={x.Id}"
        }));

        var strategyRecords = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .ToList();
        foreach (var record in strategyRecords)
        {
            items.Add(new SearchResultReadModel
            {
                ObjectType = "strategy_record",
                ObjectId = record.Id.ToString(),
                Title = record.RecommendedGoal,
                Summary = record.MicroStep,
                Status = null,
                Priority = null,
                UpdatedAt = record.CreatedAt,
                Link = $"/outcomes?strategyRecordId={record.Id}"
            });

            var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(record.Id, ct);
            items.AddRange(options.Select(x => new SearchResultReadModel
            {
                ObjectType = "strategy_option",
                ObjectId = x.Id.ToString(),
                Title = x.ActionType,
                Summary = x.Summary,
                Status = null,
                Priority = x.IsPrimary ? "primary" : "alternative",
                UpdatedAt = record.CreatedAt,
                Link = $"/outcomes?strategyRecordId={record.Id}"
            }));

            var drafts = await _strategyDraftRepository.GetDraftRecordsByStrategyRecordIdAsync(record.Id, ct);
            items.AddRange(drafts.Select(x => new SearchResultReadModel
            {
                ObjectType = "draft_record",
                ObjectId = x.Id.ToString(),
                Title = "draft",
                Summary = x.MainDraft,
                Status = null,
                Priority = null,
                UpdatedAt = x.CreatedAt,
                Link = $"/outcomes?draftId={x.Id}"
            }));

            var outcomes = await _strategyDraftRepository.GetDraftOutcomesByStrategyRecordIdAsync(record.Id, ct);
            items.AddRange(outcomes.Select(x => new SearchResultReadModel
            {
                ObjectType = "draft_outcome",
                ObjectId = x.Id.ToString(),
                Title = x.OutcomeLabel,
                Summary = x.Notes ?? $"{x.MatchedBy ?? "match"} score={(x.MatchScore ?? 0f):0.00}",
                Status = x.SystemOutcomeLabel,
                Priority = x.UserOutcomeLabel,
                UpdatedAt = x.CreatedAt,
                Link = $"/outcomes?outcomeId={x.Id}"
            }));
        }

        var (selfSenderId, otherSenderId) = await ResolveSelfOtherSendersAsync(request.ChatId, ct);
        var profileTargets = new[]
        {
            ("self", selfSenderId.ToString()),
            ("other", otherSenderId.ToString()),
            ("pair", $"{selfSenderId}:{otherSenderId}")
        };

        foreach (var (subjectType, subjectId) in profileTargets)
        {
            var snapshots = await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(request.CaseId, subjectType, subjectId, ct);
            var snapshot = snapshots
                .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
            if (snapshot == null)
            {
                continue;
            }

            items.Add(new SearchResultReadModel
            {
                ObjectType = "profile_snapshot",
                ObjectId = snapshot.Id.ToString(),
                Title = $"{subjectType} profile",
                Summary = snapshot.Summary,
                Status = null,
                Priority = null,
                UpdatedAt = snapshot.CreatedAt,
                Link = "/profiles"
            });

            var traits = await _stateProfileRepository.GetProfileTraitsBySnapshotIdAsync(snapshot.Id, ct);
            items.AddRange(traits.Take(6).Select(x => new SearchResultReadModel
            {
                ObjectType = "profile_trait",
                ObjectId = x.Id.ToString(),
                Title = x.TraitKey,
                Summary = x.ValueLabel,
                Status = null,
                Priority = x.IsSensitive ? "sensitive" : "standard",
                UpdatedAt = x.CreatedAt,
                Link = "/profiles"
            }));
        }

        return items;
    }

    private async Task<SavedViewReadModel> BuildBlockingViewAsync(WebReadRequest request, int limit, CancellationToken ct)
    {
        var rows = (await _inboxConflictRepository.GetInboxItemsAsync(request.CaseId, "open", ct))
            .Where(x => (x.ChatId == null || x.ChatId == request.ChatId) && (x.IsBlocking || x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .Select(x => new SearchResultReadModel
            {
                ObjectType = "inbox_item",
                ObjectId = x.Id.ToString(),
                Title = x.Title,
                Summary = x.Summary,
                Status = x.Status,
                Priority = x.Priority,
                UpdatedAt = x.UpdatedAt,
                Link = $"/history-object?objectType=inbox_item&objectId={x.Id}"
            })
            .ToList();

        return new SavedViewReadModel
        {
            ViewKey = "blocking",
            Title = "Сохраненный вид: блокирующие",
            Description = "Открытые блокирующие элементы, требующие внимания в первую очередь.",
            Items = rows
        };
    }

    private async Task<SavedViewReadModel> BuildCurrentPeriodViewAsync(WebReadRequest request, CancellationToken ct)
    {
        var current = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .FirstOrDefault(x => x.IsOpen)
            ?? (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .FirstOrDefault();

        var items = new List<SearchResultReadModel>();
        if (current != null)
        {
            items.Add(new SearchResultReadModel
            {
                ObjectType = "period",
                ObjectId = current.Id.ToString(),
                Title = current.Label,
                Summary = current.Summary,
                Status = current.IsOpen ? "open" : "closed",
                Priority = current.ReviewPriority.ToString(),
                UpdatedAt = current.UpdatedAt,
                Link = $"/history-object?objectType=period&objectId={current.Id}"
            });
        }

        return new SavedViewReadModel
        {
            ViewKey = "current-period",
            Title = "Сохраненный вид: текущий период",
            Description = "Быстрый переход к текущему периоду.",
            Items = items
        };
    }

    private async Task<SavedViewReadModel> BuildConflictsViewAsync(WebReadRequest request, int limit, CancellationToken ct)
    {
        var rows = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => (x.ChatId == null || x.ChatId == request.ChatId)
                        && (x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
                            || x.Status.Equals("deferred", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .Select(x => new SearchResultReadModel
            {
                ObjectType = "conflict_record",
                ObjectId = x.Id.ToString(),
                Title = x.ConflictType,
                Summary = x.Summary,
                Status = x.Status,
                Priority = x.Severity,
                UpdatedAt = x.UpdatedAt,
                Link = $"/history-object?objectType=conflict_record&objectId={x.Id}"
            })
            .ToList();

        return new SavedViewReadModel
        {
            ViewKey = "conflicts",
            Title = "Сохраненный вид: конфликты",
            Description = "Открытые и отложенные конфликты для повторной интерпретации.",
            Items = rows
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

    private static bool ContainsInvariant(string text, string query)
    {
        return !string.IsNullOrWhiteSpace(text)
               && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
