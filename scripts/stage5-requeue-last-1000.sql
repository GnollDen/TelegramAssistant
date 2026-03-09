-- Requeue only the latest 1000 messages for Stage5 quality verification.
-- Safe for production: limits reprocessing window to the tail of the dataset.
--
-- Usage:
--   docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5-requeue-last-1000.sql

\set ON_ERROR_STOP on

BEGIN;

INSERT INTO analysis_state(key, value, updated_at)
VALUES ('stage5:watermark', 0, NOW())
ON CONFLICT (key) DO NOTHING;

WITH bounds AS (
    SELECT
        MAX(id) AS max_id,
        GREATEST(MAX(id) - 1000, 0) AS lower_bound
    FROM messages
),
deleted AS (
    DELETE FROM message_extractions me
    USING bounds b
    WHERE me.message_id > b.lower_bound
    RETURNING me.message_id
)
UPDATE analysis_state s
SET value = b.lower_bound,
    updated_at = NOW()
FROM bounds b
WHERE s.key = 'stage5:watermark';

COMMIT;

WITH bounds AS (
    SELECT
        MAX(id) AS max_id,
        GREATEST(MAX(id) - 1000, 0) AS lower_bound
    FROM messages
)
SELECT
    b.lower_bound AS watermark_start,
    b.max_id AS max_message_id,
    (SELECT COUNT(*) FROM message_extractions) AS extractions_total,
    (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive) AS expensive_backlog
FROM bounds b;
