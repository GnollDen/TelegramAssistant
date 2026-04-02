// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Clarification;

public class ClarificationAnswerApplier : IClarificationAnswerApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IClarificationRepository _clarificationRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<ClarificationAnswerApplier> _logger;

    public ClarificationAnswerApplier(
        IClarificationRepository clarificationRepository,
        IInboxConflictRepository inboxConflictRepository,
        IStateProfileRepository stateProfileRepository,
        IPeriodRepository periodRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<ClarificationAnswerApplier> logger)
    {
        _clarificationRepository = clarificationRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _stateProfileRepository = stateProfileRepository;
        _periodRepository = periodRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task<(ClarificationQuestion Question, ClarificationAnswer Answer, List<ConflictRecord> Conflicts)> ApplyAsync(
        ClarificationApplyRequest request,
        CancellationToken ct = default)
    {
        var question = await _clarificationRepository.GetQuestionByIdAsync(request.QuestionId, ct)
            ?? throw new InvalidOperationException($"Clarification question '{request.QuestionId}' not found.");

        var existingAnswers = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
        var normalizedSourceClass = NormalizeSourceClass(request.SourceClass, request.SourceType);
        var answer = await _clarificationRepository.ApplyAnswerAsync(
            question.Id,
            new ClarificationAnswer
            {
                AnswerType = string.IsNullOrWhiteSpace(request.AnswerType) ? "text" : request.AnswerType.Trim(),
                AnswerValue = request.AnswerValue.Trim(),
                AnswerConfidence = Math.Clamp(request.AnswerConfidence, 0, 1),
                SourceClass = normalizedSourceClass,
                AffectedObjectsJson = NormalizeJsonArray(request.AffectedObjectsJson),
                SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "user" : request.SourceType.Trim(),
                SourceId = request.SourceId,
                SourceMessageId = request.SourceMessageId,
                SourceSessionId = request.SourceSessionId,
                CreatedAt = DateTime.UtcNow
            },
            request.MarkResolved,
            request.Actor,
            request.Reason,
            ct);

        var conflicts = await EnsureConflictsAsync(question, answer, existingAnswers, request, ct);

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "clarification_answer",
            ObjectId = answer.Id.ToString(),
            Action = "applied",
            NewValueRef = JsonSerializer.Serialize(new
            {
                answer.SourceClass,
                answer.AnswerType,
                answer.AnswerConfidence,
                question.Status
            }, JsonOptions),
            Reason = request.Reason,
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        var refreshedQuestion = await _clarificationRepository.GetQuestionByIdAsync(question.Id, ct) ?? question;
        _logger.LogInformation(
            "Clarification answer persisted: question_id={QuestionId}, answer_id={AnswerId}, source_class={SourceClass}, conflicts={ConflictCount}",
            refreshedQuestion.Id,
            answer.Id,
            answer.SourceClass,
            conflicts.Count);

        return (refreshedQuestion, answer, conflicts);
    }

    private async Task<List<ConflictRecord>> EnsureConflictsAsync(
        ClarificationQuestion question,
        ClarificationAnswer newAnswer,
        List<ClarificationAnswer> existingAnswers,
        ClarificationApplyRequest request,
        CancellationToken ct)
    {
        var conflicts = new List<ConflictRecord>();
        var normalizedAnswer = NormalizeAnswerValue(newAnswer.AnswerValue);

        foreach (var existing in existingAnswers)
        {
            if (!IsContradiction(NormalizeAnswerValue(existing.AnswerValue), normalizedAnswer))
            {
                continue;
            }

            var conflict = await CreateOrUpdateConflictAsync(
                caseId: question.CaseId,
                chatId: question.ChatId,
                objectAType: "clarification_question",
                objectAId: question.Id.ToString(),
                objectBType: "clarification_answer",
                objectBId: existing.Id.ToString(),
                severity: string.Equals(question.Priority, "blocking", StringComparison.OrdinalIgnoreCase) ? "high" : "medium",
                summary: "Contradictory clarification answer against previous answer.",
                actor: request.Actor,
                reason: request.Reason,
                ct: ct);
            conflicts.Add(conflict);
        }

        var stateSnapshots = await _stateProfileRepository.GetStateSnapshotsByCaseAsync(question.CaseId, 1, ct);
        var latestState = stateSnapshots.FirstOrDefault();
        if (latestState != null && latestState.Confidence >= 0.75f && ContradictsCurrentState(question, normalizedAnswer, latestState))
        {
            var conflict = await CreateOrUpdateConflictAsync(
                caseId: question.CaseId,
                chatId: question.ChatId,
                objectAType: "clarification_question",
                objectAId: question.Id.ToString(),
                objectBType: "state_snapshot",
                objectBId: latestState.Id.ToString(),
                severity: "high",
                summary: "Clarification answer contradicts strong current state interpretation.",
                actor: request.Actor,
                reason: request.Reason,
                ct: ct);
            conflicts.Add(conflict);
        }

        if (question.RelatedHypothesisId.HasValue)
        {
            var hypothesis = await _periodRepository.GetHypothesisByIdAsync(question.RelatedHypothesisId.Value, ct);
            if (hypothesis != null && hypothesis.Confidence >= 0.7f &&
                (string.Equals(hypothesis.Status, "supported", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(hypothesis.Status, "accepted", StringComparison.OrdinalIgnoreCase)) &&
                IsNegative(normalizedAnswer))
            {
                var conflict = await CreateOrUpdateConflictAsync(
                    caseId: question.CaseId,
                    chatId: question.ChatId,
                    objectAType: "clarification_question",
                    objectAId: question.Id.ToString(),
                    objectBType: "hypothesis",
                    objectBId: hypothesis.Id.ToString(),
                    severity: "high",
                    summary: "Clarification answer contradicts strong hypothesis assumption.",
                    actor: request.Actor,
                    reason: request.Reason,
                    ct: ct);
                conflicts.Add(conflict);
            }
        }

        return conflicts
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();
    }

    private async Task<ConflictRecord> CreateOrUpdateConflictAsync(
        long caseId,
        long? chatId,
        string objectAType,
        string objectAId,
        string objectBType,
        string objectBId,
        string severity,
        string summary,
        string actor,
        string? reason,
        CancellationToken ct)
    {
        var existing = (await _inboxConflictRepository.GetConflictRecordsAsync(caseId, null, ct))
            .FirstOrDefault(x =>
                string.Equals(x.ConflictType, "clarification_contradiction", StringComparison.OrdinalIgnoreCase) &&
                SamePair(x.ObjectAType, x.ObjectAId, x.ObjectBType, x.ObjectBId, objectAType, objectAId, objectBType, objectBId));

        if (existing != null)
        {
            if (!string.Equals(existing.Status, "open", StringComparison.OrdinalIgnoreCase))
            {
                await _inboxConflictRepository.UpdateConflictStatusAsync(existing.Id, "open", actor, reason, ct);
            }

            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "conflict_record",
                ObjectId = existing.Id.ToString(),
                Action = "observed_again",
                NewValueRef = JsonSerializer.Serialize(new { severity, summary }, JsonOptions),
                Reason = reason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            }, ct);

            return existing;
        }

        var created = await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
        {
            ConflictType = "clarification_contradiction",
            ObjectAType = objectAType,
            ObjectAId = objectAId,
            ObjectBType = objectBType,
            ObjectBId = objectBId,
            Summary = summary,
            Severity = severity,
            Status = "open",
            CaseId = caseId,
            ChatId = chatId,
            LastActor = actor,
            LastReason = reason,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "conflict_record",
            ObjectId = created.Id.ToString(),
            Action = "created",
            NewValueRef = JsonSerializer.Serialize(new
            {
                created.ConflictType,
                created.ObjectAType,
                created.ObjectAId,
                created.ObjectBType,
                created.ObjectBId,
                created.Severity
            }, JsonOptions),
            Reason = reason,
            Actor = actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        return created;
    }

    private static bool SamePair(
        string currentAType,
        string currentAId,
        string currentBType,
        string currentBId,
        string newAType,
        string newAId,
        string newBType,
        string newBId)
    {
        var direct = string.Equals(currentAType, newAType, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(currentAId, newAId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(currentBType, newBType, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(currentBId, newBId, StringComparison.OrdinalIgnoreCase);
        if (direct)
        {
            return true;
        }

        return string.Equals(currentAType, newBType, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(currentAId, newBId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(currentBType, newAType, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(currentBId, newAId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContradictsCurrentState(ClarificationQuestion question, string normalizedAnswer, StateSnapshot state)
    {
        var questionType = question.QuestionType.Trim().ToLowerInvariant();
        if (!questionType.Contains("state") && !questionType.Contains("status") && !questionType.Contains("relationship"))
        {
            return false;
        }

        var positiveState = IsPositiveStatus(state.RelationshipStatus) || IsPositiveStatus(state.DynamicLabel);
        return positiveState && IsNegative(normalizedAnswer);
    }

    private static bool IsPositiveStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("warm") || normalized.Contains("stable") || normalized.Contains("positive") || normalized.Contains("close");
    }

    private static bool IsContradiction(string oldValue, string newValue)
    {
        if (string.IsNullOrWhiteSpace(oldValue) || string.IsNullOrWhiteSpace(newValue))
        {
            return false;
        }

        if (string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if ((IsPositive(oldValue) && IsNegative(newValue)) || (IsNegative(oldValue) && IsPositive(newValue)))
        {
            return true;
        }

        return true;
    }

    private static bool IsPositive(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "yes" or "true" or "1" or "supported" or "confirmed";
    }

    private static bool IsNegative(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "no" or "false" or "0" or "unsupported" or "rejected";
    }

    private static string NormalizeAnswerValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeSourceClass(string sourceClass, string sourceType)
    {
        if (!string.IsNullOrWhiteSpace(sourceClass))
        {
            var normalized = sourceClass.Trim().ToLowerInvariant();
            if (normalized is "user_confirmed" or "user_reported" or "operator_override" or "system_inferred")
            {
                return normalized;
            }
        }

        var sourceTypeNormalized = sourceType.Trim().ToLowerInvariant();
        return sourceTypeNormalized switch
        {
            "user" => "user_confirmed",
            "operator" => "operator_override",
            "system" => "system_inferred",
            _ => "user_reported"
        };
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
