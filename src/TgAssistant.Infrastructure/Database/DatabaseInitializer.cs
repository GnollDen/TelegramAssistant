using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Infrastructure.Database;

public class DatabaseInitializer
{
    private readonly DatabaseSettings _settings;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IOptions<DatabaseSettings> settings, ILogger<DatabaseInitializer> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_settings.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Database schema initialized");
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS messages (
            id BIGSERIAL PRIMARY KEY,
            telegram_message_id BIGINT NOT NULL,
            chat_id BIGINT NOT NULL,
            sender_id BIGINT NOT NULL,
            sender_name TEXT NOT NULL DEFAULT '',
            timestamp TIMESTAMPTZ NOT NULL,
            text TEXT,
            media_type SMALLINT NOT NULL DEFAULT 0,
            media_path TEXT,
            media_transcription TEXT,
            media_description TEXT,
            reply_to_message_id BIGINT,
            edit_timestamp TIMESTAMPTZ,
            reactions_json TEXT,
            forward_json TEXT,
            source SMALLINT NOT NULL DEFAULT 0,
            processing_status SMALLINT NOT NULL DEFAULT 0,
            processed_at TIMESTAMPTZ,
            needs_reanalysis BOOLEAN NOT NULL DEFAULT FALSE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_messages_chat_timestamp ON messages(chat_id, timestamp);
        CREATE INDEX IF NOT EXISTS idx_messages_processing ON messages(processing_status) WHERE processing_status = 0;
        CREATE INDEX IF NOT EXISTS idx_messages_sender ON messages(sender_id, timestamp);
        CREATE INDEX IF NOT EXISTS idx_messages_needs_reanalysis ON messages(needs_reanalysis) WHERE needs_reanalysis = TRUE;
        -- Deduplicate before enforcing uniqueness to avoid startup failures on existing data.
        DELETE FROM messages m
        USING messages d
        WHERE m.id > d.id
          AND m.source = d.source
          AND m.chat_id = d.chat_id
          AND m.telegram_message_id = d.telegram_message_id;
        CREATE UNIQUE INDEX IF NOT EXISTS uq_messages_source_chat_tg_message ON messages(source, chat_id, telegram_message_id);

        CREATE TABLE IF NOT EXISTS entities (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            type SMALLINT NOT NULL,
            name TEXT NOT NULL,
            aliases TEXT[] DEFAULT '{}',
            actor_key TEXT,
            telegram_user_id BIGINT,
            telegram_username TEXT,
            metadata JSONB DEFAULT '{}',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entities_telegram_user ON entities(telegram_user_id) WHERE telegram_user_id IS NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entities_actor_key ON entities(actor_key) WHERE actor_key IS NOT NULL;

        CREATE TABLE IF NOT EXISTS entity_aliases (
            id BIGSERIAL PRIMARY KEY,
            entity_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
            alias TEXT NOT NULL,
            alias_norm TEXT NOT NULL,
            source_message_id BIGINT,
            confidence REAL NOT NULL DEFAULT 1.0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entity_aliases_unique ON entity_aliases(entity_id, alias_norm);
        CREATE INDEX IF NOT EXISTS idx_entity_aliases_norm ON entity_aliases(alias_norm);

        CREATE TABLE IF NOT EXISTS entity_merge_candidates (
            id BIGSERIAL PRIMARY KEY,
            entity_low_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
            entity_high_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
            alias_norm TEXT NOT NULL,
            evidence_count INT NOT NULL DEFAULT 1,
            score REAL NOT NULL DEFAULT 0,
            review_priority SMALLINT NOT NULL DEFAULT 1,
            status SMALLINT NOT NULL DEFAULT 0,
            decision_note TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CHECK (entity_low_id <> entity_high_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entity_merge_candidates_unique ON entity_merge_candidates(entity_low_id, entity_high_id, alias_norm);
        CREATE INDEX IF NOT EXISTS idx_entity_merge_candidates_status ON entity_merge_candidates(status, updated_at DESC);

        CREATE TABLE IF NOT EXISTS entity_merge_decisions (
            id BIGSERIAL PRIMARY KEY,
            candidate_id BIGINT,
            entity_low_id UUID NOT NULL,
            entity_high_id UUID NOT NULL,
            alias_norm TEXT NOT NULL,
            decision SMALLINT NOT NULL,
            note TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_entity_merge_decisions_candidate ON entity_merge_decisions(candidate_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_entity_merge_decisions_pair ON entity_merge_decisions(entity_low_id, entity_high_id, created_at DESC);

        CREATE TABLE IF NOT EXISTS entity_merge_commands (
            id BIGSERIAL PRIMARY KEY,
            candidate_id BIGINT NOT NULL REFERENCES entity_merge_candidates(id) ON DELETE CASCADE,
            command TEXT NOT NULL,
            reason TEXT,
            status SMALLINT NOT NULL DEFAULT 0,
            error TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            processed_at TIMESTAMPTZ
        );
        CREATE INDEX IF NOT EXISTS idx_entity_merge_commands_status ON entity_merge_commands(status, created_at);

        CREATE TABLE IF NOT EXISTS relationships (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            from_entity_id UUID NOT NULL REFERENCES entities(id),
            to_entity_id UUID NOT NULL REFERENCES entities(id),
            type TEXT NOT NULL,
            status SMALLINT NOT NULL DEFAULT 2,
            confidence REAL NOT NULL DEFAULT 0.5,
            context_text TEXT,
            source_message_id BIGINT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_relationships_from ON relationships(from_entity_id);
        CREATE INDEX IF NOT EXISTS idx_relationships_to ON relationships(to_entity_id);

        CREATE TABLE IF NOT EXISTS facts (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            entity_id UUID NOT NULL REFERENCES entities(id),
            category TEXT NOT NULL,
            key TEXT NOT NULL,
            value TEXT NOT NULL,
            status SMALLINT NOT NULL DEFAULT 2,
            confidence REAL NOT NULL DEFAULT 0.5,
            source_message_id BIGINT,
            valid_from TIMESTAMPTZ,
            valid_until TIMESTAMPTZ,
            is_current BOOLEAN NOT NULL DEFAULT TRUE,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_facts_entity_current ON facts(entity_id) WHERE is_current = TRUE;

        CREATE TABLE IF NOT EXISTS daily_summaries (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            chat_id BIGINT NOT NULL,
            entity_id UUID REFERENCES entities(id),
            date DATE NOT NULL,
            summary TEXT NOT NULL,
            message_count INT NOT NULL DEFAULT 0,
            media_count INT NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_summaries_chat_date ON daily_summaries(chat_id, date);

        CREATE TABLE IF NOT EXISTS analysis_sessions (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            entity_id UUID NOT NULL REFERENCES entities(id),
            phase SMALLINT NOT NULL DEFAULT 0,
            status SMALLINT NOT NULL DEFAULT 0,
            prompt_template_id TEXT NOT NULL DEFAULT '',
            messages_json TEXT NOT NULL DEFAULT '[]',
            final_report TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS prompt_templates (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT,
            system_prompt TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS sticker_cache (
            content_hash TEXT PRIMARY KEY,
            description TEXT NOT NULL,
            model TEXT NOT NULL,
            hit_count BIGINT NOT NULL DEFAULT 1,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            last_used_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS archive_import_runs (
            id UUID PRIMARY KEY,
            source_path TEXT NOT NULL,
            status SMALLINT NOT NULL DEFAULT 0,
            last_message_index INT NOT NULL DEFAULT -1,
            imported_messages BIGINT NOT NULL DEFAULT 0,
            queued_media BIGINT NOT NULL DEFAULT 0,
            total_messages BIGINT NOT NULL DEFAULT 0,
            total_media BIGINT NOT NULL DEFAULT 0,
            estimated_cost_usd NUMERIC(12,4) NOT NULL DEFAULT 0,
            error TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_archive_import_runs_source ON archive_import_runs(source_path, created_at DESC);
        CREATE TABLE IF NOT EXISTS analysis_state (
            key TEXT PRIMARY KEY,
            value BIGINT NOT NULL,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS message_extractions (
            id BIGSERIAL PRIMARY KEY,
            message_id BIGINT NOT NULL UNIQUE REFERENCES messages(id) ON DELETE CASCADE,
            cheap_json JSONB NOT NULL DEFAULT '{}'::jsonb,
            expensive_json JSONB,
            needs_expensive BOOLEAN NOT NULL DEFAULT FALSE,
            expensive_retry_count INT NOT NULL DEFAULT 0,
            expensive_next_retry_at TIMESTAMPTZ,
            expensive_last_error TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_message_extractions_needs_expensive ON message_extractions(needs_expensive) WHERE needs_expensive = TRUE;
        CREATE INDEX IF NOT EXISTS idx_message_extractions_retry_due ON message_extractions(needs_expensive, expensive_next_retry_at);
        CREATE INDEX IF NOT EXISTS idx_messages_processed_by_id ON messages(id) WHERE processing_status = 1;
        DO $$ BEGIN
            ALTER TABLE message_extractions ADD COLUMN expensive_retry_count INT NOT NULL DEFAULT 0;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE message_extractions ADD COLUMN expensive_next_retry_at TIMESTAMPTZ;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE message_extractions ADD COLUMN expensive_last_error TEXT;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        CREATE INDEX IF NOT EXISTS idx_message_extractions_retry_due ON message_extractions(needs_expensive, expensive_next_retry_at);

        CREATE TABLE IF NOT EXISTS extraction_errors (
            id BIGSERIAL PRIMARY KEY,
            stage TEXT NOT NULL,
            message_id BIGINT,
            reason TEXT NOT NULL,
            payload TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_extraction_errors_created ON extraction_errors(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_extraction_errors_message ON extraction_errors(message_id);
        CREATE INDEX IF NOT EXISTS idx_extraction_errors_stage ON extraction_errors(stage);

        CREATE TABLE IF NOT EXISTS stage5_metrics_snapshots (
            id BIGSERIAL PRIMARY KEY,
            captured_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            processed_messages BIGINT NOT NULL DEFAULT 0,
            extractions_total BIGINT NOT NULL DEFAULT 0,
            expensive_backlog BIGINT NOT NULL DEFAULT 0,
            merge_candidates_pending BIGINT NOT NULL DEFAULT 0,
            fact_reviews_pending BIGINT NOT NULL DEFAULT 0,
            extraction_errors_1h BIGINT NOT NULL DEFAULT 0,
            analysis_requests_1h BIGINT NOT NULL DEFAULT 0,
            analysis_tokens_1h BIGINT NOT NULL DEFAULT 0,
            analysis_cost_usd_1h NUMERIC(12,6) NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_stage5_metrics_snapshots_captured ON stage5_metrics_snapshots(captured_at DESC);
        DO $$ BEGIN
            ALTER TABLE stage5_metrics_snapshots ADD COLUMN fact_reviews_pending BIGINT NOT NULL DEFAULT 0;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE stage5_metrics_snapshots ADD COLUMN analysis_requests_1h BIGINT NOT NULL DEFAULT 0;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE stage5_metrics_snapshots ADD COLUMN analysis_tokens_1h BIGINT NOT NULL DEFAULT 0;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE stage5_metrics_snapshots ADD COLUMN analysis_cost_usd_1h NUMERIC(12,6) NOT NULL DEFAULT 0;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;

        CREATE TABLE IF NOT EXISTS analysis_usage_events (
            id BIGSERIAL PRIMARY KEY,
            phase TEXT NOT NULL,
            model TEXT NOT NULL,
            prompt_tokens INT NOT NULL DEFAULT 0,
            completion_tokens INT NOT NULL DEFAULT 0,
            total_tokens INT NOT NULL DEFAULT 0,
            cost_usd NUMERIC(12,6) NOT NULL DEFAULT 0,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_analysis_usage_events_created ON analysis_usage_events(created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_analysis_usage_events_phase_model_created ON analysis_usage_events(phase, model, created_at DESC);

        -- Add forward_json if missing (migration for existing DB)
        DO $$ BEGIN
            ALTER TABLE messages ADD COLUMN forward_json TEXT;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE messages ADD COLUMN needs_reanalysis BOOLEAN NOT NULL DEFAULT FALSE;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        CREATE INDEX IF NOT EXISTS idx_messages_needs_reanalysis ON messages(needs_reanalysis) WHERE needs_reanalysis = TRUE;

        -- Add actor_key if missing (migration for existing DB)
        DO $$ BEGIN
            ALTER TABLE entities ADD COLUMN actor_key TEXT;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entities_actor_key ON entities(actor_key) WHERE actor_key IS NOT NULL;

        -- Backward migration for entity_aliases columns (if table was created manually)
        DO $$ BEGIN
            ALTER TABLE entity_aliases ADD COLUMN alias_norm TEXT;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        UPDATE entity_aliases SET alias_norm = LOWER(TRIM(alias)) WHERE alias_norm IS NULL;
        ALTER TABLE entity_aliases ALTER COLUMN alias_norm SET NOT NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entity_aliases_unique ON entity_aliases(entity_id, alias_norm);
        CREATE INDEX IF NOT EXISTS idx_entity_aliases_norm ON entity_aliases(alias_norm);

        -- Backward migration for entity_merge_candidates
        CREATE TABLE IF NOT EXISTS entity_merge_candidates (
            id BIGSERIAL PRIMARY KEY,
            entity_low_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
            entity_high_id UUID NOT NULL REFERENCES entities(id) ON DELETE CASCADE,
            alias_norm TEXT NOT NULL,
            evidence_count INT NOT NULL DEFAULT 1,
            score REAL NOT NULL DEFAULT 0,
            review_priority SMALLINT NOT NULL DEFAULT 1,
            status SMALLINT NOT NULL DEFAULT 0,
            decision_note TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CHECK (entity_low_id <> entity_high_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entity_merge_candidates_unique ON entity_merge_candidates(entity_low_id, entity_high_id, alias_norm);
        CREATE INDEX IF NOT EXISTS idx_entity_merge_candidates_status ON entity_merge_candidates(status, updated_at DESC);
        DO $$ BEGIN
            ALTER TABLE entity_merge_candidates ADD COLUMN score REAL NOT NULL DEFAULT 0;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE entity_merge_candidates ADD COLUMN review_priority SMALLINT NOT NULL DEFAULT 1;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;

        CREATE TABLE IF NOT EXISTS entity_merge_decisions (
            id BIGSERIAL PRIMARY KEY,
            candidate_id BIGINT,
            entity_low_id UUID,
            entity_high_id UUID,
            alias_norm TEXT,
            decision SMALLINT NOT NULL,
            note TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_entity_merge_decisions_candidate ON entity_merge_decisions(candidate_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS idx_entity_merge_decisions_pair ON entity_merge_decisions(entity_low_id, entity_high_id, created_at DESC);

        CREATE TABLE IF NOT EXISTS entity_merge_commands (
            id BIGSERIAL PRIMARY KEY,
            candidate_id BIGINT NOT NULL REFERENCES entity_merge_candidates(id) ON DELETE CASCADE,
            command TEXT NOT NULL,
            reason TEXT,
            status SMALLINT NOT NULL DEFAULT 0,
            error TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            processed_at TIMESTAMPTZ
        );
        CREATE INDEX IF NOT EXISTS idx_entity_merge_commands_status ON entity_merge_commands(status, created_at);

        CREATE TABLE IF NOT EXISTS fact_review_commands (
            id BIGSERIAL PRIMARY KEY,
            fact_id UUID NOT NULL REFERENCES facts(id) ON DELETE CASCADE,
            command TEXT NOT NULL,
            reason TEXT,
            status SMALLINT NOT NULL DEFAULT 0,
            error TEXT,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            processed_at TIMESTAMPTZ
        );
        CREATE INDEX IF NOT EXISTS idx_fact_review_commands_status ON fact_review_commands(status, created_at);

        DO $$ BEGIN
            ALTER TABLE entity_merge_decisions ADD COLUMN entity_low_id UUID;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE entity_merge_decisions ADD COLUMN entity_high_id UUID;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        DO $$ BEGIN
            ALTER TABLE entity_merge_decisions ADD COLUMN alias_norm TEXT;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        """;
}

