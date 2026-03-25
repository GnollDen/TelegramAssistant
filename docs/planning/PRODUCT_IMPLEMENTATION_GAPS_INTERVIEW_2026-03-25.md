# Product Implementation Gaps Interview

## Date

2026-03-25

## Purpose

This document captures the remaining product and operator decisions needed to turn the existing backlogs into an implementation-ready program.

It complements:
- `docs/planning/archive/FULL_REVIEW_BACKLOG_2026-03-25.md`
- `docs/planning/archive/PROJECT_REVIEW_BACKLOG_2026-03-25.md`
- `docs/planning/archive/WORKING_SPRINTS_2026-03-25.md`
- `docs/planning/STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md`

The goal is not to restate technical remediation work.
The goal is to close the missing product contracts that would otherwise force agents to improvise during implementation.

## Current Conclusion

The project already has:
- a strong Stage 5 substrate
- a Stage 6 implementation direction
- execution backlogs for control-plane, data correctness, and AI execution discipline

What was still missing before implementation:
- product boundary decisions
- artifact and case semantics
- operator workflow rules
- reasoning contract decisions
- acceptance framing

This document records a first working set of those decisions.

## Interview-Derived Working Decisions

## A. Product Boundary

### A.1 What counts as a working Stage 6

Stage 6 should be considered working when the operator can use it daily through bot and web and reliably get:
- useful `current_state`
- useful `dossier`
- working `draft` and `review`
- live `clarification` loop
- a small but useful `case queue`

Working does not mean:
- every possible advanced mode exists
- full autonomous operation exists
- all later-case automation is already implemented

### A.2 First practical release scope

The first practical release should include:
- `dossier`
- `current_state`
- `draft`
- `review`
- `clarification`
- `timeline`
- minimal `case queue`

The first practical release should not require:
- advanced strategy graphing
- rich automation everywhere
- broad archive-wide materialization
- polished public-product UX
- graph-first reasoning as a hard dependency

## B. Artifact Model

### B.1 Persisted artifacts required

The following artifacts should be persisted:
- `dossier`
- `current_state`
- `strategy`
- `draft`
- `review`
- `clarification_state`

Intermediate reasoning traces do not need first-wave persistence by default.

### B.2 Refresh model

Automatic refresh should cover:
- `current_state`
- `clarification_state`
- freshness markers for case generation

Mostly on-demand refresh should cover:
- `dossier`
- `strategy`
- `draft`
- `review`

### B.3 Stale rules

An artifact should become stale when:
- new messages arrive after generation
- a clarification gap is resolved
- an offline event is added
- manual user input changes interpretation
- time-based freshness threshold is exceeded

## C. Case System

### C.1 Required first-wave case types

First-wave case types:
- `needs_input`
- `needs_review`
- `risk`
- `state_refresh_needed`
- `dossier_candidate`
- `draft_candidate`

Later optional types:
- `strategy_refresh_needed`
- `conflict_case`
- `timeline_rebuild_needed`

### C.2 Required case statuses

Required statuses:
- `new`
- `ready`
- `needs_user_input`
- `resolved`
- `rejected`
- `stale`

`accepted` is optional and should only be added if it creates meaningful operator value.

### C.3 Automatic case creation rules

Cases should be created automatically when:
- uncertainty is high
- a strong risk signal appears
- a blocking clarification gap appears
- `current_state` becomes stale
- new evidence materially changes dossier-worthy understanding

Cases should not be created automatically for:
- weak/noisy signals
- minor stylistic suggestions
- every small message update
- outputs without a clear action path

### C.4 Noise definition

Case noise includes:
- no clear action
- no priority
- no evidence basis
- repeated duplicates after nearby events
- "interesting" but not actionable surfacing

## D. Operator Workflow

### D.1 Bot vs web split

Bot should handle:
- `/state`
- `/draft`
- `/review`
- `/gaps`
- `/answer`
- `/timeline`
- urgent items
- quick decisions
- primary fast clarification intake
- short user-supplied context answers

Web should handle:
- dossier
- expanded timeline
- case queue
- artifact history
- deep review
- richer operator controls
- expanded clarification review
- long-form user context, corrections, and evidence inspection

### D.2 Expected daily operator flow

Expected daily flow:
1. check urgent and ready items
2. inspect current state
3. answer top clarification gaps
4. run draft or review when needed
5. move to web only for deep review, dossier, and timeline work

### D.3 Case queue requirement

