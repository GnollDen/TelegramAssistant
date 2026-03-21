# Sprint 01.1 Repair Task Pack

## Name

Foundation Repair: Canonical Layer Alignment

## Goal

Repair Sprint 1 so the new foundation does not become a parallel truth system.

The key principle for this sprint:

- reuse the existing canonical operational layer wherever it already models the concept well
- add only truly new domain objects
- remove or refactor unnecessary parallel domain entities/tables if they duplicate existing meaning

## Why This Sprint Exists

Sprint 1 introduced a broad `domain_*` layer.

Review found a critical architectural risk:

- legacy operational layer and new domain layer can drift into two unsynchronized truth systems

This sprint fixes that before clarification, timeline, and state logic build on top of it.

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [SPRINT_01_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_01_TASK_PACK.md)
3. [SPRINT_01_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_01_ACCEPTANCE.md)
4. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
5. [AGENT_ROLES.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\AGENT_ROLES.md)

Also inspect:

- current Sprint 1 changes
- current review findings
- existing legacy canonical tables/models/repos

## Canonical Layer Decision

Treat these existing objects as canonical where possible:

- messages
- entities
- relationships
- facts
- communication events
- chat sessions
- existing archive/runtime message substrate

Add only genuinely new domain objects as separate persistence:

- periods
- transitions
- clarification questions
- clarification answers
- state snapshots
- profile snapshots
- profile traits
- strategy records
- strategy options
- draft records
- draft outcomes
- inbox items
- conflict records
- dependency links
- offline events and audio-specific objects if no existing canonical equivalent fits

## Main Repair Objectives

### 1. Eliminate Parallel Truth Risk

Review every new Sprint 1 object and decide:

- keep as new first-class domain object
- re-link to existing canonical object
- remove or collapse if it duplicates existing meaning

Do not keep a new domain entity just because it was added in Sprint 1.

### 2. Define Explicit Canonical Referencing

New domain objects must reference existing canonical objects directly where relevant.

Examples:

- periods should reference canonical chat/case context
- clarifications should reference periods and affected canonical/domain outputs
- state should reference canonical evidence
- profiles should reference canonical evidence
- strategy/drafts should reference canonical sessions/messages where relevant

### 3. Add Missing Update Paths

Add update methods needed for Sprint 2, at minimum for:

- clarification question status/priority/resolve
- clarification answer application support
- inbox item status
- conflict status
- period lifecycle updates
- hypothesis lifecycle updates

### 4. Add Minimum Review/Event Trail

Implement the minimum viable review/audit layer for new domain objects:

- actor where relevant
- review event/history records
- reason/comment fields where required

Do not overbuild, but do not leave new domain entities without auditability.

### 5. Close Acceptance Gap

Close the Sprint 1 runtime/acceptance hole by proving more than just `--foundation-smoke`.

At minimum:

- app startup path still works
- existing runtime registration is not broken
- migration state is coherent

## In Scope

- schema cleanup/refactor from Sprint 1
- repository contract fixes
- audit/review event minimum
- canonical linking
- acceptance verification improvements

## Out of Scope

- clarification generation
- period boundary detection
- score-to-label engine
- bot UI expansion
- web UI
- GraphRAG rollout

## Specific Tasks

### Task 1. Canonical Mapping Audit

Audit every Sprint 1-added object family and classify it:

- canonical existing layer
- valid new domain layer
- invalid duplication

Document that mapping in code comments or repo-local docs if needed.

### Task 2. Schema Refactor

Refactor/remove any new objects that duplicate existing canonical concepts unnecessarily.

Expected default:

- do not create duplicate domain versions of facts/entities/relationships/messages if existing canonical versions already serve that role

### Task 3. Repository Contract Repair

Extend repository interfaces and implementations with necessary update paths for Sprint 2.

### Task 4. Audit Trail Minimum

Ensure new domain objects have enough provenance/review history support to be safely reviewed and evolved.

### Task 5. Runtime Acceptance Repair

Add verification proving that the app startup path is not only passing the foundation smoke shortcut.

### Task 6. Sprint 2 Readiness

Leave the code in a state where Sprint 2 clarification orchestration can start without further persistence redesign.

## Deliverables

By the end of Sprint 1.1:

- canonical-vs-new layer split is explicit and defensible
- unnecessary duplicated domain objects are removed or repurposed
- new domain objects reference canonical existing objects properly
- update paths exist for Sprint 2 lifecycle needs
- review/audit minimum exists
- runtime acceptance is stronger than foundation-smoke only

## Definition of Done

Sprint 1.1 is complete only if:

1. there is no obvious “second unsynchronized truth system” risk
2. existing canonical layer is reused wherever it should be
3. required update methods exist for Sprint 2
4. minimum audit/review trail exists for new domain objects
5. startup/runtime acceptance gap is reduced with real verification
6. the repo is ready for Sprint 2 without another schema rethink

## Verification Required

Codex must verify and report:

- build result
- migration state
- app startup behavior
- updated create/read/update checks for critical new domain objects
- rationale for what remains new-domain and what is canonical-existing

## Final Report Required

Final report must include:

1. which Sprint 1 objects stayed as new first-class entities
2. which objects were changed to rely on existing canonical layer
3. which duplications were removed or avoided
4. files changed
5. verification performed
6. remaining risks before Sprint 2
