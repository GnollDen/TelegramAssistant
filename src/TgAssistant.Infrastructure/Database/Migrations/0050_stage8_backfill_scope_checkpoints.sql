create table if not exists stage8_backfill_scope_checkpoints (
    scope_key text primary key,
    status text not null default 'ready',
    active_queue_item_id uuid references stage8_recompute_queue_items(id) on delete set null,
    active_target_family text,
    active_lease_token uuid,
    active_lease_owner text,
    lease_expires_at_utc timestamptz,
    last_queue_item_id uuid references stage8_recompute_queue_items(id) on delete set null,
    last_target_family text,
    last_result_status text,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    last_error text,
    completed_item_count integer not null default 0,
    failed_item_count integer not null default 0,
    resume_count integer not null default 0,
    first_started_at_utc timestamptz not null default now(),
    last_checkpoint_at_utc timestamptz,
    last_completed_at_utc timestamptz,
    updated_at_utc timestamptz not null default now()
);

create index if not exists idx_stage8_backfill_scope_checkpoints_status_lease
    on stage8_backfill_scope_checkpoints(status, lease_expires_at_utc, updated_at_utc);

create index if not exists idx_stage8_backfill_scope_checkpoints_last_model_pass_run
    on stage8_backfill_scope_checkpoints(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
