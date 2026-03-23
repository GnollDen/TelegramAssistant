using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ChatSessionRepository : IChatSessionRepository
{
    private const long SyntheticSmokeChatIdMin = 9_000_000_000_000L;
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
                LastMessageAt = session.LastMessageAt == default ? session.EndDate : session.LastMessageAt,
                Summary = session.Summary,
                IsFinalized = session.IsFinalized,
                IsAnalyzed = session.IsAnalyzed,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            var boundariesChanged = row.StartDate != session.StartDate
                                    || row.EndDate != session.EndDate
                                    || row.LastMessageAt != (session.LastMessageAt == default ? session.EndDate : session.LastMessageAt);
            var summaryChanged = !string.Equals(row.Summary ?? string.Empty, session.Summary ?? string.Empty, StringComparison.Ordinal);
            row.StartDate = session.StartDate;
            row.EndDate = session.EndDate;
            row.LastMessageAt = session.LastMessageAt == default ? session.EndDate : session.LastMessageAt;
            row.Summary = session.Summary ?? string.Empty;
            row.IsFinalized = row.IsFinalized || session.IsFinalized;
            row.IsAnalyzed = session.IsAnalyzed || (row.IsAnalyzed && !boundariesChanged && !summaryChanged);
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
                    LastMessageAt = x.LastMessageAt,
                    Summary = x.Summary,
                    IsFinalized = x.IsFinalized,
                    IsAnalyzed = x.IsAnalyzed,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                }).ToList();
        }

        return result;
    }

    public async Task<Dictionary<long, List<ChatSession>>> GetByPeriodAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var result = new Dictionary<long, List<ChatSession>>();
        if (toUtc < fromUtc)
        {
            return result;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .AsNoTracking()
            .Where(x => x.EndDate >= fromUtc && x.EndDate <= toUtc)
            .OrderBy(x => x.ChatId)
            .ThenBy(x => x.EndDate)
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
                    LastMessageAt = x.LastMessageAt,
                    Summary = x.Summary,
                    IsFinalized = x.IsFinalized,
                    IsAnalyzed = x.IsAnalyzed,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                }).ToList();
        }

        return result;
    }

    public async Task<List<ChatSession>> GetPendingAnalysisSessionsAsync(DateTime staleBeforeUtc, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .AsNoTracking()
            .Where(x => !x.IsAnalyzed
                        && !x.IsFinalized
                        && x.ChatId < SyntheticSmokeChatIdMin
                        && x.LastMessageAt <= staleBeforeUtc)
            .OrderBy(x => x.LastMessageAt)
            .ThenBy(x => x.ChatId)
            .ThenBy(x => x.SessionIndex)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        return rows.Select(x => new ChatSession
        {
            Id = x.Id,
            ChatId = x.ChatId,
            SessionIndex = x.SessionIndex,
            StartDate = x.StartDate,
            EndDate = x.EndDate,
            LastMessageAt = x.LastMessageAt,
            Summary = x.Summary,
            IsFinalized = x.IsFinalized,
            IsAnalyzed = x.IsAnalyzed,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }

    public async Task<Dictionary<long, List<ChatSession>>> GetPendingAggregationCandidatesAsync(DateTime staleBeforeUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .AsNoTracking()
            .Where(x => x.IsAnalyzed
                        && x.ChatId < SyntheticSmokeChatIdMin
                        && (x.LastMessageAt <= staleBeforeUtc || string.IsNullOrWhiteSpace(x.Summary)))
            .Where(x => !x.IsFinalized || string.IsNullOrWhiteSpace(x.Summary))
            .OrderBy(x => x.ChatId)
            .ThenBy(x => x.LastMessageAt)
            .ThenBy(x => x.SessionIndex)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ChatId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(row => new ChatSession
                {
                    Id = row.Id,
                    ChatId = row.ChatId,
                    SessionIndex = row.SessionIndex,
                    StartDate = row.StartDate,
                    EndDate = row.EndDate,
                    LastMessageAt = row.LastMessageAt,
                    Summary = row.Summary,
                    IsFinalized = row.IsFinalized,
                    IsAnalyzed = row.IsAnalyzed,
                    CreatedAt = row.CreatedAt,
                    UpdatedAt = row.UpdatedAt
                }).ToList());
    }

    public async Task MarkAnalyzedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .Where(x => sessionIds.Contains(x.Id))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsAnalyzed = true;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkNeedsAnalysisAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .Where(x => sessionIds.Contains(x.Id))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsAnalyzed = false;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkFinalizedAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatSessions
            .Where(x => sessionIds.Contains(x.Id))
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsFinalized = true;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<long> CountPendingAnalysisSessionsAsync(DateTime staleBeforeUtc, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ChatSessions
            .AsNoTracking()
            .LongCountAsync(
                x => !x.IsAnalyzed
                     && !x.IsFinalized
                     && x.ChatId < SyntheticSmokeChatIdMin
                     && x.LastMessageAt <= staleBeforeUtc,
                ct);
    }

    public async Task<bool> TryUpdateSummaryIfShapeUnchangedAsync(
        Guid sessionId,
        long chatId,
        int sessionIndex,
        DateTime expectedStartDate,
        DateTime expectedEndDate,
        DateTime expectedLastMessageAt,
        string summary,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ChatSessions.FirstOrDefaultAsync(
            x => x.Id == sessionId
                 && x.ChatId == chatId
                 && x.SessionIndex == sessionIndex,
            ct);
        if (row == null)
        {
            return false;
        }

        if (row.StartDate != expectedStartDate
            || row.EndDate != expectedEndDate
            || row.LastMessageAt != expectedLastMessageAt)
        {
            return false;
        }

        row.Summary = summary ?? string.Empty;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
