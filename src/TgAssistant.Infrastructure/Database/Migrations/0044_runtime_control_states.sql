create table if not exists runtime_control_states (
    id bigserial primary key,
    state text not null,
    reason text not null default '',
    source text not null default '',
    details_json jsonb not null default '{}'::jsonb,
    is_active boolean not null default true,
    activated_at_utc timestamptz not null default now(),
    deactivated_at_utc timestamptz
);

create unique index if not exists uq_runtime_control_states_active
    on runtime_control_states(is_active)
    where is_active = true;

create index if not exists idx_runtime_control_states_state
    on runtime_control_states(state, activated_at_utc desc);
