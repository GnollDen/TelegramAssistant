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
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_messages_chat_timestamp ON messages(chat_id, timestamp);
        CREATE INDEX IF NOT EXISTS idx_messages_processing ON messages(processing_status) WHERE processing_status = 0;
        CREATE INDEX IF NOT EXISTS idx_messages_sender ON messages(sender_id, timestamp);
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
            telegram_user_id BIGINT,
            telegram_username TEXT,
            metadata JSONB DEFAULT '{}',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_entities_telegram_user ON entities(telegram_user_id) WHERE telegram_user_id IS NOT NULL;

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

        -- Add forward_json if missing (migration for existing DB)
        DO $$ BEGIN
            ALTER TABLE messages ADD COLUMN forward_json TEXT;
        EXCEPTION WHEN duplicate_column THEN NULL;
        END $$;
        """;
}
