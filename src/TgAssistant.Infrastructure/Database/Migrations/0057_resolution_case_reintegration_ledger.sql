create table if not exists resolution_case_reintegration_ledger (
    id uuid primary key,
    reintegration_entry_id text not null,
    scope_key text not null,
    scope_item_key text not null,
    tracked_person_id uuid not null references persons(id) on delete cascade,
    carry_forward_case_id text not null,
    origin_source_kind text not null,
    previous_status text null,
    next_status text not null,
    predecessor_ledger_entry_id uuid null references resolution_case_reintegration_ledger(id) on delete set null,
    successor_ledger_entry_id uuid null references resolution_case_reintegration_ledger(id) on delete set null,
    resolution_action_id uuid null references operator_resolution_actions(id) on delete set null,
    conflict_session_id uuid null references operator_resolution_conflict_sessions(id) on delete set null,
    recompute_queue_item_id uuid null references stage8_recompute_queue_items(id) on delete set null,
    recompute_target_family text null,
    recompute_target_ref text null,
    unresolved_residue_json jsonb not null default '{}'::jsonb,
    recorded_at_utc timestamptz not null default now(),
    created_at_utc timestamptz not null default now(),
    updated_at_utc timestamptz not null default now(),
    constraint ck_resolution_case_reintegration_scope_key
        check (btrim(scope_key) <> ''),
    constraint ck_resolution_case_reintegration_scope_item_key
        check (btrim(scope_item_key) <> ''),
    constraint ck_resolution_case_reintegration_case_id
        check (btrim(carry_forward_case_id) <> '' and carry_forward_case_id <> scope_item_key),
    constraint ck_resolution_case_reintegration_origin_kind
        check (origin_source_kind in (
            'stage7_durable_profile',
            'stage7_pair_dynamics',
            'stage7_durable_timeline',
            'stage8_recompute_request',
            'resolution_action')),
    constraint ck_resolution_case_reintegration_previous_status
        check (previous_status is null or previous_status in (
            'open',
            'resolving_ai',
            'resolved_by_ai',
            'needs_more_context',
            'needs_operator',
            'deferred_to_next_pass',
            'superseded')),
    constraint ck_resolution_case_reintegration_next_status
        check (next_status in (
            'open',
            'resolving_ai',
            'resolved_by_ai',
            'needs_more_context',
            'needs_operator',
            'deferred_to_next_pass',
            'superseded')),
    constraint ck_resolution_case_reintegration_residue_json_object
        check (jsonb_typeof(unresolved_residue_json) = 'object')
);

create unique index if not exists uq_resolution_case_reintegration_entry_id
    on resolution_case_reintegration_ledger(reintegration_entry_id);

create index if not exists ix_resolution_case_reintegration_scope_item_recorded
    on resolution_case_reintegration_ledger(scope_key, scope_item_key, recorded_at_utc desc, id desc);

create index if not exists ix_resolution_case_reintegration_scope_case_recorded
    on resolution_case_reintegration_ledger(scope_key, carry_forward_case_id, recorded_at_utc desc, id desc);

create index if not exists ix_resolution_case_reintegration_predecessor
    on resolution_case_reintegration_ledger(predecessor_ledger_entry_id)
    where predecessor_ledger_entry_id is not null;

create index if not exists ix_resolution_case_reintegration_successor
    on resolution_case_reintegration_ledger(successor_ledger_entry_id)
    where successor_ledger_entry_id is not null;
