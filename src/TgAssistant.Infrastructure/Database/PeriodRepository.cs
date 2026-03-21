using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class PeriodRepository : IPeriodRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public PeriodRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Period> CreatePeriodAsync(Period period, CancellationToken ct = default)
    {
        var row = new DbPeriod
        {
            Id = period.Id == Guid.Empty ? Guid.NewGuid() : period.Id,
            CaseId = period.CaseId,
            ChatId = period.ChatId,
            Label = period.Label,
            CustomLabel = period.CustomLabel,
            StartAt = period.StartAt,
            EndAt = period.EndAt,
            IsOpen = period.IsOpen,
            Summary = period.Summary,
            KeySignalsJson = period.KeySignalsJson,
            WhatHelped = period.WhatHelped,
            WhatHurt = period.WhatHurt,
            OpenQuestionsCount = period.OpenQuestionsCount,
            BoundaryConfidence = period.BoundaryConfidence,
            InterpretationConfidence = period.InterpretationConfidence,
            ReviewPriority = period.ReviewPriority,
            IsSensitive = period.IsSensitive,
            StatusSnapshot = period.StatusSnapshot,
            DynamicSnapshot = period.DynamicSnapshot,
            Lessons = period.Lessons,
            StrategicPatterns = period.StrategicPatterns,
            ManualNotes = period.ManualNotes,
            UserOverrideSummary = period.UserOverrideSummary,
            SourceType = period.SourceType,
            SourceId = period.SourceId,
            SourceMessageId = period.SourceMessageId,
            SourceSessionId = period.SourceSessionId,
            EvidenceRefsJson = period.EvidenceRefsJson,
            CreatedAt = period.CreatedAt == default ? DateTime.UtcNow : period.CreatedAt,
            UpdatedAt = period.UpdatedAt == default ? DateTime.UtcNow : period.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.Periods.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<Period?> GetPeriodByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Periods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<Period>> GetPeriodsByCaseAsync(long caseId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Periods
                .AsNoTracking()
                .Where(x => x.CaseId == caseId)
                .OrderByDescending(x => x.StartAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<PeriodTransition> CreateTransitionAsync(PeriodTransition transition, CancellationToken ct = default)
    {
        var row = new DbPeriodTransition
        {
            Id = transition.Id == Guid.Empty ? Guid.NewGuid() : transition.Id,
            FromPeriodId = transition.FromPeriodId,
            ToPeriodId = transition.ToPeriodId,
            TransitionType = transition.TransitionType,
            Summary = transition.Summary,
            IsResolved = transition.IsResolved,
            Confidence = transition.Confidence,
            GapId = transition.GapId,
            EvidenceRefsJson = transition.EvidenceRefsJson,
            SourceType = transition.SourceType,
            SourceId = transition.SourceId,
            SourceMessageId = transition.SourceMessageId,
            SourceSessionId = transition.SourceSessionId,
            CreatedAt = transition.CreatedAt == default ? DateTime.UtcNow : transition.CreatedAt,
            UpdatedAt = transition.UpdatedAt == default ? DateTime.UtcNow : transition.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.PeriodTransitions.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<PeriodTransition?> GetTransitionByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.PeriodTransitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<PeriodTransition>> GetTransitionsByPeriodAsync(Guid periodId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.PeriodTransitions
                .AsNoTracking()
                .Where(x => x.FromPeriodId == periodId || x.ToPeriodId == periodId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<Hypothesis> CreateHypothesisAsync(Hypothesis hypothesis, CancellationToken ct = default)
    {
        var row = new DbHypothesis
        {
            Id = hypothesis.Id == Guid.Empty ? Guid.NewGuid() : hypothesis.Id,
            HypothesisType = hypothesis.HypothesisType,
            SubjectType = hypothesis.SubjectType,
            SubjectId = hypothesis.SubjectId,
            CaseId = hypothesis.CaseId,
            ChatId = hypothesis.ChatId,
            PeriodId = hypothesis.PeriodId,
            Statement = hypothesis.Statement,
            Confidence = hypothesis.Confidence,
            Status = hypothesis.Status,
            SourceType = hypothesis.SourceType,
            SourceId = hypothesis.SourceId,
            SourceMessageId = hypothesis.SourceMessageId,
            SourceSessionId = hypothesis.SourceSessionId,
            EvidenceRefsJson = hypothesis.EvidenceRefsJson,
            ConflictRefsJson = hypothesis.ConflictRefsJson,
            ValidationTargetsJson = hypothesis.ValidationTargetsJson,
            CreatedAt = hypothesis.CreatedAt == default ? DateTime.UtcNow : hypothesis.CreatedAt,
            UpdatedAt = hypothesis.UpdatedAt == default ? DateTime.UtcNow : hypothesis.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.Hypotheses.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<Hypothesis?> GetHypothesisByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Hypotheses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<Hypothesis>> GetHypothesesByCaseAsync(long caseId, string? status = null, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var query = db.Hypotheses.AsNoTracking().Where(x => x.CaseId == caseId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            var rows = await query
                .OrderByDescending(x => x.UpdatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<bool> UpdatePeriodLifecycleAsync(
        Guid id,
        string label,
        string summary,
        bool isOpen,
        DateTime? endAt,
        short reviewPriority,
        string actor,
        string? reason = null,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Periods.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null)
            {
                return false;
            }

            var oldRef = JsonSerializer.Serialize(new
            {
                row.Label,
                row.Summary,
                row.IsOpen,
                row.EndAt,
                row.ReviewPriority
            });

            row.Label = label;
            row.Summary = summary;
            row.IsOpen = isOpen;
            row.EndAt = endAt;
            row.ReviewPriority = reviewPriority;
            row.UpdatedAt = DateTime.UtcNow;

            await WriteReviewEventAsync(
                db,
                "period",
                row.Id.ToString(),
                "update_lifecycle",
                oldRef,
                JsonSerializer.Serialize(new { row.Label, row.Summary, row.IsOpen, row.EndAt, row.ReviewPriority }),
                actor,
                reason);

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<bool> UpdateHypothesisLifecycleAsync(
        Guid id,
        string status,
        float confidence,
        string actor,
        string? reason = null,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Hypotheses.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null)
            {
                return false;
            }

            var oldRef = JsonSerializer.Serialize(new { row.Status, row.Confidence });
            row.Status = status;
            row.Confidence = confidence;
            row.UpdatedAt = DateTime.UtcNow;

            await WriteReviewEventAsync(
                db,
                "hypothesis",
                row.Id.ToString(),
                "update_lifecycle",
                oldRef,
                JsonSerializer.Serialize(new { row.Status, row.Confidence }),
                actor,
                reason);

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private static Period ToDomain(DbPeriod row) => new()
    {
        Id = row.Id,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        Label = row.Label,
        CustomLabel = row.CustomLabel,
        StartAt = row.StartAt,
        EndAt = row.EndAt,
        IsOpen = row.IsOpen,
        Summary = row.Summary,
        KeySignalsJson = row.KeySignalsJson,
        WhatHelped = row.WhatHelped,
        WhatHurt = row.WhatHurt,
        OpenQuestionsCount = row.OpenQuestionsCount,
        BoundaryConfidence = row.BoundaryConfidence,
        InterpretationConfidence = row.InterpretationConfidence,
        ReviewPriority = row.ReviewPriority,
        IsSensitive = row.IsSensitive,
        StatusSnapshot = row.StatusSnapshot,
        DynamicSnapshot = row.DynamicSnapshot,
        Lessons = row.Lessons,
        StrategicPatterns = row.StrategicPatterns,
        ManualNotes = row.ManualNotes,
        UserOverrideSummary = row.UserOverrideSummary,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        EvidenceRefsJson = row.EvidenceRefsJson,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static PeriodTransition ToDomain(DbPeriodTransition row) => new()
    {
        Id = row.Id,
        FromPeriodId = row.FromPeriodId,
        ToPeriodId = row.ToPeriodId,
        TransitionType = row.TransitionType,
        Summary = row.Summary,
        IsResolved = row.IsResolved,
        Confidence = row.Confidence,
        GapId = row.GapId,
        EvidenceRefsJson = row.EvidenceRefsJson,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static Hypothesis ToDomain(DbHypothesis row) => new()
    {
        Id = row.Id,
        HypothesisType = row.HypothesisType,
        SubjectType = row.SubjectType,
        SubjectId = row.SubjectId,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        PeriodId = row.PeriodId,
        Statement = row.Statement,
        Confidence = row.Confidence,
        Status = row.Status,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        EvidenceRefsJson = row.EvidenceRefsJson,
        ConflictRefsJson = row.ConflictRefsJson,
        ValidationTargetsJson = row.ValidationTargetsJson,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
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

    private static Task WriteReviewEventAsync(
        TgAssistantDbContext db,
        string objectType,
        string objectId,
        string action,
        string? oldValueRef,
        string? newValueRef,
        string actor,
        string? reason)
    {
        db.DomainReviewEvents.Add(new DbDomainReviewEvent
        {
            Id = Guid.NewGuid(),
            ObjectType = objectType,
            ObjectId = objectId,
            Action = action,
            OldValueRef = oldValueRef,
            NewValueRef = newValueRef,
            Actor = actor,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }
}
