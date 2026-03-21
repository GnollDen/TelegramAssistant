using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Intelligence.Stage6.Clarification;

public class ClarificationOrchestrator : IClarificationOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IClarificationRepository _clarificationRepository;
    private readonly IDependencyLinkRepository _dependencyLinkRepository;
    private readonly IClarificationAnswerApplier _answerApplier;
    private readonly IClarificationDependencyResolver _dependencyResolver;
    private readonly IRecomputeTargetPlanner _recomputeTargetPlanner;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<ClarificationOrchestrator> _logger;

    public ClarificationOrchestrator(
        IClarificationRepository clarificationRepository,
        IDependencyLinkRepository dependencyLinkRepository,
        IClarificationAnswerApplier answerApplier,
        IClarificationDependencyResolver dependencyResolver,
        IRecomputeTargetPlanner recomputeTargetPlanner,
        IDomainReviewEventRepository domainReviewEventRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<ClarificationOrchestrator> logger)
    {
        _clarificationRepository = clarificationRepository;
        _dependencyLinkRepository = dependencyLinkRepository;
        _answerApplier = answerApplier;
        _dependencyResolver = dependencyResolver;
        _recomputeTargetPlanner = recomputeTargetPlanner;
        _domainReviewEventRepository = domainReviewEventRepository;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClarificationQuestion>> EnqueueQuestionsAsync(
        long caseId,
        IReadOnlyCollection<ClarificationQuestionDraft> drafts,
        string actor,
        CancellationToken ct = default)
    {
        if (drafts.Count == 0)
        {
            return [];
        }

        var created = new List<ClarificationQuestion>(drafts.Count);
        foreach (var draft in drafts)
        {
            var priority = NormalizePriority(draft.Priority, draft.QuestionType, draft.AffectedOutputsJson, draft.ExpectedGain);
            var question = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
            {
                CaseId = caseId,
                ChatId = draft.ChatId,
                QuestionText = draft.QuestionText.Trim(),
                QuestionType = draft.QuestionType.Trim(),
                Priority = priority,
                Status = "open",
                PeriodId = draft.PeriodId,
                RelatedHypothesisId = draft.RelatedHypothesisId,
                AffectedOutputsJson = NormalizeJsonArray(draft.AffectedOutputsJson),
                WhyItMatters = draft.WhyItMatters,
                ExpectedGain = Math.Clamp(draft.ExpectedGain, 0, 1),
                AnswerOptionsJson = NormalizeJsonArray(draft.AnswerOptionsJson),
                SourceType = string.IsNullOrWhiteSpace(draft.SourceType) ? "system" : draft.SourceType,
                SourceId = draft.SourceId,
                SourceMessageId = draft.SourceMessageId,
                SourceSessionId = draft.SourceSessionId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct);

            created.Add(question);

            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "clarification_question",
                ObjectId = question.Id.ToString(),
                Action = "enqueue",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    question.Priority,
                    question.QuestionType,
                    question.ExpectedGain,
                    question.AffectedOutputsJson
                }, JsonOptions),
                Reason = "orchestration_enqueue",
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        _logger.LogInformation("Clarification queue enqueue: case_id={CaseId}, created={Count}", caseId, created.Count);
        return created;
    }

    public async Task<IReadOnlyList<ClarificationQueueItem>> BuildQueueAsync(long caseId, CancellationToken ct = default)
    {
        var openQuestions = await _clarificationRepository.GetQuestionsAsync(caseId, status: "open", ct: ct);
        var inProgressQuestions = await _clarificationRepository.GetQuestionsAsync(caseId, status: "in_progress", ct: ct);
        var candidates = openQuestions.Concat(inProgressQuestions).ToList();

        var queueItems = new List<ClarificationQueueItem>(candidates.Count);
        foreach (var question in candidates)
        {
            var impacts = DetectImpacts(question.QuestionType, question.AffectedOutputsJson, question.PeriodId.HasValue);
            var blockedByDependency = await IsBlockedByDependencyAsync(question, ct);
            var score = ScoreQuestion(question.Priority, question.ExpectedGain, impacts.TimelineImpact, impacts.StateImpact, impacts.StrategyImpact, blockedByDependency);

            queueItems.Add(new ClarificationQueueItem
            {
                Question = question,
                QueueScore = score,
                IsBlockedByDependency = blockedByDependency,
                TimelineImpact = impacts.TimelineImpact,
                StateImpact = impacts.StateImpact,
                StrategyImpact = impacts.StrategyImpact
            });
        }

        return queueItems
            .OrderByDescending(x => x.QueueScore)
            .ThenByDescending(x => x.Question.CreatedAt)
            .ToList();
    }

    public async Task<ClarificationApplyResult> ApplyAnswerAsync(ClarificationApplyRequest request, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        using var scope = AmbientDbContextScope.Enter(db);

        var (question, answer, conflicts) = await _answerApplier.ApplyAsync(request, ct);
        var dependencyUpdates = await _dependencyResolver.ResolveAfterParentAnswerAsync(
            question,
            answer,
            request.Actor,
            request.Reason,
            ct);

        var recomputePlan = await _recomputeTargetPlanner.BuildPlanAsync(question, answer, dependencyUpdates, conflicts, ct);

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "clarification_question",
            ObjectId = question.Id.ToString(),
            Action = "recompute_targets_planned",
            NewValueRef = JsonSerializer.Serialize(new { targets = recomputePlan.Targets }, JsonOptions),
            Reason = request.Reason,
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Clarification answer applied: question_id={QuestionId}, conflicts={ConflictCount}, dependency_updates={DependencyCount}, recompute_targets={TargetCount}",
            question.Id,
            conflicts.Count,
            dependencyUpdates.Count,
            recomputePlan.Targets.Count);

        return new ClarificationApplyResult
        {
            Question = question,
            Answer = answer,
            Conflicts = conflicts,
            DependencyUpdates = dependencyUpdates,
            RecomputePlan = recomputePlan
        };
    }

    private async Task<bool> IsBlockedByDependencyAsync(ClarificationQuestion question, CancellationToken ct)
    {
        var upstreamLinks = await _dependencyLinkRepository.GetByDownstreamAsync("clarification_question", question.Id.ToString(), ct);
        foreach (var link in upstreamLinks)
        {
            if (!IsBlockingLink(link.LinkType))
            {
                continue;
            }

            if (!Guid.TryParse(link.UpstreamId, out var parentId))
            {
                continue;
            }

            var parent = await _clarificationRepository.GetQuestionByIdAsync(parentId, ct);
            if (parent == null)
            {
                continue;
            }

            if (!string.Equals(parent.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ScoreQuestion(string priority, float expectedGain, bool timelineImpact, bool stateImpact, bool strategyImpact, bool blockedByDependency)
    {
        var priorityScore = priority.Trim().ToLowerInvariant() switch
        {
            "blocking" => 300,
            "important" => 200,
            _ => 100
        };

        var impactScore = 0;
        if (timelineImpact)
        {
            impactScore += 45;
        }

        if (stateImpact)
        {
            impactScore += 35;
        }

        if (strategyImpact)
        {
            impactScore += 30;
        }

        var gainScore = (int)Math.Round(Math.Clamp(expectedGain, 0, 1) * 20);
        var blockedPenalty = blockedByDependency ? 80 : 0;
        return priorityScore + impactScore + gainScore - blockedPenalty;
    }

    private static string NormalizePriority(string? priority, string questionType, string affectedOutputsJson, float expectedGain)
    {
        if (!string.IsNullOrWhiteSpace(priority))
        {
            var normalized = priority.Trim().ToLowerInvariant();
            if (normalized is "blocking" or "important" or "optional")
            {
                return normalized;
            }
        }

        var impacts = DetectImpacts(questionType, affectedOutputsJson, hasPeriod: false);
        var impactCount = (impacts.TimelineImpact ? 1 : 0) + (impacts.StateImpact ? 1 : 0) + (impacts.StrategyImpact ? 1 : 0);
        if (impactCount >= 2 || expectedGain >= 0.75f)
        {
            return "blocking";
        }

        if (impactCount == 1 || expectedGain >= 0.35f)
        {
            return "important";
        }

        return "optional";
    }

    private static (bool TimelineImpact, bool StateImpact, bool StrategyImpact) DetectImpacts(string questionType, string affectedOutputsJson, bool hasPeriod)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in ParseJsonStringArray(affectedOutputsJson))
        {
            tokens.Add(token);
        }

        tokens.Add(questionType);

        var timelineImpact = hasPeriod || tokens.Any(x => x.Contains("period", StringComparison.OrdinalIgnoreCase) || x.Contains("timeline", StringComparison.OrdinalIgnoreCase) || x.Contains("transition", StringComparison.OrdinalIgnoreCase));
        var stateImpact = tokens.Any(x => x.Contains("state", StringComparison.OrdinalIgnoreCase) || x.Contains("status", StringComparison.OrdinalIgnoreCase));
        var strategyImpact = tokens.Any(x => x.Contains("strategy", StringComparison.OrdinalIgnoreCase) || x.Contains("draft", StringComparison.OrdinalIgnoreCase) || x.Contains("action", StringComparison.OrdinalIgnoreCase));
        return (timelineImpact, stateImpact, strategyImpact);
    }

    private static IEnumerable<string> ParseJsonStringArray(string json)
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

            var result = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value.Trim());
                    }
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsBlockingLink(string linkType)
    {
        var normalized = linkType.Trim().ToLowerInvariant();
        return normalized is "depends_on" or "blocks" or "duplicate_of";
    }

    private static string NormalizeJsonArray(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return "[]";
            }

            return doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return "[]";
        }
    }
}
