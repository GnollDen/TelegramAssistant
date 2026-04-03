create table if not exists stage8_recompute_queue_items (
    id uuid primary key,
    scope_key text not null,
    person_id uuid references persons(id) on delete set null,
    target_family text not null,
    target_ref text not null,
    dedupe_key text not null,
    active_dedupe_key text,
    trigger_kind text not null,
    trigger_ref text,
    status text not null default 'pending',
    priority integer not null default 100,
    attempt_count integer not null default 0,
    max_attempts integer not null default 5,
    available_at_utc timestamptz not null default now(),
    leased_until_utc timestamptz,
    lease_token uuid,
    last_error text,
    last_result_status text,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    completed_at_utc timestamptz
);

create unique index if not exists uq_stage8_recompute_queue_items_active_dedupe
    on stage8_recompute_queue_items(active_dedupe_key)
    where active_dedupe_key is not null;

create index if not exists idx_stage8_recompute_queue_items_due
    on stage8_recompute_queue_items(status, available_at_utc, priority, created_at_utc);

create index if not exists idx_stage8_recompute_queue_items_scope_family
    on stage8_recompute_queue_items(scope_key, target_family, status, available_at_utc);

create index if not exists idx_stage8_recompute_queue_items_person_family
    on stage8_recompute_queue_items(person_id, target_family, status)
    where person_id is not null;

create index if not exists idx_stage8_recompute_queue_items_last_model_pass_run
    on stage8_recompute_queue_items(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
