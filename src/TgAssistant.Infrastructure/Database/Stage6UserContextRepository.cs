using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage6UserContextRepository : IStage6UserContextRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage6UserContextRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage6UserContextEntry> CreateAsync(Stage6UserContextEntry entry, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = new DbStage6UserContextEntry
        {
            Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
            Stage6CaseId = entry.Stage6CaseId,
            ScopeCaseId = entry.ScopeCaseId,
            ChatId = entry.ChatId,
            SourceKind = NormalizeSourceKind(entry.SourceKind),
            ClarificationQuestionId = entry.ClarificationQuestionId,
            ContentText = entry.ContentText,
            StructuredPayloadJson = entry.StructuredPayloadJson,
            AppliesToRefsJson = NormalizeJson(entry.AppliesToRefsJson, "[]"),
            EnteredVia = entry.EnteredVia,
            UserReportedCertainty = Math.Clamp(entry.UserReportedCertainty, 0f, 1f),
            SourceType = entry.SourceType,
            SourceId = entry.SourceId,
            SourceMessageId = entry.SourceMessageId,
            SourceSessionId = entry.SourceSessionId,
            SupersedesContextEntryId = entry.SupersedesContextEntryId,
            ConflictsWithRefsJson = NormalizeJson(entry.ConflictsWithRefsJson, "[]"),
            CreatedAt = entry.CreatedAt == default ? now : entry.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.Stage6UserContextEntries.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<Stage6UserContextEntry>> GetByScopeCaseAsync(long scopeCaseId, int limit = 200, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Stage6UserContextEntries
                .AsNoTracking()
                .Where(x => x.ScopeCaseId == scopeCaseId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(safeLimit)
                .ToListAsync(ct);

            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static Stage6UserContextEntry ToDomain(DbStage6UserContextEntry row) => new()
    {
        Id = row.Id,
        Stage6CaseId = row.Stage6CaseId,
        ScopeCaseId = row.ScopeCaseId,
        ChatId = row.ChatId,
        SourceKind = row.SourceKind,
        ClarificationQuestionId = row.ClarificationQuestionId,
        ContentText = row.ContentText,
        StructuredPayloadJson = row.StructuredPayloadJson,
        AppliesToRefsJson = row.AppliesToRefsJson,
        EnteredVia = row.EnteredVia,
        UserReportedCertainty = row.UserReportedCertainty,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        SupersedesContextEntryId = row.SupersedesContextEntryId,
        ConflictsWithRefsJson = row.ConflictsWithRefsJson,
        CreatedAt = row.CreatedAt
    };

    private static string NormalizeSourceKind(string sourceKind)
    {
        var normalized = (sourceKind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            UserContextSourceKinds.ClarificationAnswer or
            UserContextSourceKinds.LongFormContext or
            UserContextSourceKinds.OfflineContextNote => normalized,
            _ => UserContextSourceKinds.ClarificationAnswer
        };
    }

    private static string NormalizeJson(string? json, string fallback)
    {
        return string.IsNullOrWhiteSpace(json) ? fallback : json;
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
