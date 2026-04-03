create table if not exists identity_merge_histories (
    id uuid primary key,
    scope_key text not null,
    target_person_id uuid not null references persons(id) on delete cascade,
    source_person_id uuid not null references persons(id) on delete cascade,
    confidence_tier text not null,
    status text not null,
    review_status text not null,
    reason text not null default '',
    requested_by text not null default 'system',
    reviewed_by text,
    review_note text,
    reversed_by text,
    reversal_reason text,
    model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    before_state_json jsonb not null default '{}'::jsonb,
    after_state_json jsonb not null default '{}'::jsonb,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    applied_at_utc timestamptz,
    reversed_at_utc timestamptz,
    constraint ck_identity_merge_histories_distinct_persons check (target_person_id <> source_person_id)
);

create index if not exists idx_identity_merge_histories_scope_status
    on identity_merge_histories(scope_key, status, created_at_utc desc);

create index if not exists idx_identity_merge_histories_scope_pair
    on identity_merge_histories(scope_key, target_person_id, source_person_id, created_at_utc desc);

create index if not exists idx_identity_merge_histories_run
    on identity_merge_histories(model_pass_run_id)
    where model_pass_run_id is not null;
