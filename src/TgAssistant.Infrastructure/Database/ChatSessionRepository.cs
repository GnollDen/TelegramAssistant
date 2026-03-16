using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ChatSessionRepository : IChatSessionRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ChatSessionRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task UpsertAsync(ChatSession session, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var row = await db.ChatSessions.FirstOrDefaultAsync(
            x => x.ChatId == session.ChatId && x.SessionIndex == session.SessionIndex,
            ct);

        if (row == null)
        {
            db.ChatSessions.Add(new DbChatSession
            {
                Id = session.Id == Guid.Empty ? Guid.NewGuid() : session.Id,
                ChatId = session.ChatId,
                SessionIndex = session.SessionIndex,
                StartDate = session.StartDate,
                EndDate = session.EndDate,
                Summary = session.Summary,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.StartDate = session.StartDate;
            row.EndDate = session.EndDate;
            row.Summary = session.Summary;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<long, List<ChatSession>>> GetByChatsAsync(IReadOnlyCollection<long> chatIds, CancellationToken ct = default)
    {
        var result = new Dictionary<long, List<ChatSession>>();
        if (chatIds.Count == 0)
        {
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .AsNoTracking()
            .Where(x => chatIds.Contains(x.ChatId))
            .OrderBy(x => x.ChatId)
            .ThenBy(x => x.SessionIndex)
            .ToListAsync(ct);

        foreach (var group in rows.GroupBy(x => x.ChatId))
        {
            result[group.Key] = group.Select(x => new ChatSession
            {
                Id = x.Id,
                ChatId = x.ChatId,
                SessionIndex = x.SessionIndex,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Summary = x.Summary,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            }).ToList();
        }

        return result;
    }
}
