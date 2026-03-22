# Sprint 18 Acceptance Draft

## Purpose

Validate that competing relationship context is integrated safely as a reviewable advisory layer and does not silently override canonical reasoning.

## Acceptance Checklist

## Invocation and Ordering

- Stage6 invokes competing interpretation after Sprint 17 ingestion artifacts are available
- invocation order is deterministic and replay-safe
- no mutation of Sprint 17 ingestion records by competing interpreter

## Advisory Output Contract

- outputs include graph/timeline/state/strategy advisory artifacts
- outputs are explicitly marked non-authoritative
- provenance is present for each produced artifact

## Review Gate Enforcement

- high-impact modifiers require explicit review before canonical effect
- approve/reject/defer flow is auditable
- blocked override attempts are emitted with reason and review path

## Safety Rules

- `status_override` is blocked
- `period_rewrite` is blocked
- `edge_delete` is blocked
- `strategy_force_primary` is blocked

## Runtime Stability

- existing baseline runtime path remains functional
- Sprint 17 ingestion behavior is not regressed
- no accidental coupling to shared smoke wiring

## Hold Conditions

Hold Sprint 18 if any of these are true:

- competing outputs can directly override canonical state without review
- blocked operations are not blocked or not visible
- provenance/review fields are missing on advisory artifacts
- Stage6 ordering conflicts with Sprint 17 ingestion lifecycle
- baseline runtime behavior regresses

## Pass Condition

Sprint 18 passes if:

- competing context works as a controlled advisory extension
- review gate enforces authoritativeness boundaries
- runtime stability and provenance/audit guarantees are preserved
