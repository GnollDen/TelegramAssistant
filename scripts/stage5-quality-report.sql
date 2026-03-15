-- Stage5 extraction quality report.

-- 1) Global snapshot.
WITH x AS (
  SELECT
    me.id,
    me.message_id,
    me.needs_expensive,
    me.created_at,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Entities', me.cheap_json -> 'entities', '[]'::jsonb)), 0) AS entities_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Facts', me.cheap_json -> 'facts', '[]'::jsonb)), 0) AS facts_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Claims', me.cheap_json -> 'claims', '[]'::jsonb)), 0) AS claims_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Observations', me.cheap_json -> 'observations', '[]'::jsonb)), 0) AS observations_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Events', me.cheap_json -> 'events', '[]'::jsonb)), 0) AS events_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Relationships', me.cheap_json -> 'relationships', '[]'::jsonb)), 0) AS rel_cnt
  FROM message_extractions me
)
SELECT
  NOW() AS ts,
  COUNT(*) AS extractions_total,
  COUNT(*) FILTER (WHERE needs_expensive) AS expensive_requested,
  ROUND(100.0 * COUNT(*) FILTER (WHERE needs_expensive) / NULLIF(COUNT(*), 0), 2) AS expensive_requested_pct,
  COUNT(*) FILTER (WHERE entities_cnt = 0 AND facts_cnt = 0 AND claims_cnt = 0 AND observations_cnt = 0 AND events_cnt = 0 AND rel_cnt = 0) AS empty_extractions,
  ROUND(100.0 * COUNT(*) FILTER (WHERE entities_cnt = 0 AND facts_cnt = 0 AND claims_cnt = 0 AND observations_cnt = 0 AND events_cnt = 0 AND rel_cnt = 0) / NULLIF(COUNT(*), 0), 2) AS empty_extractions_pct,
  COUNT(*) FILTER (WHERE facts_cnt > 0) AS with_facts,
  ROUND(100.0 * COUNT(*) FILTER (WHERE facts_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS with_facts_pct,
  COUNT(*) FILTER (WHERE claims_cnt > 0) AS with_claims,
  ROUND(100.0 * COUNT(*) FILTER (WHERE claims_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS with_claims_pct,
  COUNT(*) FILTER (WHERE observations_cnt > 0) AS with_observations,
  ROUND(100.0 * COUNT(*) FILTER (WHERE observations_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS with_observations_pct,
  COUNT(*) FILTER (WHERE events_cnt > 0) AS with_events,
  ROUND(100.0 * COUNT(*) FILTER (WHERE events_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS with_events_pct,
  COUNT(*) FILTER (WHERE rel_cnt > 0) AS with_relationships,
  ROUND(100.0 * COUNT(*) FILTER (WHERE rel_cnt > 0) / NULLIF(COUNT(*), 0), 2) AS with_relationships_pct
FROM x;

-- 2) Hourly trend for the last 24h.
WITH x AS (
  SELECT
    date_trunc('hour', me.created_at) AS h,
    me.needs_expensive,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Entities', me.cheap_json -> 'entities', '[]'::jsonb)), 0) AS entities_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Facts', me.cheap_json -> 'facts', '[]'::jsonb)), 0) AS facts_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Claims', me.cheap_json -> 'claims', '[]'::jsonb)), 0) AS claims_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Observations', me.cheap_json -> 'observations', '[]'::jsonb)), 0) AS observations_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Events', me.cheap_json -> 'events', '[]'::jsonb)), 0) AS events_cnt,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Relationships', me.cheap_json -> 'relationships', '[]'::jsonb)), 0) AS rel_cnt
  FROM message_extractions me
  WHERE me.created_at >= NOW() - INTERVAL '24 hours'
)
SELECT
  h,
  COUNT(*) AS total,
  COUNT(*) FILTER (WHERE needs_expensive) AS expensive_requested,
  COUNT(*) FILTER (WHERE entities_cnt = 0 AND facts_cnt = 0 AND claims_cnt = 0 AND observations_cnt = 0 AND events_cnt = 0 AND rel_cnt = 0) AS empty_extractions,
  COUNT(*) FILTER (WHERE facts_cnt > 0) AS with_facts,
  COUNT(*) FILTER (WHERE claims_cnt > 0) AS with_claims,
  COUNT(*) FILTER (WHERE observations_cnt > 0) AS with_observations,
  COUNT(*) FILTER (WHERE events_cnt > 0) AS with_events,
  COUNT(*) FILTER (WHERE rel_cnt > 0) AS with_relationships
FROM x
GROUP BY h
ORDER BY h DESC;

