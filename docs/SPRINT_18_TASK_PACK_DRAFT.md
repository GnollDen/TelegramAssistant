# Sprint 18 Task Pack Draft

## Name

Competing Relationship Context Integration

## Goal

Integrate competing relationship context into Stage6 as a reviewable, non-authoritative advisory layer that consumes Sprint 17 external archive outputs without breaking existing runtime behavior.

## Why This Sprint

After Sprint 17, external archive ingestion exists as a controlled runtime capability.

Sprint 18 focuses on a special domain case:

- competing relationship context interpretation
- guarded influence on graph/timeline/state/strategy
- strict review-gated application

## Read First

1. `docs/PRODUCT_DECISIONS.md`
2. `docs/CASE_ID_POLICY.md`
3. `docs/EXTERNAL_ARCHIVE_INGESTION_POLICY.md`
4. `docs/COMPETING_RELATIONSHIP_CONTEXT_POLICY.md`
5. `docs/COMPETING_CONTEXT_SPRINT18_INTEGRATION_DESIGN.md`
6. `docs/SPRINT_17_TASK_PACK.md`
7. `docs/SPRINT_17_ACCEPTANCE.md`

## Scope

In scope:

- Stage6 invocation of competing-context interpretation
- additive competing-context artifact persistence/read models
- review gate contract for competing-context artifacts
- guarded state/strategy advisory consumption
- deterministic replay from Sprint 17 persisted artifacts

Out of scope:

- direct canonical overrides from competing context
- migration of existing Stage5 budget/eval behavior
- changes to unrelated smoke infrastructure

## Product Rules To Respect

- competing context is non-authoritative by default
- additive hints are allowed; authoritative replacement is not
- provenance, review history, and rollback are mandatory
- blocked override attempts must be explicit and visible

## Required Deliverables

### 1. Stage6 Orchestration Hook

Add Stage6 orchestration step that invokes competing-context interpretation after Sprint 17 ingestion outputs are available.

### 2. Advisory Artifact Layer

Persist/read competing outputs as advisory artifacts:

- graph hints
- timeline annotations
- state modifiers
- strategy constraints
- blocked override alerts

### 3. Review Gate Integration

Implement approve/reject/defer contract for competing artifacts with actor, reason, and timestamp.

### 4. Consumption Guardrails

Ensure state/strategy services:

- can read advisory outputs only under policy
- cannot consume blocked operations as actionable instructions
- cannot treat unreviewed high-impact modifiers as authoritative

### 5. Deterministic Replay

Support re-interpretation based on persisted Sprint 17 ingestion artifacts and stable payload hashes.

### 6. Safety Verification Path

Add competing-context safety smoke/check path proving:

- blocked overrides are blocked
- high-impact artifacts require review
- non-authoritative flags are preserved

## Verification Required

1. `dotnet build TelegramAssistant.sln`
2. startup check for unchanged baseline paths
3. external archive flow still works as in Sprint 17
4. competing-context safety smoke/check passes
5. review-gate enforcement verified for high-impact modifiers

## Definition of Done

Sprint 18 is complete only if:

1. competing context is integrated as advisory Stage6 layer
2. no silent override behavior exists
3. review gate controls canonical impact
4. baseline runtime remains stable
5. blocked override attempts are visible and auditable

## Final Report Required

1. what changed
2. files changed
3. how Stage6 invocation order works now
4. what was verified
5. remaining risks and next hardening step
