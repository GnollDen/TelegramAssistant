# Stage 6 Full Implementation Plan

## Purpose

This document fixes the non-MVP implementation plan for Stage 6 as a product layer above Stage 5.

Stage 5 is treated as the stabilized substrate:
- messages
- message extractions
- sessions
- summaries
- facts
- relationships

Stage 6 is treated as the scenario-driven reasoning and operator layer that turns the Stage 5 substrate into:
- artifacts
- cases
- suggestions
- drafts
- dossiers
- state snapshots
- reviewable operator outputs

This plan is intentionally broader than MVP. It is meant to guide agent execution and future sprint planning.

## Target Outcome

Full Stage 6 should not be just "model answers on top of data".

It should become a managed system that:
- materializes useful artifacts
- finds and prioritizes actionable cases
- surfaces them in bot/web
- supports human review and correction
- refreshes stale outputs predictably
- stays measurable in quality, cost, and latency

## What Already Exists

Current foundation already available:
- Stage 5 stabilized and operational
- Stage 6 code scope and runtime wiring
- BotChat path
- scenario engines
- smoke/eval harness
- A/B baseline work
- prompt hardening for:
  - no-tool chat-aware path
  - post-tool synthesis

Current default baseline:
- `openai/gpt-4o-mini`

Current strong candidate:
- `x-ai/grok-4-fast`

## Gaps To Full Stage 6

Main missing layers:

1. Artifact persistence
- Stage 6 outputs need stable stored records, not one-off generations only.

2. Case model
- The system must represent actionable work items explicitly.

3. Auto case generation
- The system must surface what needs review, input, or action.

4. Operator workflow
- Bot/web must become actual working interfaces for Stage 6 cases and artifacts.

5. Feedback loop
- Human corrections and outcomes must be captured and reused.

6. Evaluation and economics
- Quality, latency, and cost must be visible by scenario.

## Sprint Roadmap

### Sprint 1: Artifact Foundation

Goal:
- Turn Stage 6 outputs into stable artifacts.

Scope:
- Define artifact types:
  - `dossier`
  - `current_state`
  - `strategy`
  - `draft`
  - `review`
  - `clarification`
  - `outcome`
- Add artifact storage model:
  - artifact type
  - chat scope
  - entity scope when relevant
  - status
  - confidence
  - source basis
  - generated at
  - refreshed at
  - stale/refresh markers
- Add read path for bot/web.
- Add simple refresh policy:
  - on-demand
  - stale
  - manual refresh

Manual testing:
- Re-run dossier and current-state on real chats.
- Check artifact reuse vs regeneration.
- Check that artifact reads stay stable across repeated queries.

A/B testing:
- Optional spot checks only.

Exit condition:
- Stage 6 artifacts exist as persisted outputs.

### Sprint 2: Dossier and State Quality

Goal:
- Make the two core artifact types production-usable.

Scope:
- Improve dossier synthesis.
- Improve current-state synthesis.
- Keep clean separation between:
  - confirmed facts
  - relationships
  - notable events
  - uncertainties
- Keep anti-dump behavior.
- Keep anti-hallucination discipline.

Manual testing:
- Blind/manual review on 8-12 real dossier/state cases.
- Check factuality vs source substrate.
- Check omissions and over-interpretation.

A/B testing:
- `gpt-4o-mini` vs `grok-4-fast`
- Run on:
  - dossier
  - current_state

Exit condition:
- Stable baseline quality on core artifacts.

### Sprint 3: Case Model

Goal:
- Represent Stage 6 work as explicit cases, not only outputs.

Scope:
- Define case types:
  - `needs_review`
  - `needs_input`
  - `risk`
  - `dossier_candidate`
  - `draft_candidate`
  - `strategy_candidate`
  - `state_refresh_needed`
- Add case schema:
  - type
  - reason
  - priority
  - confidence
  - linked artifact
  - owner
  - status
  - timestamps
- Add lifecycle:
  - `new`
  - `ready`
  - `needs_user_input`
  - `accepted`
  - `rejected`
  - `resolved`
  - `stale`

Manual testing:
- Validate case creation on real chats.
- Check noise vs usefulness.
- Check no uncontrolled duplication.

