create table if not exists durable_events (
    id uuid primary key,
    scope_key text not null,
    person_id uuid not null references persons(id) on delete cascade,
    related_person_id uuid references persons(id) on delete set null,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    event_type text not null,
    status text not null default 'active',
    boundary_confidence real not null default 0,
    event_confidence real not null default 0,
    closure_state text not null default 'open',
    occurred_from_utc timestamptz,
    occurred_to_utc timestamptz,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_events_scope_person_type unique (scope_key, person_id, event_type)
);

create unique index if not exists uq_durable_events_metadata
    on durable_events(durable_object_metadata_id);

create index if not exists idx_durable_events_scope_person_status
    on durable_events(scope_key, person_id, status);

create index if not exists idx_durable_events_related_person
    on durable_events(scope_key, related_person_id, status)
    where related_person_id is not null;

create index if not exists idx_durable_events_last_model_pass_run
    on durable_events(last_model_pass_run_id)
    where last_model_pass_run_id is not null;

create table if not exists durable_timeline_episodes (
    id uuid primary key,
    scope_key text not null,
    person_id uuid not null references persons(id) on delete cascade,
    related_person_id uuid references persons(id) on delete set null,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    episode_type text not null,
    status text not null default 'active',
    boundary_confidence real not null default 0,
    closure_state text not null default 'open',
    started_at_utc timestamptz,
    ended_at_utc timestamptz,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_timeline_episodes_scope_person_type unique (scope_key, person_id, episode_type)
);

create unique index if not exists uq_durable_timeline_episodes_metadata
    on durable_timeline_episodes(durable_object_metadata_id);

create index if not exists idx_durable_timeline_episodes_scope_person_status
    on durable_timeline_episodes(scope_key, person_id, status);

create index if not exists idx_durable_timeline_episodes_related_person
    on durable_timeline_episodes(scope_key, related_person_id, status)
    where related_person_id is not null;

create index if not exists idx_durable_timeline_episodes_last_model_pass_run
    on durable_timeline_episodes(last_model_pass_run_id)
    where last_model_pass_run_id is not null;

create table if not exists durable_story_arcs (
    id uuid primary key,
    scope_key text not null,
    person_id uuid not null references persons(id) on delete cascade,
    related_person_id uuid references persons(id) on delete set null,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    arc_type text not null,
    status text not null default 'active',
    boundary_confidence real not null default 0,
    closure_state text not null default 'open',
    opened_at_utc timestamptz,
    closed_at_utc timestamptz,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_story_arcs_scope_person_type unique (scope_key, person_id, arc_type)
);

create unique index if not exists uq_durable_story_arcs_metadata
    on durable_story_arcs(durable_object_metadata_id);

create index if not exists idx_durable_story_arcs_scope_person_status
    on durable_story_arcs(scope_key, person_id, status);

create index if not exists idx_durable_story_arcs_related_person
    on durable_story_arcs(scope_key, related_person_id, status)
    where related_person_id is not null;

create index if not exists idx_durable_story_arcs_last_model_pass_run
    on durable_story_arcs(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
