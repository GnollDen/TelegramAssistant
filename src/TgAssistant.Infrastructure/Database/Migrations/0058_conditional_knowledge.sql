create table if not exists conditional_knowledge_states (
    id uuid primary key,
    scope_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    fact_family text not null,
    subject_ref text not null,
    rule_kind text not null,
    rule_id uuid not null,
    parent_rule_id uuid null,
    baseline_value text null,
    exception_value text null,
    style_label text null,
    phase_label text null,
    phase_reason text null,
    condition_clause_ids_json jsonb not null default '[]'::jsonb,
    source_ref_ids_json jsonb not null default '[]'::jsonb,
    linked_temporal_state_ids_json jsonb not null default '[]'::jsonb,
    evidence_refs_json jsonb not null default '[]'::jsonb,
    valid_from_utc timestamptz not null,
    valid_to_utc timestamptz null,
    confidence real null,
    state_status text not null default 'open',
    supersedes_state_id uuid null references conditional_knowledge_states(id) on delete set null,
    superseded_by_state_id uuid null references conditional_knowledge_states(id) on delete set null,
    trigger_kind text not null,
    trigger_ref text null,
    trigger_model_pass_run_id uuid null,
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ck_conditional_knowledge_scope_key
        check (btrim(scope_key) <> ''),
    constraint ck_conditional_knowledge_fact_family
        check (fact_family in ('profile_preference', 'behavior_pattern', 'style_drift', 'phase_marker')),
    constraint ck_conditional_knowledge_subject_ref
        check (btrim(subject_ref) <> ''),
    constraint ck_conditional_knowledge_rule_kind
        check (rule_kind in ('baseline_rule', 'exception_rule', 'style_drift', 'phase_marker')),
    constraint ck_conditional_knowledge_state_status
        check (state_status in ('open', 'closed', 'superseded')),
    constraint ck_conditional_knowledge_trigger_kind
        check (btrim(trigger_kind) <> ''),
    constraint ck_conditional_knowledge_valid_range
        check (valid_to_utc is null or valid_to_utc >= valid_from_utc),
    constraint ck_conditional_knowledge_condition_clause_ids_array
        check (jsonb_typeof(condition_clause_ids_json) = 'array'),
    constraint ck_conditional_knowledge_source_ref_ids_array
        check (jsonb_typeof(source_ref_ids_json) = 'array'),
    constraint ck_conditional_knowledge_linked_temporal_state_ids_array
        check (jsonb_typeof(linked_temporal_state_ids_json) = 'array'),
    constraint ck_conditional_knowledge_evidence_refs_array
        check (jsonb_typeof(evidence_refs_json) = 'array'),
    constraint ck_conditional_knowledge_evidence_refs_required
        check (jsonb_array_length(evidence_refs_json) > 0),
    constraint ck_conditional_knowledge_rule_payload
        check (
            (rule_kind = 'baseline_rule' and baseline_value is not null and btrim(baseline_value) <> '')
            or (rule_kind = 'exception_rule' and exception_value is not null and btrim(exception_value) <> '' and parent_rule_id is not null)
            or (rule_kind = 'style_drift' and style_label is not null and btrim(style_label) <> '')
            or (rule_kind = 'phase_marker' and phase_label is not null and btrim(phase_label) <> '' and phase_reason is not null and btrim(phase_reason) <> '')
        )
);

create index if not exists ix_conditional_knowledge_scope_person_family_subject_valid_from
    on conditional_knowledge_states(scope_key, tracked_person_id, fact_family, subject_ref, valid_from_utc desc);

create index if not exists ix_conditional_knowledge_scope_person_status_valid_from
    on conditional_knowledge_states(scope_key, tracked_person_id, state_status, valid_from_utc desc);

create index if not exists ix_conditional_knowledge_supersedes_state
    on conditional_knowledge_states(supersedes_state_id)
    where supersedes_state_id is not null;

create index if not exists ix_conditional_knowledge_superseded_by_state
    on conditional_knowledge_states(superseded_by_state_id)
    where superseded_by_state_id is not null;

create unique index if not exists uq_conditional_knowledge_scope_rule_identity
    on conditional_knowledge_states(scope_key, tracked_person_id, fact_family, rule_kind, rule_id);
