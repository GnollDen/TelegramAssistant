alter table if exists domain_strategy_records
    add column if not exists micro_step text not null default '',
    add column if not exists horizon_json jsonb;
