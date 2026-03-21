# Sprint 04 Task Pack

## Name

Current State Engine

## Goal

Implement the first real current-state engine so the system can answer:

- where the relationship is now
- what the dominant current dynamic is
- what the main risks are
- what the next move should roughly optimize for

This sprint should transform periods and recent interaction evidence into a real state layer.

## Why This Sprint

The timeline is now available.

The next critical layer is state:

- periods explain the past
- current state interprets the present

Without this layer:

- strategy remains weak
- bot `/state` remains shallow
- clarification impact cannot be reflected in a current, actionable interpretation

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_04_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_04_TASK_PACK.md)
4. [SPRINT_04_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_04_ACCEPTANCE.md)
5. [CLARIFICATION_LINK_CONVENTIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CLARIFICATION_LINK_CONVENTIONS.md)
6. [CLARIFICATION_CONTRADICTION_RULES.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CLARIFICATION_CONTRADICTION_RULES.md)

Also inspect:

- current periodization services
- clarification orchestration services
- state snapshot schema from Sprint 1

## Scope

In scope:

- state score computation
- dynamic label mapping
- relationship status mapping
- confidence and ambiguity logic
- alternative status logic
- history-aware weighting
- state snapshot persistence
- state recompute service
- verification paths

Out of scope:

- strategy option engine
- draft engine
- bot `/state` UX polish
- web state screen
- GraphRAG integration
- deep ML calibration

## Product Rules To Respect

State must represent both:

- relationship status
- current dynamics

Use the agreed dimensions:

- initiative
- responsiveness
- openness
- warmth
- reciprocity
- ambiguity
- avoidance risk
- escalation readiness
- external pressure

State should use:

- one primary dynamic label
- one primary relationship status
- optional alternative status when ambiguity is materially high

Mapping should use:

- hybrid logic
- score heuristics
- plus model interpretation where appropriate

Use hysteresis:

- avoid noisy label flipping

When ambiguity is high:

- prefer uncertain interpretation
- not optimistic overreach

## Required Deliverables

### 1. State Engine Service Layer

Implement dedicated services for:

- score computation
- state label mapping
- status mapping
- confidence evaluation
- state persistence

You may structure this into multiple services, for example:

- `CurrentStateEngine`
- `StateScoreCalculator`
- `DynamicLabelMapper`
- `RelationshipStatusMapper`
- `StateConfidenceEvaluator`

### 2. Score Computation

Compute the agreed dimensions from:

- several recent sessions
- current period
- clarifications
- offline events where relevant
- historical patterns when similar enough

Current period should dominate.

History should modulate adaptively.

### 3. Dynamic Label Mapping

Support these dynamic labels:

- `warming`
- `stable`
- `cooling`
- `fragile`
- `uncertain_shift`
- `low_reciprocity`
- `testing_space`
- `reengaging`

Use the agreed priority and ambiguity rules.

### 4. Relationship Status Mapping

Support these relationship statuses:

- `platonic`
- `warm_platonic`
- `ambiguous`
- `reopening`
- `romantic_history_distanced`
- `fragile_contact`
- `detached`

Support one alternative status when ambiguity is high and a competing reading is genuinely plausible.

### 5. Confidence and Conflict Logic

Compute confidence from:

- score coherence
- evidence quality
- conflict level

Also surface:

- conflict between current signals and historical pattern where relevant

### 6. Snapshot Persistence

Persist state snapshots with:

- all score dimensions
- labels
- confidence
- signal refs
- risk refs

### 7. Verification Path

Add a verification path such as:

- `--state-smoke`

That proves:

- score computation
- non-empty state snapshot
- dynamic label selection
- relationship status selection
- ambiguity/confidence behavior
- historical modulation or conflict handling in at least one scenario

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. state smoke success
4. non-empty state snapshot persisted
5. dynamic label produced
6. relationship status produced
7. confidence produced
8. ambiguity or history conflict path demonstrated in at least one scenario

## Definition of Done

Sprint 4 is complete only if:

1. the system can compute current state from real substrate
2. the result includes scores, labels, and confidence
3. ambiguity handling is visible and not silently optimistic
4. state snapshots persist as a usable layer for later strategy work
5. the implementation is ready for later `/state` bot integration

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how state computation now works
4. what verification was run
5. remaining limitations before Sprint 5
