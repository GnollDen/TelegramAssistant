alter table if exists identity_merge_histories
    add column if not exists recompute_plan_json jsonb not null default '{}'::jsonb;

alter table if exists identity_merge_histories
    add column if not exists recompute_enqueued_at_utc timestamptz;

create index if not exists idx_identity_merge_histories_recompute_enqueued
    on identity_merge_histories(recompute_enqueued_at_utc)
    where recompute_enqueued_at_utc is not null;
