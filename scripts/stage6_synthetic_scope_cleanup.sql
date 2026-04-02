-- Targeted cleanup: synthetic/smoke Stage 6 residue isolated from operator workload.
-- Scope marker: chat_id >= 9_000_000_000_000.
-- Safety: non-destructive status transitions + explicit review/outcome audit trail.

\set ON_ERROR_STOP on
\pset pager off

begin;

create temporary table tmp_stage6_synthetic_cleanup as
select
    c.id as stage6_case_id,
    c.scope_case_id,
    c.chat_id,
    c.status as old_stage6_status,
    c.ready_at as old_ready_at,
    c.resolved_at as old_resolved_at,
    c.rejected_at as old_rejected_at,
    c.stale_at as old_stale_at
from stage6_cases c
where c.chat_id >= 9000000000000
  and c.status in ('new', 'ready', 'needs_user_input', 'stale');

create temporary table tmp_inbox_synthetic_cleanup as
select
    i.id as inbox_item_id,
    i.case_id as scope_case_id,
    i.chat_id,
    i.status as old_inbox_status
from domain_inbox_items i
where i.chat_id >= 9000000000000
  and i.status in ('open', 'pending', 'in_progress');

-- Snapshot target population.
select
    (select count(*) from tmp_stage6_synthetic_cleanup) as target_stage6_active_rows,
    (select count(*) from tmp_inbox_synthetic_cleanup) as target_inbox_active_rows;

-- 1) Remove synthetic Stage 6 cases from active operator queue.
update stage6_cases c
set
    status = 'rejected',
    updated_at = now(),
    rejected_at = coalesce(c.rejected_at, now()),
    resolved_at = null,
    stale_at = null
from tmp_stage6_synthetic_cleanup t
where c.id = t.stage6_case_id;

-- 2) Remove linked synthetic inbox residue from active queue.
update domain_inbox_items i
set
    status = 'rejected',
    updated_at = now(),
    last_actor = 'ops_cleanup_stage6_synthetic',
    last_reason = 'synthetic_scope_isolation_2026_04_02'
from tmp_inbox_synthetic_cleanup t
where i.id = t.inbox_item_id;

-- 3) Explicit review trail for Stage 6 case status transition.
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
    'synthetic_scope_isolation_2026_04_02',
    'ops_cleanup_stage6_synthetic',
    now()
from tmp_stage6_synthetic_cleanup t;

-- 4) Explicit review trail for inbox status transition.
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
    'synthetic_scope_isolation_2026_04_02',
    'ops_cleanup_stage6_synthetic',
    now()
from tmp_inbox_synthetic_cleanup t;

-- 5) Persist Stage 6 outcomes for auditability/debug path.
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
    'synthetic_scope_isolation_2026_04_02',
    'ops_sql',
    'ops_cleanup_stage6_synthetic',
    now()
from tmp_stage6_synthetic_cleanup t;

commit;

-- Post-apply verification.
select
    count(*) as stage6_active_synthetic_after
from stage6_cases
where chat_id >= 9000000000000
  and status in ('new', 'ready', 'needs_user_input', 'stale');

select
    count(*) as inbox_active_synthetic_after
from domain_inbox_items
where chat_id >= 9000000000000
  and status in ('open', 'pending', 'in_progress');
