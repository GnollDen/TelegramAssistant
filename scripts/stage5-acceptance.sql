-- Stage5 acceptance check (technical health after import/run).
-- Usage:
--   docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5-acceptance.sql

SELECT NOW() AS ts;

SELECT COUNT(*) AS messages_total FROM messages;
SELECT COUNT(*) AS extractions_total FROM message_extractions;

SELECT key, value, updated_at
FROM analysis_state
WHERE key IN ('stage5:watermark', 'stage5:entity_embedding_watermark_ms')
ORDER BY key;

SELECT processing_status, COUNT(*) AS cnt
FROM messages
GROUP BY processing_status
ORDER BY processing_status;

SELECT
  COUNT(*) FILTER (WHERE needs_expensive) AS expensive_backlog,
  COUNT(*) FILTER (WHERE needs_expensive AND (expensive_next_retry_at IS NULL OR expensive_next_retry_at <= NOW())) AS expensive_ready_now,
  COUNT(*) FILTER (WHERE needs_expensive AND expensive_next_retry_at > NOW()) AS expensive_delayed
FROM message_extractions;

SELECT COUNT(*) AS extraction_errors_24h
FROM extraction_errors
WHERE created_at >= NOW() - INTERVAL '24 hours';

SELECT
  COUNT(*) FILTER (WHERE cheap_json IS NULL) AS null_cheap_json,
  COUNT(*) FILTER (
    WHERE COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Facts', cheap_json -> 'facts', '[]'::jsonb)), 0) > 0
  ) AS with_facts,
  COUNT(*) FILTER (
    WHERE COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Events', cheap_json -> 'events', '[]'::jsonb)), 0) > 0
  ) AS with_events,
  COUNT(*) FILTER (
    WHERE COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Relationships', cheap_json -> 'relationships', '[]'::jsonb)), 0) > 0
  ) AS with_relationships
FROM message_extractions;

SELECT
  captured_at,
  processed_messages,
  extractions_total,
  expensive_backlog,
  analysis_requests_1h,
  analysis_tokens_1h,
  analysis_cost_usd_1h
FROM stage5_metrics_snapshots
ORDER BY captured_at DESC
LIMIT 10;
