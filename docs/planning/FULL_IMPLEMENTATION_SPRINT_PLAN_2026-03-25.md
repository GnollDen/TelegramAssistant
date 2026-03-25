# Full Implementation Sprint Plan

## Date

2026-03-25

## Purpose

This document combines the current technical remediation backlogs and the Stage 6 productization roadmap into one execution-ready sprint plan.

Source inputs:
- `docs/planning/archive/FULL_REVIEW_BACKLOG_2026-03-25.md`
- `docs/planning/archive/PROJECT_REVIEW_BACKLOG_2026-03-25.md`
- `docs/planning/archive/WORKING_SPRINTS_2026-03-25.md`
- `docs/planning/STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`
- `docs/planning/PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`

The intent is to give one coherent answer to:
- what we build first
- what must be fixed before product expansion
- where manual testing is required
- where A/B quality gates are required
- what "done" means at each phase

## Program Structure

The full execution program is split into two major phases:

0. Pre-Sprint Gate
- source-of-truth cleanup
- runtime topology clarification
- first-wave operator contract clarification
- Stage 6 queue/artifact identity coverage added to backlog

1. Foundation and Risk Removal
- control-plane safety
- queue and delivery correctness
- DB identity and control-state safety
- prompt lifecycle and Stage 6 execution discipline

2. Stage 6 Productization
- artifacts
- cases
- operator workflow
- feedback and evaluation
- product contracts
- behavioral and user-context intelligence

The key rule is:
- correctness and silent-failure risk removal come before broader product breadth

## Pre-Sprint 0: Planning and Topology Gate

### Goal

Remove false assumptions before Sprint 1 starts.

This is a short gate sprint, not broad implementation work.
Its purpose is to make sure the execution program starts from one trusted planning set and a truthful runtime model.

### Scope

- lock one planning source of truth
- downgrade conflicting status docs from authoritative status
- write a short runtime topology note for what is actually deployable now
- clarify the first-wave Stage 6 operator contract
- add explicit backlog coverage for Stage 6 queue/artifact dedupe and canonical identity

### Why this exists

Pre-review verdict was:
- ready with a small pre-sprint fix list

Meaning:
- the project has enough real substrate to start execution
- but Sprint 1 should not start from conflicting planning assumptions

### Required outputs

1. Planning source-of-truth rule is explicit.
2. `BACKLOG_STATUS.md` is no longer read as an equally current execution authority.
3. Runtime topology is written down clearly:
   - what `Ingest` means
   - what `Stage 5` means
   - what `Stage 6` means
   - what `Web` means
   - what `Ops` means
   - which of these are truly deployable today
4. First-wave operator contract is explicit:
   - what `dossier` means
   - what is internal/raw vs operator-facing
   - how case scope and chat scope are established for first use
5. Stage 6 queue/artifact dedupe and canonical identity is explicit backlog work, not implicit future cleanup.

### Files/docs likely involved

- `docs/planning/README.md`
- `docs/planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md`
- `docs/planning/STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md`
- `docs/BACKLOG_STATUS.md`
- a short runtime-topology note in `docs/planning`

### Exit criteria

- planning pack is clearly authoritative for sprint start
- pre-sprint assumptions are explicit, not inferred
- no major conflict remains between project status wording and actual sprint entry conditions

### Manual testing

- planning-doc consistency review
- sprint-entry checklist review

### A/B testing

- not needed

## Phase 1: Foundation and Risk Removal

## Sprint 1: Control-Plane Safety

### Goal

Remove fail-open and unhealthy-runtime ambiguity in the control plane.

### Scope

- fail-closed runtime-role parsing
- readiness vs liveness split
- startup guards for unsafe or incomplete config
- insecure default removal in compose and env posture

This sprint is a hard gate, not optional polish.

### Backlog sources

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P0.1 fail-closed runtime roles
- P0.2 readiness vs liveness
- P0.7 insecure runtime defaults

### Likely files/components

- `src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs`
- host health and readiness paths
- `docker-compose.yml`
- related startup/config validation paths

### Exit criteria

- invalid role config cannot silently widen runtime scope
- readiness fails when critical queue or Stage 5 path is not operational
- liveness stays process-oriented
- startup blocks or warns hard on unsafe config defaults

