using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Clarification;

public class ClarificationOrchestrationVerificationService
{
    private readonly IClarificationOrchestrator _clarificationOrchestrator;
    private readonly IDependencyLinkRepository _dependencyLinkRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly ILogger<ClarificationOrchestrationVerificationService> _logger;

    public ClarificationOrchestrationVerificationService(
        IClarificationOrchestrator clarificationOrchestrator,
        IDependencyLinkRepository dependencyLinkRepository,
        IClarificationRepository clarificationRepository,
        ILogger<ClarificationOrchestrationVerificationService> logger)
    {
        _clarificationOrchestrator = clarificationOrchestrator;
        _dependencyLinkRepository = dependencyLinkRepository;
        _clarificationRepository = clarificationRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var caseId = 92020000 + DateTime.UtcNow.Minute;
        var created = await _clarificationOrchestrator.EnqueueQuestionsAsync(
            caseId,
            [
                new ClarificationQuestionDraft
                {
                    ChatId = caseId,
                    QuestionText = "Did transition happen after the conflict event?",
                    QuestionType = "timeline_state_strategy",
                    Priority = "blocking",
                    ExpectedGain = 0.9f,
                    AffectedOutputsJson = "[\"periods\",\"state\",\"profiles\",\"strategy\"]",
                    WhyItMatters = "Affects period ordering, state and strategy"
                },
                new ClarificationQuestionDraft
                {
                    ChatId = caseId,
                    QuestionText = "If yes, what exact date?",
                    QuestionType = "timeline_followup",
                    Priority = "important",
                    ExpectedGain = 0.6f,
                    AffectedOutputsJson = "[\"periods\"]",
                    WhyItMatters = "Refines parent answer"
                },
                new ClarificationQuestionDraft
                {
                    ChatId = caseId,
                    QuestionText = "Confirm again if transition happened after conflict",
                    QuestionType = "timeline_duplicate",
                    Priority = "important",
                    ExpectedGain = 0.4f,
                    AffectedOutputsJson = "[\"periods\"]",
                    WhyItMatters = "Duplicate check"
                }
            ],
            actor: "clarification_smoke",
            ct: ct);

        if (created.Count != 3)
        {
            throw new InvalidOperationException($"Clarification smoke failed during queue creation. Expected 3 questions, got {created.Count}.");
        }

        var parent = created[0];
        var child = created[1];
        var duplicate = created[2];

        await _dependencyLinkRepository.CreateDependencyLinkAsync(new DependencyLink
        {
            UpstreamType = "clarification_question",
            UpstreamId = parent.Id.ToString(),
            DownstreamType = "clarification_question",
            DownstreamId = child.Id.ToString(),
            LinkType = "depends_on",
            LinkReason = "resolve_on_parent"
        }, ct);

        await _dependencyLinkRepository.CreateDependencyLinkAsync(new DependencyLink
        {
            UpstreamType = "clarification_question",
            UpstreamId = parent.Id.ToString(),
            DownstreamType = "clarification_question",
            DownstreamId = duplicate.Id.ToString(),
            LinkType = "duplicate_of",
            LinkReason = "collapse_under_parent"
        }, ct);

        var queue = await _clarificationOrchestrator.BuildQueueAsync(caseId, ct);
        if (queue.Count < 3 || queue[0].Question.Id != parent.Id)
        {
            throw new InvalidOperationException("Clarification smoke queue ordering check failed.");
        }

        _ = await _clarificationOrchestrator.ApplyAnswerAsync(new ClarificationApplyRequest
        {
            QuestionId = parent.Id,
            AnswerType = "boolean",
            AnswerValue = "yes",
            AnswerConfidence = 0.95f,
            SourceClass = "user_confirmed",
            SourceType = "user",
            SourceId = "smoke-user",
            Actor = "clarification_smoke",
            Reason = "first answer",
            MarkResolved = true
        }, ct);

        var childAfter = await _clarificationRepository.GetQuestionByIdAsync(child.Id, ct);
        var duplicateAfter = await _clarificationRepository.GetQuestionByIdAsync(duplicate.Id, ct);
        if (childAfter == null || duplicateAfter == null)
        {
            throw new InvalidOperationException("Clarification smoke failed to read dependent questions.");
        }

        if (!string.Equals(childAfter.Status, "resolved", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(childAfter.Priority, "optional", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Clarification smoke dependency resolution check failed for child question.");
        }

        if (!string.Equals(duplicateAfter.Status, "resolved", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Clarification smoke duplicate collapse check failed.");
        }

        var secondApply = await _clarificationOrchestrator.ApplyAnswerAsync(new ClarificationApplyRequest
        {
            QuestionId = parent.Id,
            AnswerType = "boolean",
            AnswerValue = "no",
            AnswerConfidence = 0.95f,
            SourceClass = "user_confirmed",
            SourceType = "user",
            SourceId = "smoke-user",
            Actor = "clarification_smoke",
            Reason = "contradictory answer",
            MarkResolved = true
        }, ct);

        if (secondApply.Conflicts.Count == 0)
        {
            throw new InvalidOperationException("Clarification smoke contradiction check failed: no conflict was created or updated.");
        }

        var layers = secondApply.RecomputePlan.Targets.Select(x => x.Layer).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!layers.Contains("periods") || !layers.Contains("state") || !layers.Contains("profiles") || !layers.Contains("strategy_artifacts"))
        {
            throw new InvalidOperationException("Clarification smoke recompute target planning check failed: required layers are missing.");
        }

        _logger.LogInformation(
            "Clarification smoke passed. case_id={CaseId}, queue={QueueCount}, conflicts={ConflictCount}, targets={TargetCount}",
            caseId,
            queue.Count,
            secondApply.Conflicts.Count,
            secondApply.RecomputePlan.Targets.Count);
    }
}
