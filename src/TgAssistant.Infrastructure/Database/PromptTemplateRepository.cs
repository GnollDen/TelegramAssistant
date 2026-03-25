using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Core.Prompts;
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
            Version = row.Version,
            Checksum = row.Checksum,
            SystemPrompt = row.SystemPrompt,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    public async Task<PromptTemplate> UpsertAsync(PromptTemplate template, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var normalizedPrompt = PromptTemplateChecksum.Normalize(template.SystemPrompt);
        var normalizedVersion = string.IsNullOrWhiteSpace(template.Version) ? "v1" : template.Version.Trim();
        var normalizedChecksum = string.IsNullOrWhiteSpace(template.Checksum)
            ? PromptTemplateChecksum.Compute(normalizedPrompt)
            : template.Checksum.Trim().ToUpperInvariant();

        var existing = await db.PromptTemplates.FirstOrDefaultAsync(x => x.Id == template.Id, ct);
        if (existing == null)
        {
            db.PromptTemplates.Add(new DbPromptTemplate
            {
                Id = template.Id,
                Name = template.Name,
                Description = template.Description,
                Version = normalizedVersion,
                Checksum = normalizedChecksum,
                SystemPrompt = normalizedPrompt,
                CreatedAt = now,
                UpdatedAt = now
            });
            template.CreatedAt = now;
            template.UpdatedAt = now;
        }
        else
        {
            var existingVersion = string.IsNullOrWhiteSpace(existing.Version) ? "v1" : existing.Version;
            var existingChecksum = string.IsNullOrWhiteSpace(existing.Checksum)
                ? PromptTemplateChecksum.Compute(existing.SystemPrompt)
                : existing.Checksum;
            var changed = !string.Equals(existing.Name, template.Name, StringComparison.Ordinal) ||
                          !string.Equals(existing.Description, template.Description, StringComparison.Ordinal) ||
                          !string.Equals(existingVersion, normalizedVersion, StringComparison.Ordinal) ||
                          !string.Equals(existingChecksum, normalizedChecksum, StringComparison.Ordinal) ||
                          !string.Equals(existing.SystemPrompt, normalizedPrompt, StringComparison.Ordinal);
            if (!changed)
            {
                template.CreatedAt = existing.CreatedAt;
                template.UpdatedAt = existing.UpdatedAt;
                template.Version = existingVersion;
                template.Checksum = existingChecksum;
                template.SystemPrompt = existing.SystemPrompt;
                return template;
            }

            existing.Name = template.Name;
            existing.Description = template.Description;
            existing.Version = normalizedVersion;
            existing.Checksum = normalizedChecksum;
            existing.SystemPrompt = normalizedPrompt;
            existing.UpdatedAt = now;
            template.CreatedAt = existing.CreatedAt;
            template.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        template.Version = normalizedVersion;
        template.Checksum = normalizedChecksum;
        template.SystemPrompt = normalizedPrompt;

        return template;
    }
}