### Manual testing

- startup with invalid runtime role
- startup with missing critical env/config
- readiness vs liveness differential checks

### A/B testing

- not needed

## Sprint 2: Stage 5 Delivery and Queue Semantics

### Goal

Eliminate silent-loss and queue poison/reclaim risks in Stage 5 delivery flow.

### Scope

- move durable processed state after successful effective apply
- dedupe reclaimed entries
- poison-entry quarantine/DLQ plus ack path
- unique consumer naming per instance

### Backlog sources

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P0.1 silent-loss window
- P0.2 Redis reclaim and poison semantics

### Likely files/components

- `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs`
- `src/TgAssistant.Infrastructure/Database/MessageRepository.cs`
- `src/TgAssistant.Infrastructure/Redis/RedisMessageQueue.cs`

### Exit criteria

- no message is marked fully processed before successful effective apply
- reclaimed entries do not create spurious duplicate work
- poison entries do not loop forever
- queue behavior remains stable after restart and reclaim

### Manual testing

- crash or restart during apply
- reclaim behavior on pending entries
- malformed payload handling

### A/B testing

- not needed

## Sprint 3: Database Identity and Control-State Integrity

### Goal

Lock down DB identity semantics and monotonic control-state updates.

### Scope

- unified message identity contract
- DB and repository alignment
- unique constraints and conflict-safe upserts for facts and relationships
- monotonic and CAS-safe watermarks
- Stage 6 queue/artifact dedupe and canonical identity contract

### Backlog sources

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P0.3 message identity contract
- P0.4 watermark CAS safety
- P1.3 fact and relationship uniqueness/upsert

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P0.4 DB-safe identity and conflict semantics
- P0.5 watermark monotonic/CAS-safe

### Likely files/components

- `src/TgAssistant.Infrastructure/Database/Migrations/0001_initial_schema.sql`
- `src/TgAssistant.Infrastructure/Database/MessageRepository.cs`
- `src/TgAssistant.Infrastructure/Database/FactRepository.cs`
- relationship repositories and migrations
- `src/TgAssistant.Infrastructure/Database/AnalysisStateRepository.cs`

### Exit criteria

- message identity contract is explicit and aligned across DB and runtime
- fact and relationship duplicates are structurally harder to create
- watermarks cannot move backward silently
- Stage 6 queue/artifact identity rules are explicit enough to build on safely

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
- stronger semantic validation in addition to shape validation

### Backlog sources

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P1.1 prompt version/checksum lifecycle
- P1.2 summary prompt single-source
- P2.2 semantic validation

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P1.1 prompt versioning and drift control

### Likely files/components

- Stage 5 prompt files
- summary worker prompt paths
- extraction validators
- template persistence/update paths

### Exit criteria

- prompt DB/runtime/code drift is visible and controlled
- summary prompt is no longer split across inconsistent sources
- semantic validation catches contract breakage beyond shape checks

### Manual testing

- prompt drift simulation
- validation-failure behavior
- summary consistency across paths

### A/B testing

- targeted prompt quality spot-checks only

## Sprint 5: Stage 6 Execution Discipline

### Goal

Make Stage 6 execution more controlled, cancellable, and restart-safe.

### Scope

- end-to-end `CancellationToken` propagation
- remove or limit `CancellationToken.None` in paid paths
- shared persisted cooldown state
- verify degrade and stop behavior

### Backlog sources

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P0.6 Stage 6 cancellation propagation
- P1.2 shared persisted cooldown state

### Likely files/components

- `src/TgAssistant.Intelligence/Stage6/BotChatService.cs`
- downstream Stage 6 tool/chat execution paths
- cooldown/degrade handling

### Exit criteria

- cancelled Stage 6 work stops meaningfully
- paid calls do not continue after cancellation in normal paths
- cooldown survives restart and multi-instance conditions

### Manual testing

- cancellation scenarios
- timeout/degrade scenarios

### A/B testing

- optional quality spot-check after execution changes

## Sprint 6: Observability and Recovery Tooling

### Goal

Close the highest-value blind spots and add recovery confidence tooling.

### Scope

- processed-without-apply observability
- duplicate-rate SLOs
- watermark monotonicity alarms
- Redis PEL aging/reclaim signals
- migration ordering CI guard
- integrity and fault-injection jobs

