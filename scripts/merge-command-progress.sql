-- Entity merge command queue operational report.

-- 1) Queue health snapshot.
SELECT
  NOW() AS ts,
  COUNT(*) FILTER (WHERE status = 0) AS pending,
  COUNT(*) FILTER (WHERE status = 1) AS done,
  COUNT(*) FILTER (WHERE status = 2) AS failed,
  COUNT(*) AS total
FROM entity_merge_commands;

-- 2) Pending aging buckets.
SELECT
  COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '1 hour') AS pending_1h,
  COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '24 hours' AND created_at < NOW() - INTERVAL '1 hour') AS pending_24h,
  COUNT(*) FILTER (WHERE created_at < NOW() - INTERVAL '24 hours') AS pending_old
FROM entity_merge_commands
WHERE status = 0;

-- 3) Latest pending commands with candidate context.
SELECT
  c.id AS command_id,
  c.created_at,
  c.command,
  c.reason,
  c.candidate_id,
  mc.entity_low_id,
  mc.entity_high_id,
  mc.alias_norm,
  mc.evidence_count,
  mc.score,
  mc.review_priority
FROM entity_merge_commands c
JOIN entity_merge_candidates mc ON mc.id = c.candidate_id
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
FROM entity_merge_commands
WHERE status = 2
ORDER BY created_at DESC
LIMIT 100;

-- 5) Top failure reasons.
SELECT
  COALESCE(error, '(no error)') AS error,
  COUNT(*) AS cnt
FROM entity_merge_commands
WHERE status = 2
GROUP BY 1
ORDER BY 2 DESC
LIMIT 20;
