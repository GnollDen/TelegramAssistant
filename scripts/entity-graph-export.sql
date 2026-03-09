-- Graph-oriented export view for downstream Neo4j/graph tooling
CREATE OR REPLACE VIEW v_entity_graph_edges AS
SELECT
  r.id::text AS edge_id,
  r.from_entity_id::text AS from_entity_id,
  r.to_entity_id::text AS to_entity_id,
  r.type AS edge_type,
  r.confidence,
  r.source_message_id,
  r.created_at,
  r.updated_at
FROM relationships r;

CREATE OR REPLACE VIEW v_entity_graph_nodes AS
SELECT
  e.id::text AS entity_id,
  e.type,
  e.name,
  e.aliases,
  e.telegram_user_id,
  e.telegram_username,
  e.created_at,
  e.updated_at
FROM entities e;
