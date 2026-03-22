# Sprint 18 Task Pack

## Name

Competing Relationship Context

## Goal

Integrate the competing relationship context capability into the live reasoning stack in a guarded, review-first way.

This sprint should activate the special-case runtime interpretation for high-impact external romantic context without allowing silent override of the primary relationship model.

## Why This Sprint

The product now has:

- external archive ingestion
- graph/network layer
- periodization
- current state
- strategy
- review and control layers

The next critical step is to let the system use competing romantic context in a disciplined way.

Without it:

- external archives stay generic
- high-impact competing context cannot yet constrain state and strategy properly

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_18_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_18_TASK_PACK.md)
4. [SPRINT_18_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_18_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)
6. [EXTERNAL_ARCHIVE_INGESTION_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\EXTERNAL_ARCHIVE_INGESTION_POLICY.md)
7. [COMPETING_RELATIONSHIP_CONTEXT_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\COMPETING_RELATIONSHIP_CONTEXT_POLICY.md)

Also inspect:

- Sprint 17 external archive ingestion outputs
- graph/network services
- current state engine
- strategy engine
- sidecar competing-context prototype and integration design

## Scope

In scope:

- runtime integration of competing-context interpretation
- guarded application to graph/timeline/state/strategy
- review-gated high-impact effects
- additive annotations and modifiers
- verification paths

Out of scope:

- free automatic override of primary interpretation
- GraphRAG
- broad external archive redesign

## Product Rules To Respect

Competing context must be:

- high-impact
- not auto-authoritative
- additive by default
- review-gated for high-impact conclusions

It may influence:

- graph hints and influence context
- timeline annotations
- ambiguity
- avoidance risk
- escalation readiness
- strategy constraints

It must not automatically:

- override current relationship status
- rewrite periods
- delete graph edges
- force a primary strategy option

Blocked attempts must remain visible as alerts.

## Required Deliverables

### 1. Runtime Invocation

Integrate the competing-context interpretation service into the live Stage 6 flow in a controlled order:

- baseline canonical context
- external archive artifacts
- competing-context interpretation
- guarded modifiers and reviewable outputs

### 2. Guarded Output Application

Apply competing-context outputs only as:

- additive graph hints
- timeline annotations
- bounded state modifiers
- strategy constraints

Keep:

- `IsAuthoritative = false`
- explicit review requirements
- confidence caps

### 3. Review Gate

High-impact effects must require review before they materially alter interpretation.

Blocked override attempts should persist as:

- review alerts
- audit events
- visible non-applied artifacts

### 4. State and Strategy Integration

Integrate competing-context effects into:

- current state modifiers
- strategy constraints

But only within bounded, policy-safe influence.

### 5. Visibility

Make the resulting competing-context effects inspectable in existing read/review surfaces where practical.

### 6. Verification Path

Add a verification path such as:

- `--competing-context-smoke`

That proves:

- competing context is read from ingested external archive artifacts
- additive graph/timeline/state/strategy effects are produced
- blocked override attempts are recorded
- review-required behavior is visible

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. competing-context smoke success
4. additive modifiers exist
5. blocked override attempts are recorded
6. review-required behavior is visible

## Definition of Done

Sprint 18 is complete only if:

1. competing relationship context now participates in the product safely
2. it constrains reasoning without silently overriding the primary model
3. the system is ready for later richer external-context use

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how competing-context runtime integration now works
4. what verification was run
5. remaining limitations after Sprint 18
