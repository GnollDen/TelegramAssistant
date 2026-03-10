SELECT COUNT(*) AS observations_total FROM intelligence_observations;
SELECT COUNT(*) AS claims_total FROM intelligence_claims;

SELECT
  COUNT(*) AS claims_last_1k
FROM intelligence_claims
WHERE message_id > (SELECT GREATEST(MAX(id) - 1000, 0) FROM messages);

SELECT
  COUNT(*) AS observations_last_1k
FROM intelligence_observations
WHERE message_id > (SELECT GREATEST(MAX(id) - 1000, 0) FROM messages);
