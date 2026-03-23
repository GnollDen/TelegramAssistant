alter table if exists ops_chat_coordination_states
    add column if not exists last_listener_seen_at timestamptz;

create index if not exists idx_ops_chat_coordination_listener_seen
    on ops_chat_coordination_states(last_listener_seen_at desc);
