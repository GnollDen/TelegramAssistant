-- LLM cost sanity check
SELECT
  ROUND(COALESCE(SUM(cost_usd),0)::numeric, 6) AS cost_usd_1h,
  COALESCE(SUM(total_tokens),0) AS tokens_1h,
  COALESCE(COUNT(*),0) AS requests_1h
FROM analysis_usage_events
WHERE created_at >= NOW() - INTERVAL '1 hour';

SELECT
  ROUND(COALESCE(SUM(cost_usd),0)::numeric, 6) AS cost_usd_24h,
  COALESCE(SUM(total_tokens),0) AS tokens_24h,
  COALESCE(COUNT(*),0) AS requests_24h
FROM analysis_usage_events
WHERE created_at >= NOW() - INTERVAL '24 hour';

SELECT
  captured_at,
  analysis_cost_usd_1h,
  analysis_requests_1h,
  analysis_tokens_1h
FROM stage5_metrics_snapshots
ORDER BY captured_at DESC
LIMIT 5;
