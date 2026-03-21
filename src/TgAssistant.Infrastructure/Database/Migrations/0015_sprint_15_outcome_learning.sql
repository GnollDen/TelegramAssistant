alter table if exists domain_draft_outcomes
    add column if not exists strategy_record_id uuid references domain_strategy_records(id) on delete set null,
    add column if not exists follow_up_message_id bigint references messages(id) on delete set null,
    add column if not exists matched_by text,
    add column if not exists user_outcome_label text,
    add column if not exists system_outcome_label text,
    add column if not exists outcome_confidence real,
    add column if not exists learning_signals_json jsonb not null default '[]'::jsonb;

create index if not exists idx_draft_outcomes_strategy_record on domain_draft_outcomes(strategy_record_id);
create index if not exists idx_draft_outcomes_actual_message on domain_draft_outcomes(actual_message_id);
create index if not exists idx_draft_outcomes_follow_up_message on domain_draft_outcomes(follow_up_message_id);
