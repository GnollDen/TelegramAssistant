# Project Agent Rules

## Purpose

This document defines the working rules that agents must follow when planning or implementing changes in TelegramAssistant.

It is the compact operational layer above the PRDs:

- the system is AI-centric
- deterministic code remains the control plane
- timeline/cases/conflicts exist to build a temporal person model
- operator escalation is the last-mile path, not the first interpretation path

## Core Product Goal

The system must produce:

1. the richest practical history of a person
2. the best current approximation of that person's world
3. temporal and conditional knowledge, not flat timeless facts
4. explicit uncertainty where the system cannot yet resolve meaning

Target output is not a bag of facts. Target output is a dynamic person model:

- what was true before
- what is true now
- what changed
- what only holds under certain conditions
- what is still unresolved

## Architectural Position

The system is AI-centric by design.

This means:

- semantic interpretation belongs primarily to the model
- context acquisition should be model-directed and bounded
- handcrafted branching must not become the primary engine of understanding

Deterministic logic is still required, but only as the control plane:

- auth and scope boundaries
- persistence and audit
- execution budgets and safety limits
- normalization and durable write validation
- recompute and reintegration triggers

Agents must not move semantic interpretation back into growing hardcoded rule trees unless there is a narrow, temporary, clearly justified safety reason.

## Stage Contracts

### Stage 5

Build substrate:

- normalize raw inputs
- extract meaning from media
- build embeddings and search substrate

This stage prepares evidence. It does not finalize semantic cases.

### Pre-6

Run a coarse discovery pass:

- large-context AI pass
- minimal or disabled loopback
- form rough scopes, slices, episodes, coarse timelines, candidate anchors

This stage creates a rough map, not final truth.

### Stage 6

Enrich per slice:

- build profile and pair context
- tighten boundaries
- refine episodes and candidate identity/relationship context

This stage improves context quality for later reasoning.

### Stage 7

Form cases on enriched context:

- explicit claims
- contradictions
- uncertainty clusters
- unresolved ambiguity
- blocked interpretations

This is the main semantic loopback layer.

If critical missing knowledge blocks reliable processing, Stage 7 may stop and mark the case as blocked or unresolved. It must not invent unsupported closure.

### Stage 8

Crystallize and resolve cases:

- take already formed cases/conflicts
- try to resolve them using current context
- only then ask the operator if the system still cannot close the case
- after resolution, normalize and trigger targeted recompute of affected knowledge surfaces

Stage 8 is resolution and reintegration, not raw discovery.

## Knowledge Model Rules

Timeline objects, cases, and conflicts are not the final product. They are instruments for building a temporal person model.

Agents must reason in terms of person-state over time, not timeless booleans.

Facts should be modeled as far as practical with:

- subject
- fact_type
- value
- valid_from
- valid_to or open-ended
- confidence
- evidence_refs
- state_status
- supersedes or superseded_by when applicable

The system must support:

- event-driven state changes
- preference changes over time
- style and behavior drift
- conditional knowledge and exceptions

Examples of the intended model:

- a relative may be active in earlier dialogue and absent after a death event
- a food preference may flip after a specific point in time
- language style may drift from one phrase pattern to another
- a preference may hold only under conditions rather than always

## Three Object Families

Agents should keep these families separate:

### Knowledge objects

- profile states
- timeline states
- pair dynamics
- normalized facts
- temporal boundaries

### Case objects

- contradictions
- blocked interpretations
- unresolved ambiguities
- review candidates
- uncertainty clusters

### Resolution objects

- AI resolution sessions
- operator questions
- verdicts
- normalization proposals
- apply results

Do not collapse these categories into one mixed structure.

## AI-Centric Resolution Rules

The intended resolution order is:

1. the system discovers cases
2. AI tries to resolve them first
3. successful AI resolution enriches the system
4. only unresolved residue escalates to the operator

Operator review is therefore a last-mile arbitration layer.

Agents should prefer:

- AI internal resolution
- bounded adaptive retrieval
- bounded operator follow-up only when needed

Agents should avoid:

- immediate operator surfacing of raw uncertainty
- large handcrafted conflict-state machines
- confident operator copy without evidence-backed claims

## Loop and Session Rules

### Interpretation loops

Bounded AI loops are preferred over static preassembled context packets.

Allowed pattern:

1. start with small context
2. let the model judge context sufficiency
3. allow bounded extra retrieval
4. produce structured interpretation

Disallowed pattern:

- unbounded autonomous chat
- cross-scope retrieval
- direct model writes to durable truth

### Conflict resolution sessions

Conflict handling should evolve toward bounded AI sessions:

- one case enters a resolution session
- AI may ask bounded follow-up questions
- AI returns structured verdict plus normalization proposal
- deterministic layer validates and applies through the existing action path

The model may propose truth. It may not write truth directly.

## Operator Rules

The operator should see:

- what the system already tried
- what the system found
- what remains unresolved
- why operator input is needed now

The operator should not be the first semantic parser of raw evidence.

If evidence-backed claims are empty, agents must not publish confident operator-facing interpretation text. In such cases the system should show explicit insufficient-evidence or escalation-only messaging.

## Recompute Rules

After accepted resolution:

- affected knowledge surfaces must be recomputed
- recompute should be targeted to impacted profile, timeline, pair, evidence, and downstream case surfaces
- the system must not rely on stale pre-resolution interpretations

## Rules for New Work

When proposing or implementing new project work, agents must apply these checks:

### Check 1. Is this semantic or control-plane logic?

If semantic:

- prefer AI-centric interpretation
- avoid hardcoding unless it is bounded temporary scaffolding

If control-plane:

- deterministic implementation is correct

### Check 2. Does this add rule explosion?

Reject or challenge changes that:

- add many branch-specific semantic heuristics
- encode too many edge-case interpretations in code
- attempt to preassemble the perfect context by hand

### Check 3. Does this preserve boundedness?

Every AI-centric feature must declare:

- scope limits
- retrieval limits
- turn limits
- audit trail
- deterministic fallback

### Check 4. Does this improve the temporal person model?

Prefer work that improves:

- state over time
- change detection
- conditional behavior understanding
- current world approximation

Prefer less work on surfaces that only rearrange UI without improving actual person understanding.

## Migration Rule

Existing heuristic logic is temporary scaffolding unless proven necessary as a durable control-plane rule.

Agents must not expand heuristic semantic logic by default.

When a new semantic need appears, the preferred order is:

1. bounded AI interpretation or retrieval improvement
2. bounded session or loop refinement
3. deterministic guard only if needed for safety or publication honesty

## Task Formulation Rule

When agents create tasks or propose slices, they should frame work in this order:

1. what person/world understanding gap exists
2. what stage owns it
3. whether AI or deterministic layer should own the fix
4. what bounded slice proves the change
5. what artifact proves success

Task wording should avoid vague goals like "improve logic" or "handle more cases".

Preferred wording describes:

- exact scope
- exact stage
- exact object family
- exact bounded proof
- exact safety boundary

## Final Principle

Model compute is cheaper than the long-term complexity of incorrect handcrafted semantic logic.

TelegramAssistant should spend tokens on understanding people, changes, conditions, and conflicts.
It should spend deterministic code on boundaries, safety, normalization, and durable correctness.
