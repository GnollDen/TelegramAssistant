create table if not exists dossier_field_families (
    id uuid primary key,
    family_key text not null,
    canonical_category text not null,
    canonical_key text not null,
    approval_state text not null,
    is_seeded boolean not null default false,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists ux_dossier_field_families_family_key
    on dossier_field_families(family_key);

create index if not exists idx_dossier_field_families_canonical
    on dossier_field_families(canonical_category, canonical_key);

create index if not exists idx_dossier_field_families_approval
    on dossier_field_families(approval_state);

create table if not exists dossier_field_aliases (
    id bigserial primary key,
    dossier_field_family_id uuid not null references dossier_field_families(id) on delete cascade,
    alias_category text not null,
    alias_key text not null,
    alias_token text not null,
    approval_state text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists ux_dossier_field_aliases_alias_token
    on dossier_field_aliases(alias_token);

create index if not exists idx_dossier_field_aliases_family_approval
    on dossier_field_aliases(dossier_field_family_id, approval_state);
