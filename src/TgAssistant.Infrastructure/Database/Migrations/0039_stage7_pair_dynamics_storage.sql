create table if not exists durable_pair_dynamics (
    id uuid primary key,
    scope_key text not null,
    left_person_id uuid not null references persons(id) on delete cascade,
    right_person_id uuid not null references persons(id) on delete cascade,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    pair_dynamics_type text not null default 'operator_tracked_pair',
    status text not null default 'active',
    current_revision_number integer not null default 1,
    current_revision_hash text not null,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_pair_dynamics_scope_pair_type unique (scope_key, left_person_id, right_person_id, pair_dynamics_type),
    constraint ck_durable_pair_dynamics_distinct_persons check (left_person_id <> right_person_id)
);

create unique index if not exists uq_durable_pair_dynamics_metadata_id
    on durable_pair_dynamics(durable_object_metadata_id);

create index if not exists idx_durable_pair_dynamics_scope_pair_status
    on durable_pair_dynamics(scope_key, left_person_id, right_person_id, status);

create index if not exists idx_durable_pair_dynamics_last_model_pass_run
    on durable_pair_dynamics(last_model_pass_run_id)
    where last_model_pass_run_id is not null;

create table if not exists durable_pair_dynamics_revisions (
    id uuid primary key,
    durable_pair_dynamics_id uuid not null references durable_pair_dynamics(id) on delete cascade,
    revision_number integer not null,
    revision_hash text not null,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null default 0,
    freshness real not null default 0,
    stability real not null default 0,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_durable_pair_dynamics_revisions_number unique (durable_pair_dynamics_id, revision_number),
    constraint uq_durable_pair_dynamics_revisions_hash unique (durable_pair_dynamics_id, revision_hash)
);

create index if not exists idx_durable_pair_dynamics_revisions_model_pass_run
    on durable_pair_dynamics_revisions(model_pass_run_id)
    where model_pass_run_id is not null;

create index if not exists idx_durable_pair_dynamics_revisions_pair_created
    on durable_pair_dynamics_revisions(durable_pair_dynamics_id, created_at desc);
