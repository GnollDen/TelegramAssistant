# Implementation Backlog

## Purpose

This backlog is for evolving the existing Telegram assistant into a modular communication intelligence platform with:

- archive analysis
- live Telegram monitoring
- context reconstruction through question/answer
- period-based relationship timeline
- behavioral profiling
- current-state evaluation
- strategy and reply coaching
- hybrid retrieval with graph support

The product should remain universal at the platform level and support the relationship use case as a domain module, not as the entire system identity.

## Product North Star

For a target chat, the system should answer:

1. What happened across the relationship timeline?
2. What is missing from the observed data?
3. What should be clarified through focused questions?
4. What patterns describe both participants?
5. Where is the relationship now?
6. What is the safest and most effective next move?

## Delivery Model

- Use short, shippable sprints.
- Each sprint must end with working code, verification notes, and A/B checks.
- Do not combine core schema changes, major prompt rewrites, and UI work in the same sprint unless strictly necessary.
- Preserve the existing ingestion, archive import, Stage5 extraction, and Stage6 bot foundations.

## Agent Roster

### Agent A1: Platform Architect

Scope:

- schemas
- repositories
- migrations
- service interfaces
- module boundaries

Primary write areas:

- `src/TgAssistant.Core`
- `src/TgAssistant.Infrastructure`
- `src/TgAssistant.Host`

Definition of success:

- new domain entities are introduced cleanly
- code remains modular
- no damage to existing ingestion/runtime flow

### Agent A2: Timeline Engineer

Scope:

- period detection
- transition analysis
- timeline summaries
- evidence linking

Primary write areas:

- `src/TgAssistant.Intelligence`
- `src/TgAssistant.Core`
- `src/TgAssistant.Infrastructure`

Definition of success:

- archive can be segmented into meaningful periods
- each period has evidence-backed summaries and transition notes

### Agent A3: Reconstruction Engineer

Scope:

- context gap detection
- clarification question generation
- answer ingestion
- re-analysis triggers

Primary write areas:

- `src/TgAssistant.Intelligence`
- `src/TgAssistant.Core`
- `src/TgAssistant.Infrastructure`
- `src/TgAssistant.Telegram`

Definition of success:

- system asks targeted questions based on processed evidence
- user answers feed back into state and timeline

### Agent A4: Profile Analyst

Scope:

- behavioral profile synthesis
- confidence and evidence policy
- profile snapshots by period and overall

Primary write areas:

- `src/TgAssistant.Intelligence`
- `src/TgAssistant.Core`
- `src/TgAssistant.Infrastructure`

Definition of success:

- profiles remain descriptive, evidence-backed, and non-diagnostic

### Agent A5: State Engine Engineer

Scope:

- current-state scoring
- relationship snapshots
- risk flags
- confidence logic

Primary write areas:

- `src/TgAssistant.Intelligence`
- `src/TgAssistant.Core`
- `src/TgAssistant.Infrastructure`

Definition of success:

- system can answer "where are we now?" with evidence and uncertainty

### Agent A6: Strategy Engineer

Scope:

- next-step reasoning
- pacing advice
- reply draft generation
- safety rules

Primary write areas:

- `src/TgAssistant.Intelligence`
- `src/TgAssistant.Telegram`
- prompt and policy files

Definition of success:

- system produces usable advice without manipulative escalation

### Agent A7: GraphRAG Engineer

Scope:

- graph schema
- Neo4j sync extensions
- hybrid retrieval assembly
- local-to-global reasoning context

Primary write areas:

- `src/TgAssistant.Intelligence`
- `src/TgAssistant.Infrastructure`
- `scripts`
- graph sync logic

Definition of success:

- graph retrieval improves period, profile, and strategy context quality

### Agent A8: Evaluation Engineer

Scope:

- evaluation dataset
- regression checks
- quality metrics
- acceptance scenarios

Primary write areas:

- `docs`
- `scripts`
- test projects or validation scripts

Definition of success:

- every sprint can be evaluated against repeatable checks

### Agent A9: Operator UX Engineer

Scope:

- Telegram bot commands
- MCP surfaces
- operator workflows
- input flows for offline events and answers

Primary write areas:

- `src/TgAssistant.Telegram`
- `src/TgAssistant.Mcp`
- optionally `src/TgAssistant.Web`

Definition of success:

