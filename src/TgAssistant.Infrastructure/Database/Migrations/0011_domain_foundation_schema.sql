create table if not exists domain_periods (
    id uuid primary key,
    case_id bigint not null,
    label text not null,
    custom_label text,
    start_at timestamptz not null,
    end_at timestamptz,
    is_open boolean not null default false,
    summary text not null,
    key_signals_json jsonb not null default '[]'::jsonb,
    what_helped text not null default '',
    what_hurt text not null default '',
    open_questions_count integer not null default 0,
    boundary_confidence real not null default 0,
    interpretation_confidence real not null default 0,
    review_priority smallint not null default 0,
    is_sensitive boolean not null default false,
    status_snapshot text not null default '',
    dynamic_snapshot text not null default '',
    lessons text,
    strategic_patterns text,
    manual_notes text,
    user_override_summary text,
    source_type text not null default 'system',
    source_id text not null default '',
    evidence_refs_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_periods_case_start on domain_periods(case_id, start_at desc);
create index if not exists idx_periods_case_open on domain_periods(case_id, is_open);

create table if not exists domain_period_transitions (
    id uuid primary key,
    from_period_id uuid not null references domain_periods(id) on delete cascade,
    to_period_id uuid not null references domain_periods(id) on delete cascade,
    transition_type text not null,
    summary text not null,
    is_resolved boolean not null default false,
    confidence real not null default 0,
    gap_id uuid,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    source_type text not null default 'system',
    source_id text not null default '',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_period_transitions_from on domain_period_transitions(from_period_id);
create index if not exists idx_period_transitions_to on domain_period_transitions(to_period_id);
create index if not exists idx_period_transitions_resolved on domain_period_transitions(is_resolved);

create table if not exists domain_hypotheses (
    id uuid primary key,
    hypothesis_type text not null,
    subject_type text not null,
    subject_id text not null,
    case_id bigint not null,
    period_id uuid references domain_periods(id) on delete set null,
    statement text not null,
    confidence real not null default 0,
    status text not null,
    source_type text not null,
    source_id text not null,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    conflict_refs_json jsonb not null default '[]'::jsonb,
    validation_targets_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_hypotheses_case_status on domain_hypotheses(case_id, status);
create index if not exists idx_hypotheses_period on domain_hypotheses(period_id);

create table if not exists domain_clarification_questions (
    id uuid primary key,
    case_id bigint not null,
    question_text text not null,
    question_type text not null,
    priority text not null,
    status text not null,
    period_id uuid references domain_periods(id) on delete set null,
    related_hypothesis_id uuid references domain_hypotheses(id) on delete set null,
    affected_outputs_json jsonb not null default '[]'::jsonb,
    why_it_matters text not null default '',
    expected_gain real not null default 0,
    answer_options_json jsonb not null default '[]'::jsonb,
    source_type text not null default 'system',
    source_id text not null default '',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_clarification_questions_case_status_priority on domain_clarification_questions(case_id, status, priority);
create index if not exists idx_clarification_questions_period on domain_clarification_questions(period_id);

create table if not exists domain_clarification_answers (
    id uuid primary key,
    question_id uuid not null references domain_clarification_questions(id) on delete cascade,
    answer_type text not null,
    answer_value text not null,
    answer_confidence real not null default 0,
    source_class text not null,
    affected_objects_json jsonb not null default '[]'::jsonb,
    source_type text not null,
    source_id text not null,
    created_at timestamptz not null default now()
);
create index if not exists idx_clarification_answers_question on domain_clarification_answers(question_id);

create table if not exists domain_offline_events (
    id uuid primary key,
    case_id bigint not null,
    event_type text not null,
    title text not null,
    user_summary text not null,
    auto_summary text,
    timestamp_start timestamptz not null,
    timestamp_end timestamptz,
    period_id uuid references domain_periods(id) on delete set null,
    review_status text not null,
    impact_summary text,
    source_type text not null,
    source_id text not null,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_offline_events_case_time on domain_offline_events(case_id, timestamp_start desc);
create index if not exists idx_offline_events_case_review_status on domain_offline_events(case_id, review_status);

create table if not exists domain_audio_assets (
    id uuid primary key,
    offline_event_id uuid not null references domain_offline_events(id) on delete cascade,
    file_path text not null,
    duration_seconds integer,
    transcript_status text not null,
    transcript_text text,
    speaker_review_status text not null,
    processing_status text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_audio_assets_offline_event on domain_audio_assets(offline_event_id);

create table if not exists domain_audio_segments (
    id uuid primary key,
    audio_asset_id uuid not null references domain_audio_assets(id) on delete cascade,
    segment_index integer not null,
    start_seconds numeric(10, 3) not null,
    end_seconds numeric(10, 3) not null,
    speaker_label text,
    transcript_text text not null,
    confidence real not null default 0,
    created_at timestamptz not null default now(),
    constraint uq_domain_audio_segments_asset_segment unique (audio_asset_id, segment_index)
);

create table if not exists domain_audio_snippets (
    id uuid primary key,
    audio_asset_id uuid not null references domain_audio_assets(id) on delete cascade,
    audio_segment_id uuid references domain_audio_segments(id) on delete set null,
    snippet_type text not null,
    text text not null,
    confidence real not null default 0,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now()
);
create index if not exists idx_audio_snippets_asset on domain_audio_snippets(audio_asset_id);
create index if not exists idx_audio_snippets_segment on domain_audio_snippets(audio_segment_id);

create table if not exists domain_state_snapshots (
    id uuid primary key,
    case_id bigint not null,
    as_of timestamptz not null,
    dynamic_label text not null,
    relationship_status text not null,
    alternative_status text,
    initiative_score real not null,
    responsiveness_score real not null,
    openness_score real not null,
    warmth_score real not null,
    reciprocity_score real not null,
    ambiguity_score real not null,
    avoidance_risk_score real not null,
    escalation_readiness_score real not null,
    external_pressure_score real not null,
    confidence real not null,
    period_id uuid references domain_periods(id) on delete set null,
    key_signal_refs_json jsonb not null default '[]'::jsonb,
    risk_refs_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now()
);
create index if not exists idx_state_snapshots_case_asof on domain_state_snapshots(case_id, as_of desc);

create table if not exists domain_profile_snapshots (
    id uuid primary key,
    subject_type text not null,
    subject_id text not null,
    case_id bigint not null,
    period_id uuid references domain_periods(id) on delete set null,
    summary text not null,
    confidence real not null,
    stability real not null,
    created_at timestamptz not null default now()
);
create index if not exists idx_profile_snapshots_case_subject_time on domain_profile_snapshots(case_id, subject_type, subject_id, created_at desc);

create table if not exists domain_profile_traits (
    id uuid primary key,
    profile_snapshot_id uuid not null references domain_profile_snapshots(id) on delete cascade,
    trait_key text not null,
    value_label text not null,
    confidence real not null,
    stability real not null,
    is_sensitive boolean not null default false,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    created_at timestamptz not null default now()
);
create index if not exists idx_profile_traits_snapshot_trait on domain_profile_traits(profile_snapshot_id, trait_key);

create table if not exists domain_strategy_records (
    id uuid primary key,
    case_id bigint not null,
    period_id uuid references domain_periods(id) on delete set null,
    state_snapshot_id uuid references domain_state_snapshots(id) on delete set null,
    strategy_confidence real not null,
    recommended_goal text not null,
    why_not_others text not null,
    created_at timestamptz not null default now()
);
create index if not exists idx_strategy_records_case_time on domain_strategy_records(case_id, created_at desc);

create table if not exists domain_strategy_options (
    id uuid primary key,
    strategy_record_id uuid not null references domain_strategy_records(id) on delete cascade,
    action_type text not null,
    summary text not null,
    purpose text not null,
    risk text not null,
    when_to_use text not null,
    success_signs text not null,
    failure_signs text not null,
    is_primary boolean not null default false
);
create index if not exists idx_strategy_options_record on domain_strategy_options(strategy_record_id);

create table if not exists domain_draft_records (
    id uuid primary key,
    strategy_record_id uuid not null references domain_strategy_records(id) on delete cascade,
    source_session_id uuid references chat_sessions(id) on delete set null,
    main_draft text not null,
    alt_draft_1 text,
    alt_draft_2 text,
    style_notes text,
    confidence real not null,
    created_at timestamptz not null default now()
);
create index if not exists idx_draft_records_strategy_record on domain_draft_records(strategy_record_id);

create table if not exists domain_draft_outcomes (
    id uuid primary key,
    draft_id uuid not null references domain_draft_records(id) on delete cascade,
    actual_message_id bigint references messages(id) on delete set null,
    match_score real,
    outcome_label text not null,
    notes text,
    created_at timestamptz not null default now()
);
create index if not exists idx_draft_outcomes_draft on domain_draft_outcomes(draft_id);

create table if not exists domain_inbox_items (
    id uuid primary key,
    item_type text not null,
    source_object_type text not null,
    source_object_id text not null,
    priority text not null,
    is_blocking boolean not null default false,
    title text not null,
    summary text not null,
    period_id uuid references domain_periods(id) on delete set null,
    case_id bigint not null,
    status text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_inbox_items_case_status_priority on domain_inbox_items(case_id, status, priority);

create table if not exists domain_conflict_records (
    id uuid primary key,
    conflict_type text not null,
    object_a_type text not null,
    object_a_id text not null,
    object_b_type text not null,
    object_b_id text not null,
    summary text not null,
    severity text not null,
    status text not null,
    period_id uuid references domain_periods(id) on delete set null,
    case_id bigint not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists idx_conflict_records_case_status_severity on domain_conflict_records(case_id, status, severity);

create table if not exists domain_dependency_links (
    id uuid primary key,
    upstream_type text not null,
    upstream_id text not null,
    downstream_type text not null,
    downstream_id text not null,
    link_type text not null,
    created_at timestamptz not null default now()
);
create index if not exists idx_dependency_links_upstream on domain_dependency_links(upstream_type, upstream_id);
create index if not exists idx_dependency_links_downstream on domain_dependency_links(downstream_type, downstream_id);
