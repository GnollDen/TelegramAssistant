create table if not exists ops_budget_operational_states (
    path_key text primary key,
    modality text not null,
    state text not null,
    reason text not null,
    details_json jsonb not null default '{}'::jsonb,
    updated_at timestamptz not null default now()
);

create index if not exists idx_ops_budget_state_updated
    on ops_budget_operational_states(state, updated_at desc);

create table if not exists ops_eval_runs (
    id uuid primary key,
    run_name text not null,
    passed boolean not null default false,
    started_at timestamptz not null,
    finished_at timestamptz not null,
    summary text not null default '',
    metrics_json jsonb not null default '{}'::jsonb
);

create index if not exists idx_ops_eval_runs_name_started
    on ops_eval_runs(run_name, started_at desc);

create table if not exists ops_eval_scenario_results (
    id uuid primary key,
    run_id uuid not null references ops_eval_runs(id) on delete cascade,
    scenario_name text not null,
    passed boolean not null,
    summary text not null default '',
    metrics_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);

create index if not exists idx_ops_eval_results_run_created
    on ops_eval_scenario_results(run_id, created_at);
