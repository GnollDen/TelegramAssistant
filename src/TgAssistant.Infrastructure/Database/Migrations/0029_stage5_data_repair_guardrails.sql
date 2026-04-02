-- sprint 23: stage 5 data repair guardrails
-- focus: evidence-level dedupe, claim-level dedupe, and relationship hygiene.

-- 1) remove exact duplicate claims per evidence key within one message payload.
with ranked_claims as (
    select
        id,
        row_number() over (
            partition by
                message_id,
                coalesce(entity_id, '00000000-0000-0000-0000-000000000000'::uuid),
                lower(trim(category)),
                lower(trim(key)),
                lower(trim(replace(value, 'ё', 'е')))
            order by confidence desc, created_at desc, id desc
        ) as rn
    from intelligence_claims
)
delete from intelligence_claims c
using ranked_claims r
where c.id = r.id
  and r.rn > 1;

create unique index if not exists uq_intelligence_claims_evidence_identity
    on intelligence_claims(
        message_id,
        coalesce(entity_id, '00000000-0000-0000-0000-000000000000'::uuid),
        lower(trim(category)),
        lower(trim(key)),
        lower(trim(replace(value, 'ё', 'е')))
    );

-- 2) remove exact duplicate facts per evidence key.
with ranked_facts as (
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
)
delete from facts f
using ranked_facts r
where f.id = r.id
  and r.rn > 1;

create unique index if not exists uq_facts_evidence_identity
    on facts(
        entity_id,
        source_message_id,
        lower(trim(category)),
        lower(trim(key)),
        lower(trim(replace(value, 'ё', 'е'))),
        is_current
    )
    where source_message_id is not null;

-- 3) remove noisy self-loop relationships.
delete from relationships
where from_entity_id = to_entity_id;
