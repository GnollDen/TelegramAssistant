# Stage 6 Remediation Sprint Plan

## Date

2026-03-26

## Purpose

This document defines the focused remediation track for Stage 6 after the Stage 5 contract audit and the recent Stage 6 operator/runtime findings.

It exists to answer:
- what must be fixed in Stage 6 before and after the global Stage 5 rebuild
- what must be prepared in dev/runtime before remediation starts
- which Stage 6 issues are product-facing vs data-contract-facing
- what must be rebuilt once the Stage 5 corpus is reprocessed

This is not a broad Stage 6 redesign plan.
It is a repair and stabilization track built on top of:
- the Stage 5 contract normalization work
- the planned full Stage 5 rerun
- the already delivered bot/web/operator substrate

## Why A Separate Remediation Track Exists

The original Stage 6 implementation work delivered:
- artifacts
- cases
- operator workflow
- feedback/eval/economics
- bot/web surfaces

But post-launch and post-audit investigation revealed additional gaps:
- Stage 6 still contains EN-centric lexical heuristics for RU corpora
- some sender/participant assumptions are too brittle
- case provenance is not sufficiently chronology-first or operator-explainable
- bot first-run and default scope behavior needed cleanup
- web single-user scope resolution needed hardening
- real operator scopes must be prioritized over technical smoke scopes
- Stage 6 needs a controlled rebuild path after the Stage 5 full rerun

## Target Outcome

After this remediation track, Stage 6 should be:
- aligned to the repaired Stage 5 substrate
- safer for RU-language chats
- more truthful in participant handling
- more explainable in case generation and provenance
- operationally ready to rebuild after the Stage 5 corpus rerun

## Track Structure

This remediation track is split into:

1. `Pre-Sprint S6-R0: Dev Environment and Rebuild Prep`
2. `Sprint S6-R1: RU-Aware Data Semantics and Participant Safety`
3. `Sprint S6-R2: Case Provenance and Chronology Alignment`
4. `Sprint S6-R3: Operator Context and Scope Quality`
5. `Sprint S6-R4: Rebuild, Verification, and Readiness Hardening`

The key rule:
- do not start the Stage 6 rebuild itself from hidden environment ambiguity
- do not mix old Stage 6 artifacts with the rebuilt Stage 5 corpus

## Pre-Sprint S6-R0: Dev Environment and Rebuild Prep

### Goal

Prepare a truthful dev/runtime baseline for Stage 6 remediation and the eventual Stage 6 rebuild after the Stage 5 full rerun.

### Scope

- document which runtime roles are required for:
  - Stage 5 full rerun
  - Stage 6 rebuild
  - bot/web operator verification
- lock provider/env assumptions:
  - LLM provider credentials
  - DB/Redis access
  - OpenRouter/runtime auth health
- define the safe rebuild sequence:
  - Stage 5 full rerun first
  - Stage 6 rebuild second
- define which existing Stage 6 artifacts/cases must be cleared or rebuilt after Stage 5 rerun
- define dev commands/runbooks for:
  - rebuild workers
  - targeted smokes
  - verification outputs

### Why this exists

Recent work showed that:
- `web,ops` runtime is not enough for Stage 5 reanalysis
- provider auth issues can stop rebuilds mid-run
- Stage 6 remediation should not begin from a misleading local/runtime setup

### Required outputs

1. Stage 6 remediation runtime topology note.
2. Stage 6 rebuild prerequisites note.
3. Explicit command/runbook for Stage 6 rebuild and verification.
4. Clear statement of what must be rebuilt vs preserved.

### Exit criteria

- dev/runtime assumptions for Stage 6 remediation are explicit
- Stage 6 rebuild prerequisites are explicit
- no hidden environment blocker remains unidentified before remediation execution

## Sprint S6-R1: RU-Aware Data Semantics and Participant Safety

### Goal

Make Stage 6 semantically safer for RU chats and less brittle around participant/sender assumptions.

### Scope

