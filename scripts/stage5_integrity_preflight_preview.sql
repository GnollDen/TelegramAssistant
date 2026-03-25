-- Sprint 6 helper (read-only): integrity + silent-regression preflight snapshot.
-- Usage examples:
--   \set chat_id 885574984
--   \i scripts/stage5_integrity_preflight_preview.sql
--
-- If chat scope is not needed, set chat_id to 0.

-- 1) Scoped extraction/control-state summary
select
  count(*)::bigint as scoped_messages,
  count(*) filter (where me.id is null)::bigint as missing_extraction_rows,
  count(*) filter (where me.id is not null and me.needs_expensive)::bigint as awaiting_expensive_apply,
  count(*) filter (where coalesce(me.is_quarantined, false))::bigint as quarantined_extractions,
  count(*) filter (
    where m.processing_status = 1
      and m.needs_reanalysis = false
      and (
        me.id is null
        or me.needs_expensive
        or coalesce(me.is_quarantined, false)
      )
  )::bigint as terminal_processed_anomalies
from messages m
left join message_extractions me
  on me.message_id = m.id
where (:chat_id = 0 or m.chat_id = :chat_id);

-- 2) Duplicate message business keys (canonical identity is chat_id + telegram_message_id)
with duplicate_keys as (
  select
    m.chat_id,
    m.telegram_message_id,
    count(*)::bigint as duplicate_rows
  from messages m
  where (:chat_id = 0 or m.chat_id = :chat_id)
  group by m.chat_id, m.telegram_message_id
  having count(*) > 1
)
select
  count(*)::bigint as duplicate_key_groups,
  coalesce(sum(duplicate_rows - 1), 0)::bigint as duplicate_key_rows
from duplicate_keys;

-- 3) Top duplicate business keys for drill-down
select
  m.chat_id,
  m.telegram_message_id,
  count(*)::bigint as duplicate_rows
from messages m
where (:chat_id = 0 or m.chat_id = :chat_id)
group by m.chat_id, m.telegram_message_id
having count(*) > 1
order by duplicate_rows desc, m.chat_id asc, m.telegram_message_id asc
limit 100;

-- 4) Duplicate detail rows for operator review
with duplicate_keys as (
  select
    m.chat_id,
    m.telegram_message_id
  from messages m
  where (:chat_id = 0 or m.chat_id = :chat_id)
  group by m.chat_id, m.telegram_message_id
  having count(*) > 1
)
select
  m.chat_id,
  m.telegram_message_id,
  m.id as message_id,
  m.source,
  m.processing_status,
  m.needs_reanalysis,
  m.created_at,
  m.edit_timestamp
from messages m
join duplicate_keys dk
  on dk.chat_id = m.chat_id
 and dk.telegram_message_id = m.telegram_message_id
order by m.chat_id asc, m.telegram_message_id asc, m.created_at asc, m.id asc
limit 200;

-- 5) Queue/backlog health signals relevant to Stage5 integrity review
select
  count(*) filter (where me.needs_expensive)::bigint as expensive_backlog,
  count(*) filter (where coalesce(me.is_quarantined, false))::bigint as quarantine_total,
  count(*) filter (
    where coalesce(me.is_quarantined, false)
      and me.quarantined_at <= now() - interval '6 hour'
  )::bigint as quarantine_stuck_6h
from message_extractions me
join messages m
  on m.id = me.message_id
where (:chat_id = 0 or m.chat_id = :chat_id);

-- 6) Session continuity quick check
with session_bounds as (
  select
    cs.chat_id,
    min(cs.session_index) as min_idx,
    max(cs.session_index) as max_idx,
    count(distinct cs.session_index) as present_count
  from chat_sessions cs
  where (:chat_id = 0 or cs.chat_id = :chat_id)
  group by cs.chat_id
)
select
  sb.chat_id,
  sb.min_idx,
  sb.max_idx,
  sb.present_count,
  (sb.max_idx - sb.min_idx + 1) as expected_count,
  (sb.max_idx - sb.min_idx + 1) - sb.present_count as missing_indexes
from session_bounds sb
order by sb.chat_id asc;

-- 7) Control-state snapshot for relevant watermarks/cursors
select
  s.key,
  s.value,
  s.updated_at
from analysis_state s
where s.key in (
  'stage5:session_watermark_ms',
  'stage5:session_seed_message_watermark',
  'stage6:continuous_refinement:cursor'
)
order by s.key asc;

-- 8) Confirmed cursor/watermark anomalies derived from durable state
with analyzed_frontier as (
  select
    coalesce(max((extract(epoch from cs.end_date) * 1000)::bigint), 0) as max_analyzed_session_end_ms
  from chat_sessions cs
  where cs.is_analyzed = true
),
processed_frontier as (
  select
    coalesce(max(m.id), 0) as max_processed_message_id
  from messages m
  where m.processing_status = 1
),
extraction_frontier as (
  select
    coalesce(max(me.id), 0) as max_extraction_id
  from message_extractions me
),
state as (
  select
    s.key,
    s.value,
    s.updated_at
  from analysis_state s
  where s.key in (
    'stage5:session_watermark_ms',
    'stage5:session_seed_message_watermark',
    'stage6:continuous_refinement:cursor'
  )
)
select
  anomaly_key,
  persisted_value,
  expected_floor,
  expected_ceiling,
  updated_at,
  anomaly_reason
