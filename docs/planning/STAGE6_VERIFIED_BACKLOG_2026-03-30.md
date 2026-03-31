# Stage 6 Verified Backlog Snapshot

> Status note (2026-03-31): historical/supporting snapshot from pre-rebuild remediation cycle.
> Current operational baseline authority moved to `N1_READINESS_BASELINE_2026-03-31.md`.

## Date

2026-03-30

## Purpose

This document converts the recent Stage 6 code audit into a verified backlog snapshot.

It exists to answer:
- which Claude-raised points were confirmed by code
- which were only partially true
- which were false
- what should be fixed before Stage 6 rebuild versus after Stage 6 rebuild
- how to break the remaining work into agent-sized execution slices

This is not a speculative wish list.
It is a code-verified backlog baseline.

## Verification Baseline

The following files were checked directly:
- `src/TgAssistant.Mcp/index.ts`
- `src/TgAssistant.Intelligence/Stage6/BotCommandService.cs`
- `src/TgAssistant.Intelligence/Stage6/BotChatService.cs`
- `src/TgAssistant.Intelligence/Stage6/Profiles/ProfileTraitExtractor.cs`
- `src/TgAssistant.Intelligence/Stage6/Profiles/ProfileEngine.cs`
- `src/TgAssistant.Intelligence/Stage6/Profiles/PatternSynthesisService.cs`
- `src/TgAssistant.Intelligence/Stage6/CurrentState/StateScoreCalculator.cs`
- `src/TgAssistant.Intelligence/Stage6/CurrentState/RelationshipStatusMapper.cs`
- `src/TgAssistant.Intelligence/Stage6/Drafts/DraftGenerator.cs`
- `src/TgAssistant.Web/Read/WebReadService.cs`

The audit verdict for the Claude points was:

### Confirmed

- MCP does not expose Stage 6 artifacts/read models.
- Stage 6 profile and state lexical heuristics are EN-centric.
- `profile_signal` output from Stage 5 is not bridged into `ProfileEngine`.
- `DraftGenerator` is template-based, not LLM-backed.
- post-breakup taxonomy/status is missing.
- bot command `/profile` is missing.
- `PatternSynthesisService` outputs EN summaries.
- `BotChatService` does not consume Stage 6 state/strategy/profile artifacts.

### Partially confirmed

- `PeriodizationService` is implemented and can run on demand, but not exposed as a persistent always-on baseline.
- `ProfileEngine` exists and is used from web reads, but not exposed through bot command flow.
- offline breakup events are helpful and already wired, but they are not the only possible anchor.
- `ArchiveOnlyMode` risk is real operationally, but it is a runbook issue rather than a Stage 6 product bug.

### Rejected

- `CurrentStateEngine` is not missing; it already runs via `/state` and web reads.

## Priority Rule

Before the Stage 6 rebuild:
- fix data-consumer and operator-read gaps that would make the rebuilt Stage 6 misleading or inaccessible

After the Stage 6 rebuild:
- improve strategy breadth and draft quality once the rebuilt artifacts are available

Do not mix:
- Stage 6 semantic repair
- Stage 6 rebuild execution
- post-rebuild product expansion

## Must-Fix Before Stage 6 Rebuild

### B1. MCP Stage 6 Read Surface

Priority: P0

Why it matters:
- without this, agents and operators still see only Stage 5 facts or raw web pages
- rebuilt Stage 6 quality will be hard to inspect or trust

Required coverage:
- `get_current_state`
- `get_strategy`
- `get_profiles`
- `get_periods`
- `get_profile_signals`
- `get_session_summaries`

Expected result:
- Stage 6 artifacts become inspectable without manual DB digging

### B2. RU-Aware Profile and State Heuristics

Priority: P0

Why it matters:
- `ProfileTraitExtractor`
- `PatternSynthesisService`
- `StateScoreCalculator`
still rely on EN lexical cues

Required fixes:
- add RU lexical equivalents to profile trait extraction
- add RU lexical equivalents to current-state scoring
- stop EN-only pattern summaries from being the default output for RU corpora

Expected result:
- rebuilt Stage 6 can score RU chats materially better

### B3. Bridge Stage 5 `profile_signal` Into Stage 6 Profile Computation

Priority: P0

Why it matters:
- Stage 5 already extracts canonical profile signals
- `ProfileEngine` currently ignores them
- profile quality stays lower than the available upstream signal

Required fixes:
- load `IntelligenceClaim` rows with `ClaimType = "profile_signal"`
- merge them into profile evidence context
- use them conservatively so they enrich rather than overwrite message-derived evidence

Expected result:
- Stage 6 profile snapshots use the best Stage 5 behavioral signal already present in the corpus

### B4. Bot and MCP Access to Stage 6 Artifacts

Priority: P0

Why it matters:
- `BotChatService` currently answers from local chat context and facts
- it does not surface current state, strategy, profile, or periods

Required fixes:
- extend bot context/tool path with Stage 6 artifacts
- preserve concise behavior; do not dump raw artifacts

Expected result:
- bot answers become Stage 6-aware instead of Stage 5-only

### B5. `/profile` Operator Command

Priority: P1

Why it matters:
- bot flow has `/state` and `/timeline`
- profile remains inaccessible from bot despite existing engine

