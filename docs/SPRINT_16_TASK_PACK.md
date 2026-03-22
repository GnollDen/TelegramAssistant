# Sprint 16 Task Pack

## Name

Budget Guardrails + Eval Harness

## Goal

Add the first usable control layer for:

- spend protection
- budget-aware degradation
- evaluation runs
- regression visibility

This sprint should make the system safer to operate and ready for controlled experimentation.

## Why This Sprint

The product now has:

- ingestion
- reasoning
- strategy
- drafts/review
- bot/web surfaces
- outcome linkage

The next critical step is operational control.

Without it:

- runaway cost remains a real risk
- retry storms can waste money
- A/B testing lacks a stable measurement harness

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_16_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_16_TASK_PACK.md)
4. [SPRINT_16_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_16_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- Stage 5 usage logging
- Stage 5 hardening work
- analysis usage repository
- smoke infrastructure
- current outcome layer

## Scope

In scope:

- budget guardrails
- stage-level spend policies
- quota/billing error handling
- pause/degrade behavior
- basic eval run harness
- regression result recording
- verification paths

Out of scope:

- fully automated experimentation platform
- advanced dashboards
- GraphRAG

## Product Rules To Respect

Budget control must support:

- soft limits
- hard limits
- stage-level policies
- modality-aware control

When budget is constrained:

- optional expensive paths should pause first
- ingestion should degrade gracefully where possible
- the system should not enter useless retry storms

Eval harness should support:

- repeatable runs
- stable comparisons
- compact result recording

It should be practical, not overbuilt.

## Required Deliverables

### 1. Budget Guardrail Layer

Implement services or policies for:

- daily budget
- import budget
- stage-level budget
- modality-aware budget control

At minimum, distinguish:

- text/analysis
- embeddings
- vision
- audio

### 2. Soft / Hard Limit Behavior

Support:

- soft limit: degrade optional paths
- hard limit: pause affected workers/paths

Examples of degradations:

- disable expensive adjudication
- disable optional summaries
- pause media/audio enrichment

### 3. Budget-Aware Error Handling

When provider errors indicate quota/billing/insufficient credits:

- do not keep retrying as if it were a transient network issue
- move to an explicit budget-blocked / quota-blocked state where appropriate

### 4. Operational Visibility

Expose enough visibility to answer:

- which paths are budget-limited
- what is paused
- why it is paused

This can be logs, lightweight read models, or both.

### 5. Eval Harness MVP

Implement a practical evaluation harness that can:

- run named smoke/eval scenarios
- record results
- compare pass/fail and key metrics across runs

This does not need to be a huge framework.

### 6. Regression Recording

Persist or at least reliably emit:

- eval run id
- scenario name
- pass/fail
- key metrics or summary

### 7. Verification Path

Add verification paths such as:

- `--budget-smoke`
- `--eval-smoke`

That prove:

- budget guardrails can trigger soft/hard behavior
- quota-like failure does not cause retry storm behavior
- eval run can be recorded and inspected

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. budget smoke success
4. eval smoke success
5. budget-limited behavior is visible
6. eval result recording works

## Definition of Done

Sprint 16 is complete only if:

1. the system is materially safer against runaway spend
2. budget pressure causes controlled degradation, not chaos
3. evaluation runs can be repeated and compared
4. the product is ready for real A/B and cost-aware iteration

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how budget guardrails now work
4. how eval harness now works
5. what verification was run
6. remaining limitations before Sprint 17
