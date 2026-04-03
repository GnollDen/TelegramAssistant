create table if not exists operator_resolution_actions (
    id uuid primary key,
    request_id text not null,
    scope_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    scope_item_key text not null,
    item_type text not null,
    source_kind text not null,
    source_ref text not null,
    affected_family text not null,
    affected_object_ref text not null,
    decision text not null,
    explanation text null,
    clarification_payload_json jsonb null,
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
    submitted_at_utc timestamptz not null,
    created_at_utc timestamptz not null default now(),
    constraint uq_operator_resolution_actions_request_id unique (request_id),
    constraint ck_operator_resolution_actions_decision
        check (decision in ('approve', 'reject', 'defer', 'clarify')),
    constraint ck_operator_resolution_actions_surface
        check (surface in ('telegram', 'web')),
    constraint ck_operator_resolution_actions_explanation
        check (decision = 'approve' or (explanation is not null and btrim(explanation) <> ''))
);

create index if not exists ix_operator_resolution_actions_scope_person_created
    on operator_resolution_actions(scope_key, tracked_person_id, created_at_utc desc);

create index if not exists ix_operator_resolution_actions_scope_item
    on operator_resolution_actions(scope_item_key, created_at_utc desc);

create index if not exists ix_operator_resolution_actions_operator_session
    on operator_resolution_actions(operator_session_id, created_at_utc desc);

create table if not exists operator_audit_events (
    audit_event_id uuid primary key,
    request_id text not null,
    scope_key text null,
    tracked_person_id uuid null references persons(id) on delete set null,
    scope_item_key text null,
    item_type text null,
    operator_id text not null,
    operator_display text not null,
    operator_session_id text not null,
    surface text not null,
    surface_subject text not null,
    auth_source text not null,
    active_mode text not null,
    unfinished_step_kind text null,
    action_type text null,
    session_event_type text null,
    decision_outcome text not null,
    failure_reason text null,
    details_json jsonb not null default '{}'::jsonb,
    event_time_utc timestamptz not null
);

create index if not exists ix_operator_audit_events_scope_person_time
    on operator_audit_events(scope_key, tracked_person_id, event_time_utc desc);

create index if not exists ix_operator_audit_events_request_id
    on operator_audit_events(request_id, event_time_utc desc);

create index if not exists ix_operator_audit_events_operator_session
    on operator_audit_events(operator_session_id, event_time_utc desc);
