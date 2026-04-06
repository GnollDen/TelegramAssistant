# AI-Centric Requirements Supplement

Date: `2026-04-06`
Status: `supplemental requirements for follow-up task-pack and orchestrator prompt generation`

## Purpose

This document supplements the current bounded implementation pack.

It exists because the current `DTP-001..015` pack is useful as a bounded hardening and operator-surface stabilization phase, but it does not fully cover the intended AI-centric product model.

This supplement defines the additional requirements needed to reach the intended system shape:

- temporal person understanding
- iterative enrichment across passes
- AI-first conflict resolution
- current-world approximation
- conditional and revisable knowledge

This document is not a rewrite of the current task pack. It is the requirements layer used to generate the next prompt and the next implementation task pack.

## Current Assessment

The current detailed task pack is valid as:

- AI-centric infrastructure hardening
- bounded loop control and audit
- offline-evidence admission
- operator-surface stabilization
- web home/dashboard closure

The current detailed task pack is not sufficient as:

- a full AI-centric person-model roadmap
- a complete iterative case-resolution roadmap
- a complete temporal world-model roadmap

Therefore:

- keep the current `DTP-001..015` pack as `Phase A`
- use this supplement to define `Phase B`

## Product Output Requirement

The target output of the system is not a flat fact store.

The target output is:

1. a rich history of the person
2. the best current approximation of the person's world
3. temporal and conditional person knowledge
4. explicit unresolved uncertainty where closure is not yet justified

The system should be able to represent outputs like:

- a relative was active in earlier dialogue and absent after a death event
- a preference changed after a specific point in time
- a linguistic style shifted over time
- a preference is conditionally true rather than globally true

Example target knowledge shape:

- person usually does not eat pineapple
- exception: with chicken
- additional condition: on Thursdays
- valid in the current period unless superseded
- supported by bounded evidence refs
- confidence and uncertainty remain explicit

## Core Architectural Requirement

The project remains AI-centric.

This means:

- semantic interpretation belongs to the model first
- context sufficiency judgment belongs to the model first
- bounded retrieval should be model-directed
- conflict and case resolution should be model-led before operator escalation

Deterministic code remains mandatory, but only for:

- auth
- scope
- audit
- persistence
- budget enforcement
- normalization
- durable write validation
- recompute and reintegration

The system must not continue drifting toward handcrafted semantic branching as the primary understanding layer.

## Stage Intent Requirements

### Stage 5

Must produce substrate:

- normalized message/media meaning
- search and embedding substrate
- base evidence references

### Pre-6

Must produce rough discovery:

- rough scopes
- slices
- episodes
- coarse timelines
- coarse person and relation anchors

Loopback should be minimal or off here.

### Stage 6

Must enrich context:

- profile enrichment
- pair enrichment
- boundary refinement
- candidate state refinement

### Stage 7

Must form cases:

- explicit claims
- contradictions
- ambiguity clusters
- blocked interpretations
- unresolved questions

Stage 7 may stop on critical missing knowledge instead of inventing closure.

### Stage 8

Must crystallize and resolve cases:

- attempt AI-first case closure with the current context
- produce operator questions only for unresolved residue
- reintegrate accepted resolution outcomes into the knowledge model
- trigger targeted recompute of affected surfaces

## Knowledge Modeling Requirements

The system must support temporal person-state modeling.

Knowledge must be representable as bounded state over time, not only timeless facts.

Required conceptual fields:

- subject
- fact_type
- value
- valid_from
- valid_to or open-ended
- confidence
- evidence_refs
- state_status
- supersedes or superseded_by when applicable

Required knowledge categories:

1. stable traits
2. temporal traits
3. event-conditioned states
4. uncertain or contested states

Required behavior:

- old truth remains valid for old timeline windows
- current truth may differ from historical truth
- the system must not flatten state transitions into one eternal value

## Case and Resolution Requirements

The system must keep separate:

### Knowledge objects

- profile states
- timeline states
- pair states
- world-state approximations

### Case objects

- contradictions
- unresolved ambiguities
- blocked interpretations
- operator-review candidates

### Resolution objects

- AI session
- follow-up question
- operator answer
- verdict
- normalization proposal
- apply result

This separation is mandatory for task design and implementation boundaries.

## Iterative Processing Requirement

