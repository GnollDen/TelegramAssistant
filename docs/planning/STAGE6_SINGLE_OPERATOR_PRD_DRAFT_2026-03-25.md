# Stage 6 Single-Operator PRD Draft

## Date

2026-03-25

## Product

This is a personal Telegram intelligence system for one operator.

The system ingests communication, turns it into structured memory, and helps the operator make better decisions in ambiguous relational contexts.

## Primary User

There is one primary user:
- one operator

This is not a broad consumer product and not a multi-user SaaS workflow in the current form.

## Core Product Value

The system should:
- reduce cognitive overload
- preserve context over time
- distinguish facts from anxious reconstruction
- surface what needs attention
- produce useful drafts and reviews
- support cleaner and more mature decisions under emotional uncertainty

## System Shape

### Stage 5

Stage 5 is the substrate layer:
- messages
- extractions
- sessions
- summaries
- facts
- relationships

### Stage 6

Stage 6 is the reasoning layer:
- artifacts
- cases
- interpretation
- strategy
- drafts
- review
- clarification

### Bot and Web

Bot and web are the operator layer:
- bot for fast interaction and urgent work
- web for deep review and queue-oriented work

## Core Outputs

Stage 6 should produce:
- `dossier`
- `current_state`
- `strategy`
- `draft`
- `review`
- `clarification`
- `timeline`
- `cases`

## What a Case Is

A case is a unit of useful operator work.

Typical case examples:
- risk needs review
- clarification input needed
- dossier refresh needed
- draft candidate available
- current state needs refresh

Cases should carry:
- type
- priority
- confidence
- status
- reason
- linked artifact
- timestamps

## Main Product Flow

1. New Telegram messages arrive.
2. Stage 5 turns them into structured substrate.
3. Stage 6:
- answers direct operator requests
- and/or surfaces useful cases
4. Bot and web surface outputs and cases.
5. The operator:
- accepts
- rejects
- resolves
- clarifies
- requests refresh
6. The system stores the result and improves future use.

## What Happens Automatically

Automatic:
- ingestion
- Stage 5 processing
- substrate maintenance
- freshness tracking
- case creation for strong signals

On-demand:
- dossier refresh
- strategy generation
- draft generation
- review generation
- deep timeline explanation

## Product Contracts

### Fact vs Interpretation

Important outputs should distinguish:
- observed facts
- likely interpretation
- uncertainty
- missing information

### Signal Strength

First practical signal scale:
- `strong`
- `medium`
- `weak`
- `contradictory`

### Relational Patterns

The system should surface:
- participant patterns
- pair dynamics
- repeated interaction modes
- changes over time

### Strategy Ethics

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
- short-term "winning" over long-term clarity

### Personal Style

The system should preserve the operator's style by avoiding:
- preachy heaviness
- service-tone drift
- anxious over-explaining
- emotional overfilling

It should preserve:
- depth
- clarity
- strength
- warmth when appropriate
- directness without unnecessary coldness

## First Practical Release

The first practical release should include:
- `dossier`
- `current_state`
- `draft`
- `review`
- `clarification`
- `timeline`
- minimal `case queue`

It should not require:
- graph-first reasoning dependency
- broad archive-wide materialization
- public-product polish
- full autonomous operation

## Operator Surface Split

### Bot

Bot should handle:
- `/state`
- `/draft`
- `/review`
- `/gaps`
- `/answer`
- `/timeline`
- urgent items
- quick decisions

### Web

Web should handle:
- dossier
- expanded timeline
- case queue
- artifact history
- deep review
- richer operator controls

## Daily Workflow

Expected daily flow:
1. check urgent and ready items
2. inspect current state
3. answer top clarification gaps
4. run draft or review when needed
5. use web for deep review, dossier, and timeline work

## Acceptance Criteria

Stage 6 should be considered meaningfully working when:
- dossier is useful instead of being a raw dump
- current state is stable and plausible
- draft is sendable without embarrassment
- review materially improves text
- clarification reduces uncertainty
- case queue stays useful and not noisy

## Success Metrics

The product is successful when:
- outputs are useful in daily work
- cases are actionable and low-noise
- bot/web reduce raw-manual reasoning burden
- latency and cost stay predictable
- silent regressions are visible early

## Current State

As of 2026-03-25:
- Stage 5 is stabilized and operational
- Stage 6 is ready for controlled use
- productization of Stage 6 remains the next major implementation phase

## Relation To Planning Docs

This PRD draft should be read together with:
- [FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md)
- [PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md)
