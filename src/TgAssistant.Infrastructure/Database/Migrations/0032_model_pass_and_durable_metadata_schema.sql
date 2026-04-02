create table if not exists model_pass_runs (
    id uuid primary key,
    scope_key text not null,
    stage text not null,
    pass_family text not null,
    run_kind text not null,
    status text not null,
    target_type text not null,
    target_ref text not null,
    person_id uuid references persons(id) on delete set null,
    source_object_id uuid references source_objects(id) on delete set null,
    evidence_item_id uuid references evidence_items(id) on delete set null,
    trigger_kind text,
    trigger_ref text,
    schema_version integer not null default 1,
    requested_model text,
    input_summary_json jsonb not null default '{}'::jsonb,
    output_summary_json jsonb not null default '{}'::jsonb,
    metrics_json jsonb not null default '{}'::jsonb,
    failure_json jsonb not null default '{}'::jsonb,
    started_at timestamptz not null default now(),
    finished_at timestamptz,
    created_at timestamptz not null default now()
);

create index if not exists idx_model_pass_runs_scope_family_status
    on model_pass_runs(scope_key, stage, pass_family, status, started_at);

create index if not exists idx_model_pass_runs_target
    on model_pass_runs(scope_key, target_type, target_ref, started_at);

create index if not exists idx_model_pass_runs_person
    on model_pass_runs(scope_key, person_id, started_at)
    where person_id is not null;

create index if not exists idx_model_pass_runs_source_object
    on model_pass_runs(source_object_id)
    where source_object_id is not null;

create index if not exists idx_model_pass_runs_evidence_item
    on model_pass_runs(evidence_item_id)
    where evidence_item_id is not null;

create table if not exists normalization_runs (
    id uuid primary key,
    model_pass_run_id uuid not null references model_pass_runs(id) on delete cascade,
    scope_key text not null,
    status text not null,
    target_type text not null,
    target_ref text not null,
    truth_layer text not null,
    person_id uuid references persons(id) on delete set null,
    source_object_id uuid references source_objects(id) on delete set null,
    evidence_item_id uuid references evidence_items(id) on delete set null,
    schema_version integer not null default 1,
    candidate_counts_json jsonb not null default '{}'::jsonb,
    normalized_payload_json jsonb not null default '{}'::jsonb,
    conflicts_json jsonb not null default '[]'::jsonb,
    blocked_reason text,
    created_at timestamptz not null default now(),
    finished_at timestamptz,
    constraint uq_normalization_runs_model_pass unique (model_pass_run_id)
);

create index if not exists idx_normalization_runs_scope_status
    on normalization_runs(scope_key, status, created_at);

create index if not exists idx_normalization_runs_target
    on normalization_runs(scope_key, target_type, target_ref, created_at);

create index if not exists idx_normalization_runs_person
    on normalization_runs(scope_key, person_id, created_at)
    where person_id is not null;

create index if not exists idx_normalization_runs_source_object
    on normalization_runs(source_object_id)
    where source_object_id is not null;

create index if not exists idx_normalization_runs_evidence_item
    on normalization_runs(evidence_item_id)
    where evidence_item_id is not null;

create index if not exists idx_normalization_runs_truth_layer
    on normalization_runs(scope_key, truth_layer, status);

create table if not exists durable_object_metadata (
    id uuid primary key,
    scope_key text not null,
    object_family text not null,
    object_key text not null,
    status text not null default 'active',
    truth_layer text not null default 'proposal_layer',
    promotion_state text not null default 'pending',
    owner_person_id uuid references persons(id) on delete set null,
    related_person_id uuid references persons(id) on delete set null,
    created_by_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    last_normalization_run_id uuid references normalization_runs(id) on delete set null,
    last_promotion_run_id uuid references model_pass_runs(id) on delete set null,
    confidence real not null default 0,
    coverage real not null default 0,
    freshness real not null default 0,
    stability real not null default 0,
    contradiction_markers_json jsonb not null default '[]'::jsonb,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_durable_object_metadata unique (object_family, object_key)
);

create index if not exists idx_durable_object_metadata_scope_family_state
    on durable_object_metadata(scope_key, object_family, promotion_state, updated_at);

create index if not exists idx_durable_object_metadata_owner_person
    on durable_object_metadata(scope_key, owner_person_id, object_family)
    where owner_person_id is not null;

create index if not exists idx_durable_object_metadata_related_person
    on durable_object_metadata(scope_key, related_person_id, object_family)
    where related_person_id is not null;

create index if not exists idx_durable_object_metadata_created_by_run
    on durable_object_metadata(created_by_model_pass_run_id)
    where created_by_model_pass_run_id is not null;

create index if not exists idx_durable_object_metadata_normalization_run
    on durable_object_metadata(last_normalization_run_id)
    where last_normalization_run_id is not null;

create index if not exists idx_durable_object_metadata_promotion_run
    on durable_object_metadata(last_promotion_run_id)
    where last_promotion_run_id is not null;

create table if not exists durable_object_evidence_links (
    id bigserial primary key,
    durable_object_metadata_id uuid not null references durable_object_metadata(id) on delete cascade,
    scope_key text not null,
    evidence_item_id uuid not null references evidence_items(id) on delete cascade,
    link_role text not null default 'supporting',
    created_at timestamptz not null default now(),
    constraint uq_durable_object_evidence_links unique (durable_object_metadata_id, evidence_item_id, link_role)
);

create index if not exists idx_durable_object_evidence_links_scope_evidence
    on durable_object_evidence_links(scope_key, evidence_item_id, link_role);

create index if not exists idx_durable_object_evidence_links_scope_metadata
    on durable_object_evidence_links(scope_key, durable_object_metadata_id);
