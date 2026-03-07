using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class MessageExtractionRepository : IMessageExtractionRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public MessageExtractionRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertCheapAsync(long messageId, string cheapJson, bool needsExpensive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.MessageExtractions.FirstOrDefaultAsync(x => x.MessageId == messageId, ct);
        if (existing == null)
        {
            db.MessageExtractions.Add(new DbMessageExtraction
            {
                MessageId = messageId,
                CheapJson = cheapJson,
                NeedsExpensive = needsExpensive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.CheapJson = cheapJson;
            existing.NeedsExpensive = needsExpensive;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<List<MessageExtractionRecord>> GetExpensiveBacklogAsync(int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.MessageExtractions.AsNoTracking()
            .Where(x => x.NeedsExpensive)
            .OrderBy(x => x.UpdatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(x => new MessageExtractionRecord
        {
            Id = x.Id,
            MessageId = x.MessageId,
            CheapJson = x.CheapJson,
            ExpensiveJson = x.ExpensiveJson,
            NeedsExpensive = x.NeedsExpensive
        }).ToList();
    }

    public async Task ResolveExpensiveAsync(long extractionId, string expensiveJson, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.MessageExtractions.FirstOrDefaultAsync(x => x.Id == extractionId, ct);
        if (row == null)
        {
            return;
        }

        row.ExpensiveJson = expensiveJson;
        row.NeedsExpensive = false;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
