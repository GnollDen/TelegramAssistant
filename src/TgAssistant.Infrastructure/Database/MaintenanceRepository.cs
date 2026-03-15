using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class MaintenanceRepository : IMaintenanceRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public MaintenanceRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<MaintenanceCleanupResult> CleanupAsync(MaintenanceCleanupRequest request, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var result = new MaintenanceCleanupResult();

        if (request.FactDecayEnabled)
        {
            var instantCutoff = DateTime.UtcNow.AddHours(-24);
            var fastCutoff = DateTime.UtcNow.AddDays(-30);
            var slowCutoff = DateTime.UtcNow.AddDays(-730);
            result.FactsExpired = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE facts
                   SET is_current = FALSE,
                       valid_until = NOW(),
                       updated_at = NOW()
                 WHERE is_current = TRUE
                   AND (
                        (decay_class = 'instant' AND updated_at < {instantCutoff})
                     OR (decay_class = 'fast' AND updated_at < {fastCutoff})
                     OR (decay_class = 'slow' AND updated_at < {slowCutoff})
                   )
                """, ct);
        }

        var factReviewPendingCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, request.FactReviewPendingTimeoutDays));
        result.FactReviewCommandsTimedOut = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE fact_review_commands
               SET status = 2,
                   error = COALESCE(error, 'timeout'),
                   processed_at = NOW()
             WHERE status = 0
               AND created_at < {factReviewPendingCutoff}
            """, ct);

        var errCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, request.ExtractionErrorsRetentionDays));
        result.ExtractionErrorsDeleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM extraction_errors WHERE created_at < {errCutoff}
            """, ct);

        var metricsCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, request.Stage5MetricsRetentionDays));
        result.Stage5MetricsDeleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM stage5_metrics_snapshots WHERE captured_at < {metricsCutoff}
            """, ct);

        var mergeCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, request.MergeDecisionsRetentionDays));
        result.MergeDecisionsDeleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM entity_merge_decisions WHERE created_at < {mergeCutoff}
            """, ct);

        var factReviewCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, request.FactReviewCommandsRetentionDays));
        result.FactReviewCommandsDeleted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM fact_review_commands
            WHERE status <> 0
              AND COALESCE(processed_at, created_at) < {factReviewCutoff}
            """, ct);

        return result;
    }
}
