# Sprint 03 Task Pack

## Name

Periodization MVP

## Goal

Implement the first real periodization layer so the system can turn raw interaction history into:

- periods
- transitions
- unresolved transitions
- evidence-backed period summaries

This sprint should make the timeline usable as a product layer, not just as stored empty period objects.

## Why This Sprint

The product depends on periods as the main narrative unit.

Without periodization, the system cannot reliably support:

- relationship timeline review
- clarification targeting by phase
- state interpretation with historical context
- period-level lessons
- downstream strategy reasoning

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_03_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_03_TASK_PACK.md)
4. [SPRINT_03_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_03_ACCEPTANCE.md)
5. [CLARIFICATION_LINK_CONVENTIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CLARIFICATION_LINK_CONVENTIONS.md)
6. [CLARIFICATION_CONTRADICTION_RULES.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CLARIFICATION_CONTRADICTION_RULES.md)

Also inspect:

- existing chat session model/repositories
- message repository/query patterns
- clarification orchestration services
- period-related repositories from Sprint 1

## Scope

In scope:

- period boundary detection MVP
- period assembly from existing runtime/canonical data
- transition object creation
- unresolved transition creation
- period summary generation
- evidence pack assembly
- merge/split proposal support
- period review priority calculation
- minimal verification paths

Out of scope:

- full current-state engine
- strategy engine
- web timeline UI
- bot timeline UX polish
- GraphRAG integration
- deep semantic/LLM-heavy period summarization optimization

## Product Rules To Respect

- Periods are top-level timeline units.
- Events live inside periods.
- Period boundaries come from a combination of:
  - pauses
  - key events
  - dynamic shifts
- Event and dynamic shifts matter more than pauses alone.
- Long periods may contain substages.
- Periods must support:
  - label
  - dates
  - summary
  - key signals
  - what helped
  - what hurt
  - open questions
  - boundary confidence
  - interpretation confidence
  - third-party influence
  - alternative interpretations
  - evidence pack
  - review priority
  - sensitive flag
  - lessons
  - strategic patterns
- Unknown transition causes should create gaps, not silent assumptions.
- Merge/split should be proposed, not auto-applied.

## Required Deliverables

### 1. Periodization Service Layer

Implement dedicated services for:

- boundary detection
- period assembly
- transition construction
- period evidence assembly
- merge/split proposal logic

You may split responsibilities into multiple services, for example:

- `PeriodBoundaryDetector`
- `TimelineAssembler`
- `TransitionBuilder`
- `PeriodEvidenceAssembler`
- `PeriodProposalService`

### 2. Boundary Detection MVP

Use existing canonical data to detect candidate boundaries from:

- message/session gaps
- significant interaction dynamic changes
- important events
- clarification outcomes where relevant

This should be heuristic and explainable, not over-optimized.

### 3. Period Assembly

Build actual periods from boundary candidates.

At minimum, each assembled period must populate:

- dates
- label
- summary
- key signals
- what helped
- what hurt
- open question count or references
- confidences

### 4. Transition Handling

Create transition objects between periods.

If the cause of transition is unclear:

- create unresolved transition state
- and/or clarification gap linkage

### 5. Evidence Pack Assembly

Each period should have evidence support built from:

- canonical messages
- relevant sessions
- clarification answers
- offline events
- important snippets where available

The evidence pack can be compact, but must be real.

### 6. Merge/Split Proposals

Implement proposal logic for:

- likely merge candidates
- likely split candidates

Do not auto-apply.

### 7. Review Priority

Compute review priority from a combination of:

- low confidence
- high impact on current situation
- conflict in data

## Verification Required

Codex must verify:

1. build success
2. startup success
3. periodization run over at least one real or seeded case
4. creation of multiple periods
5. creation of transitions
6. unresolved transition behavior for at least one ambiguous case
7. evidence pack population
8. at least one merge/split proposal path

## Definition of Done

Sprint 3 is complete only if:

1. raw history can be assembled into periods
2. transitions exist between periods
3. unresolved transitions are represented explicitly
4. each period has meaningful summary/evidence fields
5. merge/split proposals exist as reviewable outputs
6. the result is usable as the historical substrate for current-state logic

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how periodization now works
4. what verification was run
5. remaining limitations before Sprint 4
