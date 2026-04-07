create table if not exists operator_handoff_token_consumptions (
    id uuid primary key,
    token_hash text not null,
    consumed_at_utc timestamptz not null default now(),
    expires_at_utc timestamptz not null,
    constraint ck_operator_handoff_token_hash
        check (btrim(token_hash) <> ''),
    constraint ck_operator_handoff_token_expiry
        check (expires_at_utc >= consumed_at_utc)
);

create unique index if not exists uq_operator_handoff_token_consumptions_hash
    on operator_handoff_token_consumptions(token_hash);

create index if not exists ix_operator_handoff_token_consumptions_expires
    on operator_handoff_token_consumptions(expires_at_utc);
