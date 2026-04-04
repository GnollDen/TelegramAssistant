create table if not exists operator_offline_events (
    id uuid primary key,
    scope_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    summary_text text not null,
    recording_reference text null,
    status text not null default 'draft',
    capture_payload_json jsonb not null default '{}'::jsonb,
    clarification_state_json jsonb not null default '{}'::jsonb,
    timeline_linkage_json jsonb not null default '{}'::jsonb,
    confidence real null,
    operator_id text not null,
    operator_display text not null,
    operator_session_id text not null,
    surface text not null,
    surface_subject text not null,
    auth_source text not null,
    auth_time_utc timestamptz not null,
    session_authenticated_at_utc timestamptz not null,
    session_last_seen_at_utc timestamptz not null,
    session_expires_at_utc timestamptz null,
    active_mode text not null,
    unfinished_step_kind text null,
    unfinished_step_state text null,
    unfinished_step_started_at_utc timestamptz null,
    captured_at_utc timestamptz not null,
    saved_at_utc timestamptz null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ck_operator_offline_events_status
        check (status in ('draft', 'captured', 'saved', 'archived')),
    constraint ck_operator_offline_events_surface
        check (surface in ('telegram', 'web')),
    constraint ck_operator_offline_events_summary
        check (btrim(summary_text) <> '')
);

create index if not exists ix_operator_offline_events_scope_person_updated
    on operator_offline_events(scope_key, tracked_person_id, updated_at_utc desc);

create index if not exists ix_operator_offline_events_operator_session_created
    on operator_offline_events(operator_session_id, created_at_utc desc);

create index if not exists ix_operator_offline_events_scope_status_created
    on operator_offline_events(scope_key, status, created_at_utc desc);
