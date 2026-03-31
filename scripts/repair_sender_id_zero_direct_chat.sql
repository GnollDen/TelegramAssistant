-- One-time targeted repair for ingestion defect:
-- in direct 1:1 chats some ordinary messages were persisted with sender_id=0.
-- Safe repair rule:
--   - chat has sender_id=0 rows
--   - chat has exactly two distinct non-zero sender ids
--   - one of non-zero sender ids equals chat_id (direct peer id)
--   - another non-zero sender id exists (self participant)
-- For such rows sender_id is recoverable as chat_id.

begin;

with candidate_chats as (
    select
        chat_id
    from messages
    group by chat_id
    having count(*) filter (where sender_id = 0) > 0
       and count(distinct sender_id) filter (where sender_id <> 0) = 2
       and count(*) filter (where sender_id = chat_id) > 0
       and count(*) filter (where sender_id <> 0 and sender_id <> chat_id) > 0
),
fixed as (
    update messages m
    set sender_id = m.chat_id
    where m.sender_id = 0
      and m.chat_id in (select chat_id from candidate_chats)
    returning m.id, m.chat_id, m.telegram_message_id, m.timestamp
)
select
    chat_id,
    count(*) as updated_rows,
    min(timestamp) as min_timestamp,
    max(timestamp) as max_timestamp
from fixed
group by chat_id
order by chat_id;

commit;
