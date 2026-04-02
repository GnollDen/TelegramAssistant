create table if not exists persons (
    id uuid primary key,
    scope_key text not null,
    person_type text not null,
    display_name text not null,
    canonical_name text not null,
    status text not null default 'active',
    primary_actor_key text,
    primary_telegram_user_id bigint,
    primary_telegram_username text,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists idx_persons_scope_type_status
    on persons(scope_key, person_type, status);

create index if not exists idx_persons_scope_canonical_name
    on persons(scope_key, lower(canonical_name));

create unique index if not exists uq_persons_scope_actor_key
    on persons(scope_key, primary_actor_key)
    where primary_actor_key is not null;

create index if not exists idx_persons_scope_telegram_user_id
    on persons(scope_key, primary_telegram_user_id)
    where primary_telegram_user_id is not null;

create table if not exists person_operator_links (
    id bigserial primary key,
    scope_key text not null,
    operator_person_id uuid not null references persons(id) on delete cascade,
    person_id uuid not null references persons(id) on delete cascade,
    link_type text not null,
    status text not null default 'active',
    source_binding_type text,
    source_binding_value text,
    source_binding_normalized text,
    source_message_id bigint references messages(id) on delete set null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_person_operator_links unique (scope_key, operator_person_id, person_id, link_type)
);

create index if not exists idx_person_operator_links_person_status
    on person_operator_links(scope_key, person_id, status);

create index if not exists idx_person_operator_links_source_binding
    on person_operator_links(scope_key, source_binding_type, source_binding_normalized)
    where source_binding_type is not null and source_binding_normalized is not null;

create table if not exists person_identity_bindings (
    id bigserial primary key,
    person_id uuid not null references persons(id) on delete cascade,
    scope_key text not null,
    binding_type text not null,
    binding_value text not null,
    binding_normalized text not null,
    source_message_id bigint references messages(id) on delete set null,
    confidence real not null default 0,
    is_primary boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uq_person_identity_bindings unique (person_id, binding_type, binding_normalized)
);

create index if not exists idx_person_identity_bindings_scope_lookup
    on person_identity_bindings(scope_key, binding_type, binding_normalized);

create index if not exists idx_person_identity_bindings_source_message
    on person_identity_bindings(source_message_id)
    where source_message_id is not null;

create index if not exists idx_person_identity_bindings_person_primary
    on person_identity_bindings(person_id, binding_type, is_primary);

create table if not exists candidate_identity_states (
    id uuid primary key,
    scope_key text not null,
    candidate_type text not null default 'person',
    status text not null,
    display_label text not null,
    source_binding_type text not null,
    source_binding_value text not null,
    source_binding_normalized text not null,
    source_message_id bigint references messages(id) on delete set null,
    matched_person_id uuid references persons(id) on delete set null,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists idx_candidate_identity_states_scope_status
    on candidate_identity_states(scope_key, status);

create index if not exists idx_candidate_identity_states_scope_binding
    on candidate_identity_states(scope_key, source_binding_type, source_binding_normalized);

create index if not exists idx_candidate_identity_states_matched_person
    on candidate_identity_states(matched_person_id)
    where matched_person_id is not null;

create table if not exists relationship_edge_anchors (
    id uuid primary key,
    scope_key text not null,
    from_person_id uuid not null references persons(id) on delete cascade,
    to_person_id uuid not null references persons(id) on delete cascade,
    anchor_type text not null,
    status text not null default 'candidate',
    source_binding_type text,
    source_binding_value text,
    source_binding_normalized text,
    source_message_id bigint references messages(id) on delete set null,
    candidate_identity_state_id uuid references candidate_identity_states(id) on delete set null,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_relationship_edge_anchors_distinct_persons check (from_person_id <> to_person_id)
);

create index if not exists idx_relationship_edge_anchors_persons
    on relationship_edge_anchors(from_person_id, to_person_id, anchor_type);

create index if not exists idx_relationship_edge_anchors_scope_status
    on relationship_edge_anchors(scope_key, anchor_type, status);

create index if not exists idx_relationship_edge_anchors_scope_binding
    on relationship_edge_anchors(scope_key, source_binding_type, source_binding_normalized)
    where source_binding_type is not null and source_binding_normalized is not null;

create index if not exists idx_relationship_edge_anchors_candidate
    on relationship_edge_anchors(candidate_identity_state_id)
    where candidate_identity_state_id is not null;
