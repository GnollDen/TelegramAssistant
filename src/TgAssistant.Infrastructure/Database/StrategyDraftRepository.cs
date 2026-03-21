using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class StrategyDraftRepository : IStrategyDraftRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public StrategyDraftRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<StrategyRecord> CreateStrategyRecordAsync(StrategyRecord record, CancellationToken ct = default)
    {
        var row = new DbStrategyRecord
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            CaseId = record.CaseId,
            ChatId = record.ChatId,
            PeriodId = record.PeriodId,
            StateSnapshotId = record.StateSnapshotId,
            StrategyConfidence = record.StrategyConfidence,
            RecommendedGoal = record.RecommendedGoal,
            WhyNotOthers = record.WhyNotOthers,
            SourceSessionId = record.SourceSessionId,
            SourceMessageId = record.SourceMessageId,
            CreatedAt = record.CreatedAt == default ? DateTime.UtcNow : record.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.StrategyRecords.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<StrategyRecord?> GetStrategyRecordByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.StrategyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<StrategyRecord>> GetStrategyRecordsByCaseAsync(long caseId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.StrategyRecords.AsNoTracking().Where(x => x.CaseId == caseId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<StrategyOption> CreateStrategyOptionAsync(StrategyOption option, CancellationToken ct = default)
    {
        var row = new DbStrategyOption
        {
            Id = option.Id == Guid.Empty ? Guid.NewGuid() : option.Id,
            StrategyRecordId = option.StrategyRecordId,
            ActionType = option.ActionType,
            Summary = option.Summary,
            Purpose = option.Purpose,
            Risk = option.Risk,
            WhenToUse = option.WhenToUse,
            SuccessSigns = option.SuccessSigns,
            FailureSigns = option.FailureSigns,
            IsPrimary = option.IsPrimary
        };

        await WithDbContextAsync(async db =>
        {
            db.StrategyOptions.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<StrategyOption>> GetStrategyOptionsByRecordIdAsync(Guid strategyRecordId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.StrategyOptions.AsNoTracking().Where(x => x.StrategyRecordId == strategyRecordId).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<DraftRecord> CreateDraftRecordAsync(DraftRecord draft, CancellationToken ct = default)
    {
        var row = new DbDraftRecord
        {
            Id = draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id,
            StrategyRecordId = draft.StrategyRecordId,
            SourceSessionId = draft.SourceSessionId,
            MainDraft = draft.MainDraft,
            AltDraft1 = draft.AltDraft1,
            AltDraft2 = draft.AltDraft2,
            StyleNotes = draft.StyleNotes,
            Confidence = draft.Confidence,
            SourceMessageId = draft.SourceMessageId,
            CreatedAt = draft.CreatedAt == default ? DateTime.UtcNow : draft.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.DraftRecords.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<DraftRecord?> GetDraftRecordByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.DraftRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<DraftRecord>> GetDraftRecordsByStrategyRecordIdAsync(Guid strategyRecordId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.DraftRecords.AsNoTracking().Where(x => x.StrategyRecordId == strategyRecordId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<DraftOutcome> CreateDraftOutcomeAsync(DraftOutcome outcome, CancellationToken ct = default)
    {
        var row = new DbDraftOutcome
        {
            Id = outcome.Id == Guid.Empty ? Guid.NewGuid() : outcome.Id,
            DraftId = outcome.DraftId,
            ActualMessageId = outcome.ActualMessageId,
            MatchScore = outcome.MatchScore,
            OutcomeLabel = outcome.OutcomeLabel,
            Notes = outcome.Notes,
            SourceSessionId = outcome.SourceSessionId,
            SourceMessageId = outcome.SourceMessageId,
            CreatedAt = outcome.CreatedAt == default ? DateTime.UtcNow : outcome.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.DraftOutcomes.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<DraftOutcome>> GetDraftOutcomesByDraftIdAsync(Guid draftId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.DraftOutcomes.AsNoTracking().Where(x => x.DraftId == draftId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static StrategyRecord ToDomain(DbStrategyRecord row) => new()
    {
        Id = row.Id,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        PeriodId = row.PeriodId,
        StateSnapshotId = row.StateSnapshotId,
        StrategyConfidence = row.StrategyConfidence,
        RecommendedGoal = row.RecommendedGoal,
        WhyNotOthers = row.WhyNotOthers,
        CreatedAt = row.CreatedAt,
        SourceSessionId = row.SourceSessionId,
        SourceMessageId = row.SourceMessageId
    };

    private static StrategyOption ToDomain(DbStrategyOption row) => new()
    {
        Id = row.Id,
        StrategyRecordId = row.StrategyRecordId,
        ActionType = row.ActionType,
        Summary = row.Summary,
        Purpose = row.Purpose,
        Risk = row.Risk,
        WhenToUse = row.WhenToUse,
        SuccessSigns = row.SuccessSigns,
        FailureSigns = row.FailureSigns,
        IsPrimary = row.IsPrimary
    };

    private static DraftRecord ToDomain(DbDraftRecord row) => new()
    {
        Id = row.Id,
        StrategyRecordId = row.StrategyRecordId,
        SourceSessionId = row.SourceSessionId,
        MainDraft = row.MainDraft,
        AltDraft1 = row.AltDraft1,
        AltDraft2 = row.AltDraft2,
        StyleNotes = row.StyleNotes,
        Confidence = row.Confidence,
        CreatedAt = row.CreatedAt,
        SourceMessageId = row.SourceMessageId
    };

    private static DraftOutcome ToDomain(DbDraftOutcome row) => new()
    {
        Id = row.Id,
        DraftId = row.DraftId,
        ActualMessageId = row.ActualMessageId,
        MatchScore = row.MatchScore,
        OutcomeLabel = row.OutcomeLabel,
        Notes = row.Notes,
        CreatedAt = row.CreatedAt,
        SourceSessionId = row.SourceSessionId,
        SourceMessageId = row.SourceMessageId
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