The system must follow iterative AI enrichment rather than one-shot closure.

Required cycle:

1. discovery pass finds cases and uncertainties
2. AI tries to resolve them internally
3. successful resolution enriches system knowledge
4. later passes operate on richer context
5. unresolved residue carries forward
6. only the hard residue escalates to operator

Required carry-forward statuses:

- open
- resolving_ai
- resolved_by_ai
- needs_more_context
- needs_operator
- deferred_to_next_pass
- superseded

The system must avoid unstable case churn across passes.

## AI-First Resolution Requirement

Conflict resolution must move toward bounded AI sessions.

Required pattern:

1. a case enters an AI resolution session
2. AI receives bounded initial context
3. AI may request bounded additional context
4. AI may ask bounded operator follow-up only when needed
5. AI returns a structured verdict
6. deterministic normalization validates and applies the proposal

Operator escalation is a last-mile arbitration path, not the first semantic path.

## Current World Approximation Requirement

The system must produce a current-world approximation for the person, not only person-internal facts.

This includes:

- active people around the subject now
- inactive or dropped-out people
- current relationship states
- currently active conditions and circumstances
- recent major events that changed the world model

This world approximation must be explicitly temporal and revisable.

## Conditional Knowledge Requirement

The system must support knowledge that is true only under conditions.

Required ability:

- represent exceptions
- represent situational preferences
- represent conditional habits and speech patterns
- distinguish normal rule from exception rule

The system must not force conditional knowledge into flat yes/no values when the evidence supports a conditional pattern instead.

## Publication Honesty Requirement

Operator-facing interpretation must remain honest.

The system must not publish strong interpretation text when:

- evidence-backed claims are empty
- evidence refs are empty
- the system only has escalation-level confidence

In such cases the output must explicitly say:

- insufficient evidence
- escalation-only
- manual review required

This rule applies even when AI loops, sessions, or summaries are otherwise present.

## Phase B Requirement Areas

The next task pack must add explicit workstreams for the following areas.

### B1. Temporal Person-State Model

Need:

- bounded model shape for temporal person-state
- state transitions
- supersession rules
- temporal validity fields
- event-conditioned updates

### B2. Iterative Pass Reintegration

Need:

- carry-forward case identity
- unresolved case persistence across passes
- resolved-case reintegration into later passes
- targeted recompute after accepted resolution

### B3. AI Conflict Resolution Session Execution

Need:

- real bounded AI session execution beyond design-only status
- one-case session lifecycle
- operator follow-up budget
- structured verdict contract
- deterministic normalization handoff

### B4. Current World Approximation

Need:

- current active-world surface
- state of surrounding people/relations
- temporal current-world read model

### B5. Conditional Preference And Behavior Modeling

Need:

- preference exceptions
- conditional behavior rules
- style drift and phase markers
- bounded evidence-backed representation

### B6. Stage 6/7/8 Semantic Contracts

Need:

- explicit ownership boundaries between enrichment, case formation, and case resolution
- avoid Stage 7 and Stage 8 blending into one heuristic blob

## Prompt-Generation Requirements

When generating the next orchestrator or execution prompt from this supplement, the prompt must:

1. explicitly treat the current `DTP-001..015` pack as `Phase A`
2. explicitly define the next work as `Phase B`
3. avoid reopening already-hardened bounded guardrail work unless a new blocker proves it necessary
4. require sequential orchestration
5. require artifact-backed outputs
6. require task wording suitable for weak executors

The next prompt must not ask for vague "improve AI logic" work.

It must ask for:

- concrete requirement decomposition
- explicit new tracks
- bounded proof conditions
- deterministic boundaries

## Task-Pack Completion Criteria

The next task pack is only complete if it contains bounded tasks for:

- temporal person-state modeling
- iterative pass reintegration
- AI conflict resolution session execution
- current world approximation
- conditional preference and behavior modeling
- Stage 6/7/8 semantic contract clarification

and if each task includes:

- exact purpose
- exact scope
- files or areas
- step-by-step instructions
- verification
- acceptance criteria
- dependencies
- risks
- do-not-do

## Final Rule

The next task pack must move the project from:

- bounded AI loop hardening

to:

- bounded AI-centric person/world modeling and case-resolution execution

without abandoning the deterministic control plane.