### Backlog sources

From `FULL_REVIEW_BACKLOG_2026-03-25.md`:
- P2.1 CI migration guard
- P2.2 fault-injection recovery testing
- P2.4 silent-regression observability

From `PROJECT_REVIEW_BACKLOG_2026-03-25.md`:
- P2.1 migration ordering/naming guard
- P2.3 integrity/fault-injection jobs

### Exit criteria

- major silent-regression classes have explicit signals
- migration-order drift is caught in CI
- recovery paths are stress-tested, not only assumed

### Manual testing

- restart and recovery drills
- reclaim or corrupted payload drills

### A/B testing

- not needed

## Phase 2: Stage 6 Productization

## Sprint 7: Stage 6 Artifact Foundation

### Goal

Turn Stage 6 outputs into persisted, reusable artifacts.

### Scope

- artifact types and schemas
- artifact storage model
- read paths for bot/web
- refresh markers
- stale handling

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- persisted artifacts:
  - `dossier`
  - `current_state`
  - `strategy`
  - `draft`
  - `review`
  - `clarification_state`
- initial stale rules
- refresh split between auto and on-demand

### Exit criteria

- core Stage 6 outputs exist as stable artifacts
- repeated access can reuse artifacts instead of regenerating everything ad hoc

### Manual testing

- dossier and current_state regeneration vs reuse
- stale marking and refresh behavior

### A/B testing

- not required for first persistence pass

## Sprint 8: Dossier and Current-State Quality

### Goal

Raise the two core artifact types to a stable operator-usable baseline.

### Scope

- dossier synthesis quality
- current_state synthesis quality
- anti-dump behavior
- fact vs interpretation separation
- uncertainty framing
- signal-strength presentation

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- fact vs interpretation separation
- signal-strength scale:
  - `strong`
  - `medium`
  - `weak`
  - `contradictory`
- relational pattern output

### Exit criteria

- dossier is useful instead of a raw dump
- current_state is stable and plausible
- uncertainty is surfaced instead of hidden

### Manual testing

- blind/manual review on real cases
- factuality and omission review
- over-interpretation review

### A/B testing

- `gpt-4o-mini` vs `grok-4-fast`
- dossier/state only

## Sprint 9: Stage 6 Product Contracts

### Goal

Implement the missing reasoning and style contracts that define what Stage 6 is supposed to do.

### Scope

- fact vs interpretation contract in outputs
- relational pattern contract
- ethical strategy contract
- personal style calibration contract
- draft output shape:
  - main
  - softer alternative
  - more direct alternative

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- strategic optimization toward clarity, dignity, and non-manipulation
- anti-dushnost/service-tone/anxious-overexplaining rules
- lightweight persisted personal style profile

### Exit criteria

- Stage 6 outputs reflect explicit reasoning rules instead of ad hoc prompt behavior
- draft and strategy outputs respect style and ethical constraints consistently

### Manual testing

- style review on representative chats
- strategy review for manipulation/overreach risks

### A/B testing

- prompt/style A/B only if contract changes materially

## Pre-Sprint 9.5: Agent Context Refresh and Clarification Design

### Goal

Refresh the dev-agent context and lock the design for interactive clarification and user-supplied context before the case-system sprints begin.

### Scope

- refresh agents on latest code, PRD, backlog, and completed sprints
- validate that Sprint 10+ still match current system reality
- define interactive clarification case contract
- define user-supplied context contract
- define behavioral-profile introduction plan

### Why this exists

After Sprint 9, the system has stronger product contracts.
Before moving into case generation and operator workflows, agents should be re-anchored on the current code and planning truth so later implementation does not use stale assumptions.

### Deliverables

- refreshed implementation context packet for agents
- clarification-case design note
- user-supplied context contract note
- behavioral-profile backlog note
- updated sprint mapping for Sprints 10-13 if needed

Execution artifacts produced on `2026-03-25`:
- `PRE_SPRINT_9_5_CONTEXT_REFRESH_PACKET_2026-03-25.md`
- `PRE_SPRINT_9_5_CLARIFICATION_CASE_CONTRACT_2026-03-25.md`
- `PRE_SPRINT_9_5_USER_SUPPLIED_CONTEXT_CONTRACT_2026-03-25.md`
- `PRE_SPRINT_9_5_BEHAVIORAL_PROFILE_INTRO_PLAN_2026-03-25.md`
- `PRE_SPRINT_9_5_SPRINT_10_13_FIT_CHECK_2026-03-25.md`

