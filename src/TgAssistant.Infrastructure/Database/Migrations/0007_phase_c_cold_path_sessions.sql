alter table if exists chat_sessions
    add column if not exists is_finalized boolean not null default false;

alter table if exists chat_sessions
    add column if not exists last_message_at timestamptz;

update chat_sessions
set last_message_at = coalesce(last_message_at, end_date)
where last_message_at is null;

alter table if exists chat_sessions
    alter column last_message_at set not null;

create index if not exists idx_chat_sessions_finalized_last_message_at
    on chat_sessions(is_finalized, last_message_at desc);
