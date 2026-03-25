using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage6FeedbackRepository : IStage6FeedbackRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage6FeedbackRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage6FeedbackEntry> AddAsync(Stage6FeedbackEntry entry, CancellationToken ct = default)
    {
        var row = new DbStage6FeedbackEntry
        {
            Id = entry.Id == Guid.Empty ? Guid.NewGuid() : entry.Id,
            ScopeCaseId = entry.ScopeCaseId,
            ChatId = entry.ChatId,
            Stage6CaseId = entry.Stage6CaseId,
            ArtifactType = string.IsNullOrWhiteSpace(entry.ArtifactType) ? null : entry.ArtifactType.Trim(),
            FeedbackKind = NormalizeFeedbackKind(entry.FeedbackKind),
            FeedbackDimension = NormalizeFeedbackDimension(entry.FeedbackDimension),
            IsUseful = entry.IsUseful,
            Note = string.IsNullOrWhiteSpace(entry.Note) ? null : entry.Note.Trim(),
            SourceChannel = string.IsNullOrWhiteSpace(entry.SourceChannel) ? "web" : entry.SourceChannel.Trim(),
            Actor = string.IsNullOrWhiteSpace(entry.Actor) ? "operator" : entry.Actor.Trim(),
            CreatedAt = entry.CreatedAt == default ? DateTime.UtcNow : entry.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.Stage6FeedbackEntries.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<Stage6FeedbackEntry>> GetByCaseAsync(Guid stage6CaseId, int limit = 100, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Stage6FeedbackEntries
                .AsNoTracking()
                .Where(x => x.Stage6CaseId == stage6CaseId)
                .OrderByDescending(x => x.CreatedAt)
                .Take(safeLimit)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<List<Stage6FeedbackEntry>> GetByArtifactAsync(
        long scopeCaseId,
        long? chatId,
        string artifactType,
        int limit = 100,
        CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);
        var normalizedArtifactType = (artifactType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedArtifactType))
        {
            return [];
        }

        return await WithDbContextAsync(async db =>
        {
            var query = db.Stage6FeedbackEntries
                .AsNoTracking()
                .Where(x => x.ScopeCaseId == scopeCaseId
                            && x.ArtifactType == normalizedArtifactType);

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

    private static string NormalizeFeedbackKind(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Stage6FeedbackKinds.AcceptUseful or
            Stage6FeedbackKinds.RejectNotUseful or
            Stage6FeedbackKinds.CorrectionNote or
            Stage6FeedbackKinds.RefreshRequested => normalized,
            _ => Stage6FeedbackKinds.CorrectionNote
        };
    }

    private static string NormalizeFeedbackDimension(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Stage6FeedbackDimensions.General or
            Stage6FeedbackDimensions.ClarificationUsefulness or
            Stage6FeedbackDimensions.BehavioralUsefulness => normalized,
            _ => Stage6FeedbackDimensions.General
        };
    }

    private static Stage6FeedbackEntry ToDomain(DbStage6FeedbackEntry row) => new()
    {
        Id = row.Id,
        ScopeCaseId = row.ScopeCaseId,
        ChatId = row.ChatId,
        Stage6CaseId = row.Stage6CaseId,
        ArtifactType = row.ArtifactType,
        FeedbackKind = row.FeedbackKind,
        FeedbackDimension = row.FeedbackDimension,
        IsUseful = row.IsUseful,
        Note = row.Note,
        SourceChannel = row.SourceChannel,
        Actor = row.Actor,
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
