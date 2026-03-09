-- Approve merge candidate manually
-- Replace :candidate_id and :reason
INSERT INTO entity_merge_commands (candidate_id, command, reason, status, created_at)
VALUES (:candidate_id, 'approve', :reason, 0, NOW());

-- Reject merge candidate manually
-- Replace :candidate_id and :reason
INSERT INTO entity_merge_commands (candidate_id, command, reason, status, created_at)
VALUES (:candidate_id, 'reject', :reason, 0, NOW());

-- Observe command execution status
SELECT id, candidate_id, command, status, error, created_at, processed_at
FROM entity_merge_commands
ORDER BY id DESC
LIMIT 100;
