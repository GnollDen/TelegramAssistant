# Sprint 11 Auto Case Generation Implementation Notes (2026-03-25)

## What Was Implemented

Sprint 11 adds an explicit Stage 6 autonomous case-generation runtime path:

- `Stage6AutoCaseGenerationWorkerService` registered only for runtime role `Stage6`
- `Stage6AutoCaseGenerationService` as first-wave rules engine over unified `stage6_cases`
- semantic dedupe via stable auto-rule source identity:
  - `source_object_type = auto_case_rule`
  - deterministic `source_object_id` per rule intent

## First-Wave Auto Cases

Implemented first-wave generation covers:

- `needs_input` (fallback for blocked strategy alignment)
- `needs_review` (low-confidence strategy)
- `risk` (state-risk signals)
- `state_refresh_needed` (stale state + material message delta)
- `dossier_candidate` (material evidence delta)
- `draft_candidate` (pending strategy without fresh draft outcome)
- clarification/missing-context typed cases:
  - `clarification_missing_data`
  - `clarification_ambiguity`
  - `clarification_evidence_interpretation_conflict`
  - `clarification_next_step_blocked`

## Clarification Grounding Rule

Auto clarification questions are generated only with concrete gap anchors:

- message-local anchors (`message_id`, timestamp)
- date/period anchoring gaps
- people ambiguity gaps (multiple participants)
- evidence vs interpretation conflict in current state
- blocked next step requiring operator input

## Dedupe, Prioritization, Reopen, Noise Suppression

- dedupe is semantic: one stable rule-keyed case per scope/rule intent
- prioritization is deterministic first-wave (`blocking` / `important` / `optional`) from signal strength
- reopen occurs only when new material evidence arrives after close/stale timestamp
- ordinary churn suppression:
  - message-count thresholds for refresh/candidate rules
  - update cooldown to avoid frequent rewrites
  - stale demotion for long-inactive auto signals

## Verification Hook

Added smoke entrypoint:

- `--auto-case-smoke`

Smoke verifies:

- risk auto-case generation
- clarification case generation with grounded question text
- duplicate suppression across repeated generation runs
- reopen after new evidence
- no queue multiplication under low ordinary churn
