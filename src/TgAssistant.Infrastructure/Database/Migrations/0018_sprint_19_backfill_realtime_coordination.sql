create table if not exists ops_chat_coordination_states (
    chat_id bigint primary key,
    state text not null,
    reason text not null default '',
    last_backfill_started_at timestamptz,
    last_backfill_completed_at timestamptz,
    handover_ready_at timestamptz,
    realtime_activated_at timestamptz,
    updated_at timestamptz not null default now()
);

create index if not exists idx_ops_chat_coordination_state_updated
    on ops_chat_coordination_states(state, updated_at desc);
