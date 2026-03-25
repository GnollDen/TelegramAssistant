create table if not exists stage6_cases (
    id uuid primary key,
    scope_case_id bigint not null,
    chat_id bigint,
    scope_type text not null default 'chat',
    case_type text not null,
    case_subtype text,
    status text not null,
    priority text not null default 'important',
    confidence real,
    reason_summary text not null default '',
    clarification_kind text,
    question_text text,
    response_mode text,
    response_channel_hint text,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    subject_refs_json jsonb not null default '[]'::jsonb,
    target_artifact_types_json jsonb not null default '[]'::jsonb,
    reopen_trigger_rules_json jsonb not null default '[]'::jsonb,
    provenance_json jsonb not null default '{}'::jsonb,
    source_object_type text not null,
    source_object_id text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    ready_at timestamptz,
    resolved_at timestamptz,
    rejected_at timestamptz,
    stale_at timestamptz,
    constraint ck_stage6_cases_type check (case_type in (
        'needs_input',
        'needs_review',
        'risk',
        'state_refresh_needed',
        'dossier_candidate',
        'draft_candidate',
        'clarification_missing_data',
        'clarification_ambiguity',
        'clarification_evidence_interpretation_conflict',
        'clarification_next_step_blocked',
        'user_context_correction',
        'user_context_conflict_review'
    )),
    constraint ck_stage6_cases_status check (status in (
        'new',
        'ready',
        'needs_user_input',
        'resolved',
        'rejected',
        'stale'
    ))
);

create unique index if not exists ux_stage6_cases_natural_identity
    on stage6_cases(scope_case_id, case_type, source_object_type, source_object_id);

create index if not exists idx_stage6_cases_scope_status_priority
    on stage6_cases(scope_case_id, status, priority, updated_at desc);

create index if not exists idx_stage6_cases_scope_type_status
    on stage6_cases(scope_case_id, case_type, status, updated_at desc);

create table if not exists stage6_case_links (
    id uuid primary key,
    stage6_case_id uuid not null references stage6_cases(id) on delete cascade,
    linked_object_type text not null,
    linked_object_id text not null,
    link_role text not null,
    metadata_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now()
);

create unique index if not exists ux_stage6_case_links_natural_identity
    on stage6_case_links(stage6_case_id, linked_object_type, linked_object_id, link_role);

create index if not exists idx_stage6_case_links_by_object
    on stage6_case_links(linked_object_type, linked_object_id, created_at desc);

create table if not exists stage6_user_context_entries (
    id uuid primary key,
    stage6_case_id uuid references stage6_cases(id) on delete set null,
    scope_case_id bigint not null,
    chat_id bigint not null,
    source_kind text not null,
    clarification_question_id uuid references domain_clarification_questions(id) on delete set null,
    content_text text not null,
    structured_payload_json jsonb,
    applies_to_refs_json jsonb not null default '[]'::jsonb,
    entered_via text not null,
    user_reported_certainty real not null,
    source_type text not null,
    source_id text not null,
    source_message_id bigint,
    source_session_id uuid,
    supersedes_context_entry_id uuid references stage6_user_context_entries(id) on delete set null,
    conflicts_with_refs_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    constraint ck_stage6_user_context_source_kind check (source_kind in (
        'clarification_answer',
        'long_form_context',
        'offline_context_note'
    ))
);

create index if not exists idx_stage6_user_context_scope_created
    on stage6_user_context_entries(scope_case_id, created_at desc);

create index if not exists idx_stage6_user_context_case_created
    on stage6_user_context_entries(stage6_case_id, created_at desc);
