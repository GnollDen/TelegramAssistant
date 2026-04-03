create table if not exists bootstrap_pool_outputs (
    id uuid primary key,
    scope_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    output_type text not null,
    output_key text not null,
    candidate_identity_state_id uuid references candidate_identity_states(id) on delete set null,
    relationship_edge_anchor_id uuid references relationship_edge_anchors(id) on delete set null,
    source_message_id bigint references messages(id) on delete set null,
    status text not null default 'active',
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_bootstrap_pool_outputs unique (scope_key, tracked_person_id, output_type, output_key)
);

create index if not exists idx_bootstrap_pool_outputs_scope_type_status
    on bootstrap_pool_outputs(scope_key, tracked_person_id, output_type, status);

create index if not exists idx_bootstrap_pool_outputs_candidate_identity_state
    on bootstrap_pool_outputs(candidate_identity_state_id)
    where candidate_identity_state_id is not null;

create index if not exists idx_bootstrap_pool_outputs_relationship_edge_anchor
    on bootstrap_pool_outputs(relationship_edge_anchor_id)
    where relationship_edge_anchor_id is not null;

create index if not exists idx_bootstrap_pool_outputs_source_message
    on bootstrap_pool_outputs(source_message_id)
    where source_message_id is not null;

create index if not exists idx_bootstrap_pool_outputs_last_model_pass_run
    on bootstrap_pool_outputs(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