A/B testing:
- Not required by default.

Exit condition:
- Stage 6 can create explicit actionable cases.

### Sprint 4: Minimal Auto Case Generation

Goal:
- Let Stage 6 identify useful work on its own.

Scope:
- Add generation rules for:
  - `risk`
  - `needs_input`
  - `dossier_candidate`
  - `draft_candidate`
- Add case dedupe.
- Add minimal prioritization/ranking.
- Add refresh/reopen logic.

Manual testing:
- Review 20-30 auto-generated cases.
- Measure false positives and missed useful cases.

A/B testing:
- Threshold/rule tuning if needed.

Exit condition:
- Stage 6 generates an operator queue with acceptable signal-to-noise.

### Sprint 5: Operator Layer Surfacing

Goal:
- Make Stage 6 cases and artifacts usable in bot/web.

Scope:
- Web queue of cases.
- Web case detail view.
- Web artifact detail view.
- Bot surfacing for:
  - urgent cases
  - ready outputs
  - requests for input
- Human actions:
  - accept
  - reject
  - resolve
  - request refresh
  - add notes

Manual testing:
- Full operator workflow in web.
- Full operator workflow in bot.
- Verify state transitions after human actions.

A/B testing:
- Optional UX/content testing.

Exit condition:
- Stage 6 has a real operator workflow.

### Sprint 6: Feedback, Evaluation, and Economics

Goal:
- Turn Stage 6 into a measurable, improvable system.

Scope:
- Add feedback capture.
- Add review outcome tracking.
- Add artifact/case resolution tracking.
- Add regression packs.
- Add cost/latency analytics by scenario.
- Add per-model and per-scenario quality views.

Manual testing:
- Review loop on real cases.
- Verify feedback persistence and replayability.
- Verify regression reporting.

A/B testing:
- Regular model/prompt A/B:
  - dossier/state
  - draft/review
  - bot chat

Exit condition:
- Stage 6 has a closed quality loop.

## Backlog

### P0

- Define final Stage 6 artifact types and schemas.
- Define final Stage 6 case types and lifecycle.
- Decide what is materialized vs on-demand.
- Define operator contract:
  - what appears in web
  - what appears in bot
  - what requires human input

### P1

- Persist dossier artifacts.
- Persist current_state artifacts.
- Persist draft and review artifacts.
- Add artifact refresh policy.
- Implement case schema and lifecycle.
- Add minimal case generation rules.

### P2

- Add web queue for Stage 6 cases.
- Add artifact/case detail screens.
- Add bot surfacing of:
  - urgent cases
  - ready outputs
  - input-needed items
- Add accept/reject/resolve flow.

### P3

- Add feedback-based refinement.
- Add better prioritization/ranking.
- Add richer orchestration and scheduling.
- Add broader scenario coverage.
- Add UX polish across operator surfaces.

## Manual Testing Gates

Manual review is required at these points:

1. After Sprint 2
- dossier/state quality gate

2. After Sprint 4
- auto case generation usefulness gate

3. After Sprint 5
- operator workflow gate

4. After Sprint 6
- broader readiness gate

## A/B Test Gates

Recommended A/B checkpoints:

1. Sprint 2
- dossier/state output quality

2. Sprint 5
- bot-facing response quality if prompts changed

3. Sprint 6
- regular model/prompt regression A/B

## Recommended Execution Order

1. Artifact Foundation
2. Dossier and State Quality
3. Case Model
4. Minimal Auto Case Generation
5. Operator Layer Surfacing
6. Feedback, Evaluation, and Economics

## Working Rule For Agents

When using this plan for delegated work:
- prefer narrow sprint-scoped tasks
- keep Stage 5 untouched unless a Stage 6 task proves a real dependency
- treat artifact model and case model as first-class deliverables
- require manual review gates after major quality-affecting changes
- prefer controlled A/B over intuition when changing prompts/models

## Current Practical Baseline

As of 2026-03-24:
- Stage 5 is stabilized and operational
- Stage 6 is ready for controlled use
- baseline model remains `openai/gpt-4o-mini`
- `x-ai/grok-4-fast` remains a strong candidate but not the default

This document should be treated as the planning baseline for full Stage 6 implementation work.
