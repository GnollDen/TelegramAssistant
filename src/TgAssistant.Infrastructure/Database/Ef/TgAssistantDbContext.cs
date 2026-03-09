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
    public DbSet<DbEntityAlias> EntityAliases => Set<DbEntityAlias>();
    public DbSet<DbEntityMergeCandidate> EntityMergeCandidates => Set<DbEntityMergeCandidate>();
    public DbSet<DbEntityMergeDecision> EntityMergeDecisions => Set<DbEntityMergeDecision>();
    public DbSet<DbEntityMergeCommand> EntityMergeCommands => Set<DbEntityMergeCommand>();
    public DbSet<DbFactReviewCommand> FactReviewCommands => Set<DbFactReviewCommand>();
    public DbSet<DbFact> Facts => Set<DbFact>();
    public DbSet<DbRelationship> Relationships => Set<DbRelationship>();
    public DbSet<DbCommunicationEvent> CommunicationEvents => Set<DbCommunicationEvent>();
    public DbSet<DbDailySummary> DailySummaries => Set<DbDailySummary>();
    public DbSet<DbPromptTemplate> PromptTemplates => Set<DbPromptTemplate>();
    public DbSet<DbAnalysisState> AnalysisStates => Set<DbAnalysisState>();
    public DbSet<DbMessageExtraction> MessageExtractions => Set<DbMessageExtraction>();
    public DbSet<DbExtractionError> ExtractionErrors => Set<DbExtractionError>();
    public DbSet<DbStage5MetricsSnapshot> Stage5MetricsSnapshots => Set<DbStage5MetricsSnapshot>();
    public DbSet<DbAnalysisUsageEvent> AnalysisUsageEvents => Set<DbAnalysisUsageEvent>();
    public DbSet<DbTextEmbedding> TextEmbeddings => Set<DbTextEmbedding>();
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
            e.Property(x => x.MediaParalinguisticsJson).HasColumnName("media_paralinguistics_json").HasColumnType("jsonb");
            e.Property(x => x.ReplyToMessageId).HasColumnName("reply_to_message_id");
            e.Property(x => x.EditTimestamp).HasColumnName("edit_timestamp");
            e.Property(x => x.ReactionsJson).HasColumnName("reactions_json").HasColumnType("jsonb");
            e.Property(x => x.ForwardJson).HasColumnName("forward_json").HasColumnType("jsonb");
            e.Property(x => x.ProcessingStatus).HasColumnName("processing_status");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.Property(x => x.NeedsReanalysis).HasColumnName("needs_reanalysis");
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
            e.Property(x => x.ActorKey).HasColumnName("actor_key");
            e.Property(x => x.TelegramUserId).HasColumnName("telegram_user_id");
            e.Property(x => x.TelegramUsername).HasColumnName("telegram_username");
            e.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.ActorKey).IsUnique();
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

        modelBuilder.Entity<DbEntityAlias>(e =>
        {
            e.ToTable("entity_aliases");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.Alias).HasColumnName("alias");
            e.Property(x => x.AliasNorm).HasColumnName("alias_norm");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.EntityId, x.AliasNorm }).IsUnique();
            e.HasIndex(x => x.AliasNorm);
        });

        modelBuilder.Entity<DbEntityMergeCandidate>(e =>
        {
            e.ToTable("entity_merge_candidates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EntityLowId).HasColumnName("entity_low_id");
            e.Property(x => x.EntityHighId).HasColumnName("entity_high_id");
            e.Property(x => x.AliasNorm).HasColumnName("alias_norm");
            e.Property(x => x.EvidenceCount).HasColumnName("evidence_count");
            e.Property(x => x.Score).HasColumnName("score");
            e.Property(x => x.ReviewPriority).HasColumnName("review_priority");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.DecisionNote).HasColumnName("decision_note");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.EntityLowId, x.EntityHighId, x.AliasNorm }).IsUnique();
            e.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<DbEntityMergeDecision>(e =>
        {
            e.ToTable("entity_merge_decisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.EntityLowId).HasColumnName("entity_low_id");
            e.Property(x => x.EntityHighId).HasColumnName("entity_high_id");
            e.Property(x => x.AliasNorm).HasColumnName("alias_norm");
            e.Property(x => x.Decision).HasColumnName("decision");
            e.Property(x => x.Note).HasColumnName("note");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.CandidateId);
            e.HasIndex(x => new { x.EntityLowId, x.EntityHighId });
        });

        modelBuilder.Entity<DbEntityMergeCommand>(e =>
        {
            e.ToTable("entity_merge_commands");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CandidateId).HasColumnName("candidate_id");
            e.Property(x => x.Command).HasColumnName("command");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Error).HasColumnName("error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<DbFactReviewCommand>(e =>
        {
            e.ToTable("fact_review_commands");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FactId).HasColumnName("fact_id");
            e.Property(x => x.Command).HasColumnName("command");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Error).HasColumnName("error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
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

        modelBuilder.Entity<DbCommunicationEvent>(e =>
        {
            e.ToTable("communication_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.EventType).HasColumnName("event_type");
            e.Property(x => x.ObjectName).HasColumnName("object_name");
            e.Property(x => x.Sentiment).HasColumnName("sentiment");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.EntityId, x.CreatedAt });
            e.HasIndex(x => new { x.MessageId, x.EventType });
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
            e.Property(x => x.CheapJson).HasColumnName("cheap_json").HasColumnType("jsonb");
            e.Property(x => x.ExpensiveJson).HasColumnName("expensive_json").HasColumnType("jsonb");
            e.Property(x => x.NeedsExpensive).HasColumnName("needs_expensive");
            e.Property(x => x.ExpensiveRetryCount).HasColumnName("expensive_retry_count");
            e.Property(x => x.ExpensiveNextRetryAt).HasColumnName("expensive_next_retry_at");
            e.Property(x => x.ExpensiveLastError).HasColumnName("expensive_last_error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.MessageId).IsUnique();
        });

        modelBuilder.Entity<DbExtractionError>(e =>
        {
            e.ToTable("extraction_errors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Stage).HasColumnName("stage");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Payload).HasColumnName("payload");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => x.Stage);
        });

        modelBuilder.Entity<DbStage5MetricsSnapshot>(e =>
        {
            e.ToTable("stage5_metrics_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CapturedAt).HasColumnName("captured_at");
            e.Property(x => x.ProcessedMessages).HasColumnName("processed_messages");
            e.Property(x => x.ExtractionsTotal).HasColumnName("extractions_total");
            e.Property(x => x.ExpensiveBacklog).HasColumnName("expensive_backlog");
            e.Property(x => x.MergeCandidatesPending).HasColumnName("merge_candidates_pending");
            e.Property(x => x.FactReviewsPending).HasColumnName("fact_reviews_pending");
            e.Property(x => x.ExtractionErrors1h).HasColumnName("extraction_errors_1h");
            e.Property(x => x.AnalysisRequests1h).HasColumnName("analysis_requests_1h");
            e.Property(x => x.AnalysisTokens1h).HasColumnName("analysis_tokens_1h");
            e.Property(x => x.AnalysisCostUsd1h).HasColumnName("analysis_cost_usd_1h");
            e.HasIndex(x => x.CapturedAt);
        });

        modelBuilder.Entity<DbAnalysisUsageEvent>(e =>
        {
            e.ToTable("analysis_usage_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Phase).HasColumnName("phase");
            e.Property(x => x.Model).HasColumnName("model");
            e.Property(x => x.PromptTokens).HasColumnName("prompt_tokens");
            e.Property(x => x.CompletionTokens).HasColumnName("completion_tokens");
            e.Property(x => x.TotalTokens).HasColumnName("total_tokens");
            e.Property(x => x.CostUsd).HasColumnName("cost_usd");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.Phase, x.Model, x.CreatedAt });
        });

        modelBuilder.Entity<DbTextEmbedding>(e =>
        {
            e.ToTable("text_embeddings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OwnerType).HasColumnName("owner_type");
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.SourceText).HasColumnName("source_text");
            e.Property(x => x.Model).HasColumnName("model");
            e.Property(x => x.Vector).HasColumnName("vector").HasColumnType("real[]");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.OwnerType, x.OwnerId, x.Model });
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
