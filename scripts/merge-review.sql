SELECT
  c.id AS candidate_id,
  c.review_priority,
  c.score,
  c.evidence_count,
  c.alias_norm,
  low.id AS entity_low_id,
  low.name AS entity_low_name,
  low.actor_key AS entity_low_actor_key,
  low.telegram_user_id AS entity_low_tg_id,
  high.id AS entity_high_id,
  high.name AS entity_high_name,
  high.actor_key AS entity_high_actor_key,
  high.telegram_user_id AS entity_high_tg_id,
  c.updated_at
FROM entity_merge_candidates c
JOIN entities low ON low.id = c.entity_low_id
JOIN entities high ON high.id = c.entity_high_id
WHERE c.status = 0
ORDER BY c.review_priority ASC, c.score DESC, c.evidence_count DESC, c.updated_at DESC
LIMIT 200;
