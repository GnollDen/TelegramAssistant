using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ExtractionErrorRepository : IExtractionErrorRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ExtractionErrorRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task LogAsync(string stage, string reason, long? messageId = null, string? payload = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ExtractionErrors.Add(new DbExtractionError
        {
            Stage = string.IsNullOrWhiteSpace(stage) ? "unknown" : stage.Trim(),
            MessageId = messageId,
            Reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason.Trim(),
            Payload = payload,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
