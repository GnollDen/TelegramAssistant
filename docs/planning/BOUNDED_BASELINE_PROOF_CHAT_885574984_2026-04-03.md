# Bounded Baseline Proof: `chat:885574984` (2026-04-03)

## 1. Scope

- `chat_id`: `885574984`
- `scope_key`: `chat:885574984`
- Operator person: `0ba107de-8fcf-4968-8cae-c2caab0b22c3`
- Tracked person: `80c384f3-a79f-4356-b832-45e636353f59`
- Bounded slice: one seeded operator/tracked pair and one bounded recompute queue set (`stage6_bootstrap`, `dossier_profile`, `pair_dynamics`, `timeline_objects`) for this scope only.
- Environment assumptions:
  - DB/Redis substrate available.
  - Runtime run in `ops` role.
  - Seed bootstrap contract applied for the same `scope_key`.

## 2. What Was Exercised

- Seed path and seed apply for the bounded scope.
- Stage6 bootstrap (`graph_init`) on seeded scope.
- Stage7 durable formation families:
  - `dossier_profile`
  - `pair_dynamics`
  - `timeline_objects` (materializing `event`, `timeline_episode`, `story_arc`)
- Stage8 recompute queue execution and outcome gating.
- Control-plane recovery path:
  - `safe_mode` deferral observed.
  - defects resolved.
  - runtime state returned to `normal`.

## 3. Evidence

### Runtime/log evidence

- Pre-fix failure capture:
  - [artifacts/stage678/stage678_ops_run_20260403T180844Z.log](/home/codex/projects/TelegramAssistant/artifacts/stage678/stage678_ops_run_20260403T180844Z.log)
- Startup guard failure before env correction:
  - [artifacts/stage678/stage678_ops_run_20260403T184301Z.log](/home/codex/projects/TelegramAssistant/artifacts/stage678/stage678_ops_run_20260403T184301Z.log)
- Control-plane `safe_mode` deferrals:
  - [artifacts/stage678/stage678_ops_run_20260403T184317Z.log](/home/codex/projects/TelegramAssistant/artifacts/stage678/stage678_ops_run_20260403T184317Z.log)
- Successful bounded run:
  - [artifacts/stage678/stage678_ops_run_20260403T184531Z.log](/home/codex/projects/TelegramAssistant/artifacts/stage678/stage678_ops_run_20260403T184531Z.log)
  - Shows:
    - Stage6 bootstrap initialized for `scope_key=chat:885574984`
    - Stage7 timeline durable outputs persisted/materialized
    - Stage8 recompute completed with `result_status=result_ready`, `gate_promoted=3`
  - Note: this final log directly shows the `timeline_objects` completion; `dossier_profile` and `pair_dynamics` completions are evidenced by persisted `model_pass_runs` and `stage8_recompute_queue_items` rows for the same scope.

### Operator/runtime path reference

- Seeded bounded runbook:
  - [docs/runbooks/bounded-seeded-pre-run-chat-885574984.md](/home/codex/projects/TelegramAssistant/docs/runbooks/bounded-seeded-pre-run-chat-885574984.md)
- Runtime host entry and guard semantics:
  - [src/TgAssistant.Host/Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs)

### Persisted DB facts (captured 2026-04-03 UTC)

- `persons` (scope seed presence):
  - 2 rows for `chat:885574984` (`operator_root` + `tracked_person`), both created at `2026-04-03 17:58:47.891878+00`.
- `person_operator_links` (seed link):
  - row exists for `chat:885574984` with `link_type=operator_tracked_seed`, `source_binding_normalized=bootstrap_scope_seed`.
- `model_pass_runs`:
  - `stage6_bootstrap` and `stage7_durable_formation` rows for this scope with `result_status=result_ready`.
- `stage8_recompute_queue_items`:
  - all four target families completed for this scope:
    - `stage6_bootstrap`
    - `dossier_profile`
    - `pair_dynamics`
    - `timeline_objects`
  - each with `status=completed` and `last_result_status=result_ready`.
- Durable objects:
  - `durable_object_metadata` has 6 families for this scope:
    - `dossier`, `profile`, `pair_dynamics`, `event`, `timeline_episode`, `story_arc`
  - all 6 are `promotion_state=promoted`, `truth_layer=canonical_truth`, `status=active`.
  - family table counts are `1` each in:
    - `durable_dossiers`
    - `durable_profiles`
    - `durable_pair_dynamics`
    - `durable_events`
    - `durable_timeline_episodes`
    - `durable_story_arcs`
- Control-plane state:
  - latest active `runtime_control_states` row is `state=normal`, activated at `2026-04-03 18:45:37.972929+00`.
  - `runtime_defects` for `chat:885574984` show control-plane defects in `resolved` status (critical/high classes resolved).

### Evidence query pack (shape)

```sql
-- runtime control state
select id,state,reason,source,is_active,activated_at_utc,deactivated_at_utc
from runtime_control_states
order by activated_at_utc desc;

-- runtime defects for bounded scope
select status,defect_class,severity,count(*) as n,min(first_seen_at_utc),max(last_seen_at_utc),max(resolved_at_utc)
from runtime_defects
where scope_key='chat:885574984'
group by status,defect_class,severity;

-- stage8 queue outcomes
select scope_key,target_family,status,last_result_status,attempt_count,max_attempts,completed_at_utc
from stage8_recompute_queue_items
where scope_key='chat:885574984'
order by updated_at_utc desc;

-- durable promotion/truth state
select object_family,promotion_state,truth_layer,status,count(*) as n
from durable_object_metadata
where scope_key='chat:885574984'
group by object_family,promotion_state,truth_layer,status;
```

## 4. What Is Proven

- Real DB-backed bounded execution for `chat:885574984` completed across Stage6 -> Stage7 -> Stage8.
- This is not smoke-only evidence:
  - queue items completed in persistent Stage8 queue tables,
  - model pass runs persisted with `result_ready`,
  - durable objects were actually written and promoted in DB,
  - control-plane states and defect lifecycle persisted and converged to `normal`.
- With valid startup config in place, the three landed fixes below were sufficient to move from observed FK/control-plane failures to successful bounded completion:
  - `0fde10c` seed apply FK-safe ordering
  - `db482c6` Stage7 dossier/pair FK ordering + defect auto-resolve
  - `3e5f29f` Stage7 timeline FK ordering

## 5. What Is Not Proven

- Not a broad archive-wide proof.
- Not a full product readiness claim.
- Not a full PRD completion claim.
- Not a broad gateway/provider rollout proof.
- Not proof that all chats/scopes will converge without additional edge-case handling.

## 6. Operational Preconditions

- Required runtime/env conditions:
  - Postgres and Redis healthy.
  - runtime role includes `ops`.
  - valid gateway/runtime startup config (guard must pass).
- Required operator actions:
  - apply bounded seed contract for `chat:885574984`,
  - run bounded recompute path under ops runtime,
  - allow control-plane to process and clear defects before final success verification.

## 7. Recommended Next Step

- Run a second independent bounded scope verification (`chat:*` with different operator/tracked pair) to check transferability of this baseline before any broader rollout decision.
