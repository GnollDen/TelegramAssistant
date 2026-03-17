-- Canary requeue for Stage5 media backlog.
--
-- Usage examples:
--   psql ... -f scripts/stage5-media-requeue-canary.sql
--   psql ... -v MODE=without_extraction -v LIMIT=200 -v SOURCE=1 -v CUTOFF="'2026-03-06 23:59:59+00'" -f scripts/stage5-media-requeue-canary.sql
--   psql ... -v MODE=both -v LIMIT=500 -v SOURCE=1 -f scripts/stage5-media-requeue-canary.sql
--
-- MODE:
--   without_extraction -> only media messages with no row in message_extractions
--   empty_signal       -> only media messages with extraction and empty signal
--   both               -> union of both groups

\set ON_ERROR_STOP on

\if :{?MODE}
\else
\set MODE 'without_extraction'
\endif

\if :{?LIMIT}
\else
\set LIMIT 200
\endif

\if :{?SOURCE}
\else
\set SOURCE 1
\endif

\if :{?CUTOFF}
\else
\set CUTOFF '''2100-01-01 00:00:00+00'''
\endif

begin;

create temp table if not exists _stage5_media_canary_ids (
    message_id bigint primary key,
    reason text not null
);

truncate _stage5_media_canary_ids;

insert into _stage5_media_canary_ids(message_id, reason)
select message_id, reason
from (
    select
        m.id as message_id,
        case
            when me.message_id is null then 'without_extraction'
            else 'empty_signal'
        end as reason
    from messages m
    left join message_extractions me on me.message_id = m.id
    where m.source = :'SOURCE'::smallint
      and m.media_type <> 0
      and m.processing_status = 1
      and m.timestamp <= :'CUTOFF'::timestamptz
      and coalesce(m.needs_reanalysis, false) = false
      and (
          (:'MODE' = 'without_extraction' and me.message_id is null)
          or
          (:'MODE' = 'empty_signal' and me.message_id is not null and (
              jsonb_array_length(coalesce(me.cheap_json->'entities', '[]'::jsonb)) +
              jsonb_array_length(coalesce(me.cheap_json->'observations', '[]'::jsonb)) +
              jsonb_array_length(coalesce(me.cheap_json->'claims', '[]'::jsonb)) +
              jsonb_array_length(coalesce(me.cheap_json->'facts', '[]'::jsonb)) +
              jsonb_array_length(coalesce(me.cheap_json->'events', '[]'::jsonb)) +
              jsonb_array_length(coalesce(me.cheap_json->'relationships', '[]'::jsonb))
          ) = 0)
          or
          (:'MODE' = 'both' and (
              me.message_id is null
              or (
                  jsonb_array_length(coalesce(me.cheap_json->'entities', '[]'::jsonb)) +
                  jsonb_array_length(coalesce(me.cheap_json->'observations', '[]'::jsonb)) +
                  jsonb_array_length(coalesce(me.cheap_json->'claims', '[]'::jsonb)) +
                  jsonb_array_length(coalesce(me.cheap_json->'facts', '[]'::jsonb)) +
                  jsonb_array_length(coalesce(me.cheap_json->'events', '[]'::jsonb)) +
                  jsonb_array_length(coalesce(me.cheap_json->'relationships', '[]'::jsonb))
              ) = 0
          ))
      )
    order by m.timestamp, m.id
    limit greatest(1, :'LIMIT'::int)
) q
on conflict (message_id) do nothing;

with updated as (
    update messages m
       set needs_reanalysis = true
      from _stage5_media_canary_ids c
     where m.id = c.message_id
       and coalesce(m.needs_reanalysis, false) = false
    returning m.id
)
select count(*) as requeued_count from updated;

select reason, count(*) as count_by_reason
from _stage5_media_canary_ids
group by reason
order by reason;

select message_id
from _stage5_media_canary_ids
order by message_id
limit 20;

commit;
