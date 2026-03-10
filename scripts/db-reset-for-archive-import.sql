\set ON_ERROR_STOP on

BEGIN;

-- Keep schema_migrations and prompt_templates intact.
TRUNCATE TABLE
    fact_review_commands,
    entity_merge_commands,
    entity_merge_decisions,
    entity_merge_candidates,
    intelligence_claims,
    intelligence_observations,
    relationships,
    communication_events,
    facts,
    entity_aliases,
    entities,
    daily_summaries,
    analysis_sessions,
    archive_import_runs,
    analysis_state,
    message_extractions,
    extraction_errors,
    stage5_metrics_snapshots,
    analysis_usage_events,
    text_embeddings,
    sticker_cache,
    messages
RESTART IDENTITY CASCADE;

COMMIT;

SELECT
    (SELECT COUNT(*) FROM messages) AS messages_total,
    (SELECT COUNT(*) FROM message_extractions) AS message_extractions_total,
    (SELECT COUNT(*) FROM intelligence_observations) AS intelligence_observations_total,
    (SELECT COUNT(*) FROM intelligence_claims) AS intelligence_claims_total,
    (SELECT COUNT(*) FROM facts) AS facts_total,
    (SELECT COUNT(*) FROM communication_events) AS communication_events_total,
    (SELECT COUNT(*) FROM relationships) AS relationships_total,
    (SELECT COUNT(*) FROM archive_import_runs) AS archive_import_runs_total;
