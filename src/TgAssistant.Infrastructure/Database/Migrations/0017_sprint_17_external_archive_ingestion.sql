create table if not exists external_archive_import_batches (
    run_id uuid primary key,
    case_id bigint not null,
    source_class text not null,
    source_ref text not null,
    import_batch_id text not null,
    request_payload_hash text not null,
    imported_at_utc timestamptz not null,
    actor text not null,
    record_count integer not null,
    accepted_count integer not null default 0,
    replayed_count integer not null default 0,
    rejected_count integer not null default 0,
    status text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists idx_external_archive_batches_dedup
    on external_archive_import_batches(case_id, source_class, source_ref, request_payload_hash);

create index if not exists idx_external_archive_batches_case_time
    on external_archive_import_batches(case_id, created_at desc);

create table if not exists external_archive_import_records (
    id uuid primary key,
    run_id uuid not null references external_archive_import_batches(run_id) on delete cascade,
    case_id bigint not null,
    source_class text not null,
    source_ref text not null,
    import_batch_id text not null,
    record_id text not null,
    occurred_at_utc timestamptz not null,
    record_type text not null,
    text text,
    subject_actor_key text,
    target_actor_key text,
    chat_id bigint,
    source_message_id bigint,
    source_session_id uuid,
    confidence real not null,
    raw_payload_json jsonb not null,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    truth_layer text not null,
    payload_hash text not null,
    base_weight real not null,
    confidence_multiplier real not null,
    corroboration_multiplier real not null,
    final_weight real not null,
    needs_clarification boolean not null default false,
    weighting_reason text not null,
    status text not null,
    created_at timestamptz not null default now()
);

create unique index if not exists idx_external_archive_records_run_record
    on external_archive_import_records(run_id, record_id);

create index if not exists idx_external_archive_records_natural_key
    on external_archive_import_records(case_id, source_class, source_ref, record_id);

create table if not exists external_archive_linkage_artifacts (
    id uuid primary key,
    run_id uuid not null references external_archive_import_batches(run_id) on delete cascade,
    record_row_id uuid not null references external_archive_import_records(id) on delete cascade,
    case_id bigint not null,
    link_type text not null,
    target_type text not null,
    target_id text not null,
    link_confidence real not null,
    reason text not null,
    review_status text not null,
    auto_apply_allowed boolean not null default false,
    created_at timestamptz not null default now()
);

create unique index if not exists idx_external_archive_linkages_unique
    on external_archive_linkage_artifacts(record_row_id, link_type, target_type, target_id);

create index if not exists idx_external_archive_linkages_case_review
    on external_archive_linkage_artifacts(case_id, review_status, created_at desc);
