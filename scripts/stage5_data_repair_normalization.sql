-- stage5 execution run: data repair and normalization
-- purpose: repair entity linkage/aliases/fact dedupe with auditable and reversible trail.

begin;

create table if not exists ops_stage5_repair_runs (
    run_id text primary key,
    started_at timestamptz not null default now(),
    finished_at timestamptz,
    notes text
);

create table if not exists ops_stage5_entity_merge_audit (
    run_id text not null,
    source_entity_id uuid not null,
    source_name text not null,
    target_entity_id uuid not null,
    target_name text not null,
    reason text not null,
    merged_at timestamptz not null default now()
);

create table if not exists ops_stage5_entity_backup (
    run_id text not null,
    entity_id uuid not null,
    payload jsonb not null,
    captured_at timestamptz not null default now()
);

create table if not exists ops_stage5_fact_backup (
    run_id text not null,
    fact_id uuid not null,
    payload jsonb not null,
    captured_at timestamptz not null default now()
);

create table if not exists ops_stage5_relationship_backup (
    run_id text not null,
    relationship_id uuid not null,
    payload jsonb not null,
    captured_at timestamptz not null default now()
);

do $$
declare
    v_run_id text := 'stage5_repair_' || to_char(clock_timestamp(), 'YYYYMMDD_HH24MISS_MS');
    v_rec record;
