using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage6ArtifactRepository : IStage6ArtifactRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage6ArtifactRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage6ArtifactRecord> UpsertCurrentAsync(Stage6ArtifactRecord artifact, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var current = await db.Stage6Artifacts
                .FirstOrDefaultAsync(x => x.ArtifactType == artifact.ArtifactType
                    && x.CaseId == artifact.CaseId
                    && x.ChatId == artifact.ChatId
                    && x.ScopeKey == artifact.ScopeKey
                    && x.IsCurrent,
                    ct);

            if (current != null
                && string.Equals(current.FreshnessBasisHash, artifact.FreshnessBasisHash, StringComparison.Ordinal)
                && string.Equals(current.PayloadObjectType ?? string.Empty, artifact.PayloadObjectType ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(current.PayloadObjectId ?? string.Empty, artifact.PayloadObjectId ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(current.PayloadJson ?? "{}", artifact.PayloadJson ?? "{}", StringComparison.Ordinal))
            {
                current.RefreshedAt = now;
                current.IsStale = false;
                current.StaleReason = null;
                current.UpdatedAt = now;
                await db.SaveChangesAsync(ct);
                return ToDomain(current);
            }

            if (current != null)
            {
                current.IsCurrent = false;
                current.UpdatedAt = now;
            }

            var row = new DbStage6Artifact
            {
                Id = artifact.Id == Guid.Empty ? Guid.NewGuid() : artifact.Id,
                ArtifactType = artifact.ArtifactType,
                CaseId = artifact.CaseId,
                ChatId = artifact.ChatId,
                ScopeKey = artifact.ScopeKey,
                PayloadObjectType = artifact.PayloadObjectType,
                PayloadObjectId = artifact.PayloadObjectId,
                PayloadJson = string.IsNullOrWhiteSpace(artifact.PayloadJson) ? "{}" : artifact.PayloadJson,
                FreshnessBasisHash = artifact.FreshnessBasisHash,
                FreshnessBasisJson = string.IsNullOrWhiteSpace(artifact.FreshnessBasisJson) ? "{}" : artifact.FreshnessBasisJson,
                GeneratedAt = artifact.GeneratedAt == default ? now : artifact.GeneratedAt,
                RefreshedAt = artifact.RefreshedAt ?? (artifact.GeneratedAt == default ? now : artifact.GeneratedAt),
                StaleAt = artifact.StaleAt,
                IsStale = artifact.IsStale,
                StaleReason = artifact.StaleReason,
                ReuseCount = artifact.ReuseCount,
                IsCurrent = true,
                SourceType = artifact.SourceType,
                SourceId = artifact.SourceId,
                SourceMessageId = artifact.SourceMessageId,
                SourceSessionId = artifact.SourceSessionId,
                CreatedAt = artifact.CreatedAt == default ? now : artifact.CreatedAt,
                UpdatedAt = artifact.UpdatedAt == default ? now : artifact.UpdatedAt
            };

            db.Stage6Artifacts.Add(row);
            await db.SaveChangesAsync(ct);
            return ToDomain(row);
        }, ct);
    }

    public async Task<Stage6ArtifactRecord?> GetCurrentAsync(
        long caseId,
        long? chatId,
        string artifactType,
        string scopeKey,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Stage6Artifacts.AsNoTracking()
                .Where(x => x.CaseId == caseId
                    && x.ChatId == chatId
                    && x.ArtifactType == artifactType
                    && x.ScopeKey == scopeKey
                    && x.IsCurrent)
                .OrderByDescending(x => x.GeneratedAt)
                .FirstOrDefaultAsync(ct);

            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<bool> MarkStaleAsync(Guid artifactId, string reason, DateTime staleAtUtc, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Stage6Artifacts.FirstOrDefaultAsync(x => x.Id == artifactId, ct);
            if (row == null)
            {
                return false;
            }

            row.IsStale = true;
            row.StaleReason = reason;
            row.StaleAt = staleAtUtc;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<bool> TouchReusedAsync(Guid artifactId, DateTime reusedAtUtc, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Stage6Artifacts.FirstOrDefaultAsync(x => x.Id == artifactId, ct);
            if (row == null)
            {
                return false;
            }

            row.ReuseCount = Math.Max(0, row.ReuseCount) + 1;
            row.RefreshedAt = reusedAtUtc;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private static Stage6ArtifactRecord ToDomain(DbStage6Artifact row) => new()
    {
        Id = row.Id,
        ArtifactType = row.ArtifactType,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        ScopeKey = row.ScopeKey,
        PayloadObjectType = row.PayloadObjectType,
        PayloadObjectId = row.PayloadObjectId,
        PayloadJson = row.PayloadJson,
        FreshnessBasisHash = row.FreshnessBasisHash,
        FreshnessBasisJson = row.FreshnessBasisJson,
        GeneratedAt = row.GeneratedAt,
        RefreshedAt = row.RefreshedAt,
        StaleAt = row.StaleAt,
        IsStale = row.IsStale,
        StaleReason = row.StaleReason,
        ReuseCount = row.ReuseCount,
        IsCurrent = row.IsCurrent,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
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
}
