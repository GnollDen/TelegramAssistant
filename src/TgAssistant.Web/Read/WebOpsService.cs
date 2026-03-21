using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebOpsService : IWebOpsService
{
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;

    public WebOpsService(
        IInboxConflictRepository inboxConflictRepository,
        IClarificationRepository clarificationRepository,
        IPeriodRepository periodRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IDomainReviewEventRepository domainReviewEventRepository)
    {
        _inboxConflictRepository = inboxConflictRepository;
        _clarificationRepository = clarificationRepository;
        _periodRepository = periodRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
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
        }

        var history = await _domainReviewEventRepository.GetByObjectAsync(normalizedType, objectId, Math.Max(1, limit), ct);
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
}
