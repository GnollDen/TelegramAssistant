using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class AnalysisUsageRepository : IAnalysisUsageRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public AnalysisUsageRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task LogAsync(AnalysisUsageEvent evt, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.AnalysisUsageEvents.Add(new DbAnalysisUsageEvent
        {
            Phase = string.IsNullOrWhiteSpace(evt.Phase) ? "unknown" : evt.Phase.Trim(),
            Model = string.IsNullOrWhiteSpace(evt.Model) ? "unknown" : evt.Model.Trim(),
            PromptTokens = Math.Max(0, evt.PromptTokens),
            CompletionTokens = Math.Max(0, evt.CompletionTokens),
            TotalTokens = Math.Max(0, evt.TotalTokens),
            CostUsd = evt.CostUsd < 0 ? 0 : evt.CostUsd,
            LatencyMs = evt.LatencyMs.HasValue ? Math.Max(0, evt.LatencyMs.Value) : null,
            CreatedAt = evt.CreatedAt == default ? DateTime.UtcNow : evt.CreatedAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetCostUsdSinceAsync(string phase, DateTime sinceUtc, CancellationToken ct = default)
    {
        var normalizedPhase = string.IsNullOrWhiteSpace(phase) ? "unknown" : phase.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var sum = await db.AnalysisUsageEvents
            .AsNoTracking()
            .Where(x => x.Phase == normalizedPhase && x.CreatedAt >= sinceUtc)
            .SumAsync(x => (decimal?)x.CostUsd, ct);
        return sum ?? 0m;
    }

    public async Task<Dictionary<string, decimal>> GetCostUsdByPhaseSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AnalysisUsageEvents
            .AsNoTracking()
            .Where(x => x.CreatedAt >= sinceUtc)
            .GroupBy(x => x.Phase)
            .Select(g => new { Phase = g.Key, Cost = g.Sum(x => x.CostUsd) })
            .ToListAsync(ct);

        return rows.ToDictionary(
            x => string.IsNullOrWhiteSpace(x.Phase) ? "unknown" : x.Phase,
            x => x.Cost < 0 ? 0 : x.Cost,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AnalysisUsageWindowSummary> SummarizeWindowAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc < fromUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AnalysisUsageEvents
            .AsNoTracking()
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc)
            .Select(x => new
            {
                x.Phase,
                x.Model,
                x.PromptTokens,
                x.CompletionTokens,
                x.TotalTokens,
                x.CostUsd,
                x.LatencyMs
            })
            .ToListAsync(ct);

        var summary = new AnalysisUsageWindowSummary
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            TotalRows = rows.Count,
            TotalCostUsd = rows.Sum(x => x.CostUsd < 0 ? 0 : x.CostUsd),
            TotalPromptTokens = rows.Sum(x => Math.Max(0, x.PromptTokens)),
            TotalCompletionTokens = rows.Sum(x => Math.Max(0, x.CompletionTokens)),
            TotalTokens = rows.Sum(x => Math.Max(0, x.TotalTokens))
        };

        var latencyRows = rows.Where(x => x.LatencyMs.HasValue).Select(x => x.LatencyMs!.Value).ToList();
        summary.AverageLatencyMs = latencyRows.Count == 0 ? 0 : latencyRows.Average();
        summary.CostByModel = rows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Model) ? "unknown" : x.Model, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.CostUsd < 0 ? 0 : x.CostUsd),
                StringComparer.OrdinalIgnoreCase);
        summary.RowsByPhaseModel = rows
            .GroupBy(x => $"{(string.IsNullOrWhiteSpace(x.Phase) ? "unknown" : x.Phase)}|{(string.IsNullOrWhiteSpace(x.Model) ? "unknown" : x.Model)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Count(),
                StringComparer.OrdinalIgnoreCase);
        summary.AvgLatencyByPhaseModel = rows
            .Where(x => x.LatencyMs.HasValue)
            .GroupBy(x => $"{(string.IsNullOrWhiteSpace(x.Phase) ? "unknown" : x.Phase)}|{(string.IsNullOrWhiteSpace(x.Model) ? "unknown" : x.Model)}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Average(x => x.LatencyMs!.Value),
                StringComparer.OrdinalIgnoreCase);

        return summary;
    }
}
