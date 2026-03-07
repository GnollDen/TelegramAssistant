using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class PromptTemplateRepository : IPromptTemplateRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public PromptTemplateRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PromptTemplate?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.PromptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row == null)
        {
            return null;
        }

        return new PromptTemplate
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            SystemPrompt = row.SystemPrompt,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    public async Task<PromptTemplate> UpsertAsync(PromptTemplate template, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var existing = await db.PromptTemplates.FirstOrDefaultAsync(x => x.Id == template.Id, ct);
        if (existing == null)
        {
            db.PromptTemplates.Add(new DbPromptTemplate
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                SystemPrompt = template.SystemPrompt,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.Name = template.Name;
            existing.Description = template.Description;
            existing.SystemPrompt = template.SystemPrompt;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        template.UpdatedAt = now;
        if (template.CreatedAt == default)
        {
            template.CreatedAt = now;
        }

        return template;
    }
}
