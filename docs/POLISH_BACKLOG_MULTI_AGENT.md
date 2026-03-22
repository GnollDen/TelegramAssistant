# Polish Backlog (Multi-Agent)

## Purpose

This document defines the remaining polish and productization backlog in a form that is safe for parallel multi-agent execution.

The core product loop is already implemented.

What remains is mostly:

- polish
- hardening
- UX refinement
- operational visibility
- experimentation support

## How To Use This

Treat each workstream as:

- a bounded lane
- with a clear write scope
- and low-conflict ownership

Do not run multiple agents on overlapping write scopes unless explicitly planned.

## Priority Order

### P1

- A/B experiment layer
- budget/eval visibility polish
- outcome UI polish

### P2

- richer web review/edit coverage
- reminders / scheduling
- deploy / monitoring hardening

### P3

- docs/archive cleanup
- non-critical UX polish
- deeper operator tooling

## Parallel Workstreams

## Lane A — A/B Experiment Layer

### Goal

Turn the eval harness into a practical experiment workflow for comparing prompts, routing, policies, and guarded behavior.

### Main tasks

- experiment definition model
- variant registration
- run comparison view/model
- scenario pack structure
- experiment result persistence and summaries

### Recommended ownership

- `src/TgAssistant.Intelligence/Stage6/Control/*`
- `src/TgAssistant.Core/Models/*` for experiment/eval-specific models
- `src/TgAssistant.Core/Interfaces/*` for experiment/eval service contracts
- selective `src/TgAssistant.Web/Read/*` if needed for experiment visibility

### Avoid touching

- Stage5 ingestion logic unless experiment explicitly targets it
- bot command runtime
- external archive / competing-context logic

### Dependencies

- depends on accepted Sprint 16

## Lane B — Budget / Eval Visibility Polish

### Goal

Expose budget/eval state cleanly through web and operator surfaces.

### Main tasks

- web pages for ops budget/eval
- compact summaries on dashboard/ops pages
- links to recent eval runs and degraded paths
- visibility for paused paths and quota-block states

### Recommended ownership

- `src/TgAssistant.Web/Read/*`
- small repository read additions only if needed

### Avoid touching

- core guardrail logic
- Stage5 processing behavior

### Dependencies

- depends on accepted Sprint 16

## Lane C — Outcome UI / Trail Polish

### Goal

Make the full outcome chain more inspectable and easier to navigate.

### Main tasks

- dedicated outcome trail page refinements
- stronger links from strategy/draft/history
- clearer rendering of user outcome vs system outcome
- compact learning signal visibility

### Recommended ownership

- `src/TgAssistant.Web/Read/*`
- small read-model additions

### Avoid touching

- core outcome recording logic unless fixing a small read-blocking issue

### Dependencies

- depends on accepted Sprint 15

## Lane D — Web Review/Edit Coverage Expansion

### Goal

Expand the MVP web review/edit layer beyond the first edit path.

### Main tasks

- add more real edit paths
- improve review cards for high-impact objects
- broaden impact preview coverage
- better operator steering for profile/strategy/transition objects

### Recommended ownership

- `src/TgAssistant.Web/Read/*`
- related review/action services

### Avoid touching

- full timeline revision architecture
- search/saved-views core

### Dependencies

- depends on accepted Sprint 11

## Lane E — Reminders / Scheduling

### Goal

Add practical scheduled nudges and reminders on top of existing bot and operational layers.

### Main tasks

- reminder model
- trigger evaluation
- scheduled delivery
- web/bot visibility of pending reminders

### Recommended ownership

- bot/runtime scheduling code
- reminder-specific service layer

### Avoid touching

- core reasoning engines

### Dependencies

- depends on accepted bot and ops layers

## Lane F — Deploy / Monitoring Hardening

### Goal

Improve production-readiness without changing core product behavior.

### Main tasks

- monitoring polish
- health/readiness refinement
- backup/restore clarity
- budget/eval operator visibility integration
- deployment cleanup

### Recommended ownership

- `deploy/*`
- `docker-compose.yml`
- monitoring config
- runbooks

### Avoid touching

- reasoning engines unless needed for observability hooks

### Dependencies

- none beyond accepted control layer

## Lane G — Docs / Archive Cleanup

### Goal

Keep documentation navigable and reduce stale-reference risk.

### Main tasks

- archive old sprint docs if desired
- mark narrow-scope docs more clearly
- maintain docs index
- keep SoT and historical docs distinct

### Recommended ownership

- `docs/*`

### Avoid touching

- runtime code

### Dependencies

- none

## Safe Parallel Bundles

### Bundle 1

- Lane A
- Lane C
- Lane F

### Bundle 2

- Lane B
- Lane D
- Lane G

### Bundle 3

- Lane E
- Lane F
- Lane G

## Recommended Next Bundle

If the goal is fastest useful polish with low conflict, run:

1. Lane A — A/B Experiment Layer
2. Lane B — Budget / Eval Visibility Polish
3. Lane C — Outcome UI / Trail Polish

## Agent Prompt Pattern

For each lane:

1. state the lane name
2. state the owned write scope
3. list forbidden shared files
4. require final report with:
   - what changed
   - files changed
   - verification
   - residual limitations

## Rule

When in doubt:

- keep work additive
- preserve auditability
- avoid shared host/runtime rewiring unless the lane explicitly owns it
