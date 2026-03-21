using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ClarificationRepository : IClarificationRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ClarificationRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
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
            db.ClarificationQuestions.Add(row);
            await db.SaveChangesAsync(ct);
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
            return ToDomain(answerRow);
        }, ct);
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
