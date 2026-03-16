using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ChatDialogSummaryRepository : IChatDialogSummaryRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ChatDialogSummaryRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Upserts summary row by chat, type and period bounds.
    /// </summary>
    public async Task UpsertAsync(ChatDialogSummary summary, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await UpsertInternalAsync(db, summary, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertAndFinalizeSessionsAsync(ChatDialogSummary summary, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await UpsertInternalAsync(db, summary, ct);

        if (sessionIds.Count > 0)
        {
            var rows = await db.ChatSessions
                .Where(x => sessionIds.Contains(x.Id))
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var row in rows)
            {
                row.IsFinalized = true;
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task UpsertInternalAsync(TgAssistantDbContext db, ChatDialogSummary summary, CancellationToken ct)
    {
        var row = await db.ChatDialogSummaries.FirstOrDefaultAsync(
            x => x.ChatId == summary.ChatId
                 && x.SummaryType == (short)summary.SummaryType
                 && x.PeriodStart == summary.PeriodStart
                 && x.PeriodEnd == summary.PeriodEnd,
            ct);

        if (row == null)
        {
            db.ChatDialogSummaries.Add(new DbChatDialogSummary
            {
                Id = summary.Id == Guid.Empty ? Guid.NewGuid() : summary.Id,
                ChatId = summary.ChatId,
                SummaryType = (short)summary.SummaryType,
                PeriodStart = summary.PeriodStart,
                PeriodEnd = summary.PeriodEnd,
                StartMessageId = summary.StartMessageId,
                EndMessageId = summary.EndMessageId,
                MessageCount = summary.MessageCount,
                Summary = summary.Summary,
                IsFinalized = summary.IsFinalized,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.StartMessageId = summary.StartMessageId;
            row.EndMessageId = summary.EndMessageId;
            row.MessageCount = summary.MessageCount;
            row.Summary = summary.Summary;
            row.IsFinalized = summary.IsFinalized;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<ChatDialogSummary?> GetByScopeAsync(
        long chatId,
        ChatDialogSummaryType summaryType,
        DateTime periodStart,
        DateTime periodEnd,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var row = await db.ChatDialogSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ChatId == chatId
                     && x.SummaryType == (short)summaryType
                     && x.PeriodStart == periodStart
                     && x.PeriodEnd == periodEnd,
                ct);
        if (row == null)
        {
            return null;
        }

        return new ChatDialogSummary
        {
            Id = row.Id,
            ChatId = row.ChatId,
            SummaryType = (ChatDialogSummaryType)row.SummaryType,
            PeriodStart = row.PeriodStart,
            PeriodEnd = row.PeriodEnd,
            StartMessageId = row.StartMessageId,
            EndMessageId = row.EndMessageId,
            MessageCount = row.MessageCount,
            Summary = row.Summary,
            IsFinalized = row.IsFinalized,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    /// <summary>
    /// Returns latest summaries for the chat ordered by period end descending.
    /// </summary>
    public async Task<List<ChatDialogSummary>> GetRecentByChatAsync(long chatId, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatDialogSummaries
            .AsNoTracking()
            .Where(x => x.ChatId == chatId)
            .OrderByDescending(x => x.PeriodEnd)
            .Take(Math.Max(1, limit))
            .ToListAsync(ct);

        return rows.Select(x => new ChatDialogSummary
        {
            Id = x.Id,
            ChatId = x.ChatId,
            SummaryType = (ChatDialogSummaryType)x.SummaryType,
            PeriodStart = x.PeriodStart,
            PeriodEnd = x.PeriodEnd,
            StartMessageId = x.StartMessageId,
            EndMessageId = x.EndMessageId,
            MessageCount = x.MessageCount,
            Summary = x.Summary,
            IsFinalized = x.IsFinalized,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }
}