Required fixes:
- add `/profile`
- reuse persisted snapshots when fresh
- trigger `ProfileEngine` when missing/stale

Expected result:
- operator can run the full `state -> timeline -> profile` triad from bot

## Fix Soon After Stage 6 Rebuild

### A1. Post-Breakup Taxonomy and Status

Priority: P1

Why it matters:
- current status set lacks `post_breakup` and `no_contact`
- current strategy action set lacks post-breakup-specific actions

Required fixes:
- add relationship-status support for breakup/no-contact modes
- add strategy options such as:
  - `re_establish_contact`
  - `acknowledge_separation`
  - `test_receptivity`

Expected result:
- Stage 6 strategy stops overusing generic `invite/deepen` logic in breakup-like states

### A2. LLM-Backed Draft Generation

Priority: P2

Why it matters:
- current template drafts are deterministic and cheap
- but they do not express pair-specific style/history very well

Required fixes:
- add controlled LLM-backed draft mode on top of Stage 6 artifacts
- keep existing template path as fallback

Expected result:
- higher-quality personalized drafts after the state/profile/strategy substrate is trustworthy

## Operational Runbook Backlog

### O1. Stage 6 Rebuild Reset Boundary

Priority: P0

Need to define explicitly:
- what Stage 6 artifacts must be cleared after Stage 5 rebuild
- what can be preserved
- how rebuild completion is verified

Baseline artifacts:
- `docs/planning/S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md`
- `docs/runbooks/stage6-rebuild-verification.md`

### O2. Archive Mode and Runtime Baseline Checks

Priority: P1

Need to verify before normal live operation:
- `ArchiveOnlyMode=false`
- `ArchiveCutoffUtc` does not suppress fresh messages
- runtime roles used for Stage 6 rebuild are explicit

This is an ops checklist, not a product feature sprint.

## Agent-Sized Sprint Structure

The work should be split into narrow sprints with disjoint ownership where possible.

## Pre-Sprint S6-R0: Runtime and Rebuild Prep

Goal:
- finalize rebuild/runbook baseline for Stage 6 after Stage 5 completion

Scope:
- Stage 6 reset/rebuild boundary
- runtime/env/provider prerequisites
- operator verification checklist
- archive-mode and topology checks

Why this size:
- mostly docs/runbook work
- one agent can own it end-to-end

## Sprint S6-R1: MCP and Operator Read Surface

Goal:
- make rebuilt Stage 6 observable and runnable by operators and agents

Scope:
- MCP Stage 6 readers
- `/profile` bot command
- session summary/profile signal/session-period read exposure

Recommended ownership split:
- Worker A: MCP tools
- Worker B: bot `/profile` and command wiring

## Sprint S6-R2: RU Semantics Hardening

Goal:
- make Stage 6 materially usable on RU corpora

Scope:
- `ProfileTraitExtractor`
- `PatternSynthesisService`
- `StateScoreCalculator`

Recommended ownership split:
- Worker A: profile extraction + pattern synthesis
- Worker B: state scoring and regression checks

## Sprint S6-R3: Profile Signal Bridge

Goal:
- feed Stage 5 behavioral signal into Stage 6 profiles

Scope:
- claim loading
- profile evidence model extension
- conservative merge logic
- verification of resulting snapshots

Why this size:
- this touches one cross-layer integration seam
- should be owned by one worker to avoid semantic drift

## Sprint S6-R4: Bot Context Integration

Goal:
- make Stage 6 artifacts actually used in operator answers

Scope:
- `BotChatService` system/tool context
- artifact reuse path
- concise response synthesis from Stage 6 state/strategy/profile/timeline

Why this size:
- this is a single integration path and should remain tightly owned

## Sprint S6-R5: Rebuild and Verification

Goal:
- rebuild Stage 6 from the repaired Stage 5 corpus and verify operator usefulness

Scope:
- clear stale Stage 6 artifacts
- run rebuild
- verify current state, timeline, profiles, strategy, bot context
- confirm MCP visibility

Why this size:
- operational rebuild and verification work should be isolated from implementation sprints

## Sprint S6-R6: Post-Rebuild Relationship Expansion

Goal:
- add breakup-aware taxonomy and optional better draft generation once rebuilt Stage 6 is trusted

Scope:
- relationship statuses for breakup/no-contact
- post-breakup strategy actions
- optional LLM-backed draft generation

Why this size:
- this is product expansion, not rebuild gating

## Sequencing Rule

The recommended execution order is:

1. `S6-R0`
2. `S6-R1`
3. `S6-R2`
4. `S6-R3`
5. `S6-R4`
6. `S6-R5`
7. `S6-R6`

Do not start `S6-R5` until:
- Stage 5 completion is confirmed
- `S6-R1` through `S6-R4` are done or explicitly waived

Do not start `S6-R6` until:
- rebuilt Stage 6 artifacts are available
- post-rebuild operator verification is complete

## What Is Explicitly Not In This Track

- more Stage 5 semantic tuning
- broad Stage 6 UI redesign
- generic prompt experimentation unrelated to verified gaps
- speculative relationship psychology expansion without code-backed need

## Final Rule

From this point, Stage 6 backlog should be driven by:
- code-verified gaps
- rebuild readiness
- operator usefulness after rebuild

Not by unverified hypotheses in isolation.