An explicit case queue is required.

Without it, Stage 6 remains a set of separate commands instead of a usable operating layer.

### D.4 Notification policy

Initial notification policy should be:
- pull-first
- narrow push only for high-signal items

The bot should not begin as a noisy broad notifier.

## E. Reasoning Contract

### E.1 Fact vs interpretation

Important Stage 6 outputs should separate:
- observed facts
- likely interpretations
- uncertainties or alternative readings
- missing information

### E.2 Signal strength model

The first practical signal-strength scale should be:
- `strong`
- `medium`
- `weak`
- `contradictory`

This is especially important for:
- `current_state`
- `dossier`
- `risk` cases

### E.3 Relational pattern output

Relational patterns should become a first-class output:
- participant patterns
- pair dynamics
- repeated interaction modes
- changes over time

### E.4 Strategic optimization target

Strategy should optimize for:
- clarity
- dignity
- non-manipulation
- less anxious overreaching
- emotionally clean decisions

Strategy should not optimize for:
- contact at any cost
- manipulative gain
- dependent retention
- "winning" at the cost of long-term clarity

## F. Personal Style Contract

### F.1 Working definition of "preserve my style"

The system should avoid:
- preachy or heavy text
- service-tone politeness drift
- anxious over-explaining
- emotional overfilling

The system should preserve:
- depth
- clarity
- strength
- warmth when appropriate
- directness without unnecessary coldness

### F.2 Draft output shape

Default draft shape should be:
- one main version
- one softer alternative
- one more direct alternative

Large draft sets are not needed in the first wave.

### F.3 Style profile persistence

A lightweight persisted personal style profile is desirable.

First-wave scope should include:
- anti-patterns
- preferred tone markers
- preferred density
- warmth/directness calibration

## G. Acceptance and Quality

### G.1 Acceptance criteria for "Stage 6 works"

Stage 6 should be considered meaningfully working when:
- `dossier` is useful instead of being a raw dump
- `current_state` is stable and plausible
- `draft` is sendable without embarrassment
- `review` materially improves text
- `clarification` reduces uncertainty
- `case queue` stays useful and not noisy

### G.2 Manual review gates

Manual review should be mandatory for:
- `dossier`
- `current_state`
- `strategy`
- case-generation thresholds

Manual review is useful but not always mandatory for:
- `draft`
- `review`

### G.3 A/B priorities

Regular A/B should focus on:
- model routing
- dossier/state prompt variants
- post-tool synthesis quality
- bot output style when the contract changes

## H. Economics and Operations

### H.1 Safe optimization targets

Cost can be reduced without major value loss by:
- refreshing dossier less often
- avoiding eager strategy materialization
- suppressing low-priority automatic cases
- limiting heavy historical refresh

### H.2 On-demand only outputs

Prefer on-demand execution for:
- dossier refresh
- strategy generation
- draft generation
- review generation
- deep timeline explanation

### H.3 Budget policy

Separate budget policy should exist for:
- Stage 5 processing
- Stage 6 reasoning
- live bot usage
- eval and A/B runs

## Backlog Additions

## P1 Product Contracts

Add to working backlog:
- Fact vs Interpretation contract
- Signal-strength model
- Relational pattern output contract
- Ethical strategy contract
- Personal style calibration contract
- Behavioral profile contract
- User-supplied context source contract

## P1 Interactive Clarification Layer

Add to working backlog:
- system-detected missing-context cases
- structured clarification prompts grounded in messages, dates, people, and missing evidence
- explicit separation between:
  - observed evidence
  - user-reported context
  - system inference
- case closure/reopen rules after user answer
- timeline/state/dossier refresh after clarification input

## P2 Behavioral Context Expansion

Add to working backlog:
- behavioral profile artifact
- use of user-supplied context in behavioral reasoning
- contradiction handling between user report and message evidence
- confidence rules for subjective inputs
- strategy implications based on repeated behavioral patterns

## P1 Operator Workflow

Add to working backlog:
- explicit case queue
- bot/web surface split
- urgent vs deep-review routing
- pull-first notification policy

## P2 Artifact and Case Semantics

Add to working backlog:
- stale rules
- artifact refresh policy
- case creation rules
- case noise-suppression rules

## Recommended Next Action

Use this document to update:
- PRD draft
- Stage 6 implementation plan
- working sprints
- acceptance criteria for Stage 6 productization

This document should be treated as a product-contract extension to the existing remediation and implementation backlog.
