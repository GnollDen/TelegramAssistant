-- Targeted cleanup: legacy Stage 6 false-positive merge-review residue (pre-b05442b).
-- Scope: only old active stage6 cases originating from inbox period_merge_proposal,
-- with exact known bad summary and empty evidence refs.
-- Safety: non-destructive status transition to rejected + audit events.

\set ON_ERROR_STOP on
\pset pager off

begin;

create temporary table tmp_stage6_legacy_merge_cleanup as
select
    c.id as stage6_case_id,
    c.scope_case_id,
    c.chat_id,
    c.status as old_stage6_status,
    c.ready_at as old_ready_at,
    c.resolved_at as old_resolved_at,
    c.rejected_at as old_rejected_at,
    c.stale_at as old_stale_at,
    c.updated_at as old_stage6_updated_at,
    c.source_object_id as inbox_item_id_text,
    i.id as inbox_item_id,
    i.status as old_inbox_status,
    i.updated_at as old_inbox_updated_at
from stage6_cases c
join domain_inbox_items i
    on i.id::text = c.source_object_id
where c.case_type = 'needs_review'
  and c.source_object_type = 'inbox_item'
  and c.status in ('new', 'ready', 'needs_user_input', 'stale')
  and c.created_at < timestamptz '2026-04-01 06:10:52+00'
  and c.reason_summary = 'Likely merge: adjacent short/low-confidence periods separated by weak or unresolved transition.'
  and coalesce(jsonb_array_length(c.evidence_refs_json), 0) = 0
  and i.item_type = 'period_merge_proposal'
  and i.source_object_type = 'period_merge_proposal'
  and i.status in ('open', 'pending', 'in_progress');

-- Snapshot target population.
select
    count(*) as target_rows,
    count(distinct stage6_case_id) as target_stage6_cases,
    count(distinct inbox_item_id) as target_inbox_items
from tmp_stage6_legacy_merge_cleanup;

-- 1) Move Stage 6 cases out of active queue.
update stage6_cases c
set
    status = 'rejected',
    updated_at = now(),
    rejected_at = coalesce(c.rejected_at, now()),
    resolved_at = null,
    stale_at = null
from tmp_stage6_legacy_merge_cleanup t
where c.id = t.stage6_case_id;

-- 2) Keep inbox/source records consistent (no active legacy item remains).
update domain_inbox_items i
set
    status = 'rejected',
    updated_at = now(),
    last_actor = 'ops_cleanup_stage6_legacy_merge',
    last_reason = 'legacy_false_positive_merge_review_pre_b05442b'
from tmp_stage6_legacy_merge_cleanup t
where i.id = t.inbox_item_id;

-- 3) Add explicit review trail for Stage 6 case status transitions.
insert into domain_review_events (
    id,
    object_type,
    object_id,
    action,
    old_value_ref,
    new_value_ref,
    reason,
    actor,
    created_at
)
select
    gen_random_uuid(),
    'stage6_case',
    t.stage6_case_id::text,
    'update_status',
    jsonb_build_object(
        'Status', t.old_stage6_status,
        'ReadyAt', t.old_ready_at,
        'ResolvedAt', t.old_resolved_at,
        'RejectedAt', t.old_rejected_at,
        'StaleAt', t.old_stale_at
    )::text,
    jsonb_build_object(
        'Status', 'rejected',
        'ReadyAt', t.old_ready_at,
        'ResolvedAt', null,
        'RejectedAt', now(),
        'StaleAt', null
    )::text,
    'legacy_false_positive_merge_review_pre_b05442b',
    'ops_cleanup_stage6_legacy_merge',
    now()
from tmp_stage6_legacy_merge_cleanup t;

-- 4) Add explicit review trail for inbox item transitions.
insert into domain_review_events (
    id,
    object_type,
    object_id,
    action,
    old_value_ref,
    new_value_ref,
    reason,
    actor,
    created_at
)
select
    gen_random_uuid(),
    'inbox_item',
    t.inbox_item_id::text,
    'update_status',
    jsonb_build_object('Status', t.old_inbox_status)::text,
    jsonb_build_object('Status', 'rejected')::text,
    'legacy_false_positive_merge_review_pre_b05442b',
    'ops_cleanup_stage6_legacy_merge',
    now()
from tmp_stage6_legacy_merge_cleanup t;

-- 5) Persist Stage 6 outcome records for auditability.
insert into stage6_case_outcomes (
    id,
    stage6_case_id,
    scope_case_id,
    chat_id,
    outcome_type,
    case_status_after,
    user_context_material,
    note,
    source_channel,
    actor,
    created_at
)
select
    gen_random_uuid(),
    t.stage6_case_id,
    t.scope_case_id,
    t.chat_id,
    'rejected',
    'rejected',
    false,
    'legacy_false_positive_merge_review_pre_b05442b',
    'ops_sql',
    'ops_cleanup_stage6_legacy_merge',
    now()
from tmp_stage6_legacy_merge_cleanup t;

commit;

-- Post-apply verification for this class.
select
    count(*) as stage6_active_residue_after
from stage6_cases c
where c.case_type = 'needs_review'
  and c.source_object_type = 'inbox_item'
  and c.status in ('new', 'ready', 'needs_user_input', 'stale')
  and c.created_at < timestamptz '2026-04-01 06:10:52+00'
  and c.reason_summary = 'Likely merge: adjacent short/low-confidence periods separated by weak or unresolved transition.'
  and coalesce(jsonb_array_length(c.evidence_refs_json), 0) = 0;

select
    count(*) as inbox_active_residue_after
from domain_inbox_items i
where i.item_type = 'period_merge_proposal'
  and i.source_object_type = 'period_merge_proposal'
  and i.status in ('open', 'pending', 'in_progress')
  and i.created_at < timestamptz '2026-04-01 06:10:52+00'
  and i.summary = 'Likely merge: adjacent short/low-confidence periods separated by weak or unresolved transition.';