begin
    insert into ops_stage5_repair_runs(run_id, notes)
    values (v_run_id, 'stage5 data repair and normalization');

    -- alias backfill from canonical names and aliases[].
    insert into entity_aliases (entity_id, alias, alias_norm, confidence, created_at, updated_at)
    select
        e.id,
        e.name,
        trim(regexp_replace(lower(replace(e.name, 'ё', 'е')), '[^[:alnum:]а-я\\s]+', ' ', 'g')),
        1.0,
        now(),
        now()
    from entities e
    where e.name is not null
      and btrim(e.name) <> ''
    on conflict (entity_id, alias_norm)
    do update
    set updated_at = now(),
        confidence = greatest(entity_aliases.confidence, excluded.confidence);

    insert into entity_aliases (entity_id, alias, alias_norm, confidence, created_at, updated_at)
    select
        e.id,
        a.alias,
        trim(regexp_replace(lower(replace(a.alias, 'ё', 'е')), '[^[:alnum:]а-я\\s]+', ' ', 'g')),
        0.95,
        now(),
        now()
    from entities e
    cross join lateral unnest(coalesce(e.aliases, '{}'::text[])) as a(alias)
    where a.alias is not null
      and btrim(a.alias) <> ''
    on conflict (entity_id, alias_norm)
    do update
    set updated_at = now(),
        confidence = greatest(entity_aliases.confidence, excluded.confidence);

    -- e/ё reversible aliases for better lookup.
    insert into entity_aliases (entity_id, alias, alias_norm, confidence, created_at, updated_at)
    select
        ea.entity_id,
        replace(ea.alias, 'е', 'ё'),
        trim(regexp_replace(lower(replace(replace(ea.alias, 'е', 'ё'), 'ё', 'е')), '[^[:alnum:]а-я\\s]+', ' ', 'g')),
        0.9,
        now(),
        now()
    from entity_aliases ea
    where ea.alias like '%е%'
    on conflict (entity_id, alias_norm)
    do update
    set updated_at = now(),
        confidence = greatest(entity_aliases.confidence, excluded.confidence);

    -- merge plan A: exact canonical duplicates (e/ё and punctuation-insensitive).
    create temp table t_stage5_merge_plan (
        source_entity_id uuid not null,
        target_entity_id uuid not null,
        reason text not null
    ) on commit drop;

    with canon as (
        select
            e.id,
            e.name,
            e.actor_key,
            e.telegram_user_id,
            e.updated_at,
            regexp_replace(lower(replace(trim(e.name), 'ё', 'е')), '[^[:alnum:]а-я]+', '', 'g') as canon_name,
            (select count(*) from facts f where f.entity_id = e.id) as facts_cnt
        from entities e
        where e.type = 0
          and e.name is not null
          and btrim(e.name) <> ''
    ),
    grouped as (
        select canon_name
        from canon
        group by canon_name
        having count(*) > 1
    ),
    ranked as (
        select
            c.*,
            row_number() over (
                partition by c.canon_name
                order by
                    (c.actor_key is not null) desc,
                    (c.telegram_user_id is not null) desc,
                    c.facts_cnt desc,
                    c.updated_at desc,
                    c.id
            ) as rn
        from canon c
        join grouped g on g.canon_name = c.canon_name
    )
    insert into t_stage5_merge_plan(source_entity_id, target_entity_id, reason)
    select
        s.id,
        t.id,
        'canonical_name_duplicate'
    from ranked s
    join ranked t
      on t.canon_name = s.canon_name
     and t.rn = 1
    where s.rn > 1
      and s.id <> t.id;

    -- merge plan B: short-name -> anchored full-name (single anchored candidate).
    with person_norm as (
        select
            e.id,
            e.name,
            e.actor_key,
            e.telegram_user_id,
            regexp_replace(lower(replace(trim(e.name), 'ё', 'е')), '\\s+', ' ', 'g') as norm_name
        from entities e
        where e.type = 0
    ),
    candidates as (
        select
            s.id as short_id,
            f.id as full_id
        from person_norm s
        join person_norm f on s.id <> f.id
        where position(' ' in s.norm_name) = 0
          and position(' ' in f.norm_name) > 0
          and split_part(f.norm_name, ' ', 1) = s.norm_name
    ),
    candidate_stats as (
        select
            c.short_id,
            count(*) as full_variants,
            count(*) filter (where f.actor_key is not null or f.telegram_user_id is not null) as anchored_variants,
            min(f.id::text) filter (where f.actor_key is not null or f.telegram_user_id is not null)::uuid as anchored_full_id
        from candidates c
        join entities f on f.id = c.full_id
        group by c.short_id
    ),
    overlap as (
        select
            cs.short_id,
            cs.anchored_full_id as full_id,
            count(*) as overlap_facts
        from candidate_stats cs
        join facts fs on fs.entity_id = cs.short_id and fs.source_message_id is not null
        join facts ff on ff.entity_id = cs.anchored_full_id and ff.source_message_id = fs.source_message_id
        where cs.anchored_variants = 1
        group by cs.short_id, cs.anchored_full_id
    )
    insert into t_stage5_merge_plan(source_entity_id, target_entity_id, reason)
    select
        cs.short_id,
        cs.anchored_full_id,
        'short_to_anchored_full'
    from candidate_stats cs
    left join overlap o on o.short_id = cs.short_id and o.full_id = cs.anchored_full_id
    join entities s on s.id = cs.short_id
    where cs.anchored_variants = 1
      and s.actor_key is null
      and s.telegram_user_id is null
      and coalesce(o.overlap_facts, 0) >= 3
      and not exists (
          select 1
          from t_stage5_merge_plan p
          where p.source_entity_id = cs.short_id
      );

    -- apply merge plan with backup trail.
    for v_rec in
        select distinct source_entity_id, target_entity_id, reason
        from t_stage5_merge_plan
        where source_entity_id <> target_entity_id
    loop
        insert into ops_stage5_entity_backup(run_id, entity_id, payload)
        select v_run_id, e.id, to_jsonb(e)
        from entities e
        where e.id in (v_rec.source_entity_id, v_rec.target_entity_id);

        insert into ops_stage5_entity_merge_audit(
            run_id,
            source_entity_id,
            source_name,
            target_entity_id,
            target_name,
            reason)
        select
            v_run_id,
            s.id,
            s.name,
            t.id,
            t.name,
            v_rec.reason
        from entities s
        join entities t on t.id = v_rec.target_entity_id
        where s.id = v_rec.source_entity_id;

        update entities t
        set aliases = (
                select array(
                    select distinct x
                    from unnest(coalesce(t.aliases, '{}'::text[])
                                || coalesce(s.aliases, '{}'::text[])
                                || array[s.name]) as x
                    where x is not null and btrim(x) <> ''
                )
            ),
            telegram_user_id = coalesce(t.telegram_user_id, s.telegram_user_id),
            telegram_username = coalesce(t.telegram_username, s.telegram_username),
            actor_key = coalesce(t.actor_key, s.actor_key),
            updated_at = now()
        from entities s
        where t.id = v_rec.target_entity_id
          and s.id = v_rec.source_entity_id;

        insert into ops_stage5_fact_backup(run_id, fact_id, payload)
        select v_run_id, sf.id, to_jsonb(sf)
        from facts sf
        join facts tf on tf.entity_id = v_rec.target_entity_id
        where sf.entity_id = v_rec.source_entity_id
          and sf.is_current = tf.is_current
          and lower(trim(sf.category)) = lower(trim(tf.category))
          and lower(trim(sf.key)) = lower(trim(tf.key))
          and lower(trim(replace(sf.value, 'ё', 'е'))) = lower(trim(replace(tf.value, 'ё', 'е')));

        delete from facts sf
        using facts tf
        where sf.entity_id = v_rec.source_entity_id
          and tf.entity_id = v_rec.target_entity_id
          and sf.is_current = tf.is_current
          and lower(trim(sf.category)) = lower(trim(tf.category))
          and lower(trim(sf.key)) = lower(trim(tf.key))
          and lower(trim(replace(sf.value, 'ё', 'е'))) = lower(trim(replace(tf.value, 'ё', 'е')));

        update facts set entity_id = v_rec.target_entity_id where entity_id = v_rec.source_entity_id;

        insert into relationships (
            from_entity_id,
            to_entity_id,
            type,
            status,
            confidence,
            context_text,
            source_message_id,
            created_at,
            updated_at
        )
        select
            v_rec.target_entity_id,
            r.to_entity_id,
            r.type,
            r.status,
            r.confidence,
            r.context_text,
            r.source_message_id,
            r.created_at,
            now()
        from relationships r
        where r.from_entity_id = v_rec.source_entity_id
        on conflict (from_entity_id, to_entity_id, type)
        do update
        set confidence = greatest(relationships.confidence, excluded.confidence),
            status = least(relationships.status, excluded.status),
            updated_at = now(),
            source_message_id = coalesce(relationships.source_message_id, excluded.source_message_id),
            context_text = coalesce(relationships.context_text, excluded.context_text);

        insert into relationships (
            from_entity_id,
            to_entity_id,
            type,
            status,
            confidence,
            context_text,
            source_message_id,
            created_at,
            updated_at
        )
        select
            r.from_entity_id,
            v_rec.target_entity_id,
            r.type,
            r.status,
            r.confidence,
            r.context_text,
            r.source_message_id,
            r.created_at,
            now()
        from relationships r
        where r.to_entity_id = v_rec.source_entity_id
        on conflict (from_entity_id, to_entity_id, type)
        do update
        set confidence = greatest(relationships.confidence, excluded.confidence),
            status = least(relationships.status, excluded.status),
            updated_at = now(),
            source_message_id = coalesce(relationships.source_message_id, excluded.source_message_id),
            context_text = coalesce(relationships.context_text, excluded.context_text);

        insert into ops_stage5_relationship_backup(run_id, relationship_id, payload)
        select v_run_id, r.id, to_jsonb(r)
        from relationships r
        where r.from_entity_id = v_rec.source_entity_id
           or r.to_entity_id = v_rec.source_entity_id;

        delete from relationships
        where from_entity_id = v_rec.source_entity_id
           or to_entity_id = v_rec.source_entity_id;
        update daily_summaries set entity_id = v_rec.target_entity_id where entity_id = v_rec.source_entity_id;
        update analysis_sessions set entity_id = v_rec.target_entity_id where entity_id = v_rec.source_entity_id;
        update communication_events set entity_id = v_rec.target_entity_id where entity_id = v_rec.source_entity_id;
        insert into entity_aliases (entity_id, alias, alias_norm, source_message_id, confidence, created_at, updated_at)
        select
            v_rec.target_entity_id,
            a.alias,
            a.alias_norm,
            a.source_message_id,
            a.confidence,
            a.created_at,
            now()
        from entity_aliases a
        where a.entity_id = v_rec.source_entity_id
        on conflict (entity_id, alias_norm)
        do update
        set confidence = greatest(entity_aliases.confidence, excluded.confidence),
            updated_at = now();

        delete from entity_aliases where entity_id = v_rec.source_entity_id;
        update intelligence_observations set entity_id = v_rec.target_entity_id where entity_id = v_rec.source_entity_id;
        update intelligence_claims set entity_id = v_rec.target_entity_id where entity_id = v_rec.source_entity_id;
        update text_embeddings
        set owner_id = v_rec.target_entity_id::text
        where owner_type = 'entity'
          and owner_id = v_rec.source_entity_id::text;

        delete from entity_aliases a
        using entity_aliases d
        where a.id > d.id
          and a.entity_id = d.entity_id
          and a.alias_norm = d.alias_norm;

        delete from facts a
        using facts d
        where a.id > d.id
          and a.entity_id = d.entity_id
          and coalesce(a.source_message_id, -1) = coalesce(d.source_message_id, -1)
          and lower(trim(a.category)) = lower(trim(d.category))
          and lower(trim(a.key)) = lower(trim(d.key))
          and lower(trim(replace(a.value, 'ё', 'е'))) = lower(trim(replace(d.value, 'ё', 'е')))
          and a.is_current = d.is_current;

        delete from relationships a
        using relationships d
        where a.id > d.id
          and a.from_entity_id = d.from_entity_id
          and a.to_entity_id = d.to_entity_id
          and a.type = d.type
          and coalesce(a.source_message_id, -1) = coalesce(d.source_message_id, -1);

        delete from entities where id = v_rec.source_entity_id;
    end loop;

    -- backup + remove duplicate facts by evidence key.
    insert into ops_stage5_fact_backup(run_id, fact_id, payload)
    select v_run_id, f.id, to_jsonb(f)
    from facts f
    join (
        select id
        from (
            select
                id,
                row_number() over (
                    partition by
                        entity_id,
                        source_message_id,
                        lower(trim(category)),
                        lower(trim(key)),
                        lower(trim(replace(value, 'ё', 'е'))),
                        is_current
                    order by updated_at desc, created_at desc, id desc
                ) as rn
            from facts
            where source_message_id is not null
        ) d
        where d.rn > 1
    ) x on x.id = f.id;

    delete from facts f
    using (
        select id
        from (
            select
                id,
                row_number() over (
                    partition by
                        entity_id,
                        source_message_id,
                        lower(trim(category)),
                        lower(trim(key)),
                        lower(trim(replace(value, 'ё', 'е'))),
                        is_current
                    order by updated_at desc, created_at desc, id desc
                ) as rn
            from facts
            where source_message_id is not null
        ) d
        where d.rn > 1
    ) x
    where x.id = f.id;

    -- backup + remove relationship self loops and low-signal placeholder links.
    insert into ops_stage5_relationship_backup(run_id, relationship_id, payload)
    select v_run_id, r.id, to_jsonb(r)
    from relationships r
    join entities ef on ef.id = r.from_entity_id
    join entities et on et.id = r.to_entity_id
    where r.from_entity_id = r.to_entity_id
       or (
            (ef.name like '[%]' or et.name like '[%]')
            and r.confidence < 0.75
          );

    delete from relationships r
    using entities ef, entities et
    where ef.id = r.from_entity_id
      and et.id = r.to_entity_id
      and (
            r.from_entity_id = r.to_entity_id
            or (
                (ef.name like '[%]' or et.name like '[%]')
                and r.confidence < 0.75
            )
      );

    -- cleanup placeholder entities with no factual evidence.
    insert into ops_stage5_entity_backup(run_id, entity_id, payload)
    select v_run_id, e.id, to_jsonb(e)
    from entities e
    where e.name like '[%]'
      and not exists (select 1 from facts f where f.entity_id = e.id)
      and not exists (select 1 from intelligence_claims c where c.entity_id = e.id)
      and not exists (select 1 from intelligence_observations o where o.entity_id = e.id);

    delete from entities e
    where e.name like '[%]'
      and not exists (select 1 from facts f where f.entity_id = e.id)
      and not exists (select 1 from intelligence_claims c where c.entity_id = e.id)
      and not exists (select 1 from intelligence_observations o where o.entity_id = e.id);

    update ops_stage5_repair_runs
    set finished_at = now()
    where run_id = v_run_id;
end $$;

commit;
