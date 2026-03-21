using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebSearchService : IWebSearchService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;

    public WebSearchService(
        IMessageRepository messageRepository,
        IInboxConflictRepository inboxConflictRepository,
        IClarificationRepository clarificationRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository)
    {
        _messageRepository = messageRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _clarificationRepository = clarificationRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
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
                Title = "Unknown view",
                Description = "Available views: blocking, current-period, conflicts",
                Items = []
            }
        };
    }

    public async Task<DossierReadModel> GetDossierAsync(WebReadRequest request, int limit = 50, CancellationToken ct = default)
    {
        var questions = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => (x.ChatId == null || x.ChatId == request.ChatId)
                        && x.Status.Equals("resolved", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UpdatedAt)
            .Take(limit)
            .ToList();

        var confirmed = new List<DossierItemReadModel>();
        foreach (var question in questions)
        {
            var latestAnswer = (await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct))
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
            if (latestAnswer == null)
            {
                continue;
            }

            confirmed.Add(new DossierItemReadModel
            {
                ObjectType = "clarification_answer",
                ObjectId = latestAnswer.Id.ToString(),
                Title = question.QuestionText,
                Summary = latestAnswer.AnswerValue,
                ConfidenceLabel = latestAnswer.AnswerConfidence.ToString("0.00"),
                Link = $"/history-object?objectType=clarification_question&objectId={question.Id}",
                UpdatedAt = latestAnswer.CreatedAt
            });
        }

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

        return new DossierReadModel
        {
            Confirmed = confirmed,
            Hypotheses = hypotheses,
            Conflicts = conflicts
        };
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
                Link = "/strategy"
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
                Link = "/strategy"
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
                Link = "/drafts-reviews"
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
                Link = $"/history-object?objectType=draft_outcome&objectId={x.Id}"
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
            Title = "Saved View: Blocking",
            Description = "Open blocking items that require immediate attention.",
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
            Title = "Saved View: Current Period",
            Description = "Current period inspection shortcut.",
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
            Title = "Saved View: Conflicts",
            Description = "Open/deferred conflicts requiring interpretation review.",
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
