# Sprint 05 Task Pack

## Name

Profiles Engine

## Goal

Implement the first usable profile engine for:

- self
- other
- pair dynamics

This sprint should produce evidence-backed profiles that are:

- useful
- restrained
- period-aware

It should not drift into pseudo-psychological overreach.

## Why This Sprint

The product now has:

- periods
- clarification orchestration
- current state

The next reasoning layer is behavioral profiling.

Profiles are needed for:

- better current-state interpretation
- future strategy personalization
- understanding what works / what fails
- comparing self, other, and pair patterns over time

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_05_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_05_TASK_PACK.md)
4. [SPRINT_05_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_05_ACCEPTANCE.md)

Also inspect:

- current periodization outputs
- current state outputs
- clarification outputs
- profile snapshot schema from Sprint 1

## Scope

In scope:

- profile synthesis services
- self/other/pair profile generation
- trait extraction and persistence
- trait confidence and stability
- sensitive trait handling
- period-sliced profile support
- what-works / what-fails pattern support
- verification paths

Out of scope:

- strategy engine
- draft engine
- bot profile UX polish
- web profile UI
- GraphRAG
- deep psychological modeling

## Product Rules To Respect

Profiles must represent:

- communication style
- closeness and distance behavior
- conflict and repair behavior

Profiles must support:

- global profile
- period slices

The system must support profiles for:

- self
- other
- pair

Traits must include:

- confidence
- stability

Low-stability traits should behave as:

- temporary
- period-specific

Sensitive traits should:

- require stronger evidence
- be surfaced more cautiously

Profiles must be:

- evidence-backed
- non-diagnostic
- useful for later strategy

## Required Deliverables

### 1. Profile Engine Service Layer

Implement dedicated services for:

- profile synthesis
- trait extraction
- trait confidence/stability evaluation
- pair profile synthesis
- pattern extraction for what works / what fails

You may structure this into services such as:

- `ProfileEngine`
- `ProfileTraitExtractor`
- `ProfileConfidenceEvaluator`
- `PairProfileSynthesizer`
- `PatternSynthesisService`

### 2. Self / Other Profiles

Generate profiles for:

- self
- other

Using:

- canonical messages
- periods
- clarifications
- offline events
- state snapshots where relevant

### 3. Pair Profile

Generate pair-dynamic profile capturing at minimum:

- initiative and rhythm
- conflict and repair
- closeness and distance

Use agreed pair traits such as:

- initiative_balance
- contact_rhythm
- repair_capacity
- distance_recovery
- escalation_fit
- ambiguity_tolerance_pair
- pressure_mismatch
- warmth_asymmetry

### 4. Trait Handling

Support:

- fixed trait set
- custom trait path if needed
- confidence
- stability
- sensitive flag

### 5. Period Slices

Support:

- global profile snapshots
- period-specific profile snapshots

And if global and period profile differ:

- preserve both
- do not collapse difference away

### 6. Patterns

Support first useful pattern layer for:

- what tends to work
- what tends to fail

Globally and by period where possible.

### 7. Verification Path

Add verification path such as:

- `--profile-smoke`

That proves:

- self profile generated
- other profile generated
- pair profile generated
- traits persisted
- confidence/stability populated
- at least one low-stability or period-specific trait path exists
- at least one what-works / what-fails pattern is produced

## Verification Required

Codex must verify:

1. build success
2. startup success
3. profile smoke success
4. self profile persisted
5. other profile persisted
6. pair profile persisted
7. trait confidence and stability exist
8. pattern output exists

## Definition of Done

Sprint 5 is complete only if:

1. the product can synthesize usable profiles
2. the profiles are evidence-backed and restrained
3. pair profile exists as a distinct reasoning layer
4. global and period-sliced profiles are both supported
5. outputs are ready for later strategy integration

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how profile synthesis now works
4. what verification was run
5. remaining limitations before Sprint 6
