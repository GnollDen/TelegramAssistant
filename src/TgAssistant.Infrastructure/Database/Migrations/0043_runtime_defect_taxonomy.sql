create table if not exists runtime_defects (
    id uuid primary key,
    defect_class text not null,
    severity text not null,
    status text not null default 'open',
    scope_key text not null,
    dedupe_key text not null,
    run_id uuid references model_pass_runs(id) on delete set null,
    object_type text,
    object_ref text,
    summary text not null,
    details_json jsonb not null default '{}'::jsonb,
    occurrence_count integer not null default 1,
    escalation_action text not null,
    escalation_reason text not null default '',
    first_seen_at_utc timestamptz not null default now(),
    last_seen_at_utc timestamptz not null default now(),
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    resolved_at_utc timestamptz
);

create unique index if not exists uq_runtime_defects_open_dedupe
    on runtime_defects(dedupe_key)
    where status = 'open';

create index if not exists idx_runtime_defects_status_class_severity
    on runtime_defects(status, defect_class, severity, last_seen_at_utc desc);

create index if not exists idx_runtime_defects_scope
    on runtime_defects(scope_key, status, last_seen_at_utc desc);

create index if not exists idx_runtime_defects_run
    on runtime_defects(run_id)
    where run_id is not null;