-- 3) Top entity aliases (fragmentation signal) for the last 7 days.
WITH ent AS (
  SELECT
    lower(trim(elem ->> 'Name')) AS entity_name,
    COUNT(*) AS mentions
  FROM message_extractions me
  CROSS JOIN LATERAL jsonb_array_elements(COALESCE(me.cheap_json -> 'Entities', me.cheap_json -> 'entities', '[]'::jsonb)) elem
  WHERE me.created_at >= NOW() - INTERVAL '7 days'
  GROUP BY 1
)
SELECT entity_name, mentions
FROM ent
WHERE entity_name <> ''
ORDER BY mentions DESC
LIMIT 20;

-- 4) Auto-review queue pressure (last 24h).
SELECT
  COUNT(*) AS review_commands_24h,
  COUNT(*) FILTER (WHERE status = 0) AS review_pending_24h,
  COUNT(*) FILTER (WHERE status = 1) AS review_done_24h,
  COUNT(*) FILTER (WHERE status = 2) AS review_failed_24h
FROM fact_review_commands
WHERE created_at >= NOW() - INTERVAL '24 hours';

-- 5) Top fact categories entering review queue (last 24h).
SELECT
  f.category,
  COUNT(*) AS cnt
FROM fact_review_commands c
JOIN facts f ON f.id = c.fact_id
WHERE c.created_at >= NOW() - INTERVAL '24 hours'
GROUP BY f.category
ORDER BY cnt DESC
LIMIT 20;

-- 6) Expensive-pass error summary by model (last 24h).
SELECT
  COALESCE(SUBSTRING(payload FROM 'model=([^;]+)'), 'unknown') AS model,
  COUNT(*) AS cnt
FROM extraction_errors
WHERE stage IN ('stage5_expensive_item', 'stage5_expensive_denied')
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY 1
ORDER BY cnt DESC;

-- 7) Analysis usage summary (last 24h).
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

-- 8) Expensive pass effectiveness (last 24h).
WITH base AS (
  SELECT
    me.message_id,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Facts', me.cheap_json -> 'facts', '[]'::jsonb)), 0) AS cheap_facts,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Claims', me.cheap_json -> 'claims', '[]'::jsonb)), 0) AS cheap_claims,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Observations', me.cheap_json -> 'observations', '[]'::jsonb)), 0) AS cheap_observations,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Events', me.cheap_json -> 'events', '[]'::jsonb)), 0) AS cheap_events,
    COALESCE(jsonb_array_length(COALESCE(me.cheap_json -> 'Relationships', me.cheap_json -> 'relationships', '[]'::jsonb)), 0) AS cheap_relationships,
    COALESCE(jsonb_array_length(COALESCE(me.expensive_json -> 'Facts', me.expensive_json -> 'facts', '[]'::jsonb)), 0) AS expensive_facts,
    COALESCE(jsonb_array_length(COALESCE(me.expensive_json -> 'Claims', me.expensive_json -> 'claims', '[]'::jsonb)), 0) AS expensive_claims,
    COALESCE(jsonb_array_length(COALESCE(me.expensive_json -> 'Observations', me.expensive_json -> 'observations', '[]'::jsonb)), 0) AS expensive_observations,
    COALESCE(jsonb_array_length(COALESCE(me.expensive_json -> 'Events', me.expensive_json -> 'events', '[]'::jsonb)), 0) AS expensive_events,
    COALESCE(jsonb_array_length(COALESCE(me.expensive_json -> 'Relationships', me.expensive_json -> 'relationships', '[]'::jsonb)), 0) AS expensive_relationships,
    me.expensive_json IS NOT NULL AS has_expensive
  FROM message_extractions me
  WHERE me.created_at >= NOW() - INTERVAL '24 hours'
),
scored AS (
  SELECT
    has_expensive,
    (cheap_facts + cheap_claims + cheap_observations + cheap_events + cheap_relationships) AS cheap_total,
    (expensive_facts + expensive_claims + expensive_observations + expensive_events + expensive_relationships) AS expensive_total
  FROM base
)
SELECT
  COUNT(*) FILTER (WHERE has_expensive) AS expensive_attempted,
  COUNT(*) FILTER (WHERE has_expensive AND expensive_total = 0) AS expensive_empty_result,
  COUNT(*) FILTER (WHERE has_expensive AND expensive_total > cheap_total) AS expensive_improved,
  ROUND(100.0 * COUNT(*) FILTER (WHERE has_expensive AND expensive_total > cheap_total) / NULLIF(COUNT(*) FILTER (WHERE has_expensive), 0), 2) AS expensive_improved_pct
FROM scored;
