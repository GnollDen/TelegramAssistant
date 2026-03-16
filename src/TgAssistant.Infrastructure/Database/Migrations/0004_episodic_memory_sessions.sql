create table if not exists chat_sessions (
    id uuid primary key default gen_random_uuid(),
    chat_id bigint not null,
    session_index integer not null,
    start_date timestamptz not null,
    end_date timestamptz not null,
    summary text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists idx_chat_sessions_chat_session
    on chat_sessions(chat_id, session_index);

create index if not exists idx_chat_sessions_chat_end
    on chat_sessions(chat_id, end_date desc);
