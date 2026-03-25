using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage6CaseOutcomeRepository : IStage6CaseOutcomeRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage6CaseOutcomeRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage6CaseOutcomeRecord> AddAsync(Stage6CaseOutcomeRecord record, CancellationToken ct = default)
    {
        var row = new DbStage6CaseOutcome
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            Stage6CaseId = record.Stage6CaseId,
            ScopeCaseId = record.ScopeCaseId,
            ChatId = record.ChatId,
            OutcomeType = NormalizeOutcomeType(record.OutcomeType),
            CaseStatusAfter = NormalizeCaseStatus(record.CaseStatusAfter),
            UserContextMaterial = record.UserContextMaterial,
            Note = string.IsNullOrWhiteSpace(record.Note) ? null : record.Note.Trim(),
            SourceChannel = string.IsNullOrWhiteSpace(record.SourceChannel) ? "web" : record.SourceChannel.Trim(),
            Actor = string.IsNullOrWhiteSpace(record.Actor) ? "operator" : record.Actor.Trim(),
            CreatedAt = record.CreatedAt == default ? DateTime.UtcNow : record.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.Stage6CaseOutcomes.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<Stage6CaseOutcomeRecord>> GetByCaseAsync(Guid stage6CaseId, int limit = 100, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Stage6CaseOutcomes
                .AsNoTracking()
                .Where(x => x.Stage6CaseId == stage6CaseId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(safeLimit)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<List<Stage6CaseOutcomeRecord>> GetByScopeAsync(long scopeCaseId, long? chatId, int limit = 300, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 1000);
        return await WithDbContextAsync(async db =>
        {
            var query = db.Stage6CaseOutcomes
                .AsNoTracking()
                .Where(x => x.ScopeCaseId == scopeCaseId);
            if (chatId.HasValue)
            {
                query = query.Where(x => x.ChatId == null || x.ChatId == chatId.Value);
            }

            var rows = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(safeLimit)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static Stage6CaseOutcomeRecord ToDomain(DbStage6CaseOutcome row) => new()
    {
        Id = row.Id,
        Stage6CaseId = row.Stage6CaseId,
        ScopeCaseId = row.ScopeCaseId,
        ChatId = row.ChatId,
        OutcomeType = row.OutcomeType,
        CaseStatusAfter = row.CaseStatusAfter,
        UserContextMaterial = row.UserContextMaterial,
        Note = row.Note,
        SourceChannel = row.SourceChannel,
        Actor = row.Actor,
        CreatedAt = row.CreatedAt
    };

    private static string NormalizeOutcomeType(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Stage6CaseOutcomeTypes.Resolved or
            Stage6CaseOutcomeTypes.Rejected or
            Stage6CaseOutcomeTypes.Stale or
            Stage6CaseOutcomeTypes.Refreshed or
            Stage6CaseOutcomeTypes.AnsweredByUser => normalized,
            _ => Stage6CaseOutcomeTypes.Resolved
        };
    }

    private static string NormalizeCaseStatus(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Stage6CaseStatuses.New or
            Stage6CaseStatuses.Ready or
            Stage6CaseStatuses.NeedsUserInput or
            Stage6CaseStatuses.Resolved or
            Stage6CaseStatuses.Rejected or
            Stage6CaseStatuses.Stale => normalized,
            _ => Stage6CaseStatuses.Ready
        };
    }

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
