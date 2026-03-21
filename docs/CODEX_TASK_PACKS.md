# Codex Task Packs

## Purpose

This file translates the product definition into execution-ready task packs for Codex runs on VPS.

Each pack is designed to be:

- concrete
- bounded
- reviewable
- independently shippable

## Global Instructions For Every Codex Run

Use these instructions with every task pack:

- inspect current code before changing architecture
- preserve existing archive import and live ingestion behavior unless task explicitly changes it
- keep migrations reversible where practical
- do not collapse facts, hypotheses, and user overrides into one layer
- keep provenance, review, and history intact
- prefer incremental implementation over sweeping rewrites
- after changes, run the relevant build/tests/checks and summarize results
- report files changed
- report open risks and follow-up items

## Standard Output Contract For Codex

For each task, require Codex to return:

1. what was changed
2. files changed
3. what was verified
4. known limitations
5. suggested next task

## Sprint 0 Pack: Architecture Freeze

### Goal

Freeze extension architecture so future implementation does not drift.

### Scope

- docs
- architecture notes
- codebase inspection

### Tasks

- inspect current project structure and integration points
- document platform/domain/operator separation
- map current modules to future modules
- identify what stays in Stage5/Stage6 and what moves into new domain layers
- document data truth layers and review model
- document risks of mixing analysis and action logic

### Deliverables

- architecture document
- updated backlog references if needed
- list of candidate new projects/namespaces

### DoD

- architecture decisions are explicit
- future coding tasks can point at exact boundaries

### Suggested Verification

- no code changes required unless clarifying docs in repo

## Sprint 1 Pack: Schema Foundations

### Goal

Add the core domain schema for periods, clarifications, profiles, strategy, outcomes, and review.

### Scope

- `src/TgAssistant.Core`
- `src/TgAssistant.Infrastructure`
- migrations
- DI registration

### Tasks

- add typed models and storage for:
  - periods
  - transitions
  - hypotheses
  - clarification questions
  - clarification answers
  - offline events
  - audio assets, segments, snippets
  - state snapshots
  - profile snapshots and traits
  - strategy records and options
  - draft records and outcomes
  - inbox items
  - conflict records
  - activity log extensions
  - dependency links
- add repositories/interfaces
- add migration scripts
- register new services in host

### Deliverables

- compilable schema layer
- migrations
- repository interfaces and implementations

### DoD

- app starts
- migrations apply
- new records can be written and read

### Suggested Verification

- build solution
- run migration init path
- smoke-test repository creation paths

## Sprint 2 Pack: Clarification Infrastructure

### Goal

Implement question queue, dependency graph, answer application, and recompute triggers.

### Scope

- intelligence/domain services
- repositories
- bot flow support

### Tasks

- implement clarification queue model
- implement priority levels: blocking / important / optional
- implement question dependency resolution
- implement dedup/parent-child logic
- implement answer typing and source class handling
- implement local plus downstream recompute triggers
- implement conflict creation on contradictory answers

### Deliverables

- clarification orchestration services
- answer application path
- recompute targeting logic

### DoD

- questions can be queued, answered, reprioritized, and resolved
- answering one question can suppress or downgrade dependent questions

### Suggested Verification

- unit tests around dependency closure
- smoke test with seeded questions and answers

## Sprint 3 Pack: Periodization MVP

### Goal

Create first working period timeline with transitions, evidence, uncertainty, and review hooks.

### Scope

- period services
- timeline storage
- evidence pack assembly

### Tasks

- implement period boundary detector
- incorporate pauses, events, dynamic shifts
- create transitions
- add unresolved transition handling
- generate period summaries
- populate key fields:
  - what helped
  - what hurt
  - open questions
  - confidence fields
  - evidence pack
- support merge/split proposals, not auto-apply

### Deliverables

- timeline builder
- transition builder
- evidence pack builder

### DoD

- a seeded archive can produce meaningful periods and transitions

### Suggested Verification

- replay one sample archive
- inspect produced periods manually

## Sprint 4 Pack: Current State Engine

### Goal

Compute current state from recent sessions plus period context.

### Scope

- state engine
- snapshot persistence
- label mapping

### Tasks

- compute score dimensions:
  - initiative
  - responsiveness
  - openness
  - warmth
  - reciprocity
  - ambiguity
  - avoidance risk
  - escalation readiness
  - external pressure
- implement hybrid score-to-label mapping
- implement confidence and hysteresis
- support alternative status when ambiguity is high
- persist snapshots and diffs

### Deliverables

- state engine service
- label mapping service
- snapshot storage

### DoD

- current state can be computed and stored from real data

### Suggested Verification

- state scenario tests
- compare several seeded cases

## Sprint 5 Pack: Profile Engine

### Goal

Create usable self/other/pair profiles with trait evidence and period slices.

### Scope

- profile synthesis
- trait generation
- history persistence

### Tasks

- build profile synthesis for:
  - self
  - other
  - pair
- implement fixed trait sets and custom trait path
- generate top relevant traits and evidence
- support trait confidence and stability
- support sensitive trait handling
- persist global plus period-sliced profiles

### Deliverables

- profile engine
- profile storage
- comparison-ready outputs

### DoD

- profiles are generated with evidence and can be reviewed

