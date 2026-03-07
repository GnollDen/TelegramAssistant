using Microsoft.EntityFrameworkCore;

namespace TgAssistant.Infrastructure.Database.Ef;

public class TgAssistantDbContext : DbContext
{
    public TgAssistantDbContext(DbContextOptions<TgAssistantDbContext> options) : base(options)
    {
    }

    public DbSet<DbMessage> Messages => Set<DbMessage>();
    public DbSet<DbArchiveImportRun> ArchiveImportRuns => Set<DbArchiveImportRun>();
    public DbSet<DbEntity> Entities => Set<DbEntity>();
    public DbSet<DbFact> Facts => Set<DbFact>();
    public DbSet<DbRelationship> Relationships => Set<DbRelationship>();
    public DbSet<DbDailySummary> DailySummaries => Set<DbDailySummary>();
    public DbSet<DbPromptTemplate> PromptTemplates => Set<DbPromptTemplate>();
    public DbSet<DbAnalysisState> AnalysisStates => Set<DbAnalysisState>();
    public DbSet<DbMessageExtraction> MessageExtractions => Set<DbMessageExtraction>();
    public DbSet<DbStickerCache> StickerCache => Set<DbStickerCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DbMessage>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TelegramMessageId).HasColumnName("telegram_message_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.SenderId).HasColumnName("sender_id");
            e.Property(x => x.SenderName).HasColumnName("sender_name");
            e.Property(x => x.Timestamp).HasColumnName("timestamp");
            e.Property(x => x.Text).HasColumnName("text");
            e.Property(x => x.MediaType).HasColumnName("media_type");
            e.Property(x => x.MediaPath).HasColumnName("media_path");
            e.Property(x => x.MediaDescription).HasColumnName("media_description");
            e.Property(x => x.MediaTranscription).HasColumnName("media_transcription");
            e.Property(x => x.ReplyToMessageId).HasColumnName("reply_to_message_id");
            e.Property(x => x.EditTimestamp).HasColumnName("edit_timestamp");
            e.Property(x => x.ReactionsJson).HasColumnName("reactions_json");
            e.Property(x => x.ForwardJson).HasColumnName("forward_json");
            e.Property(x => x.ProcessingStatus).HasColumnName("processing_status");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<DbArchiveImportRun>(e =>
        {
            e.ToTable("archive_import_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SourcePath).HasColumnName("source_path");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.LastMessageIndex).HasColumnName("last_message_index");
            e.Property(x => x.ImportedMessages).HasColumnName("imported_messages");
            e.Property(x => x.QueuedMedia).HasColumnName("queued_media");
            e.Property(x => x.TotalMessages).HasColumnName("total_messages");
            e.Property(x => x.TotalMedia).HasColumnName("total_media");
            e.Property(x => x.EstimatedCostUsd).HasColumnName("estimated_cost_usd");
            e.Property(x => x.Error).HasColumnName("error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DbEntity>(e =>
        {
            e.ToTable("entities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Aliases).HasColumnName("aliases");
            e.Property(x => x.TelegramUserId).HasColumnName("telegram_user_id");
            e.Property(x => x.TelegramUsername).HasColumnName("telegram_username");
            e.Property(x => x.Metadata).HasColumnName("metadata");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DbFact>(e =>
        {
            e.ToTable("facts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Category).HasColumnName("category");
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Value).HasColumnName("value");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.ValidUntil).HasColumnName("valid_until");
            e.Property(x => x.IsCurrent).HasColumnName("is_current");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DbRelationship>(e =>
        {
            e.ToTable("relationships");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FromEntityId).HasColumnName("from_entity_id");
            e.Property(x => x.ToEntityId).HasColumnName("to_entity_id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.ContextText).HasColumnName("context_text");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DbDailySummary>(e =>
        {
            e.ToTable("daily_summaries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.MessageCount).HasColumnName("message_count");
            e.Property(x => x.MediaCount).HasColumnName("media_count");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<DbPromptTemplate>(e =>
        {
            e.ToTable("prompt_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.SystemPrompt).HasColumnName("system_prompt");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DbAnalysisState>(e =>
        {
            e.ToTable("analysis_state");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Value).HasColumnName("value");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DbMessageExtraction>(e =>
        {
            e.ToTable("message_extractions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.CheapJson).HasColumnName("cheap_json");
            e.Property(x => x.ExpensiveJson).HasColumnName("expensive_json");
            e.Property(x => x.NeedsExpensive).HasColumnName("needs_expensive");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.MessageId).IsUnique();
        });

        modelBuilder.Entity<DbStickerCache>(e =>
        {
            e.ToTable("sticker_cache");
            e.HasKey(x => x.ContentHash);
            e.Property(x => x.ContentHash).HasColumnName("content_hash");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Model).HasColumnName("model");
            e.Property(x => x.HitCount).HasColumnName("hit_count");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        });
    }
}
