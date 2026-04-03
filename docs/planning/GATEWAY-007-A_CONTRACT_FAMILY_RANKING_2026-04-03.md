# GATEWAY-007-A: Next Contract Family Ranking (Post EditDiff v1)

## Date

2026-04-03

## Scope

Bounded ranking of the next contract-family candidates after `EditDiff v1` for reasoning-plus-shaping rollout.

Ranking dimensions:

- safety (runtime blast radius, rollback complexity)
- schema clarity (strictness and ambiguity)
- app validation clarity (deterministic in-app validation surface)
- operational value (impact for quality/ops decisions)

## Candidate Ranking

1. `SessionSummary v1` (selected for next bounded rollout)
   - safety: high
   - schema clarity: high (`{"summary":"..."}` with strict shape and bounded length)
   - app validation clarity: high (single-field deterministic validation)
   - operational value: medium-high (summary quality affects downstream context quality and operator readability)
   - rationale: low coupling to core extraction write-path, clear fallback-to-existing-summary behavior, easy feature-gate boundary.
2. `DailyCrystallization Summary v1` (deferred)
   - safety: medium
   - schema clarity: medium-high
   - app validation clarity: medium
   - operational value: medium
   - rationale: useful, but lower near-term value than session summary and less exercised in bounded pilot path.
3. `Expensive Pass Resolution v1` (deferred)
   - safety: low-medium
   - schema clarity: medium
   - app validation clarity: medium
   - operational value: high
   - rationale: important, but touches expensive-retry/budget-sensitive path with higher regression risk.
4. `Cheap Extraction Batch v1` (explicitly excluded for near-term bounded rollout)
   - safety: low
   - schema clarity: medium
   - app validation clarity: medium-high
   - operational value: very high
   - rationale: highest operational value but also highest blast radius on main Stage5 ingestion/extraction surface; not suitable for immediate bounded post-EditDiff family.

## High-Risk Families Explicitly Excluded

- cheap extraction batch contracts (core ingestion path)
- broad multi-object durable-formation contracts (Stage7/Stage8 multi-family in one step)
- any family requiring provider-policy expansion beyond bounded shaping on OpenRouter

## 007-B Selection

`SessionSummary v1` is selected as the single next family for `GATEWAY-007-B` because it maximizes safety and validation clarity while still delivering meaningful operational value.