- audit and fix RU-hostile lexical heuristics in:
  - profiles
  - current_state
  - strategy-supporting heuristics
  - pair/relationship synthesis
- reduce or eliminate brittle `SenderId > 0` assumptions where they distort Stage 6 reasoning
- verify that Stage 6 consumes canonical Stage 5 ids correctly after the Stage 5 contract normalization
- harden participant resolution for profiles/network/current_state inputs

### Why this exists

The Stage 5 audit found:
- EN-centric lexical heuristics in Stage 6
- participant assumptions that can underweight or exclude signals in RU corpora

### Exit criteria

- Stage 6 no longer materially depends on EN-only lexical cues for key RU paths
- Stage 6 participant handling is safer against degraded or recovered sender identity
- data-consumer assumptions are aligned with repaired Stage 5 semantics

## Sprint S6-R2: Case Provenance and Chronology Alignment

### Goal

Make Stage 6 cases explainable through chronology, state transitions, and evidence-driven provenance.

### Scope

- improve case provenance explanation:
  - why this case exists
  - why now
  - what changed
  - which period/transition/evidence triggered it
- align auto-case reasoning with chronology-first expectations where practical
- surface better provenance inputs for clarification/review/refresh/reopen flows
- define or implement clearer operator-readable case origin types

### Why this exists

Recent product review showed that operators currently lack a clear answer to:
- where a case came from
- whether it reflects chronology, conflict, clarification, or freshness pressure

### Exit criteria

- cases are materially more explainable
- provenance is no longer a vague reason string only
- chronology and transition signals are visible in case generation/supporting reads

## Sprint S6-R3: Operator Context and Scope Quality

### Goal

Tighten Stage 6 operator-context quality for single-user usage and reduce technical-scope leakage.

### Scope

- prioritize real operator scopes above smoke/dev/test scopes
- improve default context selection and onboarding when safe
- strengthen bot first-run scope bootstrap and operator guidance
- refine stage6-case/artifact/context reads so they favor real working contexts
- reduce technical/noisy Stage 6 contexts from default landing flows

### Why this exists

Recent live usage showed:
- technical smoke scopes surfaced in default web onboarding
- bot first-run behavior was too raw before targeted cleanup
- single-user defaults still need stronger real-scope prioritization

### Exit criteria

- default Stage 6 operator paths prefer real working contexts
- technical/smoke contexts do not dominate first-run operator experience
- bot/web context bootstrap is operator-usable

## Sprint S6-R4: Rebuild, Verification, and Readiness Hardening

### Goal

Rebuild Stage 6 on top of the repaired Stage 5 corpus and verify that the rebuilt Stage 6 is trustworthy.

### Scope

- clear/rebuild Stage 6 artifacts and cases that depend on the old Stage 5 corpus
- run the Stage 6 rebuild after the global Stage 5 rerun completes
- verify:
  - artifact quality
  - case quality
  - provenance quality
  - operator workflow integrity
  - RU-chat quality on profiles/current_state/strategy
- refresh readiness evidence for Stage 6-specific paths

### Why this exists

After a global Stage 5 rerun, mixed old/new Stage 6 materialized outputs are not trustworthy.

### Exit criteria

- Stage 6 artifacts/cases have been rebuilt from the repaired Stage 5 corpus
- Stage 6 verification evidence is green or narrowed to explicit follow-up
- rebuilt Stage 6 is safe for normal operator use

## Verification Model

This track should use a mixed verification model:

- code/build checks
- targeted Stage 6 smokes
- runtime verification after rebuild
- manual operator review on:
  - case provenance
  - current_state usefulness
  - profile/pair quality
  - bot/web operator flows

## Key Rule For This Track

Do not treat Stage 6 remediation as purely UI cleanup.

The primary remediation work here is:
- data-consumer semantics
- participant safety
- case provenance quality
- rebuild discipline after Stage 5 repair

Operator UX remains important, but this track is centered on Stage 6 trustworthiness after the Stage 5 contract reset.
