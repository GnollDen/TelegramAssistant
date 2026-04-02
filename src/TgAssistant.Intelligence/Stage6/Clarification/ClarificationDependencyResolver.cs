// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Clarification;

public class ClarificationDependencyResolver : IClarificationDependencyResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IClarificationRepository _clarificationRepository;
    private readonly IDependencyLinkRepository _dependencyLinkRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;

    public ClarificationDependencyResolver(
        IClarificationRepository clarificationRepository,
        IDependencyLinkRepository dependencyLinkRepository,
        IDomainReviewEventRepository domainReviewEventRepository)
    {
        _clarificationRepository = clarificationRepository;
        _dependencyLinkRepository = dependencyLinkRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
    }

    public async Task<List<ClarificationDependencyUpdate>> ResolveAfterParentAnswerAsync(
        ClarificationQuestion parentQuestion,
        ClarificationAnswer answer,
        string actor,
        string? reason,
        CancellationToken ct = default)
    {
        var updates = new List<ClarificationDependencyUpdate>();
        var links = await _dependencyLinkRepository.GetByUpstreamAsync("clarification_question", parentQuestion.Id.ToString(), ct);

        foreach (var link in links)
        {
            if (!string.Equals(link.DownstreamType, "clarification_question", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Guid.TryParse(link.DownstreamId, out var childId))
            {
                continue;
            }

            var child = await _clarificationRepository.GetQuestionByIdAsync(childId, ct);
            if (child == null)
            {
                continue;
            }

            if (string.Equals(child.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var linkType = link.LinkType.Trim().ToLowerInvariant();
            var oldStatus = child.Status;
            var oldPriority = child.Priority;
            var nextStatus = child.Status;
            var nextPriority = child.Priority;
            var changeReason = string.Empty;

            if (linkType is "duplicate_of" or "duplicates")
            {
                nextStatus = "resolved";
                changeReason = "duplicate_collapsed_under_parent";
            }
            else if (linkType is "depends_on" or "blocks" or "parent_of")
            {
                if (ShouldResolveDependent(answer, link.LinkReason))
                {
                    nextStatus = "resolved";
                    changeReason = "resolved_by_parent_answer";
                }
                else
                {
                    nextPriority = DowngradePriority(child.Priority);
                    nextStatus = "open";
                    changeReason = "downgraded_after_parent_answer";
                }
            }
            else
            {
                continue;
            }

            if (string.Equals(oldStatus, nextStatus, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(oldPriority, nextPriority, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var updated = await _clarificationRepository.UpdateQuestionWorkflowAsync(child.Id, nextStatus, nextPriority, actor, reason ?? changeReason, ct);
            if (!updated)
            {
                continue;
            }

            updates.Add(new ClarificationDependencyUpdate
            {
                QuestionId = child.Id,
                OldStatus = oldStatus,
                NewStatus = nextStatus,
                OldPriority = oldPriority,
                NewPriority = nextPriority,
                ChangeReason = changeReason
            });

            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "clarification_question",
                ObjectId = child.Id.ToString(),
                Action = "dependency_adjustment",
                OldValueRef = JsonSerializer.Serialize(new { status = oldStatus, priority = oldPriority }, JsonOptions),
                NewValueRef = JsonSerializer.Serialize(new { status = nextStatus, priority = nextPriority }, JsonOptions),
                Reason = reason ?? changeReason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        return updates;
    }

    private static bool ShouldResolveDependent(ClarificationAnswer answer, string? linkReason)
    {
        if (!string.IsNullOrWhiteSpace(linkReason) &&
            (linkReason.Contains("resolve_on_parent", StringComparison.OrdinalIgnoreCase) ||
             linkReason.Contains("collapse", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var normalizedAnswer = answer.AnswerValue.Trim().ToLowerInvariant();
        return normalizedAnswer is "yes" or "true" or "confirmed";
    }

    private static string DowngradePriority(string priority)
    {
        var normalized = priority.Trim().ToLowerInvariant();
        return normalized switch
        {
            "blocking" => "important",
            "important" => "optional",
            _ => "optional"
        };
    }
}
