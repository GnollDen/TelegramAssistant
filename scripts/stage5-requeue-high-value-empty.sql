begin;

set local lock_timeout = '5s';
set local statement_timeout = '10min';

create temp table if not exists _stage5_requeue_result (
    requeued_count bigint not null
);

truncate _stage5_requeue_result;

with candidate_messages as (
    select me.id as extraction_id
    from message_extractions me
    join messages m on m.id = me.message_id
    where length(coalesce(m.text, '')) > 100
      and m.sender_id <> 0
      and btrim(coalesce(m.sender_name, '')) <> ''
      and coalesce(jsonb_array_length(me.cheap_json -> 'claims'), 0) = 0
      and not exists (
          select 1
          from intelligence_claims ic
          where ic.message_id = m.id
      )
),
deleted as (
    delete from message_extractions me
    using candidate_messages c
    where me.id = c.extraction_id
    returning me.message_id
)
insert into _stage5_requeue_result(requeued_count)
select count(*)
from deleted;

select requeued_count
from _stage5_requeue_result;

commit;
