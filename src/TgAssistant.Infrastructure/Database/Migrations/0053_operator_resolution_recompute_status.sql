alter table operator_resolution_actions
    add column if not exists recompute_status text null,
    add column if not exists recompute_status_updated_at_utc timestamptz null,
    add column if not exists recompute_completed_at_utc timestamptz null,
    add column if not exists recompute_last_result_status text null,
    add column if not exists recompute_last_error text null;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_operator_resolution_actions_recompute_status'
    ) then
        alter table operator_resolution_actions
            add constraint ck_operator_resolution_actions_recompute_status
                check (
                    recompute_status is null
                    or recompute_status in ('running', 'done', 'failed', 'clarification_blocked')
                );
    end if;
end $$;

create index if not exists ix_operator_resolution_actions_recompute_status
    on operator_resolution_actions(recompute_status, recompute_status_updated_at_utc desc)
    where recompute_status is not null;

with queue_rollup as (
    select
        action_row.id as action_id,
        case
            when count(queue_item.id) = 0 then 'running'
            when bool_or(
                queue_item.status = 'failed'
                or (
                    queue_item.status = 'completed'
                    and coalesce(queue_item.last_result_status, '') in ('blocked_invalid_input', 'need_more_data', 'failed_terminally')
                )
            ) then 'failed'
            when bool_or(
                queue_item.status = 'completed'
                and queue_item.last_result_status = 'need_operator_clarification'
            ) then 'clarification_blocked'
            when bool_or(queue_item.status in ('pending', 'leased')) then 'running'
            else 'done'
        end as recompute_status,
        coalesce(max(queue_item.updated_at_utc), max(action_row.created_at_utc)) as recompute_status_updated_at_utc,
        max(queue_item.completed_at_utc) as recompute_completed_at_utc
    from operator_resolution_actions action_row
    left join stage8_recompute_queue_items queue_item
        on queue_item.trigger_ref = concat('resolution_action:', action_row.id::text)
    group by action_row.id
),
queue_ranked as (
    select distinct on (action_row.id)
        action_row.id as action_id,
        queue_item.last_result_status,
        queue_item.last_error
    from operator_resolution_actions action_row
    left join stage8_recompute_queue_items queue_item
        on queue_item.trigger_ref = concat('resolution_action:', action_row.id::text)
    order by
        action_row.id,
        case
            when queue_item.status = 'failed'
                or (
                    queue_item.status = 'completed'
                    and coalesce(queue_item.last_result_status, '') in ('blocked_invalid_input', 'need_more_data', 'failed_terminally')
                ) then 1
            when queue_item.status = 'completed'
                and queue_item.last_result_status = 'need_operator_clarification' then 2
            when queue_item.status in ('pending', 'leased') then 3
            when queue_item.status = 'completed' then 4
            else 5
        end,
        queue_item.updated_at_utc desc nulls last
)
update operator_resolution_actions action_row
set recompute_status = queue_rollup.recompute_status,
    recompute_status_updated_at_utc = queue_rollup.recompute_status_updated_at_utc,
    recompute_completed_at_utc = case
        when queue_rollup.recompute_status in ('done', 'failed', 'clarification_blocked')
            then queue_rollup.recompute_completed_at_utc
        else null
    end,
    recompute_last_result_status = queue_ranked.last_result_status,
    recompute_last_error = queue_ranked.last_error
from queue_rollup
left join queue_ranked
    on queue_ranked.action_id = queue_rollup.action_id
where action_row.id = queue_rollup.action_id
  and action_row.recompute_status is null;
