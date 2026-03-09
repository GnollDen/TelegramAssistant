-- Stage5 expensive-pass health report (errors/backoff signals by model).

-- 1) Error totals by stage for last 24h.
SELECT
  stage,
  COUNT(*) AS cnt
FROM extraction_errors
WHERE stage IN ('stage5_expensive_item', 'stage5_expensive_denied')
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY stage
ORDER BY cnt DESC;

-- 2) Error totals by model for last 24h.
-- payload format: model=<model>;exception=<type>
SELECT
  COALESCE(SUBSTRING(payload FROM 'model=([^;]+)'), 'unknown') AS model,
  COUNT(*) AS cnt
FROM extraction_errors
WHERE stage IN ('stage5_expensive_item', 'stage5_expensive_denied')
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY 1
ORDER BY cnt DESC;

-- 3) Denied errors by model for last 24h.
SELECT
  COALESCE(SUBSTRING(payload FROM 'model=([^;]+)'), 'unknown') AS model,
  COUNT(*) AS denied_cnt
FROM extraction_errors
WHERE stage = 'stage5_expensive_denied'
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY 1
ORDER BY denied_cnt DESC;

-- 4) Top error reasons for expensive pass.
SELECT
  stage,
  LEFT(reason, 180) AS reason_prefix,
  COUNT(*) AS cnt
FROM extraction_errors
WHERE stage IN ('stage5_expensive_item', 'stage5_expensive_denied')
  AND created_at >= NOW() - INTERVAL '24 hours'
GROUP BY 1, 2
ORDER BY cnt DESC
LIMIT 30;

-- 5) Latest expensive-pass errors.
SELECT
  created_at,
  stage,
  message_id,
  payload,
  LEFT(reason, 240) AS reason_prefix
FROM extraction_errors
WHERE stage IN ('stage5_expensive_item', 'stage5_expensive_denied')
ORDER BY created_at DESC
LIMIT 100;
