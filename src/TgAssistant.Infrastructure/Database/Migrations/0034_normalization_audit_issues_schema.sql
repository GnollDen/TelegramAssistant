alter table normalization_runs
    add column if not exists issues_json jsonb not null default '[]'::jsonb;

do $$
begin
    if not exists (
        select 1
        from pg_constraint
        where conname = 'ck_normalization_runs_issues_json_array'
    ) then
        alter table normalization_runs
            add constraint ck_normalization_runs_issues_json_array
            check (jsonb_typeof(issues_json) = 'array');
    end if;
end $$;
