create table if not exists clarification_branch_states (
    id uuid primary key,
    scope_key text not null,
    branch_family text not null,
    branch_key text not null,
    stage text not null,
    pass_family text not null,
    target_type text not null,
    target_ref text not null,
    person_id uuid,
    last_model_pass_run_id uuid references model_pass_runs(id) on delete set null,
    status text not null default 'open',
    block_reason text not null default '',
    required_action text,
    details_json jsonb not null default '{}'::jsonb,
    first_blocked_at_utc timestamptz not null default now(),
    last_blocked_at_utc timestamptz not null default now(),
    resolved_at_utc timestamptz
);

create unique index if not exists uq_clarification_branch_states_branch_key
    on clarification_branch_states(branch_key);

create index if not exists idx_clarification_branch_states_scope_status
    on clarification_branch_states(scope_key, status, last_blocked_at_utc desc);

create index if not exists idx_clarification_branch_states_scope_family_status
    on clarification_branch_states(scope_key, branch_family, status, last_blocked_at_utc desc);

create index if not exists idx_clarification_branch_states_run
    on clarification_branch_states(last_model_pass_run_id)
    where last_model_pass_run_id is not null;
