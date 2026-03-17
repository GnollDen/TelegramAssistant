-- Quick status report for media requeue rollout.
--
-- Usage:
--   psql ... -f scripts/stage5-media-requeue-status.sql
--   psql ... -v SOURCE=1 -v CUTOFF="'2026-03-06 23:59:59+00'" -f scripts/stage5-media-requeue-status.sql

\set ON_ERROR_STOP on

\if :{?SOURCE}
\else
\set SOURCE 1
\endif

\if :{?CUTOFF}
\else
\set CUTOFF '''2100-01-01 00:00:00+00'''
\endif

with scoped as (
    select
        m.id,
        m.needs_reanalysis,
        m.processing_status,
        me.cheap_json::jsonb as cheap_json,
        (
            jsonb_array_length(coalesce(me.cheap_json->'entities', '[]'::jsonb)) +
            jsonb_array_length(coalesce(me.cheap_json->'observations', '[]'::jsonb)) +
            jsonb_array_length(coalesce(me.cheap_json->'claims', '[]'::jsonb)) +
            jsonb_array_length(coalesce(me.cheap_json->'facts', '[]'::jsonb)) +
            jsonb_array_length(coalesce(me.cheap_json->'events', '[]'::jsonb)) +
            jsonb_array_length(coalesce(me.cheap_json->'relationships', '[]'::jsonb))
        ) as signal_count
    from messages m
    left join message_extractions me on me.message_id = m.id
    where m.source = :'SOURCE'::smallint
      and m.media_type <> 0
      and m.timestamp <= :'CUTOFF'::timestamptz
)
select
    count(*) as media_total,
    count(*) filter (where processing_status = 1) as media_processed,
    count(*) filter (where needs_reanalysis) as media_needs_reanalysis,
    count(*) filter (where cheap_json is null) as media_without_extraction,
    count(*) filter (where cheap_json is not null and signal_count = 0) as media_empty_signal,
    count(*) filter (where cheap_json is not null and signal_count > 0) as media_nonempty_signal
from scoped;
