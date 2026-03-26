# Web Delivery Sprint Plan

## Date

2026-03-25

## Purpose

This document defines a separate web-delivery track for the existing Stage 6 product.

It does not replace the backend sprint program.
It assumes the backend/operator substrate already exists and focuses only on turning the current web logic into a usable working web application.

## Position Relative To Main Program

The main implementation program already covers:
- Stage 5 and Stage 6 backend
- artifacts
- cases
- clarification and user-supplied context
- bot workflow
- evaluation and economics

What is still under-specified is the web delivery layer:
- actual web host
- operator-facing routes and screens
- queue/detail/action UX
- web-specific hardening and usability

This track exists to close that gap without rewriting the completed backend plan.

## Outcome Target

The target is not a polished consumer UI.

The target is:
- a stable internal operator web app
- usable every day
- consistent with bot workflow
- clear enough to inspect queue, cases, artifacts, and clarification context

## Execution Order

1. Pre-Sprint W0: Dev Environment Refresh
2. Sprint W1: Web Host and Operator Shell
3. Sprint W2: Core Operator Workflow
4. Sprint W3: Clarification and Deep Review
5. Sprint W4: Usability and Hardening

## Pre-Sprint W0: Dev Environment Refresh

### Goal

Make the development environment truthful and repeatable before web implementation starts.

### Scope

- confirm actual current code truth for the web layer
- confirm which web logic already exists vs what is only planned
- align local dev/runtime setup for web work
- define the web implementation baseline and non-goals
- lock the execution surface for W1-W4

### Why this exists

The current repo already contains:
- web read/review/ops services
- verification services
- Stage 6 operator logic

But it does not yet clearly expose:
- a real hosted web app
- explicit HTTP routes/API contract
- a defined web shell and page structure

Starting implementation without refreshing the dev baseline would create avoidable confusion.

### Required outputs

1. Current web code map:
   - existing services
   - existing renderers
   - existing verification/smoke paths
   - missing host/runtime pieces
2. Local web dev startup note:
   - runtime role
   - env/config prerequisites
   - DB/Redis assumptions
   - how web work will be run locally
3. Locked web MVP scope for W1-W2.
4. Locked “normal working web” scope for W3-W4.

### Exit criteria

- the web baseline is explicit
- the team knows what is already implemented and what is missing
- W1 can begin without guessing about environment or scope

## Sprint W1: Web Host and Operator Shell

### Goal

Turn the current web service layer into a real hosted web application.

### Scope

- real web host/runtime
- minimal single-operator access model
- operator shell/layout/navigation
- startup/runtime wiring for web role
- entry pages for:
  - queue
  - case detail
  - artifact detail

### Exit criteria

- web app starts locally in a repeatable way
- operator can open the main shell in a browser
- queue and detail pages exist as real web surfaces

### Manual testing

- cold start of web role
- shell load
- basic navigation
- queue/detail page open

## Sprint W2: Core Operator Workflow

### Goal

Make the web app useful for daily core operator work.

### Scope

- queue with status, priority, confidence, reason
- case detail with evidence summary
- artifact views for:
  - dossier
  - current_state
  - strategy
  - draft
  - review
- basic actions:
  - resolve
  - reject
  - refresh
  - answer clarification

### Exit criteria

- operator can process cases end-to-end in web
- web reflects real backend case/artifact state
- basic actions change lifecycle state correctly

### Manual testing

- queue filtering and ordering sanity
- case open -> action -> state change
- artifact detail open
- clarification answer flow from web

## Sprint W3: Clarification and Deep Review

### Goal

Make the web app the place for complex review rather than only quick actions.

### Scope

- expanded clarification review
- long-form user context and correction flow
- evidence drill-down
- timeline/date/people context around a case
- linked cases and linked artifacts
- history/reopen/outcome visibility where practical

### Exit criteria

- complex clarification cases are easier in web than in bot
- operator can inspect enough evidence before giving judgment
- user-supplied context is visible and reviewable in web

### Manual testing

- clarification with deep evidence review
- long-form context submission
- linked artifact/case navigation
- outcome/history inspection

## Sprint W4: Usability and Hardening

### Goal

Bring the web app to an internally solid, daily-usable state.

### Scope

- filters and sorting cleanup
- empty/loading/error states
- action confirmations where useful
- web-specific smoke/manual verification tightening
- basic performance and rendering cleanup
- visual cleanup for a calm internal-tool look

### Exit criteria

- web feels coherent and stable as a working tool
- common flows do not feel raw or confusing
- verification coverage is good enough for normal use

### Manual testing

- daily-use operator walkthrough
- error-state walkthrough
- refresh/reload/navigation stability
- repeated use without state confusion

## MVP vs Normal Working Web

### Web MVP

Web MVP is achieved after:
- Pre-Sprint W0
- Sprint W1
- Sprint W2

This should be enough for:
- opening the app
- seeing queue and details
- using core actions
- handling clarification from web when needed

### Normal Working Web

A normal working internal web layer is achieved after:
- Sprint W3
- Sprint W4

This should be enough for:
- deep review
- clarification-heavy cases
- evidence inspection
- calmer day-to-day operator use

## Recommended Ownership

- host/runtime/web startup:
  - `src/TgAssistant.Host`
  - `src/TgAssistant.Web`
- operator pages/routes/rendering:
  - `src/TgAssistant.Web`
- integration with artifacts/cases:
  - `src/TgAssistant.Intelligence.Stage6`
  - repositories and web read/ops services

## Working Rule

This web track should remain:
- delivery-focused
- operator-focused
- low-polish but high-clarity

Do not turn it into:
- a public-product redesign
- a frontend design exercise
- a second implementation of backend logic that already exists
<<<<<<< HEAD
=======

## Post-W4 Follow-Up Backlog

The core web track is complete, but the following operator-facing cleanup remains important.

### Real Scope Prioritization

Problem:
- single-user web scope bootstrap can currently surface technical or smoke contexts as candidate working scopes
- this is correct technically, but wrong for default operator UX

Required fix:
- default scope resolution should prioritize real operator contexts above smoke/dev/test contexts
- technical contexts should be excluded from default auto-resolution where possible
- if shown at all, technical contexts should appear in a clearly separate secondary section

Minimum expected behavior:
- configured default scope stays highest priority
- inferred scope should prefer real active operator cases
- smoke/dev/generated contexts should not become the default operator landing context
- onboarding list should explain real available contexts in operator language, not as raw technical scope candidates
>>>>>>> 58c7268 (Add Stage 6 remediation planning track)
