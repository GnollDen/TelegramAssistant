create table if not exists operator_resolution_conflict_sessions (
    id uuid primary key,
    request_id text not null,
    scope_key text not null,
    tracked_person_id uuid not null,
    scope_item_key text not null,
    item_type text not null,
    source_kind text not null,
    source_ref text not null,
    operator_id text not null,
    operator_session_id text not null,
    surface text not null,
    status text not null,
    state_reason text null,
    revision integer not null default 1,
    question_count integer not null default 0,
    answer_count integer not null default 0,
    model_call_count integer not null default 0,
    case_packet_json jsonb not null default '{}'::jsonb,
    question_json jsonb null,
    answer_json jsonb null,
    verdict_json jsonb null,
    normalization_proposal_json jsonb null,
    audit_trail_json jsonb not null default '[]'::jsonb,
    failure_reason text null,
    started_at_utc timestamptz not null,
    expires_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    completed_at_utc timestamptz null,
    final_action_id uuid null,
    final_action_request_id text null,
    constraint uq_operator_resolution_conflict_sessions_request_id unique (request_id),
    constraint ck_operator_resolution_conflict_sessions_surface
        check (surface in ('web')),
    constraint ck_operator_resolution_conflict_sessions_status
        check (status in ('running_initial','awaiting_operator_answer','running_final','ready_for_commit','needs_web_review','fallback','handed_off','expired','failed')),
    constraint ck_operator_resolution_conflict_sessions_revision
        check (revision >= 1),
    constraint ck_operator_resolution_conflict_sessions_question_count
        check (question_count >= 0 and question_count <= 1),
    constraint ck_operator_resolution_conflict_sessions_answer_count
        check (answer_count >= 0 and answer_count <= 1)
);

create index if not exists ix_operator_resolution_conflict_sessions_scope_item
    on operator_resolution_conflict_sessions(scope_key, tracked_person_id, scope_item_key, updated_at_utc desc);

create index if not exists ix_operator_resolution_conflict_sessions_operator_scope
    on operator_resolution_conflict_sessions(operator_session_id, scope_item_key, updated_at_utc desc);

create unique index if not exists uq_operator_resolution_conflict_sessions_open_per_scope
    on operator_resolution_conflict_sessions(operator_session_id, scope_item_key)
    where status in ('awaiting_operator_answer','ready_for_commit');

alter table operator_resolution_actions
    add column if not exists conflict_resolution_session_id uuid null;

alter table operator_resolution_actions
    add column if not exists conflict_verdict_revision integer null;

alter table operator_resolution_actions
    add column if not exists conflict_verdict_json jsonb null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_operator_resolution_actions_conflict_verdict_revision'
    ) then
        alter table operator_resolution_actions
            add constraint ck_operator_resolution_actions_conflict_verdict_revision
            check (conflict_verdict_revision is null or conflict_verdict_revision >= 1);
    end if;
end $$;

create index if not exists ix_operator_resolution_actions_conflict_session
    on operator_resolution_actions(conflict_resolution_session_id)
    where conflict_resolution_session_id is not null;
