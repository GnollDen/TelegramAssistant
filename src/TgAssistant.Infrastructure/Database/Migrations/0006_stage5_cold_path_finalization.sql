alter table if exists chat_dialog_summaries
    add column if not exists is_finalized boolean not null default false;

create index if not exists idx_chat_dialog_summaries_finalized
    on chat_dialog_summaries(chat_id, summary_type, is_finalized, period_end desc);
