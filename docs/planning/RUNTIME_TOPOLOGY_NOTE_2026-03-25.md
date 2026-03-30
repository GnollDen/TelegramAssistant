# Runtime Topology Note

> Status note (2026-03-30): supporting topology snapshot from pre-sprint baseline.
> For Stage 6 remediation runtime/rebuild prep, use `S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md`.

## Date

2026-03-25

## Purpose

This note exists to make sprint-start runtime assumptions explicit.

It is not a deep architecture document.
It is a short operational map of what the main runtime roles mean today and what should be treated as actually deployable.

## Current Runtime Areas

### Ingest

Ingest includes:
- Telegram listener/update intake
- realtime message enqueue path
- historical catchup and backfill handoff logic
- archive import related intake paths

Ingest should not be assumed to be the same as Stage 5 processing.

### Stage 5

Stage 5 includes:
- message processing
- extraction
- session slicing and reslicing
- summary generation
- reanalysis paths
- metrics and maintenance paths that keep the substrate healthy

Stage 5 is the substrate-processing layer, not the operator surface.

### Stage 6

Stage 6 includes:
- bot-facing reasoning
- current-state synthesis
- strategy, draft, and review paths
- clarification orchestration
- later artifact and case layers

Stage 6 should be treated as the reasoning/product layer above Stage 5.

### Web

Web should be treated as the operator review and inspection surface.

At sprint start, do not assume every planned web capability is already a fully mature deployable product surface.
Treat web scope as:
- partially real
- partially productization work still in progress

### Ops

Ops includes:
- startup/runtime role selection
- readiness/liveness behavior
- queue and Redis health
- DB connectivity and safety signals
- deployment and restart behavior

Ops is a first-class foundation layer and must not be treated as "later polish".

## Deployable-Now Interpretation

At sprint start, the safest interpretation is:

- Stage 5 substrate is real and operational
- Stage 6 has real implementation, but operator contracts still need normalization
- bot runtime is real, but should be treated as operator-capable rather than fully polished
- web exists, but should not be over-claimed as fully finished product surface
- runtime-role boundaries require hardening before being treated as strongly trustworthy

## Sprint-Start Rule

Planning and execution should assume:
- real foundation exists
- some runtime boundaries are still softer than planning language suggests
- Sprint 1 hardening work is mandatory gate work
- planning docs should not imply stronger runtime isolation than code/runtime currently guarantee
