# Sprint 10 Task Pack

## Name

Web Read Layer

## Goal

Expose the existing reasoning stack through a usable read-first web layer so the product has:

- dashboard
- current state view
- timeline view
- profiles view
- clarifications view
- strategy view
- drafts/reviews read view
- offline events view

This sprint should make the product materially usable in web form without expanding into full editing and review workflows.

## Why This Sprint

The product now has:

- clarification orchestration
- periodization
- current state
- profiles
- strategy
- drafts
- draft review
- bot command layer

The next critical step is a read-first web interface.

Without it:

- the product remains bot-heavy
- review and inspection are still too opaque
- dashboard-style operational use is still missing

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_10_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_10_TASK_PACK.md)
4. [SPRINT_10_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_10_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- existing web app structure
- current APIs/endpoints
- bot command output shapes
- Stage6 engine service interfaces

## Scope

In scope:

- read-focused web endpoints/view models
- dashboard
- current state page
- timeline page
- profiles page
- clarifications page
- strategy page
- drafts/reviews page
- offline events page
- basic routing/navigation

Out of scope:

- full edit mode
- review actions UI
- inbox triage UI polish
- search tuning
- saved views polish
- graph visualization polish
- GraphRAG

## Product Rules To Respect

The web layer should be:

- simple
- quiet
- analytical
- operational

It should be read-first in this sprint.

Use the agreed information architecture:

- Dashboard
- Current State
- Timeline
- Profiles
- Clarifications
- Strategy
- Drafts
- Offline Events

Dashboard should prioritize:

1. current state
2. next step
3. open clarifications
4. current period
5. alerts

Do not over-polish visuals.
Make the screens usable and coherent first.

## Required Deliverables

### 1. Web Read Models / Endpoints

Expose the existing engines and persisted outputs through web-facing read models or endpoints for:

- dashboard
- current state
- timeline
- profiles
- clarifications
- strategy
- drafts/reviews
- offline events

### 2. Dashboard

Show at minimum:

- current state summary
- strategy next step summary
- open clarifications summary
- current period summary
- recent drafts/reviews summary

### 3. Current State Page

Show:

- dynamic label
- relationship status
- scores
- key signals
- main risks
- next move summary

### 4. Timeline Page

Show:

- current period
- prior periods
- transitions
- unresolved transitions
- compact evidence hooks where possible

### 5. Profiles Page

Show:

- self
- other
- pair

Including:

- summary
- top traits
- confidence/stability
- what works / what fails

### 6. Clarifications Page

Show:

- current top questions
- why they matter
- priority
- status

### 7. Strategy Page

Show:

- primary option
- alternatives
- risks
- micro-step
- horizon when available
- compact why-not notes

### 8. Drafts / Reviews Page

Show:

- recent draft artifacts
- main/alt drafts
- latest review assessment
- safer rewrite
- more-natural rewrite

### 9. Offline Events Page

Show:

- offline events list
- summaries
- linked period when available
- transcript/evidence summary when available

### 10. Verification Path

Add a verification path such as:

- `--web-smoke`

That proves:

- routes resolve
- core pages render
- data binding works for seeded/test data
- dashboard and at least 3 major screens show non-empty content

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. web smoke success
4. dashboard renders
5. current state page renders
6. timeline page renders
7. profiles page renders
8. strategy page renders

## Definition of Done

Sprint 10 is complete only if:

1. the product now has a usable read-first web interface
2. the main analytical layers are inspectable without the bot
3. web output is grounded in real engines and persisted data
4. the system is ready for later edit/review UI work

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how the web read layer now works
4. what verification was run
5. remaining limitations before Sprint 11
