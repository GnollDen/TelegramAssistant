-- Fact review queue operational report.

-- 1) Queue health snapshot.
SELECT
  NOW() AS ts,
  COUNT(*) FILTER (WHERE status = 0) AS pending,
  COUNT(*) FILTER (WHERE status = 1) AS done,
  COUNT(*) FILTER (WHERE status = 2) AS failed,
  COUNT(*) AS total
FROM fact_review_commands;

-- 2) Pending aging buckets.
SELECT
  COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '1 hour') AS pending_1h,
  COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '24 hours' AND created_at < NOW() - INTERVAL '1 hour') AS pending_24h,
  COUNT(*) FILTER (WHERE created_at < NOW() - INTERVAL '24 hours') AS pending_old
FROM fact_review_commands
WHERE status = 0;

-- 3) Latest pending commands with fact context.
SELECT
  c.id,
  c.created_at,
  c.command,
  c.reason,
  f.id AS fact_id,
  f.entity_id,
  f.category,
  f.key,
  f.value,
  f.status AS fact_status,
  f.confidence,
  f.source_message_id
FROM fact_review_commands c
JOIN facts f ON f.id = c.fact_id
WHERE c.status = 0
ORDER BY c.created_at DESC
LIMIT 100;

-- 4) Latest failed commands.
SELECT
  id,
  created_at,
  processed_at,
  command,
  reason,
  error
FROM fact_review_commands
WHERE status = 2
ORDER BY created_at DESC
LIMIT 100;

-- 5) Top auto-review reasons in pending queue.
SELECT
  COALESCE(reason, '(no reason)') AS reason,
  COUNT(*) AS cnt
FROM fact_review_commands
WHERE status = 0
GROUP BY 1
ORDER BY 2 DESC
LIMIT 20;
