alter table if exists messages
    add column if not exists media_enrichment_state smallint not null default 0;

alter table if exists messages
    add column if not exists media_enrichment_reason text;

alter table if exists messages
    add column if not exists media_enrichment_attempts integer not null default 0;

alter table if exists messages
    add column if not exists media_enrichment_updated_at timestamptz;

alter table if exists messages
    add column if not exists media_enrichment_provider text;

alter table if exists messages
    add column if not exists media_enrichment_model text;

alter table if exists messages
    add column if not exists media_duration_seconds integer;

comment on column messages.media_enrichment_state is
    '0=pending_or_unknown, 1=ready, 2=unavailable, 3=failed_terminal';

create index if not exists idx_messages_media_enrichment_state
    on messages(source, media_enrichment_state, processing_status, timestamp)
    where media_type <> 0;

create index if not exists idx_messages_media_enrichment_replay
    on messages(source, processing_status, timestamp)
    where media_type <> 0 and media_enrichment_state in (0, 2, 3);
