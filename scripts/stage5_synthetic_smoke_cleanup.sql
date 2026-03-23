-- Stage 5 targeted synthetic-smoke backlog cleanup (safe, non-destructive).
-- Scope: only synthetic smoke chats generated via CaseScopeFactory.CreateSmokeScope
-- (chat_id >= 9_000_000_000_000) that are currently pending in chat_sessions.
--
-- Dry-run preview:
--   select chat_id, session_index, last_message_at
--   from chat_sessions
--   where chat_id >= 9000000000000
--     and not is_analyzed
--     and not is_finalized
--   order by chat_id, session_index;
--
-- Apply:
--   begin;
--   \i scripts/stage5_synthetic_smoke_cleanup.sql
--   commit;

with target as (
    select id
    from chat_sessions
    where chat_id >= 9000000000000
      and not is_analyzed
      and not is_finalized
)
update chat_sessions cs
set is_analyzed = true,
    updated_at = now()
from target t
where cs.id = t.id;
