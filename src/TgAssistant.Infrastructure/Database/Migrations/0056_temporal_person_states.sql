create table if not exists temporal_person_states (
    id uuid primary key,
    scope_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    subject_ref text not null,
    fact_type text not null,
    fact_category text not null,
    value text not null,
    valid_from_utc timestamptz not null,
    valid_to_utc timestamptz null,
    confidence real null,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    state_status text not null default 'open',
    supersedes_state_id uuid null references temporal_person_states(id) on delete set null,
    superseded_by_state_id uuid null references temporal_person_states(id) on delete set null,
    trigger_kind text not null,
    trigger_ref text null,
    trigger_model_pass_run_id uuid null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ck_temporal_person_states_fact_category
        check (fact_category in ('stable', 'temporal', 'event_conditioned', 'contested')),
    constraint ck_temporal_person_states_state_status
        check (state_status in ('open', 'closed', 'superseded')),
    constraint ck_temporal_person_states_scope_key
        check (btrim(scope_key) <> ''),
    constraint ck_temporal_person_states_subject_ref
        check (btrim(subject_ref) <> ''),
    constraint ck_temporal_person_states_fact_type
        check (btrim(fact_type) <> ''),
    constraint ck_temporal_person_states_value
        check (btrim(value) <> ''),
    constraint ck_temporal_person_states_trigger_kind
        check (btrim(trigger_kind) <> ''),
    constraint ck_temporal_person_states_valid_range
        check (valid_to_utc is null or valid_to_utc >= valid_from_utc),
    constraint ck_temporal_person_states_evidence_refs_array
        check (jsonb_typeof(evidence_refs_json) = 'array')
);

create index if not exists ix_temporal_person_states_scope_person_fact_valid_from
    on temporal_person_states(scope_key, tracked_person_id, fact_type, valid_from_utc desc);

create index if not exists ix_temporal_person_states_scope_subject_fact_status
    on temporal_person_states(scope_key, subject_ref, fact_type, state_status, valid_from_utc desc);

create index if not exists ix_temporal_person_states_supersedes_state
    on temporal_person_states(supersedes_state_id)
    where supersedes_state_id is not null;

create index if not exists ix_temporal_person_states_superseded_by_state
    on temporal_person_states(superseded_by_state_id)
    where superseded_by_state_id is not null;

create unique index if not exists uq_temporal_person_states_single_open_active
    on temporal_person_states(scope_key, subject_ref, fact_type)
    where state_status = 'open'
      and valid_to_utc is null
      and fact_type in ('profile_status', 'profile_location', 'relationship_state', 'timeline_primary_activity');
