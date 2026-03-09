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
            CreatedAt = evt.CreatedAt == default ? DateTime.UtcNow : evt.CreatedAt
        });
        await db.SaveChangesAsync(ct);
    }
}
