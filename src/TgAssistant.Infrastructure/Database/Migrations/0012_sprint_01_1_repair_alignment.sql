alter table if exists domain_periods
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_period_transitions
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_hypotheses
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_clarification_questions
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null,
    add column if not exists resolved_at timestamptz;

alter table if exists domain_clarification_answers
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_offline_events
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_state_snapshots
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_profile_snapshots
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_profile_traits
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_strategy_records
    add column if not exists chat_id bigint,
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_draft_records
    add column if not exists source_message_id bigint references messages(id) on delete set null;

alter table if exists domain_draft_outcomes
    add column if not exists source_message_id bigint references messages(id) on delete set null,
    add column if not exists source_session_id uuid references chat_sessions(id) on delete set null;

alter table if exists domain_inbox_items
    add column if not exists chat_id bigint,
    add column if not exists last_actor text,
    add column if not exists last_reason text;

alter table if exists domain_conflict_records
    add column if not exists chat_id bigint,
    add column if not exists last_actor text,
    add column if not exists last_reason text;

create table if not exists domain_review_events (
    id uuid primary key,
    object_type text not null,
    object_id text not null,
    action text not null,
    old_value_ref text,
    new_value_ref text,
    reason text,
    actor text not null,
    created_at timestamptz not null default now()
);

create index if not exists idx_domain_review_events_object_time
    on domain_review_events(object_type, object_id, created_at desc);

create index if not exists idx_domain_periods_chat on domain_periods(chat_id, start_at desc);
create index if not exists idx_domain_clarification_questions_chat_status on domain_clarification_questions(chat_id, status, priority);
create index if not exists idx_domain_state_snapshots_chat_asof on domain_state_snapshots(chat_id, as_of desc);
create index if not exists idx_domain_offline_events_chat_time on domain_offline_events(chat_id, timestamp_start desc);
