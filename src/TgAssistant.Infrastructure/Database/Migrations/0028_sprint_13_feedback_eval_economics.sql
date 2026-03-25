alter table if exists analysis_usage_events
    add column if not exists latency_ms integer;

alter table if exists ops_eval_runs
    add column if not exists scenario_pack_key text;

alter table if exists ops_eval_scenario_results
    add column if not exists scenario_type text not null default 'quality';

alter table if exists ops_eval_scenario_results
    add column if not exists latency_ms integer not null default 0;

alter table if exists ops_eval_scenario_results
    add column if not exists cost_usd numeric(12,6) not null default 0;

alter table if exists ops_eval_scenario_results
    add column if not exists model_summary_json jsonb not null default '{}'::jsonb;

alter table if exists ops_eval_scenario_results
    add column if not exists feedback_summary_json jsonb not null default '{}'::jsonb;

create table if not exists stage6_feedback_entries (
    id uuid primary key,
    scope_case_id bigint not null,
    chat_id bigint null,
    stage6_case_id uuid null references stage6_cases(id) on delete set null,
    artifact_type text null,
    feedback_kind text not null,
    feedback_dimension text not null default 'general',
    is_useful boolean null,
    note text null,
    source_channel text not null default 'web',
    actor text not null default 'operator',
    created_at timestamptz not null default now(),
    constraint ck_stage6_feedback_kind check (feedback_kind in (
        'accept_useful',
        'reject_not_useful',
        'correction_note',
        'refresh_requested'
    )),
    constraint ck_stage6_feedback_dimension check (feedback_dimension in (
        'general',
        'clarification_usefulness',
        'behavioral_usefulness'
    ))
);

create index if not exists idx_stage6_feedback_scope_time
    on stage6_feedback_entries(scope_case_id, chat_id, created_at desc);

create index if not exists idx_stage6_feedback_case_time
    on stage6_feedback_entries(stage6_case_id, created_at desc);

create index if not exists idx_stage6_feedback_artifact_time
    on stage6_feedback_entries(artifact_type, created_at desc);

create table if not exists stage6_case_outcomes (
    id uuid primary key,
    stage6_case_id uuid not null references stage6_cases(id) on delete cascade,
    scope_case_id bigint not null,
    chat_id bigint null,
    outcome_type text not null,
    case_status_after text not null,
    user_context_material boolean not null default false,
    note text null,
    source_channel text not null default 'web',
    actor text not null default 'operator',
    created_at timestamptz not null default now(),
    constraint ck_stage6_case_outcome_type check (outcome_type in (
        'resolved',
        'rejected',
        'stale',
        'refreshed',
        'answered_by_user'
    )),
    constraint ck_stage6_case_outcome_status_after check (case_status_after in (
        'new',
        'ready',
        'needs_user_input',
        'resolved',
        'rejected',
        'stale'
    ))
);

create index if not exists idx_stage6_case_outcomes_case_time
    on stage6_case_outcomes(stage6_case_id, created_at desc);

create index if not exists idx_stage6_case_outcomes_scope_time
    on stage6_case_outcomes(scope_case_id, chat_id, created_at desc);
