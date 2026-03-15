begin;

set local lock_timeout = '5s';
set local statement_timeout = '5min';

create temp table if not exists _stage5_cleanup_log (
    operation text primary key,
    affected_rows bigint not null
);

truncate _stage5_cleanup_log;

with updated as (
    update intelligence_claims
    set key = case key
        when 'текущее_место' then 'текущее_местоположение'
        when 'текущее_местонахождение' then 'текущее_местоположение'
        when 'свободное_время_ограничения' then 'свободное_время'
        when 'shared_location_link' then 'shared_location'
        else key
    end
    where key in (
        'текущее_место',
        'текущее_местонахождение',
        'свободное_время_ограничения',
        'shared_location_link'
    )
    returning 1
)
insert into _stage5_cleanup_log(operation, affected_rows)
select 'normalize_intelligence_claims_keys', count(*) from updated;

with updated as (
    update facts
    set key = case key
        when 'текущее_место' then 'текущее_местоположение'
        when 'текущее_местонахождение' then 'текущее_местоположение'
        when 'свободное_время_ограничения' then 'свободное_время'
        when 'shared_location_link' then 'shared_location'
        else key
    end
    where key in (
        'текущее_место',
        'текущее_местонахождение',
        'свободное_время_ограничения',
        'shared_location_link'
    )
    returning 1
)
insert into _stage5_cleanup_log(operation, affected_rows)
select 'normalize_facts_keys', count(*) from updated;

with deleted as (
    delete from intelligence_claims
    where category = 'system_status'
       or key in ('adblock_status', 'apple_pay_issue', 'бк')
    returning 1
)
insert into _stage5_cleanup_log(operation, affected_rows)
select 'purge_intelligence_claims_technical_leaks', count(*) from deleted;

with deleted as (
    delete from facts
    where category = 'system_status'
       or key in ('adblock_status', 'apple_pay_issue', 'бк')
    returning 1
)
insert into _stage5_cleanup_log(operation, affected_rows)
select 'purge_facts_technical_leaks', count(*) from deleted;

with updated as (
    update intelligence_observations
    set observation_type = 'relationship_signal'
    where (
            observation_type ilike '%relationship%'
            and observation_type <> 'relationship_signal'
          )
       or observation_type in (
            'emotional_state',
            'мнение_о_мужчинах',
            'мнение_о_женщинах',
            'opinion_about_men',
            'opinion_about_women'
          )
    returning 1
)
insert into _stage5_cleanup_log(operation, affected_rows)
select 'normalize_relationship_observation_types', count(*) from updated;

with deleted as (
    delete from intelligence_observations
    where observation_type = 'relationship_signal'
      and (
            value is null
            or btrim(value) = ''
            or lower(btrim(value)) in ('а не', 'ok', 'test', 'null', 'none', 'n/a')
            or btrim(value) ~ '^[0-9]+(/[0-9]+)?$'
          )
    returning 1
)
insert into _stage5_cleanup_log(operation, affected_rows)
select 'purge_relationship_observation_noise', count(*) from deleted;

select operation, affected_rows
from _stage5_cleanup_log
order by operation;

commit;