### Exit criteria

- agents have an updated shared context baseline
- clarification and user-context layers are explicit backlog work
- Sprint 10+ assumptions are confirmed or minimally adjusted

### Manual testing

- design review only

### A/B testing

- not needed

## Sprint 10: Case Model

### Goal

Represent actionable Stage 6 work as explicit cases.

### Scope

- case schema
- case types
- case statuses
- artifact linking
- timestamps, priority, confidence, reason
- clarification case typing
- user-context source typing
- map existing queue primitives (`inbox`, `clarification`, `conflict`) into one explicit case lifecycle model

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- first-wave case types:
  - `needs_input`
  - `needs_review`
  - `risk`
  - `state_refresh_needed`
  - `dossier_candidate`
  - `draft_candidate`
- statuses:
  - `new`
  - `ready`
  - `needs_user_input`
  - `resolved`
  - `rejected`
  - `stale`

### Exit criteria

- Stage 6 can represent operator work as explicit cases
- cases carry enough structure to support queueing and review

### Manual testing

- case creation on real chats
- duplication/noise review

### A/B testing

- not required by default

## Sprint 11: Auto Case Generation

### Goal

Let Stage 6 identify and prioritize useful operator work on its own.

### Scope

- automatic case-creation rules
- case dedupe
- minimal prioritization/ranking
- refresh/reopen logic
- noise suppression rules
- system-detected missing-context cases
- clarification prompts based on message/date/people gaps
- runtime ownership for autonomous generation path must be explicit before full rollout

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- auto-create when:
  - uncertainty high
  - strong risk signal
  - blocking clarification gap
  - stale current state
  - dossier-worthy evidence change
- do not auto-create for:
  - weak/noisy signals
  - minor style suggestions
  - every small message update

### Exit criteria

- Stage 6 produces an operator queue with acceptable signal-to-noise
- queue does not flood on ordinary chat churn

### Manual testing

- review 20-30 generated cases
- false-positive and missed-value analysis

### A/B testing

- threshold/rule tuning if needed

## Sprint 12: Operator Workflow in Bot and Web

### Goal

Make Stage 6 cases and artifacts usable in real operator workflows.

### Scope

- explicit case queue
- bot/web responsibility split
- web case and artifact detail views
- bot surfacing for:
  - urgent items
  - ready outputs
  - needs-input items
- human actions:
  - resolve
  - reject
  - refresh
  - annotate
- answer clarification cases with user-supplied context
- show evidence summary before asking for user judgment
- bot as primary fast clarification intake
- web as expanded clarification/evidence review surface
- implementation extends existing bot/web surfaces; no broad web-platform rebuild is implied in this sprint

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- bot handles:
  - `/state`
  - `/draft`
  - `/review`
  - `/gaps`
  - `/answer`
  - `/timeline`
  - urgent items
  - quick decisions
- web handles:
  - dossier
  - expanded timeline
  - queue
  - artifact history
  - deep review
- notification policy:
  - pull-first
  - narrow push only for high-signal items

### Exit criteria

- Stage 6 has a real operator workflow
- bot and web are complementary instead of overlapping messily
- clarification intake split between bot and web is explicit and usable

### Manual testing

- full operator workflow in bot
- full operator workflow in web
- state transition verification after human actions

### A/B testing

- optional UX/content testing

## Sprint 13: Feedback, Evaluation, and Economics

### Goal

Turn Stage 6 into a measurable, improvable system.

### Scope

- feedback capture
- outcome tracking
- case-resolution tracking
- regression packs
- cost and latency analytics by scenario
- per-model and per-scenario quality views
- budget policy by layer
- evaluation of behavioral-profile usefulness and clarification quality
- behavioral-profile evaluation remains bounded and non-diagnostic until persistence and evaluation gates are explicit

### Product contracts applied

From `PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`:
- separate budget policy for:
  - Stage 5
  - Stage 6
  - live bot usage
  - eval/A-B
- on-demand preference for expensive outputs

### Exit criteria

