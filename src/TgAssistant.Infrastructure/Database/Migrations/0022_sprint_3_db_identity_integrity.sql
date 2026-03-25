-- Sprint 3: Database identity and control-state integrity

-- 1) Message identity contract: one logical telegram message per (chat_id, telegram_message_id).
WITH ranked_messages AS (
    SELECT
        id,
        ROW_NUMBER() OVER (
            PARTITION BY chat_id, telegram_message_id
            ORDER BY
                CASE WHEN source = 1 THEN 0 ELSE 1 END,
                edit_timestamp DESC NULLS LAST,
                created_at DESC,
                id DESC
        ) AS rn
    FROM messages
)
DELETE FROM messages m
USING ranked_messages r
WHERE m.id = r.id
  AND r.rn > 1;

DROP INDEX IF EXISTS uq_messages_source_chat_tg_message;
CREATE UNIQUE INDEX IF NOT EXISTS uq_messages_chat_tg_message
    ON messages(chat_id, telegram_message_id);

-- 2) Fact uniqueness contract (current facts): (entity_id, category, key, value, is_current=true).
WITH ranked_facts AS (
    SELECT
        id,
        ROW_NUMBER() OVER (
            PARTITION BY entity_id, category, key, value
            ORDER BY updated_at DESC, created_at DESC, id DESC
        ) AS rn
    FROM facts
    WHERE is_current = TRUE
)
DELETE FROM facts f
USING ranked_facts r
WHERE f.id = r.id
  AND r.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS uq_facts_current_natural_identity
    ON facts(entity_id, category, key, value)
    WHERE is_current = TRUE;

-- 3) Relationship uniqueness contract: (from_entity_id, to_entity_id, type).
WITH ranked_relationships AS (
    SELECT
        id,
        ROW_NUMBER() OVER (
            PARTITION BY from_entity_id, to_entity_id, type
            ORDER BY updated_at DESC, created_at DESC, id DESC
        ) AS rn
    FROM relationships
)
DELETE FROM relationships rel
USING ranked_relationships r
WHERE rel.id = r.id
  AND r.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS uq_relationships_natural_identity
    ON relationships(from_entity_id, to_entity_id, type);

-- 4) Stage 6 queue/artifact identity rules for current queue layer.
-- Inbox item identity: (case_id, item_type, source_object_type, source_object_id).
WITH ranked_inbox AS (
    SELECT
        id,
        ROW_NUMBER() OVER (
            PARTITION BY case_id, item_type, source_object_type, source_object_id
            ORDER BY updated_at DESC, created_at DESC, id DESC
        ) AS rn
    FROM domain_inbox_items
)
DELETE FROM domain_inbox_items i
USING ranked_inbox r
WHERE i.id = r.id
  AND r.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS uq_domain_inbox_items_natural_identity
    ON domain_inbox_items(case_id, item_type, source_object_type, source_object_id);

-- Conflict identity is symmetric over object A/B pair.
WITH normalized_conflicts AS (
    SELECT
        id,
        CASE
            WHEN (object_a_type || ':' || object_a_id) <= (object_b_type || ':' || object_b_id)
                THEN object_a_type || ':' || object_a_id
            ELSE object_b_type || ':' || object_b_id
        END AS obj_low,
        CASE
            WHEN (object_a_type || ':' || object_a_id) <= (object_b_type || ':' || object_b_id)
                THEN object_b_type || ':' || object_b_id
            ELSE object_a_type || ':' || object_a_id
        END AS obj_high,
        ROW_NUMBER() OVER (
            PARTITION BY
                case_id,
                conflict_type,
                CASE
                    WHEN (object_a_type || ':' || object_a_id) <= (object_b_type || ':' || object_b_id)
                        THEN object_a_type || ':' || object_a_id
                    ELSE object_b_type || ':' || object_b_id
                END,
                CASE
                    WHEN (object_a_type || ':' || object_a_id) <= (object_b_type || ':' || object_b_id)
                        THEN object_b_type || ':' || object_b_id
                    ELSE object_a_type || ':' || object_a_id
                END
            ORDER BY updated_at DESC, created_at DESC, id DESC
        ) AS rn
    FROM domain_conflict_records
)
DELETE FROM domain_conflict_records c
USING normalized_conflicts n
WHERE c.id = n.id
  AND n.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS uq_domain_conflicts_natural_identity
    ON domain_conflict_records(
        case_id,
        conflict_type,
        LEAST(object_a_type || ':' || object_a_id, object_b_type || ':' || object_b_id),
        GREATEST(object_a_type || ':' || object_a_id, object_b_type || ':' || object_b_id)
    );
