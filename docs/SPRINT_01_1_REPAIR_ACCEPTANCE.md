# Sprint 01.1 Repair Acceptance

## Purpose

This checklist validates that Sprint 1 has been repaired into a usable foundation, not a parallel truth island.

## Acceptance Checklist

## Canonical Layer Alignment

- existing canonical objects are reused where appropriate
- no unnecessary duplicated domain versions of already-canonical concepts remain
- new domain objects clearly reference existing canonical objects where relevant

## No Parallel Truth Drift

- there is no obvious second unsynchronized truth system
- case/chat/evidence linkage is explicit enough for future orchestration
- affected-domain objects are not floating without canonical anchors

## Repository Readiness

Confirm update paths exist at minimum for:

- clarification question status/priority/resolution
- inbox item status
- conflict status
- period lifecycle updates
- hypothesis lifecycle updates

## Review and Audit Minimum

- actor or equivalent provenance exists where needed
- review-event or audit trail exists for new domain objects
- manual changes can be distinguished from system-generated state

## Verification and Runtime

- solution builds
- migrations are coherent
- app startup works beyond the smoke shortcut alone
- no obvious breakage of current runtime registration is introduced

## Sprint 2 Readiness

- clarification orchestration can be implemented without another persistence redesign
- conflict handling can be implemented without another repository redesign
- dependency-based recompute can be implemented without schema churn

## Hold Conditions

Do not pass Sprint 1.1 if any of these remain true:

- duplicated truth layers still exist without explicit contract
- repository update paths are still missing
- new domain objects still lack basic auditability
- runtime acceptance is still only foundation-smoke

## Pass Condition

Sprint 1.1 passes if:

- canonical-vs-new split is explicit and sane
- foundation is not duplicative
- Sprint 2 can start directly on top of it
