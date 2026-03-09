-- Requeue only low-signal extractions (Facts/Events/Relationships are empty).
-- This keeps useful rows intact and retries weak rows with current model/prompt.
--
-- Usage:
--   docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5-requeue-empty.sql

\set ON_ERROR_STOP on

BEGIN;

INSERT INTO analysis_state(key, value, updated_at)
VALUES ('stage5:watermark', 0, NOW())
ON CONFLICT (key) DO NOTHING;

WITH empty_rows AS (
    SELECT id, message_id
    FROM message_extractions
    WHERE COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Facts', cheap_json -> 'facts', '[]'::jsonb)), 0) = 0
      AND COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Events', cheap_json -> 'events', '[]'::jsonb)), 0) = 0
      AND COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Relationships', cheap_json -> 'relationships', '[]'::jsonb)), 0) = 0
),
deleted AS (
    DELETE FROM message_extractions me
    USING empty_rows er
    WHERE me.id = er.id
    RETURNING me.message_id
),
wm AS (
    SELECT COALESCE(MIN(message_id) - 1, 0) AS new_watermark
    FROM deleted
)
UPDATE analysis_state
SET value = (SELECT new_watermark FROM wm),
    updated_at = NOW()
WHERE key = 'stage5:watermark';

COMMIT;

SELECT
  (SELECT value FROM analysis_state WHERE key = 'stage5:watermark') AS watermark,
  (SELECT COUNT(*) FROM message_extractions) AS extractions_total,
  (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive) AS expensive_backlog;
