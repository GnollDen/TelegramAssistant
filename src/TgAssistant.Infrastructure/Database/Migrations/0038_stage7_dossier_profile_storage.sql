create table if not exists durable_dossiers (
    id uuid primary key,
    scope_key text not null,
    person_id uuid not null references persons(id) on delete cascade,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    dossier_type text not null default 'person_dossier',
    status text not null default 'active',
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_dossiers_scope_person_type unique (scope_key, person_id, dossier_type)
);

create unique index if not exists uq_durable_dossiers_metadata_id
    on durable_dossiers(durable_object_metadata_id);

create index if not exists idx_durable_dossiers_scope_person_status
    on durable_dossiers(scope_key, person_id, status);

create index if not exists idx_durable_dossiers_last_model_pass_run
    on durable_dossiers(last_model_pass_run_id)
    where last_model_pass_run_id is not null;

create table if not exists durable_profiles (
    id uuid primary key,
    scope_key text not null,
    person_id uuid not null references persons(id) on delete cascade,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    profile_scope text not null default 'global',
    status text not null default 'active',
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_profiles_scope_person_scope unique (scope_key, person_id, profile_scope)
);

create unique index if not exists uq_durable_profiles_metadata_id
    on durable_profiles(durable_object_metadata_id);

create index if not exists idx_durable_profiles_scope_person_status
    on durable_profiles(scope_key, person_id, status);

create index if not exists idx_durable_profiles_last_model_pass_run
    on durable_profiles(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
