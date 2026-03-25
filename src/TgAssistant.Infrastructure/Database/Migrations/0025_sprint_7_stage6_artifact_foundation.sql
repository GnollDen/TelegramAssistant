create table if not exists stage6_artifacts (
    id uuid primary key,
    artifact_type text not null,
    case_id bigint not null,
    chat_id bigint null,
    scope_key text not null,
    payload_object_type text null,
    payload_object_id text null,
    payload_json jsonb not null default '{}'::jsonb,
    freshness_basis_hash text not null,
    freshness_basis_json jsonb not null default '{}'::jsonb,
    generated_at timestamptz not null,
    refreshed_at timestamptz null,
    stale_at timestamptz null,
    is_stale boolean not null default false,
    stale_reason text null,
    reuse_count integer not null default 0,
    is_current boolean not null default true,
    source_type text not null,
    source_id text not null,
    source_message_id bigint null,
    source_session_id uuid null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists ux_stage6_artifacts_current_scope
    on stage6_artifacts(artifact_type, case_id, chat_id, scope_key)
    where is_current = true;

create index if not exists idx_stage6_artifacts_case_chat_type_generated
    on stage6_artifacts(case_id, chat_id, artifact_type, generated_at desc);

create index if not exists idx_stage6_artifacts_payload_object
    on stage6_artifacts(payload_object_type, payload_object_id, generated_at desc);
