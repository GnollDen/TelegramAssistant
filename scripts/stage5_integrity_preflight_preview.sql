-- Sprint 20 prep helper (read-only): integrity preflight snapshot for scoped Stage5/backfill operations.
-- Usage: run in psql and provide chat_id via psql variable, for example:
--   \set chat_id 885574984

-- 1) Pending extraction count by chat
select
  me.chat_id,
  count(*) as pending_extractions
from message_extractions me
where me.chat_id = :chat_id
  and (me.cheap_json is null or me.expensive_json is null)
group by me.chat_id;

-- 2) Potential duplicate message ids in messages table for chat scope
select
  m.chat_id,
  m.id as message_id,
  count(*) as duplicate_rows
from messages m
where m.chat_id = :chat_id
group by m.chat_id, m.id
having count(*) > 1
order by duplicate_rows desc, message_id asc
limit 100;

-- 3) Dual-source quarantine candidates overview (if column exists in this schema revision)
-- Note: keep this query optional in environments where quarantine_reason is absent.
select
  m.chat_id,
  m.quarantine_reason,
  count(*) as rows_count
from messages m
where m.chat_id = :chat_id
  and m.quarantine_reason is not null
group by m.chat_id, m.quarantine_reason
order by rows_count desc;

-- 4) Session index continuity check
with session_bounds as (
  select
    cs.chat_id,
    min(cs.session_index) as min_idx,
    max(cs.session_index) as max_idx,
    count(distinct cs.session_index) as present_count
  from chat_sessions cs
  where cs.chat_id = :chat_id
  group by cs.chat_id
)
select
  sb.chat_id,
  sb.min_idx,
  sb.max_idx,
  sb.present_count,
  (sb.max_idx - sb.min_idx + 1) as expected_count,
  (sb.max_idx - sb.min_idx + 1) - sb.present_count as missing_indexes
from session_bounds sb;

-- 5) Recent write-volume indicator (24h) for sanity threshold review
select
  m.chat_id,
  count(*) as messages_24h
from messages m
where m.chat_id = :chat_id
  and m.created_at >= now() - interval '24 hour'
group by m.chat_id;
