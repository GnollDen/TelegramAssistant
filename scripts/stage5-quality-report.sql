-- Stage5 extraction quality report.

-- 1) Global snapshot.
WITH x AS (
  SELECT
    me.id,
    me.message_id,
    me.needs_expensive,
    me.created_at,
    COALESCE(jsonb_array_length(me.cheap_json -> 'entities'), 0) AS entities_cnt,
    COALESCE(jsonb_array_length(me.cheap_json -> 'facts'), 0) AS facts_cnt,
    COALESCE(jsonb_array_length(me.cheap_json -> 'relationships'), 0) AS rel_cnt
  FROM message_extractions me
)
SELECT
  NOW() AS ts,
  COUNT(*) AS extractions_total,
  COUNT(*) FILTER (WHERE needs_expensive) AS expensive_requested,
  ROUND(100.0 * COUNT(*) FILTER (WHERE needs_expensive) / NULLIF(COUNT(*), 0), 2) AS expensive_requested_pct,
  COUNT(*) FILTER (WHERE entities_cnt = 0 AND facts_cnt = 0 AND rel_cnt = 0) AS empty_extractions,
  ROUND(100.0 * COUNT(*) FILTER (WHERE entities_cnt = 0 AND facts_cnt = 0 AND rel_cnt = 0) / NULLIF(COUNT(*), 0), 2) AS empty_extractions_pct,
  COUNT(*) FILTER (WHERE facts_cnt > 0) AS with_facts,
  ROUND(100.0 * COUNT(*) FILTER (WHERE facts_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS with_facts_pct
FROM x;

-- 2) Hourly trend for the last 24h.
WITH x AS (
  SELECT
    date_trunc('hour', me.created_at) AS h,
    me.needs_expensive,
    COALESCE(jsonb_array_length(me.cheap_json -> 'entities'), 0) AS entities_cnt,
    COALESCE(jsonb_array_length(me.cheap_json -> 'facts'), 0) AS facts_cnt,
    COALESCE(jsonb_array_length(me.cheap_json -> 'relationships'), 0) AS rel_cnt
  FROM message_extractions me
  WHERE me.created_at >= NOW() - INTERVAL '24 hours'
)
SELECT
  h,
  COUNT(*) AS total,
  COUNT(*) FILTER (WHERE needs_expensive) AS expensive_requested,
  COUNT(*) FILTER (WHERE entities_cnt = 0 AND facts_cnt = 0 AND rel_cnt = 0) AS empty_extractions,
  COUNT(*) FILTER (WHERE facts_cnt > 0) AS with_facts
FROM x
GROUP BY h
ORDER BY h DESC;

-- 3) Auto-review queue pressure (last 24h).
SELECT
  COUNT(*) AS review_commands_24h,
  COUNT(*) FILTER (WHERE status = 0) AS review_pending_24h,
  COUNT(*) FILTER (WHERE status = 1) AS review_done_24h,
  COUNT(*) FILTER (WHERE status = 2) AS review_failed_24h
FROM fact_review_commands
WHERE created_at >= NOW() - INTERVAL '24 hours';

-- 4) Top fact categories entering review queue (last 24h).
SELECT
  f.category,
  COUNT(*) AS cnt
FROM fact_review_commands c
JOIN facts f ON f.id = c.fact_id
WHERE c.created_at >= NOW() - INTERVAL '24 hours'
GROUP BY f.category
ORDER BY cnt DESC
LIMIT 20;

-- 5) Expensive-pass error summary by model (last 24h).
SELECT
  COALESCE(SUBSTRING(payload FROM 'model=([^;]+)'), 'unknown') AS model,
  COUNT(*) AS cnt
FROM extraction_errors
WHERE stage IN ('stage5_expensive_item', 'stage5_expensive_denied')
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY 1
ORDER BY cnt DESC;

-- 6) Analysis usage summary (last 24h).
SELECT
  phase,
  model,
  COUNT(*) AS requests,
  SUM(total_tokens) AS total_tokens,
  ROUND(SUM(cost_usd), 6) AS cost_usd
FROM analysis_usage_events
WHERE created_at >= NOW() - INTERVAL '24 hours'
GROUP BY phase, model
ORDER BY cost_usd DESC, total_tokens DESC;
