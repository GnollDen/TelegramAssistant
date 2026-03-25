with clarification_src as (
    select
        q.case_id as scope_case_id,
        q.chat_id,
        'chat'::text as scope_type,
        case
            when q.question_type = 'missing_data' then 'clarification_missing_data'
            when q.question_type = 'ambiguity' then 'clarification_ambiguity'
            when q.question_type = 'evidence_interpretation_conflict' then 'clarification_evidence_interpretation_conflict'
            when q.question_type = 'next_step_blocked' then 'clarification_next_step_blocked'
            else 'needs_input'
        end as case_type,
        q.question_type as case_subtype,
        case
            when q.status in ('open', 'in_progress') then 'needs_user_input'
            when q.status = 'answered' then 'ready'
            when q.status = 'resolved' then 'resolved'
            when q.status = 'rejected' then 'rejected'
            when q.status = 'stale' then 'stale'
            else 'new'
        end as status,
        case when q.priority in ('blocking', 'important', 'optional') then q.priority else 'important' end as priority,
        q.expected_gain as confidence,
        coalesce(nullif(q.why_it_matters, ''), q.question_text) as reason_summary,
        case
            when q.question_type in ('missing_data', 'ambiguity', 'evidence_interpretation_conflict', 'next_step_blocked')
                then q.question_type
            else null
        end as clarification_kind,
        q.question_text,
        'free_text'::text as response_mode,
        'bot_or_web'::text as response_channel_hint,
        '[]'::jsonb as evidence_refs_json,
        '[]'::jsonb as subject_refs_json,
        q.affected_outputs_json as target_artifact_types_json,
        '[]'::jsonb as reopen_trigger_rules_json,
        jsonb_build_object(
            'source_type', q.source_type,
            'source_id', q.source_id
        ) as provenance_json,
        'clarification_question'::text as source_object_type,
        q.id::text as source_object_id,
        q.created_at,
        q.updated_at,
        case when q.status = 'resolved' then q.resolved_at else null end as resolved_at,
        md5('clarification_question|' || q.id::text || '|' || q.case_id::text) as id_hash
    from domain_clarification_questions q
), inbox_src as (
    select
        i.case_id as scope_case_id,
        i.chat_id,
        'chat'::text as scope_type,
        case
            when i.item_type ilike '%refresh%' then 'state_refresh_needed'
            when i.item_type ilike '%dossier%' then 'dossier_candidate'
            when i.item_type ilike '%draft%' then 'draft_candidate'
            else 'needs_review'
        end as case_type,
        i.item_type as case_subtype,
        case
            when i.status in ('open', 'pending', 'in_progress') then 'ready'
            when i.status = 'resolved' then 'resolved'
            when i.status = 'rejected' then 'rejected'
            when i.status = 'stale' then 'stale'
            else 'new'
        end as status,
        case when i.priority in ('blocking', 'important', 'optional') then i.priority else 'important' end as priority,
        null::real as confidence,
        coalesce(nullif(i.summary, ''), i.title) as reason_summary,
        null::text as clarification_kind,
        null::text as question_text,
        null::text as response_mode,
        null::text as response_channel_hint,
        '[]'::jsonb as evidence_refs_json,
        '[]'::jsonb as subject_refs_json,
        '[]'::jsonb as target_artifact_types_json,
        '[]'::jsonb as reopen_trigger_rules_json,
        jsonb_build_object('title', i.title, 'last_actor', i.last_actor, 'last_reason', i.last_reason) as provenance_json,
        'inbox_item'::text as source_object_type,
        i.id::text as source_object_id,
        i.created_at,
        i.updated_at,
        case when i.status = 'resolved' then i.updated_at else null end as resolved_at,
        md5('inbox_item|' || i.id::text || '|' || i.case_id::text) as id_hash
    from domain_inbox_items i
), conflict_src as (
    select
        c.case_id as scope_case_id,
        c.chat_id,
        'chat'::text as scope_type,
        'risk'::text as case_type,
        c.conflict_type as case_subtype,
        case
            when c.status in ('open', 'in_progress') then 'ready'
            when c.status = 'resolved' then 'resolved'
            when c.status = 'rejected' then 'rejected'
            when c.status = 'stale' then 'stale'
            else 'new'
        end as status,
        case
            when c.severity = 'high' then 'blocking'
            when c.severity = 'low' then 'optional'
            else 'important'
        end as priority,
        case
            when c.severity = 'high' then 0.9::real
            when c.severity = 'low' then 0.35::real
            else 0.65::real
        end as confidence,
        c.summary as reason_summary,
        null::text as clarification_kind,
        null::text as question_text,
        null::text as response_mode,
        null::text as response_channel_hint,
        '[]'::jsonb as evidence_refs_json,
        '[]'::jsonb as subject_refs_json,
        '[]'::jsonb as target_artifact_types_json,
        '[]'::jsonb as reopen_trigger_rules_json,
        jsonb_build_object(
            'object_a_type', c.object_a_type,
            'object_a_id', c.object_a_id,
            'object_b_type', c.object_b_type,
            'object_b_id', c.object_b_id,
            'last_actor', c.last_actor,
            'last_reason', c.last_reason
        ) as provenance_json,
        'conflict_record'::text as source_object_type,
        c.id::text as source_object_id,
        c.created_at,
        c.updated_at,
        case when c.status = 'resolved' then c.updated_at else null end as resolved_at,
        md5('conflict_record|' || c.id::text || '|' || c.case_id::text) as id_hash
    from domain_conflict_records c
), unified as (
    select * from clarification_src
    union all
    select * from inbox_src
    union all
    select * from conflict_src
)
insert into stage6_cases (
    id,
    scope_case_id,
    chat_id,
    scope_type,
    case_type,
    case_subtype,
    status,
    priority,
    confidence,
    reason_summary,
    clarification_kind,
    question_text,
    response_mode,
    response_channel_hint,
    evidence_refs_json,
    subject_refs_json,
    target_artifact_types_json,
    reopen_trigger_rules_json,
    provenance_json,
    source_object_type,
    source_object_id,
    created_at,
    updated_at,
    ready_at,
    resolved_at,
    rejected_at,
    stale_at
)
select
    (
        substr(id_hash, 1, 8) || '-' ||
        substr(id_hash, 9, 4) || '-' ||
        substr(id_hash, 13, 4) || '-' ||
        substr(id_hash, 17, 4) || '-' ||
        substr(id_hash, 21, 12)
    )::uuid,
    scope_case_id,
    chat_id,
    scope_type,
    case_type,
    case_subtype,
    status,
    priority,
    confidence,
    reason_summary,
    clarification_kind,
    question_text,
    response_mode,
    response_channel_hint,
    evidence_refs_json,
    subject_refs_json,
    target_artifact_types_json,
    reopen_trigger_rules_json,
    provenance_json,
    source_object_type,
    source_object_id,
    created_at,
    updated_at,
    case when status in ('ready', 'needs_user_input') then updated_at else null end,
    resolved_at,
    case when status = 'rejected' then updated_at else null end,
    case when status = 'stale' then updated_at else null end
