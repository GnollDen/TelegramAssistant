using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Infrastructure.Database;

/// <summary>
/// Creates database schema on startup. Simple migration approach for MVP.
/// </summary>
public class DatabaseInitializer
{
    private readonly DatabaseSettings _settings;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        IOptions<DatabaseSettings> settings,
        ILogger<DatabaseInitializer> logger)
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
        -- Messages
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

        CREATE INDEX IF NOT EXISTS idx_messages_chat_timestamp 
            ON messages(chat_id, timestamp);
        CREATE INDEX IF NOT EXISTS idx_messages_processing 
            ON messages(processing_status) WHERE processing_status = 0;
        CREATE INDEX IF NOT EXISTS idx_messages_sender 
            ON messages(sender_id, timestamp);

        -- Entities (knowledge graph nodes)
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

        CREATE UNIQUE INDEX IF NOT EXISTS idx_entities_telegram_user 
            ON entities(telegram_user_id) WHERE telegram_user_id IS NOT NULL;

        -- Relationships (knowledge graph edges)
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

        CREATE INDEX IF NOT EXISTS idx_relationships_from 
            ON relationships(from_entity_id);
        CREATE INDEX IF NOT EXISTS idx_relationships_to 
            ON relationships(to_entity_id);

        -- Facts
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

        CREATE INDEX IF NOT EXISTS idx_facts_entity_current 
            ON facts(entity_id) WHERE is_current = TRUE;
        CREATE INDEX IF NOT EXISTS idx_facts_entity_category 
            ON facts(entity_id, category);

        -- Daily summaries
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

        CREATE UNIQUE INDEX IF NOT EXISTS idx_summaries_chat_date 
            ON daily_summaries(chat_id, date);

        -- Analysis sessions
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

        -- Prompt templates
        CREATE TABLE IF NOT EXISTS prompt_templates (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT,
            system_prompt TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;
}
