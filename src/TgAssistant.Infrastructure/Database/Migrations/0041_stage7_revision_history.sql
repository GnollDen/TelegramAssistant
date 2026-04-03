alter table durable_dossiers
    add column if not exists current_revision_number integer not null default 1;

alter table durable_dossiers
    add column if not exists current_revision_hash text not null default '';

alter table durable_profiles
    add column if not exists current_revision_number integer not null default 1;

alter table durable_profiles
    add column if not exists current_revision_hash text not null default '';

alter table durable_events
    add column if not exists current_revision_number integer not null default 1;

alter table durable_events
    add column if not exists current_revision_hash text not null default '';

alter table durable_timeline_episodes
    add column if not exists current_revision_number integer not null default 1;

alter table durable_timeline_episodes
    add column if not exists current_revision_hash text not null default '';

alter table durable_story_arcs
    add column if not exists current_revision_number integer not null default 1;

alter table durable_story_arcs
    add column if not exists current_revision_hash text not null default '';

create table if not exists durable_dossier_revisions (
    id uuid primary key,
    durable_dossier_id uuid not null references durable_dossiers(id) on delete cascade,
    revision_number integer not null,
    revision_hash text not null,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null,
    coverage real not null,
    freshness real not null,
    stability real not null,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_durable_dossier_revisions_number unique (durable_dossier_id, revision_number),
    constraint uq_durable_dossier_revisions_hash unique (durable_dossier_id, revision_hash)
);

create index if not exists idx_durable_dossier_revisions_model_pass_run
    on durable_dossier_revisions(model_pass_run_id)
    where model_pass_run_id is not null;

create index if not exists idx_durable_dossier_revisions_created_at
    on durable_dossier_revisions(durable_dossier_id, created_at);

create table if not exists durable_profile_revisions (
    id uuid primary key,
    durable_profile_id uuid not null references durable_profiles(id) on delete cascade,
    revision_number integer not null,
    revision_hash text not null,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null,
    coverage real not null,
    freshness real not null,
    stability real not null,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_durable_profile_revisions_number unique (durable_profile_id, revision_number),
    constraint uq_durable_profile_revisions_hash unique (durable_profile_id, revision_hash)
);

create index if not exists idx_durable_profile_revisions_model_pass_run
    on durable_profile_revisions(model_pass_run_id)
    where model_pass_run_id is not null;

create index if not exists idx_durable_profile_revisions_created_at
    on durable_profile_revisions(durable_profile_id, created_at);

create table if not exists durable_event_revisions (
    id uuid primary key,
    durable_event_id uuid not null references durable_events(id) on delete cascade,
    revision_number integer not null,
    revision_hash text not null,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null,
    freshness real not null,
    stability real not null,
    boundary_confidence real not null,
    event_confidence real not null,
    closure_state text not null,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_durable_event_revisions_number unique (durable_event_id, revision_number),
    constraint uq_durable_event_revisions_hash unique (durable_event_id, revision_hash)
);

create index if not exists idx_durable_event_revisions_model_pass_run
    on durable_event_revisions(model_pass_run_id)
    where model_pass_run_id is not null;

create index if not exists idx_durable_event_revisions_created_at
    on durable_event_revisions(durable_event_id, created_at);

create table if not exists durable_timeline_episode_revisions (
    id uuid primary key,
    durable_timeline_episode_id uuid not null references durable_timeline_episodes(id) on delete cascade,
    revision_number integer not null,
    revision_hash text not null,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null,
    freshness real not null,
    stability real not null,
    boundary_confidence real not null,
    closure_state text not null,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_durable_timeline_episode_revisions_number unique (durable_timeline_episode_id, revision_number),
    constraint uq_durable_timeline_episode_revisions_hash unique (durable_timeline_episode_id, revision_hash)
);

create index if not exists idx_durable_timeline_episode_revisions_model_pass_run
    on durable_timeline_episode_revisions(model_pass_run_id)
    where model_pass_run_id is not null;

create index if not exists idx_durable_timeline_episode_revisions_created_at
    on durable_timeline_episode_revisions(durable_timeline_episode_id, created_at);

create table if not exists durable_story_arc_revisions (
    id uuid primary key,
    durable_story_arc_id uuid not null references durable_story_arcs(id) on delete cascade,
    revision_number integer not null,
    revision_hash text not null,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null,
    freshness real not null,
    stability real not null,
    boundary_confidence real not null,
    closure_state text not null,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    summary_json jsonb not null default '{}'::jsonb,
    payload_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint uq_durable_story_arc_revisions_number unique (durable_story_arc_id, revision_number),
    constraint uq_durable_story_arc_revisions_hash unique (durable_story_arc_id, revision_hash)
);

create index if not exists idx_durable_story_arc_revisions_model_pass_run
    on durable_story_arc_revisions(model_pass_run_id)
    where model_pass_run_id is not null;

create index if not exists idx_durable_story_arc_revisions_created_at
    on durable_story_arc_revisions(durable_story_arc_id, created_at);
