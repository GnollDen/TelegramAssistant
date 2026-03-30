# Stage 6 Remediation Sprint Plan

## Date

2026-03-26

## Purpose

This document defines the focused remediation track for Stage 6 after the Stage 5 contract audit, the recent Stage 6 operator/runtime findings, and the later code-verified backlog audit.

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
- the verified Stage 6 backlog snapshot in `STAGE6_VERIFIED_BACKLOG_2026-03-30.md`

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

1. `Pre-Sprint S6-R0: Runtime and Rebuild Prep`
2. `Sprint S6-R1: MCP and Operator Read Surface`
3. `Sprint S6-R2: RU Semantics Hardening`
4. `Sprint S6-R3: Profile Signal Bridge`
5. `Sprint S6-R4: Bot Context Integration`
6. `Sprint S6-R5: Rebuild, Verification, and Readiness Hardening`
7. `Sprint S6-R6: Post-Rebuild Relationship Expansion`

The key rule:
- do not start the Stage 6 rebuild itself from hidden environment ambiguity
- do not mix old Stage 6 artifacts with the rebuilt Stage 5 corpus

## Pre-Sprint S6-R0: Runtime and Rebuild Prep

### Goal

Prepare a truthful dev/runtime baseline for Stage 6 remediation and the eventual Stage 6 rebuild after the Stage 5 full rerun.

### Scope

- document which runtime roles are required for:
  - Stage 5 full rerun continuation
  - Stage 6 rebuild
  - bot/web operator verification
- lock provider/env assumptions:
  - LLM provider credentials
  - DB/Redis access
  - runtime auth health
- define the safe rebuild/reset sequence:
  - Stage 5 completion first
  - Stage 6 reset/rebuild second
- define which existing Stage 6 artifacts/cases must be cleared or rebuilt after Stage 5 rerun
- define commands/runbooks for:
  - Stage 6 rebuild
  - targeted smokes
  - operator verification
  - archive-mode sanity checks

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

Execution artifacts for this pre-sprint baseline:

- `docs/planning/S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md`
- `docs/runbooks/stage6-rebuild-verification.md`

### Exit criteria

- dev/runtime assumptions for Stage 6 remediation are explicit
- Stage 6 rebuild prerequisites are explicit
- no hidden environment blocker remains unidentified before remediation execution

## Sprint S6-R1: MCP and Operator Read Surface

### Goal

Make rebuilt Stage 6 observable and runnable through MCP and bot surfaces.

### Scope

- add MCP read tools for:
  - current state
  - strategy
  - profiles
  - periods
  - profile signals
  - session summaries
- add or expose bot-level `/profile`
- ensure the operator can inspect the rebuilt Stage 6 without direct DB work

### Why this exists

The code audit found:
- MCP only exposes Stage 5-oriented reads today
- `/profile` is missing from bot commands
- Stage 6 engines exist, but part of the operator/agent read surface does not

### Exit criteria

- the rebuilt Stage 6 can be inspected through MCP
- the operator can run `/state -> /timeline -> /profile`
- direct DB inspection is no longer required for normal Stage 6 review

## Sprint S6-R2: RU Semantics Hardening

### Goal

Make Stage 6 materially safer for RU-language chats.

### Scope

- audit and fix RU-hostile lexical heuristics in:
  - `ProfileTraitExtractor`
  - `PatternSynthesisService`
  - `StateScoreCalculator`
- keep fixes narrow and deterministic
- do not broaden this sprint into generic Stage 6 redesign

### Why this exists

The verified audit found:
- EN-centric lexical heuristics in core profile/state paths
- EN summary strings in profile pattern synthesis
- RU corpora are structurally under-scored today

### Exit criteria

- key RU profile/state paths no longer depend on EN-only lexical cues
- pattern summaries are no longer hardcoded EN-only outputs for RU chats
- Stage 6 quality on RU corpora is materially improved before rebuild

## Sprint S6-R3: Profile Signal Bridge

### Goal

Feed Stage 5 behavioral signal into Stage 6 profile computation.

### Scope

- load Stage 5 `profile_signal` claims into Stage 6 profile evidence
- bridge profile-signal data into `ProfileEngine`
- preserve conservative merge behavior so Stage 5 profile signals enrich but do not dominate
- verify persisted profile snapshots after the bridge

### Why this exists

The verified audit found:
- Stage 5 already writes canonical `profile_signal`
- `ProfileEngine` does not read those claims today
- useful upstream signal is being discarded

### Exit criteria

- Stage 6 profiles consume Stage 5 profile signals
- profile snapshots reflect both direct message evidence and upstream structured behavioral evidence
- no uncontrolled semantic widening is introduced

## Sprint S6-R4: Bot Context Integration

### Goal

Make bot answers Stage 6-aware instead of Stage 5-only.

### Scope

- extend `BotChatService` context/tool path with:
  - current state
  - strategy
  - profiles
  - periods/timeline where useful
- preserve concise answer synthesis
- avoid raw artifact dumping
- verify that bot responses improve because Stage 6 artifacts are available

### Why this exists

The verified audit found:
- `BotChatService` currently uses local chat context and Stage 5 facts
- Stage 6 artifacts are not part of the bot reasoning context

### Exit criteria

- bot has access to Stage 6 artifacts
- answers are no longer limited to Stage 5-only memory patterns
- operator-facing assistant behavior materially improves

## Sprint S6-R5: Rebuild, Verification, and Readiness Hardening

### Goal

Rebuild Stage 6 on top of the repaired Stage 5 corpus and verify that the rebuilt Stage 6 is trustworthy.

### Scope

- clear/rebuild Stage 6 artifacts and cases that depend on the old Stage 5 corpus
- run the Stage 6 rebuild after the global Stage 5 rerun completes
- verify:
  - artifact quality
  - case quality
  - chronology quality
  - operator workflow integrity
  - RU-chat quality on profiles/current_state/strategy
  - MCP visibility

### Why this exists

After a global Stage 5 rerun, mixed old/new Stage 6 materialized outputs are not trustworthy.

### Exit criteria

- Stage 6 artifacts/cases have been rebuilt from the repaired Stage 5 corpus
- Stage 6 verification evidence is green or narrowed to explicit follow-up
- rebuilt Stage 6 is safe for normal operator use

## Sprint S6-R6: Post-Rebuild Relationship Expansion

### Goal

Add the remaining relationship-intelligence product gaps after rebuilt Stage 6 becomes trustworthy.

### Scope

- add relationship status support for:
  - `post_breakup`
  - `no_contact`
- add strategy actions for:
  - `re_establish_contact`
  - `acknowledge_separation`
  - `test_receptivity`
- optionally replace template-only draft generation with a controlled LLM-backed path

### Why this exists

The verified audit found:
- post-breakup-specific strategy/status handling is missing
- draft generation is still template-based

### Exit criteria

- Stage 6 understands breakup/no-contact-specific states
- strategy is less likely to overuse generic `invite/deepen` recommendations
- draft generation roadmap is explicit and, if implemented, safely gated

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
  - MCP artifact readability

## Key Rule For This Track

Do not treat Stage 6 remediation as purely UI cleanup.

The primary remediation work here is:
- data-consumer semantics
- RU-language Stage 6 quality
- Stage 5 to Stage 6 signal bridging
- operator and MCP readability
- rebuild discipline after Stage 5 repair

Operator UX remains important, but this track is centered on Stage 6 trustworthiness after the Stage 5 contract reset.
