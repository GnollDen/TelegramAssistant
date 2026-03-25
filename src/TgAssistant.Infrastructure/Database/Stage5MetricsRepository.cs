using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage5MetricsRepository : IStage5MetricsRepository
{
    private static readonly TimeSpan PendingSessionIdleThreshold = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan QuarantineStuckThreshold = TimeSpan.FromHours(6);
    private static readonly TimeSpan ProcessedWithoutApplySignalGracePeriod = TimeSpan.FromMinutes(15);
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage5MetricsRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage5MetricsSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var oneHourAgo = nowUtc.AddHours(-1);
        var staleBeforeUtc = nowUtc - PendingSessionIdleThreshold;
        var quarantineStuckBeforeUtc = nowUtc - QuarantineStuckThreshold;
        var processedWithoutApplyBeforeUtc = nowUtc - ProcessedWithoutApplySignalGracePeriod;

        var processedMessages = await db.Messages.AsNoTracking()
            .LongCountAsync(x => x.ProcessingStatus == (short)ProcessingStatus.Processed, ct);
        var totalMessageRows = await db.Messages.AsNoTracking()
            .LongCountAsync(ct);

        var extractionsTotal = await db.MessageExtractions.AsNoTracking()
            .LongCountAsync(ct);

        var expensiveBacklog = await db.MessageExtractions.AsNoTracking()
            .LongCountAsync(x => x.NeedsExpensive, ct);

        var mergeCandidatesPending = await db.EntityMergeCandidates.AsNoTracking()
            .LongCountAsync(x => x.Status == (short)MergeDecision.Pending, ct);

        var factReviewsPending = await db.FactReviewCommands.AsNoTracking()
            .LongCountAsync(x => x.Status == 0, ct);

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
        var duplicateGroupSizes = await db.Messages.AsNoTracking()
            .GroupBy(x => new { x.ChatId, x.TelegramMessageId })
            .Select(g => g.LongCount())
            .Where(x => x > 1)
            .ToListAsync(ct);
        var duplicateBusinessKeyGroups = duplicateGroupSizes.LongCount();
        var duplicateBusinessKeyRows = duplicateGroupSizes.Sum(x => x - 1);
        var duplicateBusinessKeyRowRate = totalMessageRows == 0
            ? 0m
            : decimal.Round((decimal)duplicateBusinessKeyRows / totalMessageRows, 6, MidpointRounding.AwayFromZero);
        var processedWithoutExtraction = await db.Messages.AsNoTracking()
            .LongCountAsync(
                x => x.ProcessingStatus == (short)ProcessingStatus.Processed
                     && x.ProcessedAt != null
                     && x.ProcessedAt <= processedWithoutApplyBeforeUtc
                     && !db.MessageExtractions.Any(me => me.MessageId == x.Id),
                ct);
        var processedWithoutApplyEvidenceCount = await db.Messages.AsNoTracking()
            .LongCountAsync(
                m => m.ProcessingStatus == (short)ProcessingStatus.Processed
                     && !m.NeedsReanalysis
                     && m.ProcessedAt != null
                     && m.ProcessedAt <= processedWithoutApplyBeforeUtc
                     && (
                         !db.MessageExtractions.Any(me => me.MessageId == m.Id)
                         || (db.MessageExtractions.Any(
                                 me => me.MessageId == m.Id
                                       && !me.NeedsExpensive
                                       && !me.IsQuarantined)
                             && !db.IntelligenceClaims.Any(ic => ic.MessageId == m.Id)
                             && !db.IntelligenceObservations.Any(io => io.MessageId == m.Id)
                             && !db.CommunicationEvents.Any(ce => ce.MessageId == m.Id)
                             && !db.Facts.Any(f => f.SourceMessageId == m.Id)
                             && !db.Relationships.Any(r => r.SourceMessageId == m.Id))),
                ct);
        var processedWithoutApplyEvidenceRate = processedMessages == 0
            ? 0m
            : decimal.Round((decimal)processedWithoutApplyEvidenceCount / processedMessages, 6, MidpointRounding.AwayFromZero);
        var watermarkRegressionBlocked1h = await db.AnalysisStates.AsNoTracking()
            .Where(x => EF.Functions.Like(x.Key, AnalysisStateSignalKeys.WatermarkMonotonicRegressionMinuteKeyPrefix + "%")
                        && x.UpdatedAt >= oneHourAgo)
            .Select(x => (long?)x.Value)
            .SumAsync(ct) ?? 0L;
        var watermarkMonotonicRegressionCount = await db.AnalysisStates.AsNoTracking()
            .Where(x => x.Key == AnalysisStateSignalKeys.WatermarkMonotonicRegressionCountKey)
            .Select(x => (long?)x.Value)
            .FirstOrDefaultAsync(ct) ?? 0L;
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
            QuarantineStuck = quarantineStuck,
            DuplicateMessageBusinessKeyGroups = duplicateBusinessKeyGroups,
            DuplicateMessageBusinessKeyRows = duplicateBusinessKeyRows,
            DuplicateMessageBusinessKeyRowRate = duplicateBusinessKeyRowRate,
            ProcessedWithoutExtraction = processedWithoutExtraction,
            ProcessedWithoutApplyEvidenceCount = processedWithoutApplyEvidenceCount,
            ProcessedWithoutApplyEvidenceRate = processedWithoutApplyEvidenceRate,
            WatermarkRegressionBlocked1h = watermarkRegressionBlocked1h,
            WatermarkMonotonicRegressionCount = watermarkMonotonicRegressionCount
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
            AnalysisCostUsd1h = snapshot.AnalysisCostUsd1h,
            PendingSessionsQueue = snapshot.PendingSessionsQueue,
            ReanalysisBacklog = snapshot.ReanalysisBacklog,
            QuarantineTotal = snapshot.QuarantineTotal,
            QuarantineStuck = snapshot.QuarantineStuck,
            DuplicateMessageBusinessKeyGroups = snapshot.DuplicateMessageBusinessKeyGroups,
            DuplicateMessageBusinessKeyRows = snapshot.DuplicateMessageBusinessKeyRows,
            DuplicateMessageBusinessKeyRowRate = snapshot.DuplicateMessageBusinessKeyRowRate,
            ProcessedWithoutExtraction = snapshot.ProcessedWithoutExtraction,
            ProcessedWithoutApplyEvidenceCount = snapshot.ProcessedWithoutApplyEvidenceCount,
            ProcessedWithoutApplyEvidenceRate = snapshot.ProcessedWithoutApplyEvidenceRate,
            WatermarkRegressionBlocked1h = snapshot.WatermarkRegressionBlocked1h,
            WatermarkMonotonicRegressionCount = snapshot.WatermarkMonotonicRegressionCount
        });
        await db.SaveChangesAsync(ct);
    }
}
