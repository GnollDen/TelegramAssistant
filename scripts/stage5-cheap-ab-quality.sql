-- Stage5 cheap A/B quality report (proxy metrics by arm).
-- Arm split matches AnalysisWorkerService: bucket = abs(message_id % 100),
-- candidate arm if bucket < candidate_percent else baseline arm.
--
-- Usage example:
--   docker compose exec -T postgres psql -U tgassistant -d tgassistant \
--     -v candidate_percent=50 -v window_start=4096 -f scripts/stage5-cheap-ab-quality.sql
--
-- Defaults:
\set candidate_percent 50
\set window_start 0

WITH base AS (
    SELECT
        me.message_id,
        ABS((me.message_id % 100)) AS bucket,
        me.needs_expensive,
        me.expensive_json,
        me.cheap_json
    FROM message_extractions me
    WHERE me.message_id > :window_start
),
scored AS (
    SELECT
        CASE
            WHEN bucket < :candidate_percent THEN 'candidate'
            ELSE 'baseline'
        END AS arm,
        needs_expensive,
        (expensive_json IS NOT NULL) AS has_expensive_json,
        COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Facts', cheap_json -> 'facts', '[]'::jsonb)), 0) AS facts_cnt,
        COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Events', cheap_json -> 'events', '[]'::jsonb)), 0) AS events_cnt,
        COALESCE(jsonb_array_length(COALESCE(cheap_json -> 'Relationships', cheap_json -> 'relationships', '[]'::jsonb)), 0) AS rels_cnt
    FROM base
)
SELECT
    arm,
    COUNT(*) AS total,
    COUNT(*) FILTER (WHERE facts_cnt > 0) AS with_facts,
    COUNT(*) FILTER (WHERE events_cnt > 0) AS with_events,
    COUNT(*) FILTER (WHERE rels_cnt > 0) AS with_relationships,
    COUNT(*) FILTER (WHERE facts_cnt = 0 AND events_cnt = 0 AND rels_cnt = 0) AS empty_signal,
    COUNT(*) FILTER (WHERE needs_expensive) AS needs_expensive,
    COUNT(*) FILTER (WHERE has_expensive_json) AS expensive_resolved,
    ROUND(100.0 * COUNT(*) FILTER (WHERE facts_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS facts_rate_pct,
    ROUND(100.0 * COUNT(*) FILTER (WHERE events_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS events_rate_pct,
    ROUND(100.0 * COUNT(*) FILTER (WHERE rels_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS rels_rate_pct,
    ROUND(100.0 * COUNT(*) FILTER (WHERE facts_cnt = 0 AND events_cnt = 0 AND rels_cnt = 0) / NULLIF(COUNT(*), 0), 2) AS empty_rate_pct
FROM scored
GROUP BY arm
ORDER BY arm;
