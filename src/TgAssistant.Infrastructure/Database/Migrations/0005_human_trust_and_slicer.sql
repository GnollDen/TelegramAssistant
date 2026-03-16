alter table entities
    add column if not exists is_user_confirmed boolean not null default false,
    add column if not exists trust_factor real not null default 1.0;

alter table facts
    add column if not exists is_user_confirmed boolean not null default false,
    add column if not exists trust_factor real not null default 1.0;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'chk_entities_trust_factor_range'
    ) then
        alter table entities
            add constraint chk_entities_trust_factor_range
            check (trust_factor >= 0 and trust_factor <= 1);
    end if;
end $$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'chk_facts_trust_factor_range'
    ) then
        alter table facts
            add constraint chk_facts_trust_factor_range
            check (trust_factor >= 0 and trust_factor <= 1);
    end if;
end $$;

create index if not exists idx_entities_confirmation_trust
    on entities(is_user_confirmed, trust_factor desc, updated_at desc);

create index if not exists idx_facts_entity_current_confirmation_trust
    on facts(entity_id, is_current, is_user_confirmed, trust_factor desc, updated_at desc);

create index if not exists idx_messages_processed_chat_timestamp_id
    on messages(chat_id, timestamp, id)
    where processing_status = 1;
