# Competing Context Sprint 18 Integration Design

## Purpose

Define a clean, non-conflicting integration design for Sprint 18 so competing relationship context can be connected to Stage6 outputs after Sprint 17 external archive ingestion stabilizes.

This is a design package, not live runtime activation.

## Constraints

- no Program.cs changes in this prep task
- no appsettings.json changes in this prep task
- no shared smoke wiring changes in this prep task
- no external archive persistence schema changes in this prep task
- no live runtime path activation in this prep task

## Inputs and Dependencies

Primary upstream dependency is Sprint 17 external archive ingestion output.

Sprint 18 consumes prepared/reviewable outputs from external archive ingestion, not raw external payloads directly.

Required upstream artifacts per case:

- validated external records
- provenance and weighting output
- linkage-ready artifacts for graph/timeline/clarification
- source class labels including `competing_relationship_archive`

## Integration Targets

### 1. Graph

Competing context integration should produce additive graph hints only:

- influence hints for focal and competing actors
- confidence-capped edge weighting suggestions
- hypothesis label required
- provenance references required for each hint

Must not auto-apply:

- edge deletion
- edge semantic replacement of high-confidence canonical edges
- focal pair identity changes

### 2. Timeline Annotations

Competing context integration should attach reviewable annotations to periods/events:

- annotation type `competing_context`
- uncertainty flag when evidence is ambiguous
- linkage to source records and evidence refs

Must not auto-apply:

- period boundary rewrite
- period split/merge as authoritative change
- historical certainty backfill from competing context alone

### 3. Current State Modifiers

Competing context integration should emit bounded modifiers only:

- `external_pressure_delta`
- `ambiguity_delta`
- `confidence_cap`
- rationale references

These modifiers remain additive, non-authoritative, and capped.

Must not auto-apply:

- direct current state label replacement
- direct dynamics replacement
- confidence uplift driven only by competing context

### 4. Strategy Constraints

Competing context integration should emit strategy guardrails:

- pacing constraints
- escalation safety constraints
- explicit keep-safer-alternative constraints

Must not auto-apply:

- forced primary strategy
- silent suppression of safer alternatives

### 5. Review Gate

Competing context outputs must pass explicit review gate before becoming authoritative input to canonical snapshots.

Review gate responsibilities:

- show baseline vs competing-assisted diff
- approve/reject/defer by artifact type
- track reason, actor, timestamp
- preserve rollback path

## Apply Policy Matrix

### Auto-apply allowed

Only low-risk additive artifacts may be auto-applied to non-authoritative read models:

- additive graph hint records with confidence cap
- timeline annotation candidates with `requires_review=true`
- strategy caution flags marked advisory

Auto-apply here means storage and visibility as candidate artifacts, not canonical truth replacement.

### Review-only

Always review-gated before canonical effect:

- any current-state modifier affecting confidence cap
- any strategy constraint that may reduce action space
- any timeline annotation marked medium/high severity
- any competing-context artifact linked to high influence actors

### Always additive and non-authoritative

The following remain non-authoritative even after persistence:

- competing graph hints
- competing timeline annotations
- competing state modifiers
- competing strategy constraints

They can inform interpretation but cannot replace canonical fact/state layers without explicit approval flow.

### Always blocked override attempts

Always blocked at interpretation layer and emitted as alerts:

- `status_override`
- `period_rewrite`
- `edge_delete`
- `strategy_force_primary`

Blocked alert payload must include:

- source record id
- attempted operation
- reason blocked
- required review path

## Runtime Invocation Plan (Sprint 18 Target)

## Stage6 invocation order

1. Load baseline case context (existing canonical Stage6 inputs).
2. Load Sprint 17 external archive prepared artifacts for case scope.
3. Filter to competing-context source class (`competing_relationship_archive`) and compatible signal types.
4. Run Competing Context Interpretation service.
5. Persist interpretation output as reviewable competing-context artifacts.
6. Run state and strategy readers with dual-input contract:
   - canonical baseline inputs
   - competing-context advisory inputs gated by review flags
7. Produce final Stage6 response package:
   - canonical outcome
   - competing-context advisory overlay
   - review alerts and blocked attempts

## Ordering relative to Sprint 17 ingestion

- external archive ingestion completes first (Sprint 17 path)
- competing interpretation runs after ingestion materialization
- competing interpretation must not mutate ingestion records
- retry/replay should be deterministic from persisted ingestion artifacts

## Output consumption rules

State/Profile/Strategy services may read:

- advisory competing outputs tagged additive/non-authoritative
- review-approved competing outputs where approval exists

State/Profile/Strategy services must not read:

- blocked override attempts as actionable input
- unreviewed high-impact modifiers as authoritative input
- any artifact missing provenance or review requirements

## Non-conflict contract with Sprint 17

To avoid conflicts with Sprint 17 runtime integration:

- no host wiring changes in this prep task
- no shared runtime invocation changes in this prep task
- no schema mutation in this prep task
- no reuse of Sprint 17 smoke entrypoints in this prep task

Sprint 18 implementation should integrate via additive Stage6 composition points only after Sprint 17 acceptance is stable.

## Suggested Sprint 18 Implementation Slices

1. Stage6 orchestration hook for competing interpretation.
2. Reviewable artifact repository/read model for competing outputs.
3. State/strategy dual-input readers with strict review flag checks.
4. Review gate UI/API contracts for approve/reject/defer.
5. Safety smoke for blocked overrides and non-authoritative guarantees.

## Risks and Mitigations

- Risk: competing hints accidentally treated as authoritative.
  Mitigation: hard-coded `is_authoritative=false`, review gate requirement, blocked operations list.

- Risk: ordering race with Sprint 17 ingestion.
  Mitigation: consume only persisted ingestion artifacts after successful ingestion completion marker.

- Risk: excessive strategy suppression from noisy competing data.
  Mitigation: confidence caps, additive constraints, review requirement for high-impact constraints.
