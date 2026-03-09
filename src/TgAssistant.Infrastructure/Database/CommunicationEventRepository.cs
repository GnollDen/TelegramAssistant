using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class CommunicationEventRepository : ICommunicationEventRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public CommunicationEventRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task AddRangeAsync(IEnumerable<CommunicationEvent> events, CancellationToken ct = default)
    {
        var rows = events.ToList();
        if (rows.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.CommunicationEvents.AddRange(rows.Select(evt => new DbCommunicationEvent
        {
            MessageId = evt.MessageId,
            EntityId = evt.EntityId,
            EventType = evt.EventType,
            ObjectName = evt.ObjectName,
            Sentiment = evt.Sentiment,
            Summary = evt.Summary,
            Confidence = evt.Confidence,
            CreatedAt = evt.CreatedAt == default ? DateTime.UtcNow : evt.CreatedAt
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<CommunicationEvent>> GetByEntityAsync(Guid entityId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.CommunicationEvents
            .AsNoTracking()
            .Where(x => x.EntityId == entityId && x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(x => new CommunicationEvent
        {
            Id = x.Id,
            MessageId = x.MessageId,
            EntityId = x.EntityId,
            EventType = x.EventType,
            ObjectName = x.ObjectName,
            Sentiment = x.Sentiment,
            Summary = x.Summary,
            Confidence = x.Confidence,
            CreatedAt = x.CreatedAt
        }).ToList();
    }
}
