alter table if exists chat_sessions
    add column if not exists is_analyzed boolean not null default false;

update chat_sessions
set is_analyzed = true
where is_finalized = true
  and coalesce(summary, '') <> '';

create index if not exists idx_chat_sessions_analysis_queue
    on chat_sessions(is_analyzed, is_finalized, last_message_at desc);
