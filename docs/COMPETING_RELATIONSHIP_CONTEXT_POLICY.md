# Competing Relationship Context Policy

## Purpose

Define how competing-relationship context is imported and interpreted without silently overriding primary case reasoning.

This policy is a foundation for later sprint integration.

## Scope

This policy applies to:

- external competing-context signals
- interpretation into graph/timeline/state/strategy side-effects
- review and provenance safeguards

This policy does not authorize direct runtime override of existing Stage 5/Stage 6 outputs.

## Compatibility With Existing Policies

- `case_id` remains the top-level scope key per `CASE_ID_POLICY.md`.
- canonical source anchors remain required (`chat_id`, `source_message_id`, `source_session_id`, or equivalent source ids).
- facts, hypotheses, overrides, and review history remain separated.

## Authoritativeness Rule

Competing-context input is always treated as:

- non-authoritative
- reviewable
- additive-only until explicitly approved by higher-trust review flow

It must not be treated as a hard truth layer by default.

## Interpretation Contract

Interpreter outputs must explicitly include:

- `is_authoritative=false`
- `requires_explicit_review=true`
- `blocked_override_attempts` list when restricted actions are requested
- provenance references for each produced hint

## Guarded Influence Rules

### 1. Graph

Allowed:

- additive hint edges
- confidence-limited edge weighting
- hypothesis-marked influence suggestions

Forbidden without explicit approval:

- deleting canonical edges
- replacing existing high-confidence edge semantics
- changing focal pair identity

### 2. Timeline

Allowed:

- context annotations for periods/events
- uncertainty flags for ambiguous transitions

Forbidden without explicit approval:

- boundary rewrites
- period merges/splits auto-applied from competing-context alone
- backfilling historical certainties as if confirmed

### 3. Current State Modifiers

Allowed:

- bounded increase of contextual pressure
- bounded increase of ambiguity
- confidence cap recommendation

Forbidden without explicit approval:

- direct status replacement
- direct dynamics replacement
- forced confidence uplift from competing-context only

### 4. Strategy Constraints

Allowed:

- safety constraints
- pacing constraints
- escalation guards under competing pressure

Forbidden without explicit approval:

- forcing one strategy option as authoritative
- suppressing safer alternatives silently

## Explicit Non-Authoritative Behavior

When competing-context appears to conflict with existing state/timeline interpretation:

- preserve current authoritative outputs
- emit explicit review alert
- attach a competing interpretation candidate
- keep both readings visible where ambiguity is genuine

## No-Silent-Override Safeguards

The system must surface explicit alerts for attempted operations such as:

- `status_override`
- `period_rewrite`
- `edge_delete`
- `strategy_force_primary`

Each alert must include:

- source record id
- attempted operation
- reason blocked
- required review path

## Application Policy Matrix

### Auto-apply (non-authoritative candidates only)

Allowed for automatic persistence/visibility as candidate artifacts:

- additive graph hints with confidence cap
- timeline annotation candidates with explicit review flag
- advisory strategy caution signals

Auto-apply here does not mean canonical truth replacement.

### Review-only before canonical effect

Must require explicit review before affecting canonical snapshots:

- current-state confidence cap modifiers
- strategy constraints that narrow action space
- medium/high severity timeline competing annotations
- competing artifacts linked to high influence actors

### Always additive / non-authoritative

The following are always non-authoritative by default:

- competing graph hints
- competing timeline annotations
- competing state modifiers
- competing strategy constraints

These artifacts can guide interpretation but cannot replace canonical fact/state layers without explicit approval flow.

### Always blocked override attempts

These operations remain blocked unless a dedicated authoritative override path is explicitly introduced in a future policy revision:

- `status_override`
- `period_rewrite`
- `edge_delete`
- `strategy_force_primary`

## Operational Expectations For Future Integration

Later runtime integration should include:

- explicit review gate before applying competing-context impact to canonical domain snapshots
- operator-visible diff between baseline and competing-context-assisted interpretation
- rollback mechanism for approved competing-context effects