### Suggested Verification

- profile generation tests
- manual inspection of sample outputs

## Sprint 6 Pack: Offline Audio Pipeline

### Goal

Ship a real but bounded audio ingestion flow.

### Scope

- audio ingestion
- transcript flow
- snippets
- user-summary integration

### Tasks

- accept audio upload for offline events
- create transcript records
- create segment records
- support uncertain speaker marking
- generate auto-summary
- store user summary
- create key moments and candidate snippets
- push unresolved spots into clarification pipeline
- support manual rebind to another period

### Deliverables

- working offline audio ingestion path
- transcript and snippet generation
- clarification handoff

### DoD

- one real audio flow works end-to-end

### Suggested Verification

- process sample audio
- inspect transcript, snippets, clarifications, and period impact

## Sprint 7 Pack: Bot Core Surface

### Goal

Implement the first operational bot surface.

### Scope

- Telegram bot handlers
- flow session memory
- command formatting

### Tasks

- implement:
  - `/state`
  - `/draft`
  - `/review`
  - `/offline`
  - `/gaps`
  - `/answer`
  - `/timeline`
- enforce concise formatting rules
- support guided one-by-one clarification
- support optional context for `/draft`
- add help/menu
- add short-lived flow session state

### Deliverables

- usable bot for daily operations

### DoD

- bot handles core flows end-to-end

### Suggested Verification

- command-by-command manual smoke run

## Sprint 8 Pack: Web Review Console MVP

### Goal

Ship the first usable web console.

### Scope

- web UI shell
- screen routing
- read/review/edit flows

### Tasks

- implement screens:
  - dashboard
  - dossier
  - timeline
  - clarifications
  - current state
  - profiles
  - offline events
  - inbox
- implement shared review card pattern
- implement basic edit mode with reason
- implement cross-links
- implement current-wave clarification UI

### Deliverables

- usable web review console

### DoD

- operator can inspect and review the core model in browser

### Suggested Verification

- end-to-end manual walkthrough across screens

## Sprint 9 Pack: Strategy and Draft Engine

### Goal

Make action support genuinely useful.

### Scope

- strategy engine
- draft generation
- review before send

### Tasks

- implement strategy option generation from:
  - current state
  - current period
  - profile/pair patterns
- include risk, conditions, success/failure signals
- implement micro-step support
- implement draft generation with:
  - one main option
  - two alternatives
- implement draft review:
  - risks
  - safer rewrite
  - more natural rewrite
- incorporate style fit without overriding safety

### Deliverables

- operational strategy service
- useful draft assistant

### DoD

- bot can provide strategy and drafts grounded in current model state

### Suggested Verification

- manual scenario comparisons
- draft feedback on sample cases

## Sprint 10 Pack: Social Graph MVP

### Goal

Add usable social graph support without overcomplication.

### Scope

- node creation/review
- influence edges
- information flow
- merge suggestions

### Tasks

- create people/group/place/company nodes
- add role and influence model
- add information flow edges
- add trust/reliability fields
- add new-node review path
- add merge suggestion review path
- expose graph to period and profile contexts

### Deliverables

- reviewable graph layer

### DoD

- graph exists and can influence reasoning without becoming mandatory everywhere

### Suggested Verification

- inspect seeded graph cases
- verify node review and merge proposal flows

## Sprint 11 Pack: Evaluation Harness

### Goal

Make product quality measurable.

### Scope

- scripts
- fixtures
- evaluation docs

### Tasks

- create golden cases
- create counterexample set
- add smoke suite
- add focused suites
- store eval run artifacts
- support side-by-side output diffs
- define thresholds for ship/hold

### Deliverables

- usable eval harness

### DoD

- every important sprint can be judged against baselines

### Suggested Verification

- run smoke plus one focused suite successfully

## Sprint 12 Pack: Privacy and Ops Hardening

### Goal

Make the system safe enough for long-running personal use.

### Scope

- redaction
- retention
- export/purge
- backups
- alerts

### Tasks

- implement retention config
- implement log redaction
- implement export by layer
- implement purge by case and by type
- ensure encrypted backups
- add privacy incident runbook
- add domain alerts and bot notifications

### Deliverables

- privacy baseline
- operable long-term deployment

### DoD

- sensitive data handling is no longer ad hoc

### Suggested Verification

- run export
- run purge on test data
- verify backups and redaction behavior

## Parallelization Guidance

- Sprint 1 should be done before most others.
- Sprint 2 and Sprint 3 can overlap partially after schema lands.
- Sprint 4 and Sprint 5 can proceed after enough period/state structure exists.
- Sprint 7 and Sprint 8 can begin once backend contracts are stable.
- Sprint 9 should follow state/profile foundations.
- Sprint 10 should not block early usable release.
- Sprint 11 should start as soon as first outputs stabilize, not only at the end.
- Sprint 12 can progress partly in parallel with later product sprints.

## Recommended Immediate Order

If running Codex iteratively on VPS, start with:

1. Sprint 1 Pack
2. Sprint 2 Pack
3. Sprint 3 Pack
4. Sprint 4 Pack
5. Sprint 7 Pack
6. Sprint 8 Pack

That gives the first serious vertical slice fastest.
