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
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
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
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt
        }).ToList();
    }
}
