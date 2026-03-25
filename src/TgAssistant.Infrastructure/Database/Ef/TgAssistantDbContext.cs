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
    public DbSet<DbChatDialogSummary> ChatDialogSummaries => Set<DbChatDialogSummary>();
    public DbSet<DbChatSession> ChatSessions => Set<DbChatSession>();
    public DbSet<DbPromptTemplate> PromptTemplates => Set<DbPromptTemplate>();
    public DbSet<DbAnalysisState> AnalysisStates => Set<DbAnalysisState>();
    public DbSet<DbMessageExtraction> MessageExtractions => Set<DbMessageExtraction>();
    public DbSet<DbIntelligenceObservation> IntelligenceObservations => Set<DbIntelligenceObservation>();
    public DbSet<DbIntelligenceClaim> IntelligenceClaims => Set<DbIntelligenceClaim>();
    public DbSet<DbExtractionError> ExtractionErrors => Set<DbExtractionError>();
    public DbSet<DbStage5MetricsSnapshot> Stage5MetricsSnapshots => Set<DbStage5MetricsSnapshot>();
    public DbSet<DbAnalysisUsageEvent> AnalysisUsageEvents => Set<DbAnalysisUsageEvent>();
    public DbSet<DbTextEmbedding> TextEmbeddings => Set<DbTextEmbedding>();
    public DbSet<DbStickerCache> StickerCache => Set<DbStickerCache>();
    public DbSet<DbPeriod> Periods => Set<DbPeriod>();
    public DbSet<DbPeriodTransition> PeriodTransitions => Set<DbPeriodTransition>();
    public DbSet<DbHypothesis> Hypotheses => Set<DbHypothesis>();
    public DbSet<DbClarificationQuestion> ClarificationQuestions => Set<DbClarificationQuestion>();
    public DbSet<DbClarificationAnswer> ClarificationAnswers => Set<DbClarificationAnswer>();
    public DbSet<DbOfflineEvent> OfflineEvents => Set<DbOfflineEvent>();
    public DbSet<DbAudioAsset> AudioAssets => Set<DbAudioAsset>();
    public DbSet<DbAudioSegment> AudioSegments => Set<DbAudioSegment>();
    public DbSet<DbAudioSnippet> AudioSnippets => Set<DbAudioSnippet>();
    public DbSet<DbStateSnapshot> StateSnapshots => Set<DbStateSnapshot>();
    public DbSet<DbProfileSnapshot> ProfileSnapshots => Set<DbProfileSnapshot>();
    public DbSet<DbProfileTrait> ProfileTraits => Set<DbProfileTrait>();
    public DbSet<DbStrategyRecord> StrategyRecords => Set<DbStrategyRecord>();
    public DbSet<DbStrategyOption> StrategyOptions => Set<DbStrategyOption>();
    public DbSet<DbDraftRecord> DraftRecords => Set<DbDraftRecord>();
    public DbSet<DbDraftOutcome> DraftOutcomes => Set<DbDraftOutcome>();
    public DbSet<DbInboxItem> InboxItems => Set<DbInboxItem>();
    public DbSet<DbConflictRecord> ConflictRecords => Set<DbConflictRecord>();
    public DbSet<DbDependencyLink> DependencyLinks => Set<DbDependencyLink>();
    public DbSet<DbDomainReviewEvent> DomainReviewEvents => Set<DbDomainReviewEvent>();
    public DbSet<DbBudgetOperationalState> BudgetOperationalStates => Set<DbBudgetOperationalState>();
    public DbSet<DbChatCoordinationState> ChatCoordinationStates => Set<DbChatCoordinationState>();
    public DbSet<DbChatPhaseGuard> ChatPhaseGuards => Set<DbChatPhaseGuard>();
    public DbSet<DbBackupEvidenceRecord> BackupEvidenceRecords => Set<DbBackupEvidenceRecord>();
    public DbSet<DbEvalRun> EvalRuns => Set<DbEvalRun>();
    public DbSet<DbEvalScenarioResult> EvalScenarioResults => Set<DbEvalScenarioResult>();
    public DbSet<DbExternalArchiveImportBatch> ExternalArchiveImportBatches => Set<DbExternalArchiveImportBatch>();
    public DbSet<DbExternalArchiveImportRecord> ExternalArchiveImportRecords => Set<DbExternalArchiveImportRecord>();
    public DbSet<DbExternalArchiveLinkageArtifact> ExternalArchiveLinkageArtifacts => Set<DbExternalArchiveLinkageArtifact>();

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
            e.HasIndex(x => new { x.ChatId, x.TelegramMessageId }).IsUnique();
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
            e.Property(x => x.IsUserConfirmed).HasColumnName("is_user_confirmed");
            e.Property(x => x.TrustFactor).HasColumnName("trust_factor");
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
            e.Property(x => x.DecayClass).HasColumnName("decay_class");
            e.Property(x => x.IsUserConfirmed).HasColumnName("is_user_confirmed");
            e.Property(x => x.TrustFactor).HasColumnName("trust_factor");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.DecayClass, x.IsCurrent });
            e.HasIndex(x => new { x.EntityId, x.Category, x.Key, x.Value })
                .IsUnique()
                .HasFilter("is_current = TRUE");
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
            e.HasIndex(x => new { x.FromEntityId, x.ToEntityId, x.Type }).IsUnique();
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

        modelBuilder.Entity<DbChatDialogSummary>(e =>
        {
            e.ToTable("chat_dialog_summaries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.SummaryType).HasColumnName("summary_type");
            e.Property(x => x.PeriodStart).HasColumnName("period_start");
            e.Property(x => x.PeriodEnd).HasColumnName("period_end");
            e.Property(x => x.StartMessageId).HasColumnName("start_message_id");
            e.Property(x => x.EndMessageId).HasColumnName("end_message_id");
            e.Property(x => x.MessageCount).HasColumnName("message_count");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.IsFinalized).HasColumnName("is_finalized");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.ChatId, x.SummaryType, x.PeriodStart, x.PeriodEnd }).IsUnique();
            e.HasIndex(x => new { x.ChatId, x.UpdatedAt });
        });

        modelBuilder.Entity<DbChatSession>(e =>
        {
            e.ToTable("chat_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.SessionIndex).HasColumnName("session_index");
            e.Property(x => x.StartDate).HasColumnName("start_date");
            e.Property(x => x.EndDate).HasColumnName("end_date");
            e.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.IsFinalized).HasColumnName("is_finalized");
            e.Property(x => x.IsAnalyzed).HasColumnName("is_analyzed");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.ChatId, x.SessionIndex }).IsUnique();
            e.HasIndex(x => new { x.ChatId, x.EndDate });
            e.HasIndex(x => new { x.IsFinalized, x.LastMessageAt });
            e.HasIndex(x => new { x.IsAnalyzed, x.IsFinalized, x.LastMessageAt });
        });

        modelBuilder.Entity<DbPromptTemplate>(e =>
        {
            e.ToTable("prompt_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.Checksum).HasColumnName("checksum");
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
            e.Property(x => x.IsQuarantined).HasColumnName("is_quarantined");
            e.Property(x => x.QuarantineReason).HasColumnName("quarantine_reason");
            e.Property(x => x.QuarantinedAt).HasColumnName("quarantined_at");
            e.Property(x => x.ExpensiveRetryCount).HasColumnName("expensive_retry_count");
            e.Property(x => x.ExpensiveNextRetryAt).HasColumnName("expensive_next_retry_at");
            e.Property(x => x.ExpensiveLastError).HasColumnName("expensive_last_error");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.MessageId).IsUnique();
            e.HasIndex(x => x.IsQuarantined);
        });

        modelBuilder.Entity<DbIntelligenceObservation>(e =>
        {
            e.ToTable("intelligence_observations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.SubjectName).HasColumnName("subject_name");
            e.Property(x => x.ObservationType).HasColumnName("observation_type");
            e.Property(x => x.ObjectName).HasColumnName("object_name");
            e.Property(x => x.Value).HasColumnName("value");
            e.Property(x => x.Evidence).HasColumnName("evidence");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => new { x.EntityId, x.CreatedAt });
        });

        modelBuilder.Entity<DbIntelligenceClaim>(e =>
        {
            e.ToTable("intelligence_claims");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MessageId).HasColumnName("message_id");
            e.Property(x => x.EntityId).HasColumnName("entity_id");
            e.Property(x => x.EntityName).HasColumnName("entity_name");
            e.Property(x => x.ClaimType).HasColumnName("claim_type");
            e.Property(x => x.Category).HasColumnName("category");
            e.Property(x => x.Key).HasColumnName("key");
            e.Property(x => x.Value).HasColumnName("value");
            e.Property(x => x.Evidence).HasColumnName("evidence");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => new { x.EntityId, x.Category, x.Key });
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
            e.Property(x => x.PendingSessionsQueue).HasColumnName("pending_sessions_queue");
            e.Property(x => x.ReanalysisBacklog).HasColumnName("reanalysis_backlog");
            e.Property(x => x.QuarantineTotal).HasColumnName("quarantine_total");
            e.Property(x => x.QuarantineStuck).HasColumnName("quarantine_stuck");
            e.Property(x => x.DuplicateMessageBusinessKeyGroups).HasColumnName("duplicate_message_business_key_groups");
            e.Property(x => x.DuplicateMessageBusinessKeyRows).HasColumnName("duplicate_message_business_key_rows");
            e.Property(x => x.DuplicateMessageBusinessKeyRowRate).HasColumnName("duplicate_message_business_key_row_rate");
            e.Property(x => x.ProcessedWithoutExtraction).HasColumnName("processed_without_extraction");
            e.Property(x => x.ProcessedWithoutApplyEvidenceCount).HasColumnName("processed_without_apply_evidence_count");
            e.Property(x => x.ProcessedWithoutApplyEvidenceRate).HasColumnName("processed_without_apply_evidence_rate");
            e.Property(x => x.WatermarkRegressionBlocked1h).HasColumnName("watermark_regression_blocked_1h");
            e.Property(x => x.WatermarkMonotonicRegressionCount).HasColumnName("watermark_monotonic_regression_count");
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

        modelBuilder.Entity<DbPeriod>(e =>
        {
            e.ToTable("domain_periods");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.Label).HasColumnName("label");
            e.Property(x => x.CustomLabel).HasColumnName("custom_label");
            e.Property(x => x.StartAt).HasColumnName("start_at");
            e.Property(x => x.EndAt).HasColumnName("end_at");
            e.Property(x => x.IsOpen).HasColumnName("is_open");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.KeySignalsJson).HasColumnName("key_signals_json").HasColumnType("jsonb");
            e.Property(x => x.WhatHelped).HasColumnName("what_helped");
            e.Property(x => x.WhatHurt).HasColumnName("what_hurt");
            e.Property(x => x.OpenQuestionsCount).HasColumnName("open_questions_count");
            e.Property(x => x.BoundaryConfidence).HasColumnName("boundary_confidence");
            e.Property(x => x.InterpretationConfidence).HasColumnName("interpretation_confidence");
            e.Property(x => x.ReviewPriority).HasColumnName("review_priority");
            e.Property(x => x.IsSensitive).HasColumnName("is_sensitive");
            e.Property(x => x.StatusSnapshot).HasColumnName("status_snapshot");
            e.Property(x => x.DynamicSnapshot).HasColumnName("dynamic_snapshot");
            e.Property(x => x.Lessons).HasColumnName("lessons");
            e.Property(x => x.StrategicPatterns).HasColumnName("strategic_patterns");
            e.Property(x => x.ManualNotes).HasColumnName("manual_notes");
            e.Property(x => x.UserOverrideSummary).HasColumnName("user_override_summary");
            e.Property(x => x.SourceType).HasColumnName("source_type");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.StartAt });
            e.HasIndex(x => new { x.CaseId, x.IsOpen });
        });

        modelBuilder.Entity<DbPeriodTransition>(e =>
        {
            e.ToTable("domain_period_transitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.FromPeriodId).HasColumnName("from_period_id");
            e.Property(x => x.ToPeriodId).HasColumnName("to_period_id");
            e.Property(x => x.TransitionType).HasColumnName("transition_type");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.IsResolved).HasColumnName("is_resolved");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.GapId).HasColumnName("gap_id");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.SourceType).HasColumnName("source_type");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.FromPeriodId);
            e.HasIndex(x => x.ToPeriodId);
            e.HasIndex(x => x.IsResolved);
        });

        modelBuilder.Entity<DbHypothesis>(e =>
        {
            e.ToTable("domain_hypotheses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.HypothesisType).HasColumnName("hypothesis_type");
            e.Property(x => x.SubjectType).HasColumnName("subject_type");
            e.Property(x => x.SubjectId).HasColumnName("subject_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.Statement).HasColumnName("statement");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.SourceType).HasColumnName("source_type");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.ConflictRefsJson).HasColumnName("conflict_refs_json").HasColumnType("jsonb");
            e.Property(x => x.ValidationTargetsJson).HasColumnName("validation_targets_json").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.Status });
            e.HasIndex(x => x.PeriodId);
        });

        modelBuilder.Entity<DbClarificationQuestion>(e =>
        {
            e.ToTable("domain_clarification_questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.QuestionText).HasColumnName("question_text");
            e.Property(x => x.QuestionType).HasColumnName("question_type");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.RelatedHypothesisId).HasColumnName("related_hypothesis_id");
            e.Property(x => x.AffectedOutputsJson).HasColumnName("affected_outputs_json").HasColumnType("jsonb");
            e.Property(x => x.WhyItMatters).HasColumnName("why_it_matters");
            e.Property(x => x.ExpectedGain).HasColumnName("expected_gain");
            e.Property(x => x.AnswerOptionsJson).HasColumnName("answer_options_json").HasColumnType("jsonb");
            e.Property(x => x.SourceType).HasColumnName("source_type");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.Status, x.Priority });
            e.HasIndex(x => x.PeriodId);
        });

        modelBuilder.Entity<DbClarificationAnswer>(e =>
        {
            e.ToTable("domain_clarification_answers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.QuestionId).HasColumnName("question_id");
            e.Property(x => x.AnswerType).HasColumnName("answer_type");
            e.Property(x => x.AnswerValue).HasColumnName("answer_value");
            e.Property(x => x.AnswerConfidence).HasColumnName("answer_confidence");
            e.Property(x => x.SourceClass).HasColumnName("source_class");
            e.Property(x => x.AffectedObjectsJson).HasColumnName("affected_objects_json").HasColumnType("jsonb");
            e.Property(x => x.SourceType).HasColumnName("source_type");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.QuestionId);
        });

        modelBuilder.Entity<DbOfflineEvent>(e =>
        {
            e.ToTable("domain_offline_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.EventType).HasColumnName("event_type");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.UserSummary).HasColumnName("user_summary");
            e.Property(x => x.AutoSummary).HasColumnName("auto_summary");
            e.Property(x => x.TimestampStart).HasColumnName("timestamp_start");
            e.Property(x => x.TimestampEnd).HasColumnName("timestamp_end");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.ReviewStatus).HasColumnName("review_status");
            e.Property(x => x.ImpactSummary).HasColumnName("impact_summary");
            e.Property(x => x.SourceType).HasColumnName("source_type");
            e.Property(x => x.SourceId).HasColumnName("source_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.TimestampStart });
            e.HasIndex(x => new { x.CaseId, x.ReviewStatus });
        });

        modelBuilder.Entity<DbAudioAsset>(e =>
        {
            e.ToTable("domain_audio_assets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.OfflineEventId).HasColumnName("offline_event_id");
            e.Property(x => x.FilePath).HasColumnName("file_path");
            e.Property(x => x.DurationSeconds).HasColumnName("duration_seconds");
            e.Property(x => x.TranscriptStatus).HasColumnName("transcript_status");
            e.Property(x => x.TranscriptText).HasColumnName("transcript_text");
            e.Property(x => x.SpeakerReviewStatus).HasColumnName("speaker_review_status");
            e.Property(x => x.ProcessingStatus).HasColumnName("processing_status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.OfflineEventId);
        });

        modelBuilder.Entity<DbAudioSegment>(e =>
        {
            e.ToTable("domain_audio_segments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AudioAssetId).HasColumnName("audio_asset_id");
            e.Property(x => x.SegmentIndex).HasColumnName("segment_index");
            e.Property(x => x.StartSeconds).HasColumnName("start_seconds").HasColumnType("numeric(10,3)");
            e.Property(x => x.EndSeconds).HasColumnName("end_seconds").HasColumnType("numeric(10,3)");
            e.Property(x => x.SpeakerLabel).HasColumnName("speaker_label");
            e.Property(x => x.TranscriptText).HasColumnName("transcript_text");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.AudioAssetId, x.SegmentIndex }).IsUnique();
        });

        modelBuilder.Entity<DbAudioSnippet>(e =>
        {
            e.ToTable("domain_audio_snippets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.AudioAssetId).HasColumnName("audio_asset_id");
            e.Property(x => x.AudioSegmentId).HasColumnName("audio_segment_id");
            e.Property(x => x.SnippetType).HasColumnName("snippet_type");
            e.Property(x => x.Text).HasColumnName("text");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.AudioAssetId);
            e.HasIndex(x => x.AudioSegmentId);
        });

        modelBuilder.Entity<DbStateSnapshot>(e =>
        {
            e.ToTable("domain_state_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.AsOf).HasColumnName("as_of");
            e.Property(x => x.DynamicLabel).HasColumnName("dynamic_label");
            e.Property(x => x.RelationshipStatus).HasColumnName("relationship_status");
            e.Property(x => x.AlternativeStatus).HasColumnName("alternative_status");
            e.Property(x => x.InitiativeScore).HasColumnName("initiative_score");
            e.Property(x => x.ResponsivenessScore).HasColumnName("responsiveness_score");
            e.Property(x => x.OpennessScore).HasColumnName("openness_score");
            e.Property(x => x.WarmthScore).HasColumnName("warmth_score");
            e.Property(x => x.ReciprocityScore).HasColumnName("reciprocity_score");
            e.Property(x => x.AmbiguityScore).HasColumnName("ambiguity_score");
            e.Property(x => x.AvoidanceRiskScore).HasColumnName("avoidance_risk_score");
            e.Property(x => x.EscalationReadinessScore).HasColumnName("escalation_readiness_score");
            e.Property(x => x.ExternalPressureScore).HasColumnName("external_pressure_score");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.KeySignalRefsJson).HasColumnName("key_signal_refs_json").HasColumnType("jsonb");
            e.Property(x => x.RiskRefsJson).HasColumnName("risk_refs_json").HasColumnType("jsonb");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.CaseId, x.AsOf });
        });

        modelBuilder.Entity<DbProfileSnapshot>(e =>
        {
            e.ToTable("domain_profile_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SubjectType).HasColumnName("subject_type");
            e.Property(x => x.SubjectId).HasColumnName("subject_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.Stability).HasColumnName("stability");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.CaseId, x.SubjectType, x.SubjectId, x.CreatedAt });
        });

        modelBuilder.Entity<DbProfileTrait>(e =>
        {
            e.ToTable("domain_profile_traits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ProfileSnapshotId).HasColumnName("profile_snapshot_id");
            e.Property(x => x.TraitKey).HasColumnName("trait_key");
            e.Property(x => x.ValueLabel).HasColumnName("value_label");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.Stability).HasColumnName("stability");
            e.Property(x => x.IsSensitive).HasColumnName("is_sensitive");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.ProfileSnapshotId, x.TraitKey });
        });

        modelBuilder.Entity<DbStrategyRecord>(e =>
        {
            e.ToTable("domain_strategy_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.StateSnapshotId).HasColumnName("state_snapshot_id");
            e.Property(x => x.StrategyConfidence).HasColumnName("strategy_confidence");
            e.Property(x => x.RecommendedGoal).HasColumnName("recommended_goal");
            e.Property(x => x.WhyNotOthers).HasColumnName("why_not_others");
            e.Property(x => x.MicroStep).HasColumnName("micro_step");
            e.Property(x => x.HorizonJson).HasColumnName("horizon_json").HasColumnType("jsonb");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.CaseId, x.CreatedAt });
        });

        modelBuilder.Entity<DbStrategyOption>(e =>
        {
            e.ToTable("domain_strategy_options");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StrategyRecordId).HasColumnName("strategy_record_id");
            e.Property(x => x.ActionType).HasColumnName("action_type");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.Purpose).HasColumnName("purpose");
            e.Property(x => x.Risk).HasColumnName("risk");
            e.Property(x => x.WhenToUse).HasColumnName("when_to_use");
            e.Property(x => x.SuccessSigns).HasColumnName("success_signs");
            e.Property(x => x.FailureSigns).HasColumnName("failure_signs");
            e.Property(x => x.IsPrimary).HasColumnName("is_primary");
            e.HasIndex(x => x.StrategyRecordId);
        });

        modelBuilder.Entity<DbDraftRecord>(e =>
        {
            e.ToTable("domain_draft_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.StrategyRecordId).HasColumnName("strategy_record_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.MainDraft).HasColumnName("main_draft");
            e.Property(x => x.AltDraft1).HasColumnName("alt_draft_1");
            e.Property(x => x.AltDraft2).HasColumnName("alt_draft_2");
            e.Property(x => x.StyleNotes).HasColumnName("style_notes");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.StrategyRecordId);
        });

        modelBuilder.Entity<DbDraftOutcome>(e =>
        {
            e.ToTable("domain_draft_outcomes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DraftId).HasColumnName("draft_id");
            e.Property(x => x.StrategyRecordId).HasColumnName("strategy_record_id");
            e.Property(x => x.ActualMessageId).HasColumnName("actual_message_id");
            e.Property(x => x.FollowUpMessageId).HasColumnName("follow_up_message_id");
            e.Property(x => x.MatchedBy).HasColumnName("matched_by");
            e.Property(x => x.MatchScore).HasColumnName("match_score");
            e.Property(x => x.OutcomeLabel).HasColumnName("outcome_label");
            e.Property(x => x.UserOutcomeLabel).HasColumnName("user_outcome_label");
            e.Property(x => x.SystemOutcomeLabel).HasColumnName("system_outcome_label");
            e.Property(x => x.OutcomeConfidence).HasColumnName("outcome_confidence");
            e.Property(x => x.LearningSignalsJson).HasColumnName("learning_signals_json").HasColumnType("jsonb");
            e.Property(x => x.Notes).HasColumnName("notes");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.DraftId);
            e.HasIndex(x => x.StrategyRecordId);
        });

        modelBuilder.Entity<DbInboxItem>(e =>
        {
            e.ToTable("domain_inbox_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ItemType).HasColumnName("item_type");
            e.Property(x => x.SourceObjectType).HasColumnName("source_object_type");
            e.Property(x => x.SourceObjectId).HasColumnName("source_object_id");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.IsBlocking).HasColumnName("is_blocking");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.LastActor).HasColumnName("last_actor");
            e.Property(x => x.LastReason).HasColumnName("last_reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.Status, x.Priority });
            e.HasIndex(x => new { x.CaseId, x.ItemType, x.SourceObjectType, x.SourceObjectId }).IsUnique();
        });

        modelBuilder.Entity<DbConflictRecord>(e =>
        {
            e.ToTable("domain_conflict_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ConflictType).HasColumnName("conflict_type");
            e.Property(x => x.ObjectAType).HasColumnName("object_a_type");
            e.Property(x => x.ObjectAId).HasColumnName("object_a_id");
            e.Property(x => x.ObjectBType).HasColumnName("object_b_type");
            e.Property(x => x.ObjectBId).HasColumnName("object_b_id");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.Severity).HasColumnName("severity");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.PeriodId).HasColumnName("period_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.LastActor).HasColumnName("last_actor");
            e.Property(x => x.LastReason).HasColumnName("last_reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.Status, x.Severity });
        });

        modelBuilder.Entity<DbDependencyLink>(e =>
        {
            e.ToTable("domain_dependency_links");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UpstreamType).HasColumnName("upstream_type");
            e.Property(x => x.UpstreamId).HasColumnName("upstream_id");
            e.Property(x => x.DownstreamType).HasColumnName("downstream_type");
            e.Property(x => x.DownstreamId).HasColumnName("downstream_id");
            e.Property(x => x.LinkType).HasColumnName("link_type");
            e.Property(x => x.LinkReason).HasColumnName("link_reason");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.UpstreamType, x.UpstreamId });
            e.HasIndex(x => new { x.DownstreamType, x.DownstreamId });
        });

        modelBuilder.Entity<DbDomainReviewEvent>(e =>
        {
            e.ToTable("domain_review_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ObjectType).HasColumnName("object_type");
            e.Property(x => x.ObjectId).HasColumnName("object_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.OldValueRef).HasColumnName("old_value_ref");
            e.Property(x => x.NewValueRef).HasColumnName("new_value_ref");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.ObjectType, x.ObjectId, x.CreatedAt });
        });

        modelBuilder.Entity<DbBudgetOperationalState>(e =>
        {
            e.ToTable("ops_budget_operational_states");
            e.HasKey(x => x.PathKey);
            e.Property(x => x.PathKey).HasColumnName("path_key");
            e.Property(x => x.Modality).HasColumnName("modality");
            e.Property(x => x.State).HasColumnName("state");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.DetailsJson).HasColumnName("details_json").HasColumnType("jsonb");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.State, x.UpdatedAt });
        });

        modelBuilder.Entity<DbChatCoordinationState>(e =>
        {
            e.ToTable("ops_chat_coordination_states");
            e.HasKey(x => x.ChatId);
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.State).HasColumnName("state");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.LastBackfillStartedAt).HasColumnName("last_backfill_started_at");
            e.Property(x => x.LastBackfillCompletedAt).HasColumnName("last_backfill_completed_at");
            e.Property(x => x.HandoverReadyAt).HasColumnName("handover_ready_at");
            e.Property(x => x.RealtimeActivatedAt).HasColumnName("realtime_activated_at");
            e.Property(x => x.LastListenerSeenAt).HasColumnName("last_listener_seen_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.State, x.UpdatedAt });
        });

        modelBuilder.Entity<DbChatPhaseGuard>(e =>
        {
            e.ToTable("ops_chat_phase_guards");
            e.HasKey(x => x.ChatId);
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.ActivePhase).HasColumnName("active_phase");
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.PhaseReason).HasColumnName("phase_reason");
            e.Property(x => x.ActiveSince).HasColumnName("active_since");
            e.Property(x => x.LeaseExpiresAt).HasColumnName("lease_expires_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.LastRequestedPhase).HasColumnName("last_requested_phase");
            e.Property(x => x.LastObservedPhase).HasColumnName("last_observed_phase");
            e.Property(x => x.LastDenyCode).HasColumnName("last_deny_code");
            e.Property(x => x.LastDenyReason).HasColumnName("last_deny_reason");
            e.Property(x => x.LastDeniedAt).HasColumnName("last_denied_at");
            e.Property(x => x.LastRecoveryAt).HasColumnName("last_recovery_at");
            e.Property(x => x.LastRecoveryFromOwnerId).HasColumnName("last_recovery_from_owner_id");
            e.Property(x => x.LastRecoveryCode).HasColumnName("last_recovery_code");
            e.Property(x => x.LastRecoveryReason).HasColumnName("last_recovery_reason");
            e.Property(x => x.TailReopenWindowFromUtc).HasColumnName("tail_reopen_window_from_utc");
            e.Property(x => x.TailReopenWindowToUtc).HasColumnName("tail_reopen_window_to_utc");
            e.Property(x => x.TailReopenOperator).HasColumnName("tail_reopen_operator");
            e.Property(x => x.TailReopenAuditId).HasColumnName("tail_reopen_audit_id");
            e.HasIndex(x => new { x.ActivePhase, x.UpdatedAt });
        });

        modelBuilder.Entity<DbBackupEvidenceRecord>(e =>
        {
            e.ToTable("ops_backup_evidence_records");
            e.HasKey(x => x.BackupId);
            e.Property(x => x.BackupId).HasColumnName("backup_id");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.Property(x => x.Scope).HasColumnName("scope");
            e.Property(x => x.ArtifactUri).HasColumnName("artifact_uri");
            e.Property(x => x.Checksum).HasColumnName("checksum");
            e.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc");
            e.Property(x => x.RecordedBy).HasColumnName("recorded_by");
            e.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            e.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<DbEvalRun>(e =>
        {
            e.ToTable("ops_eval_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RunName).HasColumnName("run_name");
            e.Property(x => x.Passed).HasColumnName("passed");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.MetricsJson).HasColumnName("metrics_json").HasColumnType("jsonb");
            e.HasIndex(x => new { x.RunName, x.StartedAt });
        });

        modelBuilder.Entity<DbEvalScenarioResult>(e =>
        {
            e.ToTable("ops_eval_scenario_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RunId).HasColumnName("run_id");
            e.Property(x => x.ScenarioName).HasColumnName("scenario_name");
            e.Property(x => x.Passed).HasColumnName("passed");
            e.Property(x => x.Summary).HasColumnName("summary");
            e.Property(x => x.MetricsJson).HasColumnName("metrics_json").HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.RunId, x.CreatedAt });
        });

        modelBuilder.Entity<DbExternalArchiveImportBatch>(e =>
        {
            e.ToTable("external_archive_import_batches");
            e.HasKey(x => x.RunId);
            e.Property(x => x.RunId).HasColumnName("run_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.SourceClass).HasColumnName("source_class");
            e.Property(x => x.SourceRef).HasColumnName("source_ref");
            e.Property(x => x.ImportBatchId).HasColumnName("import_batch_id");
            e.Property(x => x.RequestPayloadHash).HasColumnName("request_payload_hash");
            e.Property(x => x.ImportedAtUtc).HasColumnName("imported_at_utc");
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.RecordCount).HasColumnName("record_count");
            e.Property(x => x.AcceptedCount).HasColumnName("accepted_count");
            e.Property(x => x.ReplayedCount).HasColumnName("replayed_count");
            e.Property(x => x.RejectedCount).HasColumnName("rejected_count");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.CaseId, x.SourceClass, x.SourceRef, x.RequestPayloadHash }).IsUnique();
            e.HasIndex(x => new { x.CaseId, x.CreatedAt });
        });

        modelBuilder.Entity<DbExternalArchiveImportRecord>(e =>
        {
            e.ToTable("external_archive_import_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RunId).HasColumnName("run_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.SourceClass).HasColumnName("source_class");
            e.Property(x => x.SourceRef).HasColumnName("source_ref");
            e.Property(x => x.ImportBatchId).HasColumnName("import_batch_id");
            e.Property(x => x.RecordId).HasColumnName("record_id");
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
            e.Property(x => x.RecordType).HasColumnName("record_type");
            e.Property(x => x.Text).HasColumnName("text");
            e.Property(x => x.SubjectActorKey).HasColumnName("subject_actor_key");
            e.Property(x => x.TargetActorKey).HasColumnName("target_actor_key");
            e.Property(x => x.ChatId).HasColumnName("chat_id");
            e.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
            e.Property(x => x.SourceSessionId).HasColumnName("source_session_id");
            e.Property(x => x.Confidence).HasColumnName("confidence");
            e.Property(x => x.RawPayloadJson).HasColumnName("raw_payload_json").HasColumnType("jsonb");
            e.Property(x => x.EvidenceRefsJson).HasColumnName("evidence_refs_json").HasColumnType("jsonb");
            e.Property(x => x.TruthLayer).HasColumnName("truth_layer");
            e.Property(x => x.PayloadHash).HasColumnName("payload_hash");
            e.Property(x => x.BaseWeight).HasColumnName("base_weight");
            e.Property(x => x.ConfidenceMultiplier).HasColumnName("confidence_multiplier");
            e.Property(x => x.CorroborationMultiplier).HasColumnName("corroboration_multiplier");
            e.Property(x => x.FinalWeight).HasColumnName("final_weight");
            e.Property(x => x.NeedsClarification).HasColumnName("needs_clarification");
            e.Property(x => x.WeightingReason).HasColumnName("weighting_reason");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.RunId, x.RecordId }).IsUnique();
            e.HasIndex(x => new { x.CaseId, x.SourceClass, x.SourceRef, x.RecordId });
        });

        modelBuilder.Entity<DbExternalArchiveLinkageArtifact>(e =>
        {
            e.ToTable("external_archive_linkage_artifacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RunId).HasColumnName("run_id");
            e.Property(x => x.RecordRowId).HasColumnName("record_row_id");
            e.Property(x => x.CaseId).HasColumnName("case_id");
            e.Property(x => x.LinkType).HasColumnName("link_type");
            e.Property(x => x.TargetType).HasColumnName("target_type");
            e.Property(x => x.TargetId).HasColumnName("target_id");
            e.Property(x => x.LinkConfidence).HasColumnName("link_confidence");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.ReviewStatus).HasColumnName("review_status");
            e.Property(x => x.AutoApplyAllowed).HasColumnName("auto_apply_allowed");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.RecordRowId, x.LinkType, x.TargetType, x.TargetId }).IsUnique();
            e.HasIndex(x => new { x.CaseId, x.ReviewStatus, x.CreatedAt });
        });
    }
}