- Stage 6 has a closed quality loop
- cost and latency are visible by scenario
- feedback can improve future behavior systematically

### Manual testing

- review loop on real cases
- feedback persistence and replayability
- regression report sanity check

### A/B testing

- regular model and prompt A/B:
  - dossier/state
  - draft/review
  - bot outputs

## Sprint 14: Readiness and Launch Gate

### Goal

Validate that the combined system is operationally safe and product-usable as a single-operator system.

### Scope

- integrated readiness pass
- end-to-end bot/workflow verification
- web operator verification
- quality gate review
- cost and latency review
- silent-regression watchpoint review

### Exit criteria

- Stage 5 remains stable under the new product layer
- Stage 6 outputs are useful and not noisy
- bot and web workflows are usable in daily operation
- major product contracts are implemented and testable
- critical control points are monitored

### Manual testing

- full end-to-end operator walkthrough
- representative daily-use scenarios
- restart/recovery spot checks after productization

### A/B testing

- final targeted validation only where unresolved model/prompt choices remain

## Manual Review Gates

Manual review is required at these checkpoints:

1. After Sprint 4
- prompt lifecycle and validation sanity gate

2. After Sprint 8
- dossier/current_state quality gate

3. After Sprint 9
- style and strategy contract gate

4. After Sprint 11
- case usefulness and noise gate

5. After Sprint 12
- operator workflow gate

6. After Sprint 13
- quality/economics loop gate

7. After Sprint 14
- final readiness gate

## A/B Test Gates

Recommended A/B checkpoints:

1. Sprint 8
- dossier/state quality

2. Sprint 9
- style/strategy prompts if contract changes materially

3. Sprint 12
- bot-facing response quality if operator contract changes

4. Sprint 13
- regular model/prompt regression A/B

## Ownership Boundaries

### Foundation phase

- Host/runtime/control-plane:
  - `src/TgAssistant.Host`
  - compose/config/runtime validation

- Queue and delivery semantics:
  - `src/TgAssistant.Infrastructure/Redis`
  - `src/TgAssistant.Intelligence/Stage5`
  - `src/TgAssistant.Infrastructure/Database`

- DB identity and control state:
  - database migrations and repositories

- Prompt and execution discipline:
  - Stage 5 prompt/validation paths
  - Stage 6 bot/tool/chat execution paths

### Productization phase

- Artifact and case layers:
  - Stage 6 core models and repositories

- Bot operator surface:
  - Stage 6 bot services
  - Telegram bot runtime

- Web operator surface:
  - web read/review/operator paths

- Feedback and eval:
  - eval harness
  - analytics/usage reporting
  - review persistence

## Recommended Execution Order

0. Pre-Sprint 0 Planning and Topology Gate
1. Sprint 1 Control-Plane Safety
2. Sprint 2 Stage 5 Delivery and Queue Semantics
3. Sprint 3 Database Identity and Control-State Integrity
4. Sprint 4 Prompt Lifecycle and Validation Hardening
5. Sprint 5 Stage 6 Execution Discipline
6. Sprint 6 Observability and Recovery Tooling
7. Sprint 7 Stage 6 Artifact Foundation
8. Sprint 8 Dossier and Current-State Quality
9. Sprint 9 Stage 6 Product Contracts
9.5. Pre-Sprint 9.5 Agent Context Refresh and Clarification Design
10. Sprint 10 Case Model
11. Sprint 11 Auto Case Generation
12. Sprint 12 Operator Workflow in Bot and Web
13. Sprint 13 Feedback, Evaluation, and Economics
14. Sprint 14 Readiness and Launch Gate

## Working Rules For Agents

When using this sprint plan for delegated implementation:
- do not mix P0 correctness work with broad product expansion
- keep sprints narrow by failure domain
- require explicit verification evidence at the end of each sprint
- require manual review after quality-affecting sprints
- prefer controlled A/B over intuition when changing prompts or model routing
- treat product contracts as implementation inputs, not optional commentary

## Program Outcome

If completed in order, this sprint plan should produce:
- a safer runtime and data plane
- a more controlled Stage 6 execution model
- persisted Stage 6 artifacts
- a usable case system
- a real operator workflow in bot and web
- measurable quality, cost, and latency
- a single-operator product that is both technically safer and practically usable
