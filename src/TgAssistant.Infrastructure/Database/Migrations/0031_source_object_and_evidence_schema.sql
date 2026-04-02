create table if not exists source_objects (
    id uuid primary key,
    scope_key text not null,
    source_kind text not null,
    source_ref text not null,
    provenance_kind text not null,
    provenance_ref text not null,
    provenance_normalized text not null,
    status text not null default 'active',
    display_label text not null,
    chat_id bigint,
    source_message_id bigint references messages(id) on delete set null,
    source_session_id uuid references chat_sessions(id) on delete set null,
    archive_import_run_id uuid references archive_import_runs(id) on delete set null,
    occurred_at timestamptz,
    payload_json jsonb not null default '{}'::jsonb,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_source_objects_scope_ref unique (scope_key, source_kind, source_ref)
);

create index if not exists idx_source_objects_scope_provenance
    on source_objects(scope_key, provenance_kind, provenance_normalized);

create index if not exists idx_source_objects_source_message
    on source_objects(source_message_id)
    where source_message_id is not null;

create index if not exists idx_source_objects_source_session
    on source_objects(source_session_id)
    where source_session_id is not null;

create index if not exists idx_source_objects_archive_import_run
    on source_objects(archive_import_run_id)
    where archive_import_run_id is not null;

create index if not exists idx_source_objects_scope_chat_occurred
    on source_objects(scope_key, chat_id, occurred_at)
    where chat_id is not null and occurred_at is not null;

create table if not exists evidence_items (
    id uuid primary key,
    scope_key text not null,
    source_object_id uuid not null references source_objects(id) on delete cascade,
    evidence_kind text not null,
    status text not null default 'active',
    truth_layer text not null default 'canonical_truth',
    summary_text text,
    structured_payload_json jsonb not null default '{}'::jsonb,
    provenance_json jsonb not null default '{}'::jsonb,
    confidence real not null default 1,
    observed_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists idx_evidence_items_scope_source_kind
    on evidence_items(scope_key, source_object_id, evidence_kind, truth_layer);

create index if not exists idx_evidence_items_scope_status_observed
    on evidence_items(scope_key, status, observed_at);

create index if not exists idx_evidence_items_scope_kind_created
    on evidence_items(scope_key, evidence_kind, created_at);

create table if not exists evidence_item_person_links (
    id bigserial primary key,
    evidence_item_id uuid not null references evidence_items(id) on delete cascade,
    person_id uuid not null references persons(id) on delete cascade,
    scope_key text not null,
    link_role text not null default 'subject',
    is_primary boolean not null default false,
    created_at timestamptz not null default now(),
    constraint uq_evidence_item_person_links unique (evidence_item_id, person_id, link_role)
);

create index if not exists idx_evidence_item_person_links_scope_person_role
    on evidence_item_person_links(scope_key, person_id, link_role);

create index if not exists idx_evidence_item_person_links_scope_person_primary
    on evidence_item_person_links(scope_key, person_id, is_primary);
