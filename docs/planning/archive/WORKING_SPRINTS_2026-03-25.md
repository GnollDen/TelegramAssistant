# Working Sprints

## Date

2026-03-25

## Purpose

This document turns the current project backlogs into execution-ready working sprints.

Source inputs:
- `docs/FULL_REVIEW_BACKLOG_2026-03-25.md`
- `docs/PROJECT_REVIEW_BACKLOG_2026-03-25.md`
- `docs/STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`

The goal is to give the team a practical order of work:
- what to do first
- what belongs together
- what should not be mixed in the same sprint
- what needs manual testing or A/B validation

## Sprinting Principles

1. P0 correctness before breadth
- silent-loss, queue semantics, identity, and control-plane safety come before new feature expansion

2. Narrow sprint scopes
- each sprint should solve one class of risk
- avoid mixing broad infra, Stage 5 semantics, and Stage 6 product work in one pass

3. Verification is mandatory
- every sprint must end with explicit verification, not only code changes

4. Human review gates where quality matters
- any sprint that materially affects AI output quality or operator UX must include manual review and/or A/B checks

## Sprint 1: Control-Plane Safety

### Goal

Remove fail-open and unhealthy-runtime ambiguity in the control plane.

### Scope

- fail-closed runtime-role parsing
- readiness vs liveness split
- startup guards for unsafe/incomplete config
- insecure default removal in compose/env posture

### Primary backlog items

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P0.1 fail-closed runtime roles
- P0.2 readiness vs liveness
- P0.7 insecure runtime defaults

### Files/components likely involved

- `src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs`
- host health/readiness paths
- `docker-compose.yml`
- related startup/config validation paths

### Exit criteria

- invalid role config does not silently widen runtime scope
- readiness fails when critical queue/Stage 5 path is not operational
- liveness remains narrow and process-oriented
- startup blocks or warns hard on unsafe config defaults

### Manual testing

- startup with invalid runtime role
- startup with missing critical env/config
- readiness/liveness differential checks

### A/B testing

- not needed

## Sprint 2: Stage 5 Delivery and Queue Semantics

### Goal

Eliminate silent-loss and queue poison/reclaim risks in Stage 5 intake/apply flow.

### Scope

- move durable processed state after successful apply
- dedupe reclaimed entries
- poison-entry quarantine/DLQ + ack path
- unique consumer naming per instance

### Primary backlog items

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P0.1 silent-loss window
- P0.2 Redis reclaim and poison semantics

### Files/components likely involved

- `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs`
- `src/TgAssistant.Infrastructure/Database/MessageRepository.cs`
- `src/TgAssistant.Infrastructure/Redis/RedisMessageQueue.cs`

### Exit criteria

- no message is marked fully processed before successful effective apply
- reclaimed stream entries do not duplicate work spuriously
- poison entries do not loop forever
- queue behavior remains stable after restarts/reclaims

### Manual testing

- crash/restart during apply
- reclaim behavior on pending entries
- malformed payload handling

### A/B testing

- not needed

## Sprint 3: Database Identity and Watermark Integrity

### Goal

Lock down DB identity semantics and monotonic state updates.

### Scope

- unified message identity contract
- DB/repository alignment
- unique constraints and conflict-safe upserts for facts/relationships
- monotonic/CAS-safe watermarks

### Primary backlog items

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P0.3 message identity contract
- P0.4 watermark CAS safety
- P1.3 fact/relationship uniqueness and upsert

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P0.4 DB-safe identity and conflict semantics
- P0.5 watermark monotonic/CAS-safe

### Files/components likely involved

- `src/TgAssistant.Infrastructure/Database/Migrations/0001_initial_schema.sql`
- `src/TgAssistant.Infrastructure/Database/MessageRepository.cs`
- `src/TgAssistant.Infrastructure/Database/FactRepository.cs`
- relationship repositories
- `src/TgAssistant.Infrastructure/Database/AnalysisStateRepository.cs`

### Exit criteria

- message identity contract is explicit and aligned across DB and runtime
- fact/relationship duplicates are structurally harder to create
- watermarks cannot move backward silently

### Manual testing

- duplicate insertion attempts
- concurrent upsert simulation
- watermark regression simulation

### A/B testing

- not needed

## Sprint 4: Prompt Lifecycle and Validation Hardening

### Goal

Make prompt contracts managed, versioned, and enforceable.

### Scope

- prompt version/checksum lifecycle
- update-if-changed semantics
- single source of truth for summary prompt
- stronger semantic validation, not only shape validation

### Primary backlog items

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P1.1 prompt version/checksum lifecycle
- P1.2 summary prompt single-source
- P2.2 semantic validation

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P1.1 prompt versioning and drift control

### Files/components likely involved

- Stage 5 prompt files
- summary worker prompt paths
- extraction validators
- template persistence/update paths

### Exit criteria

