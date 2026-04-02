alter table model_pass_runs
    add column if not exists result_status text;

update model_pass_runs
set result_status = 'blocked_invalid_input'
where result_status is null;

alter table model_pass_runs
    alter column result_status set default 'blocked_invalid_input';

alter table model_pass_runs
    alter column result_status set not null;

alter table model_pass_runs
    add column if not exists scope_json jsonb not null default '{}'::jsonb;

alter table model_pass_runs
    add column if not exists source_refs_json jsonb not null default '[]'::jsonb;

alter table model_pass_runs
    add column if not exists truth_summary_json jsonb not null default '{}'::jsonb;

alter table model_pass_runs
    add column if not exists conflicts_json jsonb not null default '[]'::jsonb;

alter table model_pass_runs
    add column if not exists unknowns_json jsonb not null default '[]'::jsonb;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_model_pass_runs_result_status'
    ) then
        alter table model_pass_runs
            add constraint ck_model_pass_runs_result_status
            check (result_status in (
                'result_ready',
                'need_more_data',
                'need_operator_clarification',
                'blocked_invalid_input'
            ));
    end if;
end $$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_model_pass_runs_scope_json_object'
    ) then
        alter table model_pass_runs
            add constraint ck_model_pass_runs_scope_json_object
            check (jsonb_typeof(scope_json) = 'object');
    end if;
end $$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_model_pass_runs_source_refs_json_array'
    ) then
        alter table model_pass_runs
            add constraint ck_model_pass_runs_source_refs_json_array
            check (jsonb_typeof(source_refs_json) = 'array');
    end if;
end $$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_model_pass_runs_truth_summary_json_object'
    ) then
        alter table model_pass_runs
            add constraint ck_model_pass_runs_truth_summary_json_object
            check (jsonb_typeof(truth_summary_json) = 'object');
    end if;
end $$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_model_pass_runs_conflicts_json_array'
    ) then
        alter table model_pass_runs
            add constraint ck_model_pass_runs_conflicts_json_array
            check (jsonb_typeof(conflicts_json) = 'array');
    end if;
end $$;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_model_pass_runs_unknowns_json_array'
    ) then
        alter table model_pass_runs
            add constraint ck_model_pass_runs_unknowns_json_array
            check (jsonb_typeof(unknowns_json) = 'array');
    end if;
end $$;

create index if not exists idx_model_pass_runs_scope_result_status
    on model_pass_runs(scope_key, result_status, started_at);