from (
  select
    'stage5:session_watermark_ms'::text as anomaly_key,
    coalesce(st.value, 0) as persisted_value,
    af.max_analyzed_session_end_ms as expected_floor,
    null::bigint as expected_ceiling,
    st.updated_at,
    case
      when st.key is null and af.max_analyzed_session_end_ms > 0 then 'missing_analysis_state_row'
      when st.value < af.max_analyzed_session_end_ms then 'below_analyzed_session_frontier'
      else null
    end as anomaly_reason
  from analyzed_frontier af
  left join state st
    on st.key = 'stage5:session_watermark_ms'

  union all

  select
    'stage5:session_seed_message_watermark'::text as anomaly_key,
    coalesce(st.value, 0) as persisted_value,
    0::bigint as expected_floor,
    pf.max_processed_message_id as expected_ceiling,
    st.updated_at,
    case
      when st.key is null and pf.max_processed_message_id > 0 then 'missing_analysis_state_row'
      when st.value > pf.max_processed_message_id then 'ahead_of_processed_message_frontier'
      else null
    end as anomaly_reason
  from processed_frontier pf
  left join state st
    on st.key = 'stage5:session_seed_message_watermark'

  union all

  select
    'stage6:continuous_refinement:cursor'::text as anomaly_key,
    coalesce(st.value, 0) as persisted_value,
    0::bigint as expected_floor,
    ef.max_extraction_id as expected_ceiling,
    st.updated_at,
    case
      when st.key is null then null
      when st.value > ef.max_extraction_id then 'ahead_of_extraction_frontier'
      else null
    end as anomaly_reason
  from extraction_frontier ef
  left join state st
    on st.key = 'stage6:continuous_refinement:cursor'
) anomalies
where anomaly_reason is not null
order by anomaly_key asc;

-- 9) Quarantine overview for scoped or global review
select
  m.chat_id,
  me.quarantine_reason,
  count(*)::bigint as rows_count
from messages m
join message_extractions me
  on me.message_id = m.id
where (:chat_id = 0 or m.chat_id = :chat_id)
  and me.quarantine_reason is not null
group by m.chat_id, me.quarantine_reason
order by rows_count desc, m.chat_id asc, me.quarantine_reason asc;

-- 10) Confirmed processed-without-apply anomalies for terminally processed messages
select
  m.chat_id,
  m.id as message_id,
  m.telegram_message_id,
  m.processing_status,
  m.needs_reanalysis,
  me.id as extraction_id,
  me.needs_expensive,
  coalesce(me.is_quarantined, false) as is_quarantined,
  me.updated_at as extraction_updated_at,
  case
    when me.id is null then 'missing_extraction_row'
    when coalesce(me.is_quarantined, false) then 'quarantined_extraction'
    when me.needs_expensive then 'awaiting_expensive_apply'
    else null
  end as anomaly_reason
from messages m
left join message_extractions me
  on me.message_id = m.id
where (:chat_id = 0 or m.chat_id = :chat_id)
  and m.processing_status = 1
  and m.needs_reanalysis = false
  and (
    me.id is null
    or coalesce(me.is_quarantined, false)
    or me.needs_expensive
  )
order by anomaly_reason asc, m.chat_id asc, m.id asc
limit 200;

-- 11) Candidate processed-without-apply review set: terminally processed messages with no downstream intelligence artifacts
-- Review only; empty extractions can be legitimate.
select
  m.chat_id,
  m.id as message_id,
  m.telegram_message_id,
  me.id as extraction_id,
  me.needs_expensive,
  coalesce(
    jsonb_array_length(
      case
        when jsonb_typeof(me.cheap_json -> 'claims') = 'array'
          then me.cheap_json -> 'claims'
        else '[]'::jsonb
      end
    ),
    0
  ) as cheap_claims,
  coalesce(
    jsonb_array_length(
      case
        when jsonb_typeof(me.cheap_json -> 'observations') = 'array'
          then me.cheap_json -> 'observations'
        else '[]'::jsonb
      end
    ),
    0
  ) as cheap_observations,
  coalesce(ic.claim_rows, 0) as persisted_claim_rows,
  coalesce(io.observation_rows, 0) as persisted_observation_rows,
  coalesce(ce.event_rows, 0) as persisted_event_rows,
  coalesce(f.fact_rows, 0) as persisted_fact_rows,
  coalesce(r.relationship_rows, 0) as persisted_relationship_rows
from messages m
join message_extractions me
  on me.message_id = m.id
left join lateral (
  select count(*)::bigint as claim_rows
  from intelligence_claims ic
  where ic.message_id = m.id
) ic on true
left join lateral (
  select count(*)::bigint as observation_rows
  from intelligence_observations io
  where io.message_id = m.id
) io on true
left join lateral (
  select count(*)::bigint as event_rows
  from communication_events ce
  where ce.message_id = m.id
) ce on true
left join lateral (
  select count(*)::bigint as fact_rows
  from facts f
  where f.source_message_id = m.id
) f on true
left join lateral (
  select count(*)::bigint as relationship_rows
  from relationships r
  where r.source_message_id = m.id
) r on true
where (:chat_id = 0 or m.chat_id = :chat_id)
  and m.processing_status = 1
  and m.needs_reanalysis = false
  and me.needs_expensive = false
  and coalesce(me.is_quarantined, false) = false
  and coalesce(ic.claim_rows, 0) = 0
  and coalesce(io.observation_rows, 0) = 0
  and coalesce(ce.event_rows, 0) = 0
  and coalesce(f.fact_rows, 0) = 0
  and coalesce(r.relationship_rows, 0) = 0
order by m.chat_id asc, m.id asc
limit 200;

-- 12) Recent write-volume indicator (24h) for sanity threshold review
select
  m.chat_id,
  count(*)::bigint as messages_24h
from messages m
where (:chat_id = 0 or m.chat_id = :chat_id)
  and m.created_at >= now() - interval '24 hour'
group by m.chat_id
order by messages_24h desc, m.chat_id asc
limit 50;