- operator can use the product without touching DB tables manually

### Agent A10: Security and Privacy Engineer

Scope:

- log redaction
- data lifecycle controls
- export and purge flows
- secret hygiene

Primary write areas:

- `src/TgAssistant.Host`
- `src/TgAssistant.Infrastructure`
- `docs/runbooks`

Definition of success:

- sensitive communication data has reasonable handling and cleanup paths

### Agent A11: Ops Engineer

Scope:

- scheduling
- worker orchestration
- metrics
- dashboards
- requeue and replay runbooks

Primary write areas:

- `src/TgAssistant.Host`
- `deploy`
- `scripts`
- `docs/runbooks`

Definition of success:

- new modules are operable on VPS without guesswork

## Sprint Plan

## Sprint 0: Foundation and Guardrails

Goal:

- establish target architecture, acceptance criteria, and domain boundaries before feature work expands

Tasks:

- audit current project structure and document extension points
- define module boundaries between platform layer and domain layer
- define safety policy for strategy/reply features
- define evidence policy for claims, profiles, and state labels
- create architecture decision records for:
  - universal platform vs relationship domain
  - structured memory layers
  - Q/A reconstruction
  - graph usage policy

Assigned agents:

- A1
- A8
- A10

Deliverables:

- architecture doc
- safety policy doc
- evidence policy doc
- first acceptance checklist

Definition of done:

- no unresolved ambiguity on architecture direction
- sprint 1 implementation can begin without re-litigating fundamentals

## Sprint 1: Domain Schema Expansion

Goal:

- introduce core data structures for relationship analysis without breaking the current product

Tasks:

- add models and migrations for:
  - `offline_interactions`
  - `relationship_periods`
  - `relationship_state_snapshots`
  - `context_gaps`
  - `clarification_questions`
  - `clarification_answers`
  - `behavioral_profiles`
  - `strategy_recommendations`
  - `reply_drafts`
- add repository interfaces and implementations
- wire the new services through dependency injection

Assigned agents:

- A1
- A11

Deliverables:

- migrations
- models
- repositories
- runtime registration

Definition of done:

- app starts cleanly
- DB initializes successfully
- new entities are queryable and writable

## Sprint 2: Offline Events and Operator Input

Goal:

- make offline context first-class input

Tasks:

- add command/API flow for creating offline interaction entries
- support fields:
  - timestamp
  - title
  - summary
  - participants
  - who initiated
  - perceived tone
  - confidence
- store structured answers to clarifications
- add operator flows for reviewing unresolved gaps

Assigned agents:

- A3
- A9

Deliverables:

- Telegram bot command flow
- storage and retrieval for offline events
- initial question/answer command flow

Definition of done:

- operator can add offline events and answers without manual DB edits

## Sprint 3: Timeline and Periodization MVP

Goal:

- build period-based understanding of the relationship

Tasks:

- implement `PeriodBoundaryDetector`
- use message cadence, tone changes, session gaps, invitations, conflicts, and offline events
- generate period labels and period summaries
- store transition reasons and confidence
- expose timeline retrieval for a target chat

Assigned agents:

- A2
- A8

Deliverables:

- periodization service
- period summaries
- transition metadata
- timeline query surface

Definition of done:

- imported archive can be segmented into useful periods with evidence

## Sprint 4: Context Gap Detection and Clarification Questions

Goal:

- identify missing context and ask only the highest-value questions

Tasks:

- detect unexplained cooling, missing offline causes, ambiguous status shifts, unexplained reconnects
- rank gaps by impact on interpretation
- generate evidence-grounded questions
- track status:
  - open
  - answered
  - skipped
  - invalidated
- trigger targeted re-analysis after answers

Assigned agents:

- A3
- A8

Deliverables:

- gap detector
- question generator
- answer application flow

Definition of done:

- system can produce a top-N clarification queue for a chat

## Sprint 5: Current State Engine

Goal:

- determine current relationship state from recent evidence plus historical context

Tasks:

- compute scores for:
  - warmth
  - reciprocity
  - initiative balance
  - ambiguity
  - openness
  - avoidance risk
  - escalation readiness
- map scores to state labels
- persist snapshots
- expose `/state`

Assigned agents:

- A5
- A9

Deliverables:

- state engine
- snapshot storage
- operator query flow

Definition of done:

- system can answer current-state questions with evidence and confidence

