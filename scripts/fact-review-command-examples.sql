-- Queue manual fact review commands.
-- command: approve|confirm|reject|decline

-- 1) See latest extracted facts.
SELECT id, entity_id, category, key, value, status, confidence, source_message_id, created_at
FROM facts
ORDER BY created_at DESC
LIMIT 50;

-- 2) Enqueue approval.
-- INSERT INTO fact_review_commands (fact_id, command, reason)
-- VALUES ('00000000-0000-0000-0000-000000000000', 'approve', 'verified manually');

-- 3) Enqueue rejection.
-- INSERT INTO fact_review_commands (fact_id, command, reason)
-- VALUES ('00000000-0000-0000-0000-000000000000', 'reject', 'wrong extraction');

-- 4) Check queue + processing state.
SELECT id, fact_id, command, reason, status, error, created_at, processed_at
FROM fact_review_commands
ORDER BY id DESC
LIMIT 100;
