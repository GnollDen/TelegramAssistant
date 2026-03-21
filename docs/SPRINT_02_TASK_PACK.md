# Sprint 02 Task Pack

## Name

Clarification Orchestration Layer

## Goal

Implement the first real clarification orchestration layer on top of the accepted foundation model.

This sprint should make the system capable of:

- holding a prioritized clarification queue
- expressing dependencies between questions
- applying answers
- creating conflicts when answers contradict existing knowledge
- triggering targeted recompute for affected layers

This sprint is not yet about full question generation quality or full strategy logic.

## Why This Sprint

The product depends on adaptive clarification to reconstruct missing context from archive and offline reality.

Without clarification orchestration, later layers will be weaker:

- timeline quality
- current state quality
- strategy quality
- profile quality

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_01_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_01_TASK_PACK.md)
4. [SPRINT_01_1_REPAIR_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_01_1_REPAIR_TASK_PACK.md)
5. [SPRINT_02_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_02_TASK_PACK.md)
6. [AGENT_ROLES.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\AGENT_ROLES.md)

Also inspect current code from Sprint 1 / 1.1:

- new clarification repositories
- dependency link repository
- conflict repository
- review event support
- host wiring

## Scope

In scope:

- question priority model
- dependency graph logic
- dedup / parent-child suppression
- answer apply flow
- contradiction handling
- targeted recompute planning
- minimal service layer for clarification workflows
- verification paths

Out of scope:

- LLM-based question generation quality tuning
- period boundary detection
- state score engine
- strategy engine
- web UI
- bot UX polish
- GraphRAG rollout

## Product Rules To Respect

- Ask only materially useful questions.
- Support priority levels:
  - blocking
  - important
  - optional
- Support question dependencies.
- Support wave-based clarification logic.
- Answers may become:
  - user-confirmed truth
  - user-reported context
  - conflict records when contradictory
- Recompute should be:
  - local
  - plus affected dependent layers

## Required Deliverables

### 1. Clarification Service Layer

Create services that orchestrate:

- queue creation and ordering
- dependency-aware visibility
- answer application
- conflict handling
- recompute target planning

This should sit above repositories.

### 2. Question Priority Logic

Implement priority handling for:

- blocking
- important
- optional

Priority should be influenced by:

- impact on timeline
- impact on current state
- impact on strategy

### 3. Dependency Handling

Implement logic so that:

- questions can depend on other questions
- a resolved parent can downgrade or resolve children
- duplicate questions can be collapsed under a parent

### 4. Answer Apply Flow

Implement service-level answer application that:

- stores the answer
- classifies the source class
- updates question status
- records audit/review events
- creates or updates conflicts where required
- computes recompute targets

### 5. Conflict Creation

When a new clarification answer contradicts:

- previous answers
- strong current interpretation
- or key stateful assumptions

create or update domain conflict records.

### 6. Recompute Planning

Do not implement all downstream recompute engines yet.

But do implement:

- a recompute target planner
- dependency resolution for affected objects/layers

Minimum expected affected outputs:

- periods
- state snapshots
- profiles
- strategy artifacts

### 7. Minimal Seed/Test Path

Add the smallest reasonable way to verify:

- create queue of questions
- answer one question
- auto-resolve/downgrade dependent question(s)
- create conflict on contradictory answer
- compute recompute targets

## Implementation Notes

Preferred shape:

- repositories stay thin
- orchestration lives in dedicated services
- no bot/web coupling yet

If needed, add one or more services such as:

- `ClarificationOrchestrator`
- `ClarificationAnswerApplier`
- `ClarificationDependencyResolver`
- `RecomputeTargetPlanner`

Names may differ, but roles should be explicit.

## Verification Required

Codex must verify:

1. build success
2. app startup success
3. create/read/update flow for clarification records
4. dependency downgrade/resolve scenario
5. contradiction -> conflict scenario
6. recompute target planning scenario

## Definition of Done

Sprint 2 is complete only if:

1. clarification questions can be prioritized
2. dependencies can suppress or resolve other questions
3. answers can be applied through service logic, not raw repository calls only
4. contradictions create reviewable conflict records
5. recompute targets are planned for affected layers
6. the implementation is clearly ready for bot/web integration in later sprints

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how clarification orchestration now works
4. what verification was run
5. remaining limitations before Sprint 3
