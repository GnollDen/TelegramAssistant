# Addendum: AI-Centric Context Loop Correction

Date: `2026-04-05`  
Status: active addendum  
Amends: `PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`, `OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md`

## 1. Problem Statement

Current implementation drifted from the original AI-centric intent toward heuristic-heavy projection and rule/state layering, especially around evidence ranking, review surfacing, and operator-facing interpretation.

This is a risk for conversation intelligence:

- semantic complexity moves into hand-coded rules
- context assembly quality degrades as cases diversify
- operator trust drops when outputs look rule-derived rather than interpretation-derived

Hand-assembling the "correct context" in code is not scalable for noisy, ambiguous, evolving conversations.

## 2. Design Principle

### Deterministic layer owns

- auth/session/scope admission and deny-safe checks
- persistence, audit trails, and replayability
- state transitions and truth-layer promotion contracts
- review/block contracts and operator action policy
- execution budgets, hard limits, and runtime control states

### AI-centric layer owns

- context sufficiency judgment
- bounded adaptive retrieval requests
- evidence ranking and semantic interpretation
- ambiguity/contradiction resolution
- claim formation and uncertainty articulation
- recommendation for operator review need

Rule: model output is never written directly to durable truth. It must pass deterministic normalization and gating.

## 3. Target Loop

1. Build small initial scoped context (`tracked_person_id`, `scope_key`, optional `scope_item_key`).
2. Model evaluates sufficiency (`sufficient | retrieve_more | need_operator_review`).
3. If needed, model requests bounded extra context using typed retrieval requests.
4. Application validates requests against scope/whitelist/budgets, returns only bounded data.
5. Model updates interpretation and emits structured result.
6. Deterministic layer normalizes/gates output and persists audit metadata.
7. Final structured output includes:
- `claims` (`fact|inference|hypothesis`)
- `evidence_support` (evidence refs used)
- `uncertainties` (what remains unresolved)
- `review_recommendation` (`review|no_review` + reason)

## 4. Boundaries / Safety

- Max model calls per loop: `2`
- Max retrieval rounds: `1`
- Max retrieval requests: `2`
- Input token budget: `<= 4000`
- Output token budget: `<= 800`
- Total token budget: `<= 4800`
- Cost budget per loop: `<= $0.20`
- Scope-local retrieval only (single tracked person, single scope key)
- No MCP dependency, no raw DB access, no cross-scope retrieval
- No unconstrained autonomous agent/tool recursion
- Full audit required: context manifest, retrieval requests/results, model/version, budget usage, normalization status, gate decision

Non-goals:

- full-system rewrite
- replacing deterministic control plane
- unbounded autonomous orchestration

## 5. Insertion Point

Primary insertion point: bounded AI-centric interpretation step before review surfacing in operator resolution projection flow.

Concrete boundary:

- keep Stage8 recompute, promotion gating, and runtime controls deterministic
- insert the AI context loop at projection-time where review/no-review recommendation and semantic interpretation are currently heuristic-layered
- deterministic layer remains final authority for surfaced status/actionability

## 6. First Implementation Slice

One bounded slice only: `ResolutionInterpretationLoopV1` on canonical seeded scope `chat:885574984`.

Scope:

- path: operator resolution projection for one tracked person
- inputs: existing bounded queue/detail/evidence projection reads
- outputs: structured claims/evidence/uncertainties/review recommendation for each resolution item

Acceptance checks:

- zero cross-scope retrieval admissions
- zero direct model-to-durable writes
- every surfaced claim has evidence refs or explicit uncertainty
- fallback to current deterministic projection on schema/budget/scope failure
- audit record present for every loop run

## 7. Migration Principle

- Existing heuristics are not deleted immediately.
- New semantic complexity must stop growing in hand-coded projection/review logic where model reasoning should own interpretation.
- Heuristics in semantic zones are treated as temporary scaffolding unless explicitly justified as permanent control-plane policy.
- Rollout is additive, feature-gated, deny-safe, and reversible.
