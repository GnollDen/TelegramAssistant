SELECT
  NOW() AS ts,
  (SELECT COUNT(*) FROM messages WHERE processing_status=1) AS processed_messages,
  (SELECT COUNT(*) FROM message_extractions) AS extracted_messages,
  (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive) AS expensive_backlog,
  (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive AND (expensive_next_retry_at IS NULL OR expensive_next_retry_at <= NOW())) AS expensive_ready_now,
  (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive AND expensive_next_retry_at > NOW()) AS expensive_delayed_retry,
  (SELECT COUNT(*) FROM entity_merge_candidates WHERE status=0) AS merge_candidates_pending,
  (SELECT COUNT(*) FROM fact_review_commands WHERE status=0) AS fact_reviews_pending,
  (SELECT COUNT(*) FROM extraction_errors WHERE created_at >= NOW() - INTERVAL '1 hour') AS extraction_errors_1h;

SELECT
  captured_at,
  processed_messages,
  extractions_total,
  expensive_backlog,
  merge_candidates_pending,
  fact_reviews_pending,
  extraction_errors_1h,
  analysis_requests_1h,
  analysis_tokens_1h,
  analysis_cost_usd_1h
FROM stage5_metrics_snapshots
ORDER BY captured_at DESC
LIMIT 20;
