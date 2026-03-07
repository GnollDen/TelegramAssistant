using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class SummaryRepository : ISummaryRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public SummaryRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task SaveAsync(DailySummary summary, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DailySummaries.Add(new DbDailySummary
        {
            Id = summary.Id == Guid.Empty ? Guid.NewGuid() : summary.Id,
            ChatId = summary.ChatId,
            EntityId = summary.EntityId,
            Date = summary.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Summary = summary.Summary,
            MessageCount = summary.MessageCount,
            MediaCount = summary.MediaCount,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DailySummary>> GetByEntityAsync(Guid entityId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.DailySummaries.AsNoTracking()
            .Where(x => x.EntityId == entityId && x.Date >= fromDt && x.Date <= toDt)
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        return rows.Select(x => new DailySummary
        {
            Id = x.Id,
            ChatId = x.ChatId,
            EntityId = x.EntityId,
            Date = DateOnly.FromDateTime(x.Date),
            Summary = x.Summary,
            MessageCount = x.MessageCount,
            MediaCount = x.MediaCount,
            CreatedAt = x.CreatedAt
        }).ToList();
    }
}
