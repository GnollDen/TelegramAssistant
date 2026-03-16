alter table if exists message_extractions
    add column if not exists is_quarantined boolean not null default false;

alter table if exists message_extractions
    add column if not exists quarantine_reason text;

alter table if exists message_extractions
    add column if not exists quarantined_at timestamptz;

create index if not exists idx_message_extractions_is_quarantined
    on message_extractions(is_quarantined)
    where is_quarantined = true;
