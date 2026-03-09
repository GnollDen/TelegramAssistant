-- Stage5 requeue helper.
-- Usage examples:
--   psql ... -v MODE=all -f scripts/stage5-requeue.sql
--   psql ... -v MODE=window -v HOURS=24 -f scripts/stage5-requeue.sql
--   psql ... -v MODE=watermark_only -v WATERMARK=70000 -f scripts/stage5-requeue.sql
--
-- MODE:
--   all            -> clear all message_extractions + reset watermark to 0
--   window         -> clear recent message_extractions for processed messages in last :HOURS (default 24), set watermark accordingly
--   watermark_only -> only set stage5:watermark to :WATERMARK

\set ON_ERROR_STOP on

\if :{?MODE}
\else
\set MODE 'window'
\endif

\if :{?HOURS}
\else
\set HOURS 24
\endif

\if :{?WATERMARK}
\else
\set WATERMARK 0
\endif

BEGIN;

-- Always normalize watermark row existence.
INSERT INTO analysis_state(key, value, updated_at)
VALUES ('stage5:watermark', 0, NOW())
ON CONFLICT (key) DO NOTHING;

\if :MODE = 'all'
    DELETE FROM message_extractions;
    UPDATE analysis_state
       SET value = 0,
           updated_at = NOW()
     WHERE key = 'stage5:watermark';
\elif :MODE = 'window'
    WITH recent AS (
        SELECT id
        FROM messages
        WHERE processing_status = 1
          AND "timestamp" >= NOW() - (:'HOURS' || ' hours')::interval
    ),
    deleted AS (
        DELETE FROM message_extractions
        WHERE message_id IN (SELECT id FROM recent)
        RETURNING message_id
    ),
    wm AS (
        SELECT COALESCE(MIN(message_id) - 1, 0) AS new_wm
        FROM deleted
    )
    UPDATE analysis_state
       SET value = (SELECT new_wm FROM wm),
           updated_at = NOW()
     WHERE key = 'stage5:watermark';
\elif :MODE = 'watermark_only'
    UPDATE analysis_state
       SET value = GREATEST(0, :'WATERMARK'::bigint),
           updated_at = NOW()
     WHERE key = 'stage5:watermark';
\else
    ROLLBACK;
    \echo 'Unknown MODE. Use: all | window | watermark_only'
    \quit 1
\endif

COMMIT;

-- Final verification snapshot.
SELECT
  (SELECT value FROM analysis_state WHERE key = 'stage5:watermark') AS watermark,
  (SELECT COUNT(*) FROM message_extractions) AS extractions_total,
  (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive) AS expensive_backlog;