- prompt DB/runtime/code drift is visible and controlled
- summary prompt is no longer split across inconsistent sources
- semantic validation catches more contract breakage than shape validation alone

### Manual testing

- prompt drift simulation
- validation failure behavior
- summary consistency check across paths

### A/B testing

- targeted prompt quality spot-checks only

## Sprint 5: Stage 6 Execution Discipline

### Goal

Make Stage 6 execution more controlled and less wasteful.

### Scope

- end-to-end `CancellationToken` propagation
- remove/limit `CancellationToken.None` in paid paths
- shared persisted cooldown state
- verify degrade/stop behavior

### Primary backlog items

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P0.6 Stage 6 cancellation propagation
- P1.2 shared persisted cooldown state

### Files/components likely involved

- `src/TgAssistant.Intelligence/Stage6/BotChatService.cs`
- downstream Stage 6 tool/chat execution paths
- cooldown/degrade state handling

### Exit criteria

- cancelled Stage 6 work stops meaningfully
- paid calls do not leak after caller cancellation in normal paths
- cooldown state survives restart/multi-instance conditions correctly

### Manual testing

- cancellation scenarios
- timeout/degrade scenarios

### A/B testing

- not required for correctness
- optional quality spot-check after execution changes

## Sprint 6: Stage 6 Artifact Foundation

### Goal

Begin productization of Stage 6 with persisted artifacts.

### Scope

- artifact types
- artifact storage model
- read paths for bot/web
- refresh markers and stale handling

### Primary backlog items

From `STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`:
- Sprint 1 Artifact Foundation

### Exit criteria

- core Stage 6 outputs exist as stable artifacts
- repeated access does not require ad hoc regeneration every time

### Manual testing

- dossier/current_state regeneration vs reuse
- artifact refresh behavior

### A/B testing

- not required for first persistence pass

## Sprint 7: Stage 6 Core Quality

### Goal

Raise quality on core Stage 6 outputs before broader case generation.

### Scope

- dossier quality
- current_state quality
- tool synthesis quality
- fact/relationship/event/uncertainty structure

### Primary backlog items

From `STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`:
- Sprint 2 Dossier and State Quality

### Exit criteria

- stable baseline quality on dossier/current_state
- raw dump tendency remains controlled

### Manual testing

- blind/manual review on real cases

### A/B testing

- `gpt-4o-mini` vs `grok-4-fast`
- dossier/state only

## Sprint 8: Case Model and Minimal Auto Case Generation

### Goal

Move Stage 6 from one-off outputs toward actionable workflow.

### Scope

- case schema
- lifecycle
- linked artifacts
- minimal auto-generation rules

### Primary backlog items

From `STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`:
- Sprint 3 Case Model
- Sprint 4 Minimal Auto Case Generation

### Exit criteria

- Stage 6 can raise explicit actionable cases
- case noise is reviewable and bounded

### Manual testing

- real-case review on generated queue

### A/B testing

- threshold/rule tuning only if needed

## Sprint 9: Operator Surfacing

### Goal

Make Stage 6 usable through bot/web as an operator layer.

### Scope

- web queue
- case detail
- artifact detail
- bot surfacing of urgent/ready/input-needed items
- resolve/accept/reject flow

### Primary backlog items

From `STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`:
- Sprint 5 Operator Layer Surfacing

### Exit criteria

- Stage 6 becomes operationally usable through interfaces, not only through internal tests

### Manual testing

- full operator workflow in web
- full operator workflow in bot

### A/B testing

- optional UX/content testing

## Sprint 10: Observability, Eval, and Integrity Tooling

### Goal

Finish the loop on quality, silent regressions, and operational visibility.

### Scope

- duplicate-rate metrics
- processed-without-apply metrics
- phase-guard anomaly counters
- prompt drift metrics
- Redis PEL aging/reclaim signals
- AI quality/eval gates
- migration CI guard
- fault-injection/integrity jobs

### Primary backlog items

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P1.4 AI quality gates
- P2.1 migration CI guard
- P2.2 fault-injection recovery testing
- P2.4 silent-regression observability

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P2.1 migration ordering guard
- P2.3 integrity/fault-injection jobs

### Exit criteria

- silent regressions become easier to detect early
- quality and cost can be monitored by scenario

### Manual testing

- targeted failure drills
- integrity checks after recovery events

### A/B testing

- regular model/prompt regression packs

## Recommended Immediate Work Start

If starting now, use this order:

1. Sprint 1
2. Sprint 2
3. Sprint 3
4. Sprint 4
5. Sprint 5

Only then shift the main focus into broader Stage 6 productization (Sprints 6-10).

## Agent Guidance

When delegating these sprints:
- keep sprint boundaries narrow
- do not mix Stage 5 correctness with Stage 6 product UX in one execution pass
- require explicit verification evidence at the end of each sprint
- use manual test gates where Stage 6 output quality is affected
- use A/B only where prompt/model quality is the main question
