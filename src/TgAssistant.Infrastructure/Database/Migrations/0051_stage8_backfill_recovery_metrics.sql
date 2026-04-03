alter table stage8_backfill_scope_checkpoints
    add column if not exists retry_count integer not null default 0,
    add column if not exists deadlock_retry_count integer not null default 0,
    add column if not exists transient_retry_count integer not null default 0,
    add column if not exists last_recovery_kind text not null default 'none',
    add column if not exists last_recovery_at_utc timestamptz,
    add column if not exists last_backoff_until_utc timestamptz;

create index if not exists idx_stage8_backfill_scope_checkpoints_recovery
    on stage8_backfill_scope_checkpoints(last_recovery_kind, last_recovery_at_utc);
