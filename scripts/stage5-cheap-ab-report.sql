-- Stage5 cheap model A/B report
-- Usage:
--   docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5-cheap-ab-report.sql

-- 1) Requests/tokens/cost by cheap model in last 24h
SELECT
  model,
  COUNT(*) AS requests,
  SUM(prompt_tokens) AS prompt_tokens,
  SUM(completion_tokens) AS completion_tokens,
  SUM(total_tokens) AS total_tokens,
  ROUND(SUM(cost_usd)::numeric, 6) AS cost_usd
FROM analysis_usage_events
WHERE phase = 'cheap'
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY model
ORDER BY requests DESC;

-- 2) Runtime errors by model marker in payload (best effort)
SELECT
  split_part(split_part(payload, 'model=', 2), ';', 1) AS model,
  stage,
  COUNT(*) AS errors
FROM extraction_errors
WHERE stage IN ('stage5_cheap_batch_model', 'stage5_cheap_item', 'stage5_validation')
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY model, stage
ORDER BY errors DESC, stage;

-- 3) Latest extraction throughput snapshot
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
