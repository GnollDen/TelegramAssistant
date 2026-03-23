alter table if exists ops_chat_phase_guards
    add column if not exists lease_expires_at timestamptz;

alter table if exists ops_chat_phase_guards
    add column if not exists last_recovery_at timestamptz;

alter table if exists ops_chat_phase_guards
    add column if not exists last_recovery_from_owner_id text;

alter table if exists ops_chat_phase_guards
    add column if not exists last_recovery_code text;

alter table if exists ops_chat_phase_guards
    add column if not exists last_recovery_reason text;

update ops_chat_phase_guards
set lease_expires_at = updated_at + interval '30 minutes'
where active_phase is not null
  and lease_expires_at is null;

create index if not exists idx_ops_chat_phase_guards_lease_expires
    on ops_chat_phase_guards(lease_expires_at);
