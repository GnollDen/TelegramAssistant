using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ClarificationRepository : IClarificationRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly IStage6UserContextRepository _stage6UserContextRepository;

    public ClarificationRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IStage6CaseRepository stage6CaseRepository,
        IStage6UserContextRepository stage6UserContextRepository)
    {
        _dbFactory = dbFactory;
        _stage6CaseRepository = stage6CaseRepository;
        _stage6UserContextRepository = stage6UserContextRepository;
    }

    public async Task<ClarificationQuestion> CreateQuestionAsync(ClarificationQuestion question, CancellationToken ct = default)
    {
        var row = new DbClarificationQuestion
        {
            Id = question.Id == Guid.Empty ? Guid.NewGuid() : question.Id,
            CaseId = question.CaseId,
            ChatId = question.ChatId,
            QuestionText = question.QuestionText,
            QuestionType = question.QuestionType,
            Priority = question.Priority,
            Status = question.Status,
            PeriodId = question.PeriodId,
            RelatedHypothesisId = question.RelatedHypothesisId,
            AffectedOutputsJson = question.AffectedOutputsJson,
            WhyItMatters = question.WhyItMatters,
            ExpectedGain = question.ExpectedGain,
            AnswerOptionsJson = question.AnswerOptionsJson,
            SourceType = question.SourceType,
            SourceId = question.SourceId,
            SourceMessageId = question.SourceMessageId,
            SourceSessionId = question.SourceSessionId,
            ResolvedAt = question.ResolvedAt,
            CreatedAt = question.CreatedAt == default ? DateTime.UtcNow : question.CreatedAt,
            UpdatedAt = question.UpdatedAt == default ? DateTime.UtcNow : question.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            db.ClarificationQuestions.Add(row);
            await db.SaveChangesAsync(ct);
            await UpsertClarificationCaseAsync(row, actor: "clarification_repository", reason: "question_created", ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<ClarificationQuestion?> GetQuestionByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.ClarificationQuestions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<ClarificationQuestion>> GetQuestionsAsync(long caseId, Guid? periodId = null, string? status = null, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var query = db.ClarificationQuestions.AsNoTracking().Where(x => x.CaseId == caseId);
            if (periodId.HasValue)
            {
                query = query.Where(x => x.PeriodId == periodId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            var rows = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<ClarificationAnswer> CreateAnswerAsync(ClarificationAnswer answer, CancellationToken ct = default)
    {
        var row = new DbClarificationAnswer
        {
            Id = answer.Id == Guid.Empty ? Guid.NewGuid() : answer.Id,
            QuestionId = answer.QuestionId,
            AnswerType = answer.AnswerType,
            AnswerValue = answer.AnswerValue,
            AnswerConfidence = answer.AnswerConfidence,
            SourceClass = answer.SourceClass,
            AffectedObjectsJson = answer.AffectedObjectsJson,
            SourceType = answer.SourceType,
            SourceId = answer.SourceId,
            SourceMessageId = answer.SourceMessageId,
            SourceSessionId = answer.SourceSessionId,
            CreatedAt = answer.CreatedAt == default ? DateTime.UtcNow : answer.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.ClarificationAnswers.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<ClarificationAnswer?> GetAnswerByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.ClarificationAnswers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<ClarificationAnswer>> GetAnswersByQuestionIdAsync(Guid questionId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.ClarificationAnswers
                .AsNoTracking()
                .Where(x => x.QuestionId == questionId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<bool> UpdateQuestionWorkflowAsync(
        Guid id,
        string status,
        string priority,
        string actor,
        string? reason = null,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            var row = await db.ClarificationQuestions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null)
            {
                return false;
            }

            var oldRef = JsonSerializer.Serialize(new { row.Status, row.Priority, row.ResolvedAt });
            row.Status = status;
            row.Priority = priority;
            row.ResolvedAt = status.Equals("resolved", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null;
            row.UpdatedAt = DateTime.UtcNow;

            db.DomainReviewEvents.Add(new DbDomainReviewEvent
            {
                Id = Guid.NewGuid(),
                ObjectType = "clarification_question",
                ObjectId = row.Id.ToString(),
                Action = "update_workflow",
                OldValueRef = oldRef,
                NewValueRef = JsonSerializer.Serialize(new { row.Status, row.Priority, row.ResolvedAt }),
                Reason = reason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            await UpsertClarificationCaseAsync(row, actor, reason ?? "question_workflow_updated", ct);
            return true;
        }, ct);
    }

    public async Task<ClarificationAnswer> ApplyAnswerAsync(
        Guid questionId,
        ClarificationAnswer answer,
        bool markResolved,
        string actor,
        string? reason = null,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            var question = await db.ClarificationQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct)
                ?? throw new InvalidOperationException($"Clarification question '{questionId}' not found.");

            var answerRow = new DbClarificationAnswer
            {
                Id = answer.Id == Guid.Empty ? Guid.NewGuid() : answer.Id,
                QuestionId = questionId,
                AnswerType = answer.AnswerType,
                AnswerValue = answer.AnswerValue,
                AnswerConfidence = answer.AnswerConfidence,
                SourceClass = answer.SourceClass,
                AffectedObjectsJson = answer.AffectedObjectsJson,
                SourceType = answer.SourceType,
                SourceId = answer.SourceId,
                SourceMessageId = answer.SourceMessageId,
                SourceSessionId = answer.SourceSessionId,
                CreatedAt = answer.CreatedAt == default ? DateTime.UtcNow : answer.CreatedAt
            };
            db.ClarificationAnswers.Add(answerRow);

            var oldRef = JsonSerializer.Serialize(new { question.Status, question.ResolvedAt });
            question.Status = markResolved ? "resolved" : "answered";
            question.ResolvedAt = markResolved ? DateTime.UtcNow : question.ResolvedAt;
            question.UpdatedAt = DateTime.UtcNow;

            db.DomainReviewEvents.Add(new DbDomainReviewEvent
            {
                Id = Guid.NewGuid(),
                ObjectType = "clarification_question",
                ObjectId = question.Id.ToString(),
                Action = "apply_answer",
                OldValueRef = oldRef,
                NewValueRef = JsonSerializer.Serialize(new { question.Status, question.ResolvedAt }),
                Reason = reason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            var stage6Case = await UpsertClarificationCaseAsync(question, actor, reason ?? "answer_applied", ct);
            await _stage6UserContextRepository.CreateAsync(new Stage6UserContextEntry
            {
                Stage6CaseId = stage6Case.Id,
                ScopeCaseId = question.CaseId,
                ChatId = question.ChatId ?? 0,
                SourceKind = UserContextSourceKinds.ClarificationAnswer,
                ClarificationQuestionId = question.Id,
                ContentText = answerRow.AnswerValue,
                AppliesToRefsJson = NormalizeJsonArray(answerRow.AffectedObjectsJson),
                EnteredVia = ResolveEnteredVia(answerRow.SourceType),
                UserReportedCertainty = Math.Clamp(answerRow.AnswerConfidence, 0f, 1f),
                SourceType = answerRow.SourceType,
                SourceId = answerRow.SourceId,
                SourceMessageId = answerRow.SourceMessageId,
                SourceSessionId = answerRow.SourceSessionId,
                ConflictsWithRefsJson = "[]",
                CreatedAt = answerRow.CreatedAt
            }, ct);
            return ToDomain(answerRow);
        }, ct);
    }

    private async Task<Stage6CaseRecord> UpsertClarificationCaseAsync(
        DbClarificationQuestion question,
        string actor,
        string reason,
        CancellationToken ct)
    {
        var caseRecord = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = question.CaseId,
            ChatId = question.ChatId,
            ScopeType = "chat",
            CaseType = ResolveClarificationCaseType(question.QuestionType),
            CaseSubtype = question.QuestionType,
            Status = ResolveCaseStatusFromQuestionStatus(question.Status),
            Priority = question.Priority,
            Confidence = Math.Clamp(question.ExpectedGain, 0f, 1f),
            ReasonSummary = string.IsNullOrWhiteSpace(question.WhyItMatters) ? question.QuestionText : question.WhyItMatters,
            ClarificationKind = ResolveClarificationKind(question.QuestionType),
            QuestionText = question.QuestionText,
            ResponseMode = "free_text",
            ResponseChannelHint = "bot_or_web",
            EvidenceRefsJson = "[]",
            SubjectRefsJson = "[]",
            TargetArtifactTypesJson = NormalizeJsonArray(question.AffectedOutputsJson),
            ReopenTriggerRulesJson = """["new_evidence","operator_correction","artifact_stale_after_context_change"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                source_type = question.SourceType,
                source_id = question.SourceId,
                source_message_id = question.SourceMessageId,
                source_session_id = question.SourceSessionId,
                actor,
                reason
            }),
            SourceObjectType = "clarification_question",
            SourceObjectId = question.Id.ToString(),
            CreatedAt = question.CreatedAt,
            UpdatedAt = question.UpdatedAt,
            ResolvedAt = question.ResolvedAt
        }, ct);

        await _stage6CaseRepository.UpsertLinkAsync(new Stage6CaseLink
        {
            Stage6CaseId = caseRecord.Id,
            LinkedObjectType = "clarification_question",
            LinkedObjectId = question.Id.ToString(),
            LinkRole = Stage6CaseLinkRoles.Source,
            MetadataJson = "{}",
            CreatedAt = question.CreatedAt
        }, ct);

        foreach (var artifactType in ParseJsonStringArray(question.AffectedOutputsJson))
        {
            await _stage6CaseRepository.UpsertLinkAsync(new Stage6CaseLink
            {
                Stage6CaseId = caseRecord.Id,
                LinkedObjectType = "stage6_artifact_type",
                LinkedObjectId = artifactType,
                LinkRole = Stage6CaseLinkRoles.ArtifactTarget,
                MetadataJson = "{}",
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        return caseRecord;
    }

    private static string ResolveClarificationCaseType(string questionType)
    {
        var normalized = (questionType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "missing_data" => Stage6CaseTypes.ClarificationMissingData,
            "ambiguity" => Stage6CaseTypes.ClarificationAmbiguity,
            "evidence_interpretation_conflict" => Stage6CaseTypes.ClarificationEvidenceInterpretationConflict,
            "next_step_blocked" => Stage6CaseTypes.ClarificationNextStepBlocked,
            _ => Stage6CaseTypes.NeedsInput
        };
    }

    private static string? ResolveClarificationKind(string questionType)
    {
        var normalized = (questionType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "missing_data" or "ambiguity" or "evidence_interpretation_conflict" or "next_step_blocked"
            ? normalized
            : null;
    }

    private static string ResolveCaseStatusFromQuestionStatus(string questionStatus)
    {
        var normalized = (questionStatus ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "open" or "in_progress" => Stage6CaseStatuses.NeedsUserInput,
            "answered" => Stage6CaseStatuses.Ready,
            "resolved" => Stage6CaseStatuses.Resolved,
            "rejected" => Stage6CaseStatuses.Rejected,
            "stale" => Stage6CaseStatuses.Stale,
            _ => Stage6CaseStatuses.New
        };
    }

    private static string ResolveEnteredVia(string sourceType)
    {
        return (sourceType ?? string.Empty).Contains("telegram", StringComparison.OrdinalIgnoreCase)
            ? "bot"
            : "web";
    }

    private static IReadOnlyList<string> ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed == null)
            {
                return [];
            }

            return parsed
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeJsonArray(string? json)
    {
        var values = ParseJsonStringArray(json);
        return JsonSerializer.Serialize(values);
    }

    private static ClarificationQuestion ToDomain(DbClarificationQuestion row) => new()
    {
        Id = row.Id,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        QuestionText = row.QuestionText,
        QuestionType = row.QuestionType,
        Priority = row.Priority,
        Status = row.Status,
        PeriodId = row.PeriodId,
        RelatedHypothesisId = row.RelatedHypothesisId,
        AffectedOutputsJson = row.AffectedOutputsJson,
        WhyItMatters = row.WhyItMatters,
        ExpectedGain = row.ExpectedGain,
        AnswerOptionsJson = row.AnswerOptionsJson,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        ResolvedAt = row.ResolvedAt,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static ClarificationAnswer ToDomain(DbClarificationAnswer row) => new()
    {
        Id = row.Id,
        QuestionId = row.QuestionId,
        AnswerType = row.AnswerType,
        AnswerValue = row.AnswerValue,
        AnswerConfidence = row.AnswerConfidence,
        SourceClass = row.SourceClass,
        AffectedObjectsJson = row.AffectedObjectsJson,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        CreatedAt = row.CreatedAt
    };

    private async Task<TResult> WithDbContextAsync<TResult>(Func<TgAssistantDbContext, Task<TResult>> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await action(ambientDb);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await action(db);
    }

    private async Task WithDbContextAsync(Func<TgAssistantDbContext, Task> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            await action(ambientDb);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await action(db);
    }
}