from unified
on conflict (scope_case_id, case_type, source_object_type, source_object_id)
do update
set
    chat_id = excluded.chat_id,
    scope_type = excluded.scope_type,
    case_subtype = excluded.case_subtype,
    status = excluded.status,
    priority = excluded.priority,
    confidence = excluded.confidence,
    reason_summary = excluded.reason_summary,
    clarification_kind = excluded.clarification_kind,
    question_text = excluded.question_text,
    response_mode = excluded.response_mode,
    response_channel_hint = excluded.response_channel_hint,
    evidence_refs_json = excluded.evidence_refs_json,
    subject_refs_json = excluded.subject_refs_json,
    target_artifact_types_json = excluded.target_artifact_types_json,
    reopen_trigger_rules_json = excluded.reopen_trigger_rules_json,
    provenance_json = excluded.provenance_json,
    updated_at = excluded.updated_at,
    ready_at = excluded.ready_at,
    resolved_at = excluded.resolved_at,
    rejected_at = excluded.rejected_at,
    stale_at = excluded.stale_at;

insert into stage6_case_links (
    id,
    stage6_case_id,
    linked_object_type,
    linked_object_id,
    link_role,
    metadata_json,
    created_at
)
select
    (
        substr(md5('source|' || c.id::text || '|' || c.source_object_type || '|' || c.source_object_id), 1, 8) || '-' ||
        substr(md5('source|' || c.id::text || '|' || c.source_object_type || '|' || c.source_object_id), 9, 4) || '-' ||
        substr(md5('source|' || c.id::text || '|' || c.source_object_type || '|' || c.source_object_id), 13, 4) || '-' ||
        substr(md5('source|' || c.id::text || '|' || c.source_object_type || '|' || c.source_object_id), 17, 4) || '-' ||
        substr(md5('source|' || c.id::text || '|' || c.source_object_type || '|' || c.source_object_id), 21, 12)
    )::uuid,
    c.id,
    c.source_object_type,
    c.source_object_id,
    'source',
    '{}'::jsonb,
    c.created_at
from stage6_cases c
on conflict (stage6_case_id, linked_object_type, linked_object_id, link_role)
do nothing;

insert into stage6_case_links (
    id,
    stage6_case_id,
    linked_object_type,
    linked_object_id,
    link_role,
    metadata_json,
    created_at
)
select
    (
        substr(md5('artifact_target|' || c.id::text || '|' || refs.artifact_type), 1, 8) || '-' ||
        substr(md5('artifact_target|' || c.id::text || '|' || refs.artifact_type), 9, 4) || '-' ||
        substr(md5('artifact_target|' || c.id::text || '|' || refs.artifact_type), 13, 4) || '-' ||
        substr(md5('artifact_target|' || c.id::text || '|' || refs.artifact_type), 17, 4) || '-' ||
        substr(md5('artifact_target|' || c.id::text || '|' || refs.artifact_type), 21, 12)
    )::uuid,
    c.id,
    'stage6_artifact_type',
    refs.artifact_type,
    'artifact_target',
    '{}'::jsonb,
    c.created_at
from stage6_cases c
cross join lateral jsonb_array_elements_text(c.target_artifact_types_json) as refs(artifact_type)
where refs.artifact_type <> ''
on conflict (stage6_case_id, linked_object_type, linked_object_id, link_role)
do nothing;