## Sprint 6: Behavioral Profiles

Goal:

- synthesize stable behavioral patterns for both participants

Tasks:

- infer patterns for:
  - initiative style
  - conflict style
  - closeness regulation
  - response to uncertainty
  - response to pressure
  - repair behavior
- generate profile summaries by period and overall
- attach evidence lines and confidence
- expose `/profile me` and `/profile other`

Assigned agents:

- A4
- A9

Deliverables:

- profile synthesis service
- profile storage
- operator access commands

Definition of done:

- profiles are grounded, useful, and explicitly non-diagnostic

## Sprint 7: Strategy and Reply Engine

Goal:

- turn analysis into action support

Tasks:

- recommend next-step goals
- recommend pacing
- generate risks and `do_not_do`
- generate 2-3 reply drafts by style
- implement safety gating against pressure/manipulation
- expose `/next-step` and `/draft`

Assigned agents:

- A6
- A9
- A10

Deliverables:

- strategy service
- reply draft service
- policy filters

Definition of done:

- advice is useful, bounded, and explainable

## Sprint 8: Hybrid Retrieval and GraphRAG MVP

Goal:

- improve deep reasoning over long histories

Tasks:

- extend graph model with:
  - periods
  - offline events
  - gaps
  - questions
  - answers
  - profile signals
- add hybrid retrieval pipeline:
  - embeddings
  - graph traversal
  - fact retrieval
  - summary retrieval
- use hybrid context in timeline, profile, and strategy services

Assigned agents:

- A7
- A1
- A11

Deliverables:

- graph sync extensions
- retrieval orchestrator
- fallback logic when graph is unavailable

Definition of done:

- graph layer produces measurable quality gain on selected tasks

## Sprint 9: Evaluation Harness and Regression Control

Goal:

- make quality measurable

Tasks:

- create archive replay scenarios
- define evaluation tasks for:
  - timeline correctness
  - gap quality
  - state quality
  - profile quality
  - strategy safety
  - reply usefulness
- create baseline outputs
- add regression scripts and scorecards

Assigned agents:

- A8
- A11

Deliverables:

- evaluation dataset
- regression scripts
- score templates

Definition of done:

- every future sprint can be compared against a baseline

## Sprint 10: Security, Privacy, and Data Lifecycle

Goal:

- make the product safe enough for long-term personal use

Tasks:

- redact sensitive logs
- define retention and purge flows
- add export functionality for user review
- add explicit marking for user-provided versus inferred data
- document privacy runbooks

Assigned agents:

- A10
- A11

Deliverables:

- security improvements
- cleanup/export scripts
- runbooks

Definition of done:

- operator can control sensitive data lifecycle

## Sprint 11: Operator Experience and Stabilization

Goal:

- make the product efficient to use day to day

Tasks:

- refine bot commands and command responses
- add MCP tools for key workflows
- add backlog inbox for unresolved gaps and stale states
- improve failure visibility
- reduce operator friction

Assigned agents:

- A9
- A11
- A8

Deliverables:

- stable operator workflows
- MCP support
- usability cleanup

Definition of done:

- product is usable without ad hoc scripts for common tasks

## Suggested Sprint Sequence

1. Sprint 0
2. Sprint 1
3. Sprint 2
4. Sprint 3
5. Sprint 4
6. Sprint 5
7. Sprint 6
8. Sprint 7
9. Sprint 8
10. Sprint 9
11. Sprint 10
12. Sprint 11

## Parallelization Guidance

- A1 can overlap with A8 and A10 early.
- A3 and A9 can overlap once schema is stable.
- A2 can start after sprint 1 storage foundations land.
- A4 and A5 should start only after periodization and clarification inputs exist.
- A7 should not start full implementation before the period and Q/A models stabilize.
- A8 should run throughout, not only at the end.

## Codex Execution Rules

- assign one sprint or one sub-epic per Codex run
- require a short implementation plan before edits
- require code changes plus verification, not analysis only
- require preservation of existing ingestion/archive behavior
- require a short migration and rollback note whenever schema changes are made
- require a list of files changed

## Human Review Gates

You should manually review after these sprints:

- Sprint 1
- Sprint 3
- Sprint 4
- Sprint 5
- Sprint 7
- Sprint 8
- Sprint 10

Those are the points where product direction or safety can drift even if the code is technically correct.
