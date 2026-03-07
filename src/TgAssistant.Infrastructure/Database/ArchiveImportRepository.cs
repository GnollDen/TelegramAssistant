using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ArchiveImportRepository : IArchiveImportRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ArchiveImportRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ArchiveImportRun?> GetRunningRunAsync(string sourcePath, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ArchiveImportRuns
            .AsNoTracking()
            .Where(x => x.SourcePath == sourcePath && x.Status == (short)ArchiveImportRunStatus.Running)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return row == null ? null : ToDomain(row);
    }

    public async Task<ArchiveImportRun?> GetLatestRunAsync(string sourcePath, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ArchiveImportRuns
            .AsNoTracking()
            .Where(x => x.SourcePath == sourcePath)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return row == null ? null : ToDomain(row);
    }

    public async Task<ArchiveImportRun> CreateRunAsync(ArchiveImportRun run, CancellationToken ct = default)
    {
        run.Id = run.Id == Guid.Empty ? Guid.NewGuid() : run.Id;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ArchiveImportRuns.Add(new DbArchiveImportRun
        {
            Id = run.Id,
            SourcePath = run.SourcePath,
            Status = (short)run.Status,
            LastMessageIndex = run.LastMessageIndex,
            ImportedMessages = run.ImportedMessages,
            QueuedMedia = run.QueuedMedia,
            TotalMessages = run.TotalMessages,
            TotalMedia = run.TotalMedia,
            EstimatedCostUsd = run.EstimatedCostUsd,
            Error = run.Error,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task UpsertEstimateAsync(string sourcePath, ArchiveCostEstimate estimate, ArchiveImportRunStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var latest = await db.ArchiveImportRuns
            .Where(x => x.SourcePath == sourcePath)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest == null || latest.Status is (short)ArchiveImportRunStatus.Completed or (short)ArchiveImportRunStatus.Failed)
        {
            db.ArchiveImportRuns.Add(new DbArchiveImportRun
            {
                Id = Guid.NewGuid(),
                SourcePath = sourcePath,
                Status = (short)status,
                LastMessageIndex = -1,
                ImportedMessages = 0,
                QueuedMedia = 0,
                TotalMessages = estimate.TotalMessages,
                TotalMedia = estimate.MediaMessages,
                EstimatedCostUsd = estimate.EstimatedCostUsd,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            latest.Status = (short)status;
            latest.TotalMessages = estimate.TotalMessages;
            latest.TotalMedia = estimate.MediaMessages;
            latest.EstimatedCostUsd = estimate.EstimatedCostUsd;
            latest.Error = null;
            latest.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateProgressAsync(Guid runId, int lastMessageIndex, long importedMessages, long queuedMedia, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var run = await db.ArchiveImportRuns.FirstOrDefaultAsync(x => x.Id == runId, ct);
        if (run == null)
        {
            return;
        }

        run.LastMessageIndex = lastMessageIndex;
        run.ImportedMessages = importedMessages;
        run.QueuedMedia = queuedMedia;
        run.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteRunAsync(Guid runId, ArchiveImportRunStatus status, string? error, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var run = await db.ArchiveImportRuns.FirstOrDefaultAsync(x => x.Id == runId, ct);
        if (run == null)
        {
            return;
        }

        run.Status = (short)status;
        run.Error = error;
        run.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static ArchiveImportRun ToDomain(DbArchiveImportRun row)
    {
        return new ArchiveImportRun
        {
            Id = row.Id,
            SourcePath = row.SourcePath,
            Status = (ArchiveImportRunStatus)row.Status,
            LastMessageIndex = row.LastMessageIndex,
            ImportedMessages = row.ImportedMessages,
            QueuedMedia = row.QueuedMedia,
            TotalMessages = row.TotalMessages,
            TotalMedia = row.TotalMedia,
            EstimatedCostUsd = row.EstimatedCostUsd,
            Error = row.Error,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}
