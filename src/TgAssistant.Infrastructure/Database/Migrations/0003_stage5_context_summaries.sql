create table if not exists chat_dialog_summaries (
    id uuid primary key default gen_random_uuid(),
    chat_id bigint not null,
    summary_type smallint not null,
    period_start timestamptz not null,
    period_end timestamptz not null,
    start_message_id bigint not null,
    end_message_id bigint not null,
    message_count int not null default 0,
    summary text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists uq_chat_dialog_summaries_scope
    on chat_dialog_summaries(chat_id, summary_type, period_start, period_end);

create index if not exists idx_chat_dialog_summaries_chat_period
    on chat_dialog_summaries(chat_id, period_end desc);

create index if not exists idx_messages_chat_processed_id
    on messages(chat_id, id)
    where processing_status = 1;
