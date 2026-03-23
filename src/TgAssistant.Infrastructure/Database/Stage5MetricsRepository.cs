using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage5MetricsRepository : IStage5MetricsRepository
{
    private static readonly TimeSpan PendingSessionIdleThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan QuarantineStuckThreshold = TimeSpan.FromHours(6);
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage5MetricsRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage5MetricsSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var processedMessages = await db.Messages.AsNoTracking()
            .LongCountAsync(x => x.ProcessingStatus == 1, ct);

        var extractionsTotal = await db.MessageExtractions.AsNoTracking()
            .LongCountAsync(ct);

        var expensiveBacklog = await db.MessageExtractions.AsNoTracking()
            .LongCountAsync(x => x.NeedsExpensive, ct);

        var mergeCandidatesPending = await db.EntityMergeCandidates.AsNoTracking()
            .LongCountAsync(x => x.Status == (short)MergeDecision.Pending, ct);

        var factReviewsPending = await db.FactReviewCommands.AsNoTracking()
            .LongCountAsync(x => x.Status == 0, ct);

        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var staleBeforeUtc = DateTime.UtcNow - PendingSessionIdleThreshold;
        var quarantineStuckBeforeUtc = DateTime.UtcNow - QuarantineStuckThreshold;
        var extractionErrors1h = await db.ExtractionErrors.AsNoTracking()
            .LongCountAsync(x => x.CreatedAt >= oneHourAgo, ct);
        var pendingSessionsQueue = await db.ChatSessions.AsNoTracking()
            .LongCountAsync(
                x => !x.IsAnalyzed
                     && !x.IsFinalized
                     && x.LastMessageAt <= staleBeforeUtc,
                ct);
        var reanalysisBacklog = await db.Messages.AsNoTracking()
            .LongCountAsync(
                x => x.NeedsReanalysis
                     && x.ProcessingStatus == (short)ProcessingStatus.Processed,
                ct);
        var quarantineTotal = await db.MessageExtractions.AsNoTracking()
            .LongCountAsync(x => x.IsQuarantined, ct);
        var quarantineStuck = await db.MessageExtractions.AsNoTracking()
            .LongCountAsync(
                x => x.IsQuarantined
                     && x.QuarantinedAt != null
                     && x.QuarantinedAt <= quarantineStuckBeforeUtc,
                ct);
        var analysisUsage1h = await db.AnalysisUsageEvents.AsNoTracking()
            .Where(x => x.CreatedAt >= oneHourAgo)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Requests = g.LongCount(),
                Tokens = g.Sum(x => (long)x.TotalTokens),
                Cost = g.Sum(x => x.CostUsd)
            })
            .FirstOrDefaultAsync(ct);

        return new Stage5MetricsSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            ProcessedMessages = processedMessages,
            ExtractionsTotal = extractionsTotal,
            ExpensiveBacklog = expensiveBacklog,
            MergeCandidatesPending = mergeCandidatesPending,
            FactReviewsPending = factReviewsPending,
            ExtractionErrors1h = extractionErrors1h,
            AnalysisRequests1h = analysisUsage1h?.Requests ?? 0,
            AnalysisTokens1h = analysisUsage1h?.Tokens ?? 0,
            AnalysisCostUsd1h = analysisUsage1h?.Cost ?? 0m,
            PendingSessionsQueue = pendingSessionsQueue,
            ReanalysisBacklog = reanalysisBacklog,
            QuarantineTotal = quarantineTotal,
            QuarantineStuck = quarantineStuck
        };
    }

    public async Task SaveSnapshotAsync(Stage5MetricsSnapshot snapshot, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Stage5MetricsSnapshots.Add(new DbStage5MetricsSnapshot
        {
            CapturedAt = snapshot.CapturedAt,
            ProcessedMessages = snapshot.ProcessedMessages,
            ExtractionsTotal = snapshot.ExtractionsTotal,
            ExpensiveBacklog = snapshot.ExpensiveBacklog,
            MergeCandidatesPending = snapshot.MergeCandidatesPending,
            FactReviewsPending = snapshot.FactReviewsPending,
            ExtractionErrors1h = snapshot.ExtractionErrors1h,
            AnalysisRequests1h = snapshot.AnalysisRequests1h,
            AnalysisTokens1h = snapshot.AnalysisTokens1h,
            AnalysisCostUsd1h = snapshot.AnalysisCostUsd1h
        });
        await db.SaveChangesAsync(ct);
    }
}
