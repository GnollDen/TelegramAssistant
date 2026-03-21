using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebReviewService : IWebReviewService
{
    private readonly IMessageRepository _messageRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;

    public WebReviewService(
        IMessageRepository messageRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IInboxConflictRepository inboxConflictRepository,
        IDomainReviewEventRepository domainReviewEventRepository)
    {
        _messageRepository = messageRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
    }

    public async Task<WebReviewBoardModel> GetBoardAsync(WebReadRequest request, CancellationToken ct = default)
    {
        var cards = new List<WebReviewCardModel>();

        var questions = (await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(4)
            .ToList();
        cards.AddRange(questions.Select(q => new WebReviewCardModel
        {
            ObjectType = "clarification_question",
            ObjectId = q.Id.ToString(),
            Summary = q.QuestionText,
            Provenance = BuildProvenance(q.SourceType, q.SourceId, q.SourceMessageId, q.SourceSessionId),
            SuggestedInterpretation = $"workflow: {q.Status}/{q.Priority}",
            LinkedContext = q.PeriodId.HasValue ? $"period:{q.PeriodId}" : "period:unbound",
            Confidence = q.ExpectedGain,
            CanEdit = false
        }));

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.StartAt)
            .Take(3)
            .ToList();
        cards.AddRange(periods.Select(p => new WebReviewCardModel
        {
            ObjectType = "period",
            ObjectId = p.Id.ToString(),
            Summary = $"{p.Label}: {p.Summary}",
            Provenance = BuildProvenance(p.SourceType, p.SourceId, p.SourceMessageId, p.SourceSessionId),
            SuggestedInterpretation = $"priority={p.ReviewPriority}, open={p.IsOpen}",
            LinkedContext = $"{p.StartAt:yyyy-MM-dd}..{(p.EndAt?.ToString("yyyy-MM-dd") ?? "now")}",
            Confidence = p.InterpretationConfidence,
            CanEdit = true
        }));

        var transitions = new List<PeriodTransition>();
        foreach (var period in periods)
        {
            transitions.AddRange(await _periodRepository.GetTransitionsByPeriodAsync(period.Id, ct));
        }

        foreach (var transition in transitions.GroupBy(x => x.Id).Select(x => x.First()).Take(3))
        {
            cards.Add(new WebReviewCardModel
            {
                ObjectType = "period_transition",
                ObjectId = transition.Id.ToString(),
                Summary = $"{transition.TransitionType}: {transition.Summary}",
                Provenance = BuildProvenance(transition.SourceType, transition.SourceId, transition.SourceMessageId, transition.SourceSessionId),
                SuggestedInterpretation = transition.IsResolved ? "resolved transition" : "unresolved transition",
                LinkedContext = $"{transition.FromPeriodId} -> {transition.ToPeriodId}",
                Confidence = transition.Confidence,
                CanEdit = false
            });
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

            cards.Add(new WebReviewCardModel
            {
                ObjectType = "profile_snapshot",
                ObjectId = snapshot.Id.ToString(),
                Summary = $"{subjectType}: {snapshot.Summary}",
                Provenance = BuildProvenance("profile_engine", "snapshot", snapshot.SourceMessageId, snapshot.SourceSessionId),
                SuggestedInterpretation = $"stability={snapshot.Stability:0.00}",
                LinkedContext = snapshot.PeriodId.HasValue ? $"period:{snapshot.PeriodId}" : "period:global",
                Confidence = snapshot.Confidence,
                CanEdit = false
            });

            var trait = (await _stateProfileRepository.GetProfileTraitsBySnapshotIdAsync(snapshot.Id, ct))
                .OrderByDescending(x => x.Confidence)
                .FirstOrDefault();
            if (trait != null)
            {
                cards.Add(new WebReviewCardModel
                {
                    ObjectType = "profile_trait",
                    ObjectId = trait.Id.ToString(),
                    Summary = $"{trait.TraitKey}: {trait.ValueLabel}",
                    Provenance = BuildProvenance("profile_engine", "trait", trait.SourceMessageId, trait.SourceSessionId),
                    SuggestedInterpretation = trait.IsSensitive ? "sensitive trait" : "standard trait",
                    LinkedContext = $"snapshot:{snapshot.Id}",
                    Confidence = trait.Confidence,
                    CanEdit = false
                });
            }
        }

        var strategyRecord = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);
        if (strategyRecord != null)
        {
            cards.Add(new WebReviewCardModel
            {
                ObjectType = "strategy_record",
                ObjectId = strategyRecord.Id.ToString(),
                Summary = strategyRecord.RecommendedGoal,
                Provenance = BuildProvenance("strategy_engine", "record", strategyRecord.SourceMessageId, strategyRecord.SourceSessionId),
                SuggestedInterpretation = $"confidence={strategyRecord.StrategyConfidence:0.00}",
                LinkedContext = strategyRecord.PeriodId.HasValue ? $"period:{strategyRecord.PeriodId}" : "period:current",
                Confidence = strategyRecord.StrategyConfidence,
                CanEdit = false
            });

            var options = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(strategyRecord.Id, ct);
            foreach (var option in options.Take(2))
            {
                cards.Add(new WebReviewCardModel
                {
                    ObjectType = "strategy_option",
                    ObjectId = option.Id.ToString(),
                    Summary = $"{option.ActionType}: {option.Summary}",
                    Provenance = "strategy_option:derived",
                    SuggestedInterpretation = option.IsPrimary ? "primary option" : "alternative option",
                    LinkedContext = $"strategy:{strategyRecord.Id}",
                    Confidence = null,
                    CanEdit = false
                });
            }

            var draft = (await _strategyDraftRepository.GetDraftRecordsByStrategyRecordIdAsync(strategyRecord.Id, ct))
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
            if (draft != null)
            {
                cards.Add(new WebReviewCardModel
                {
                    ObjectType = "draft_record",
                    ObjectId = draft.Id.ToString(),
                    Summary = draft.MainDraft,
                    Provenance = BuildProvenance("draft_engine", "draft_record", draft.SourceMessageId, draft.SourceSessionId),
                    SuggestedInterpretation = $"confidence={draft.Confidence:0.00}",
                    LinkedContext = $"strategy:{strategyRecord.Id}",
                    Confidence = draft.Confidence,
                    CanEdit = false
                });
            }
        }

        var conflicts = (await _inboxConflictRepository.GetConflictRecordsAsync(request.CaseId, null, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(3)
            .ToList();
        cards.AddRange(conflicts.Select(c => new WebReviewCardModel
        {
            ObjectType = "conflict_record",
            ObjectId = c.Id.ToString(),
            Summary = c.Summary,
            Provenance = $"conflict_type:{c.ConflictType}",
            SuggestedInterpretation = $"status={c.Status}, severity={c.Severity}",
            LinkedContext = c.PeriodId.HasValue ? $"period:{c.PeriodId}" : "period:cross",
            Confidence = null,
            CanEdit = false
        }));

        return new WebReviewBoardModel
        {
            Cards = cards
                .OrderByDescending(x => x.Confidence ?? 0f)
                .ThenBy(x => x.ObjectType)
                .ToList()
        };
    }

    public async Task<WebReviewActionResult> ApplyActionAsync(WebReviewActionRequest request, CancellationToken ct = default)
    {
        var action = NormalizeAction(request.Action);
        if (string.IsNullOrWhiteSpace(action))
        {
            return new WebReviewActionResult
            {
                Success = false,
                ObjectType = request.ObjectType,
                ObjectId = request.ObjectId,
                Action = request.Action,
                Message = "Unsupported action. Use confirm, reject, or defer."
            };
        }

        var objectType = request.ObjectType.Trim().ToLowerInvariant();
        var objectId = request.ObjectId.Trim();
        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "web" : request.Actor.Trim();
        var reason = request.Reason?.Trim();
        var statusUpdated = false;

        switch (objectType)
        {
            case "clarification_question":
            {
                if (!Guid.TryParse(objectId, out var questionId))
                {
                    return Fail(request, "Invalid clarification question id.");
                }

                var question = await _clarificationRepository.GetQuestionByIdAsync(questionId, ct);
                if (question == null)
                {
                    return Fail(request, "Clarification question not found.");
                }

                var nextStatus = action switch
                {
                    "confirm" => "resolved",
                    "reject" => "deferred",
                    _ => "deferred"
                };

                var nextPriority = action switch
                {
                    "reject" => "optional",
                    _ => question.Priority
                };

                statusUpdated = await _clarificationRepository.UpdateQuestionWorkflowAsync(
                    questionId,
                    nextStatus,
                    nextPriority,
                    actor,
                    reason ?? $"web review {action}",
                    ct);
                break;
            }
            case "period":
            {
                if (!Guid.TryParse(objectId, out var periodId))
                {
                    return Fail(request, "Invalid period id.");
                }

                var period = await _periodRepository.GetPeriodByIdAsync(periodId, ct);
                if (period == null)
                {
                    return Fail(request, "Period not found.");
                }

                var nextPriority = action switch
                {
                    "confirm" => (short)Math.Max(0, period.ReviewPriority - 1),
                    "reject" => (short)Math.Min(short.MaxValue, period.ReviewPriority + 2),
                    _ => (short)Math.Min(short.MaxValue, period.ReviewPriority + 1)
                };

                statusUpdated = await _periodRepository.UpdatePeriodLifecycleAsync(
                    periodId,
                    period.Label,
                    period.Summary,
                    period.IsOpen,
                    period.EndAt,
                    nextPriority,
                    actor,
                    reason ?? $"web review {action}",
                    ct);
                break;
            }
            case "conflict_record":
            {
                if (!Guid.TryParse(objectId, out var conflictId))
                {
                    return Fail(request, "Invalid conflict id.");
                }

                var nextStatus = action switch
                {
                    "confirm" => "resolved",
                    "reject" => "open",
                    _ => "deferred"
                };

                statusUpdated = await _inboxConflictRepository.UpdateConflictStatusAsync(
                    conflictId,
                    nextStatus,
                    actor,
                    reason ?? $"web review {action}",
                    ct);
                break;
            }
            default:
                statusUpdated = true;
                break;
        }

        var reviewEvent = new DomainReviewEvent
        {
            ObjectType = objectType,
            ObjectId = objectId,
            Action = $"web_review_{action}",
            OldValueRef = null,
            NewValueRef = JsonSerializer.Serialize(new { status_updated = statusUpdated }),
            Reason = reason,
            Actor = actor
        };
        _ = await _domainReviewEventRepository.AddAsync(reviewEvent, ct);

        return new WebReviewActionResult
        {
            Success = statusUpdated,
            ObjectType = objectType,
            ObjectId = objectId,
            Action = action,
            Message = statusUpdated
                ? $"Applied '{action}' for {objectType}:{objectId}."
                : $"No object update was applied for {objectType}:{objectId}, audit event created."
        };
    }

    public async Task<WebReviewActionResult> EditPeriodAsync(WebPeriodEditRequest request, CancellationToken ct = default)
    {
        var period = await _periodRepository.GetPeriodByIdAsync(request.PeriodId, ct);
        if (period == null)
        {
            return new WebReviewActionResult
            {
                Success = false,
                ObjectType = "period",
                ObjectId = request.PeriodId.ToString(),
                Action = "edit",
                Message = "Period not found."
            };
        }

        var oldRef = JsonSerializer.Serialize(new { period.Label, period.Summary, period.ReviewPriority, period.IsOpen, period.EndAt });
        var nextLabel = string.IsNullOrWhiteSpace(request.Label) ? period.Label : request.Label.Trim();
        var nextSummary = string.IsNullOrWhiteSpace(request.Summary) ? period.Summary : request.Summary.Trim();
        var nextReviewPriority = request.ReviewPriority ?? period.ReviewPriority;
        var nextIsOpen = request.IsOpen ?? period.IsOpen;
        var nextEndAt = request.EndAt ?? period.EndAt;
        if (nextIsOpen)
        {
            nextEndAt = null;
        }

        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "web" : request.Actor.Trim();
        var updated = await _periodRepository.UpdatePeriodLifecycleAsync(
            period.Id,
            nextLabel,
            nextSummary,
            nextIsOpen,
            nextEndAt,
            nextReviewPriority,
            actor,
            request.Reason ?? "web edit period",
            ct);

        _ = await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "period",
            ObjectId = period.Id.ToString(),
            Action = "web_review_edit",
            OldValueRef = oldRef,
            NewValueRef = JsonSerializer.Serialize(new
            {
                Label = nextLabel,
                Summary = nextSummary,
                ReviewPriority = nextReviewPriority,
                IsOpen = nextIsOpen,
                EndAt = nextEndAt
            }),
            Reason = request.Reason,
            Actor = actor
        }, ct);

        return new WebReviewActionResult
        {
            Success = updated,
            ObjectType = "period",
            ObjectId = period.Id.ToString(),
            Action = "edit",
            Message = updated
                ? $"Edited period {period.Id}."
                : $"Period {period.Id} was not updated."
        };
    }

    private async Task<(long SelfSenderId, long OtherSenderId)> ResolveSelfOtherSendersAsync(long chatId, CancellationToken ct)
    {
        var messages = await _messageRepository.GetByChatAndPeriodAsync(chatId, DateTime.UtcNow.AddDays(-120), DateTime.UtcNow, 2000, ct);
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

    private static string BuildProvenance(string sourceType, string sourceId, long? sourceMessageId, Guid? sourceSessionId)
    {
        return $"{sourceType}:{sourceId} | msg={(sourceMessageId?.ToString() ?? "-")} | session={(sourceSessionId?.ToString() ?? "-")}";
    }

    private static string NormalizeAction(string action)
    {
        var value = action.Trim().ToLowerInvariant();
        return value is "confirm" or "reject" or "defer" ? value : string.Empty;
    }

    private static WebReviewActionResult Fail(WebReviewActionRequest request, string message)
    {
        return new WebReviewActionResult
        {
            Success = false,
            ObjectType = request.ObjectType,
            ObjectId = request.ObjectId,
            Action = request.Action,
            Message = message
        };
    }
}
