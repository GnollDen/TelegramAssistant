create table if not exists bootstrap_discovery_outputs (
    id uuid primary key,
    scope_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    discovery_type text not null,
    discovery_key text not null,
    person_id uuid references persons(id) on delete set null,
    candidate_identity_state_id uuid references candidate_identity_states(id) on delete set null,
    source_message_id bigint references messages(id) on delete set null,
    status text not null default 'active',
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_bootstrap_discovery_outputs unique (scope_key, tracked_person_id, discovery_type, discovery_key)
);

create index if not exists idx_bootstrap_discovery_outputs_scope_type
    on bootstrap_discovery_outputs(scope_key, tracked_person_id, discovery_type, status);

create index if not exists idx_bootstrap_discovery_outputs_person
    on bootstrap_discovery_outputs(scope_key, person_id, discovery_type)
    where person_id is not null;

create index if not exists idx_bootstrap_discovery_outputs_candidate
    on bootstrap_discovery_outputs(candidate_identity_state_id)
    where candidate_identity_state_id is not null;

create index if not exists idx_bootstrap_discovery_outputs_source_message
    on bootstrap_discovery_outputs(source_message_id)
    where source_message_id is not null;

create index if not exists idx_bootstrap_discovery_outputs_last_run
    on bootstrap_discovery_outputs(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
