-- Stage5 media coverage and quality baseline.
-- Usage:
--   psql -U tgassistant -d tgassistant -f scripts/stage5-media-coverage.sql
--
with scoped as (
    select
        m.id,
        m.source,
        m.media_type,
        m.processing_status,
        m.timestamp,
        coalesce(m.text, '') as text,
        coalesce(m.media_transcription, '') as media_transcription,
        coalesce(m.media_description, '') as media_description,
        me.cheap_json::jsonb as cheap_json
    from messages m
    left join message_extractions me on me.message_id = m.id
    where m.media_type <> 0
),
calc as (
    select
        source,
        media_type,
        processing_status,
        cheap_json,
        greatest(
            char_length(trim(text)),
            char_length(trim(media_transcription)),
            char_length(trim(media_description))
        ) as input_chars,
        (
            jsonb_array_length(coalesce(cheap_json->'entities', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'observations', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'claims', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'facts', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'events', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'relationships', '[]'::jsonb))
        ) as signal_count
    from scoped
)
select
    source,
    count(*) as media_total,
    count(*) filter (where processing_status = 1) as media_processed,
    count(*) filter (where processing_status = 3) as media_pending_review,
    count(*) filter (where cheap_json is not null) as media_with_extraction,
    count(*) filter (where cheap_json is not null and signal_count = 0) as media_empty_signal,
    count(*) filter (where cheap_json is not null and signal_count > 0) as media_nonempty_signal,
    count(*) filter (where processing_status = 1 and cheap_json is null) as processed_without_extraction,
    count(*) filter (where cheap_json is not null and signal_count = 0 and input_chars >= 120) as empty_rich_input
from calc
group by source
order by source;

with scoped as (
    select
        m.id,
        m.source,
        m.media_type,
        m.processing_status,
        m.timestamp,
        coalesce(m.text, '') as text,
        coalesce(m.media_transcription, '') as media_transcription,
        coalesce(m.media_description, '') as media_description,
        me.cheap_json::jsonb as cheap_json
    from messages m
    left join message_extractions me on me.message_id = m.id
    where m.media_type <> 0
),
calc as (
    select
        source,
        media_type,
        processing_status,
        cheap_json,
        greatest(
            char_length(trim(text)),
            char_length(trim(media_transcription)),
            char_length(trim(media_description))
        ) as input_chars,
        (
            jsonb_array_length(coalesce(cheap_json->'entities', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'observations', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'claims', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'facts', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'events', '[]'::jsonb)) +
            jsonb_array_length(coalesce(cheap_json->'relationships', '[]'::jsonb))
        ) as signal_count
    from scoped
)
select
    source,
    media_type,
    count(*) as media_total,
    count(*) filter (where cheap_json is not null and signal_count = 0) as empty_signal,
    count(*) filter (where cheap_json is not null and signal_count > 0) as nonempty_signal
from calc
group by source, media_type
order by source, media_total desc, media_type;
