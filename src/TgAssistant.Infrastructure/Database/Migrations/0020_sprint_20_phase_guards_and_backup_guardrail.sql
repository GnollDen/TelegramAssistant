create table if not exists ops_chat_phase_guards (
    chat_id bigint primary key,
    active_phase text,
    owner_id text,
    phase_reason text,
    active_since timestamptz,
    updated_at timestamptz not null default now(),
    last_requested_phase text,
    last_observed_phase text,
    last_deny_code text,
    last_deny_reason text,
    last_denied_at timestamptz,
    tail_reopen_window_from_utc timestamptz,
    tail_reopen_window_to_utc timestamptz,
    tail_reopen_operator text,
    tail_reopen_audit_id text
);

create index if not exists idx_ops_chat_phase_guards_active_phase_updated
    on ops_chat_phase_guards(active_phase, updated_at desc);

create table if not exists ops_backup_evidence_records (
    backup_id text primary key,
    created_at_utc timestamptz not null,
    scope text not null,
    artifact_uri text not null,
    checksum text not null,
    recorded_at_utc timestamptz not null default now(),
    recorded_by text not null default 'unknown',
    metadata_json jsonb not null default '{}'::jsonb
);

create index if not exists idx_ops_backup_evidence_created
    on ops_backup_evidence_records(created_at_utc desc);
