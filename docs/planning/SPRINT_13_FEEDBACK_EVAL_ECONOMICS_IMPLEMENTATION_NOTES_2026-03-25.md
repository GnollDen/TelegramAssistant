# Sprint 13 Implementation Notes (Feedback, Evaluation, Economics)

Date: 2026-03-25

## Scope Applied

This implementation pass follows the Sprint 13 planning authority for:

- feedback capture on Stage 6 cases/artifacts
- explicit case outcome/resolution tracking
- reusable Stage 6 eval scenario packs
- scenario-level cost/latency/model visibility
- explicit clarification/behavioral usefulness evaluation hooks

No broad product redesign was introduced.

## What Was Added

1. Stage 6 operator feedback persistence
- New table: `stage6_feedback_entries`
- Supports feedback kinds:
  - `accept_useful`
  - `reject_not_useful`
  - `correction_note`
  - `refresh_requested`
- Supports feedback dimensions:
  - `general`
  - `clarification_usefulness`
  - `behavioral_usefulness`
- Feedback is linked to `stage6_case_id` and/or `artifact_type` within scope.

2. Stage 6 case outcome persistence
- New table: `stage6_case_outcomes`
- Tracks explicit outcomes:
  - `resolved`
  - `rejected`
  - `stale`
  - `refreshed`
  - `answered_by_user`
- Includes `case_status_after` and `user_context_material` marker.

3. Economics telemetry enrichment
- Added `latency_ms` to `analysis_usage_events`.
- OpenRouter Stage 5 chat/embedding usage logging now captures latency where available.

4. Eval scenario visibility enrichment
- `ops_eval_runs`: `scenario_pack_key`
- `ops_eval_scenario_results`:
  - `scenario_type`
  - `latency_ms`
  - `cost_usd`
  - `model_summary_json`
  - `feedback_summary_json`

5. Stage 6 reusable eval packs
- Default experiment `stage6_guarded_default` now includes pack `stage6_quality`.
- New scenarios:
  - `stage6_dossier_current_state_quality`
  - `stage6_draft_review_quality`
  - `stage6_clarification_usefulness`
  - `stage6_case_usefulness_noise`
  - `stage6_behavioral_usefulness`

## Operator Surface Integration

- Web:
  - case detail now exposes feedback history and outcome history
  - artifact detail now exposes artifact-linked feedback history
  - case/artifact actions and clarification answers persist feedback/outcome records
- Bot:
  - `/resolve`, `/reject`, `/refresh`, `/annotate`, `/answer` now persist feedback/outcome signals
  - new explicit `/feedback` command for direct operator scoring

## Verification Shape

Verification services and route rendering were extended so Sprint 13 data shows up in operator-facing flows and ops-eval surfaces.
