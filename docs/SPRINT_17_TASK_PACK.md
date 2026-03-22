# Sprint 17 Task Pack

## Name

External Archive Ingestion

## Goal

Integrate external archive ingestion as a real product capability so the system can ingest and reason over non-primary archives in a controlled, provenance-aware way.

This sprint should turn the prepared sidecar foundation into a usable ingestion mode without yet activating the full competing-relationship special case.

## Why This Sprint

The product now has:

- primary archive processing
- graph/network layer
- clarification/timeline/state/profile/strategy/draft/review loop
- outcome layer
- control/eval layer

The next logical extension is controlled ingestion of external archives.

Without it:

- additional contextual archives remain outside the system
- the graph and timeline cannot benefit from structured external evidence

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_17_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_17_TASK_PACK.md)
4. [SPRINT_17_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_17_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)
6. [EXTERNAL_ARCHIVE_INGESTION_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\EXTERNAL_ARCHIVE_INGESTION_POLICY.md)

Also inspect the sidecar foundation prepared earlier for:

- external archive import contract
- provenance/weighting
- linkage planning

## Scope

In scope:

- persistence for external archive ingestion artifacts
- DI/runtime integration for external archive ingestion mode
- controlled ingestion worker or command path
- idempotent ingestion behavior
- provenance/weighting application
- linkage outputs to graph/period/event/clarification context
- verification paths

Out of scope:

- competing relationship runtime interpretation
- silent high-impact state overrides
- GraphRAG

## Product Rules To Respect

External archives are:

- context-rich
- high-value
- not primary by default

Source classes should support:

- supporting_context_archive
- mutual_group_archive
- indirect_mention_archive
- competing_relationship_archive

But in this sprint:

- do not yet activate special high-impact competing-context runtime effects
- keep integration generic and provenance-aware

External archives must:

- preserve separate provenance
- remain distinguishable from focal-pair evidence
- enrich graph/timeline/clarification context in a controlled way

## Required Deliverables

### 1. Persistence Layer

Add persistence for external archive ingestion artifacts, such as:

- import envelope / batch metadata
- import records
- provenance/weighting outputs
- linkage outputs or prepared linkage artifacts

Use additive schema only.

### 2. Runtime Integration

Integrate the prepared external archive services into DI/runtime in a controlled way.

This may be:

- a dedicated ingestion mode
- a worker
- a command path

But it should not replace the current primary import path.

### 3. Idempotent Ingestion

Support:

- repeat-safe ingestion
- payload hash or equivalent duplicate protection
- explicit batch/run identity

### 4. Provenance and Weighting

Apply the provenance/weighting model in the live ingestion path.

Artifacts should preserve:

- source class
- truth layer
- batch/run identity
- base/final weight
- needs_clarification where applicable

### 5. Linkage Outputs

Produce controlled linkage outputs toward:

- graph
- periods
- events
- clarifications

These may be reviewable prepared artifacts rather than auto-applied strong conclusions.

### 6. Verification Path

Add a verification path such as:

- `--external-archive-smoke`

That proves:

- external archive contract validates
- persistence works
- idempotent ingestion works
- provenance/weighting are stored
- linkage artifacts are produced

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. external archive smoke success
4. persistence works
5. idempotent ingestion works
6. linkage artifacts exist

## Definition of Done

Sprint 17 is complete only if:

1. the product can ingest external archives in a controlled way
2. provenance is preserved
3. linkage artifacts are available for later reasoning
4. the system is ready for the later competing-context special sprint

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how external archive ingestion now works
4. what verification was run
5. remaining limitations before Sprint 18
