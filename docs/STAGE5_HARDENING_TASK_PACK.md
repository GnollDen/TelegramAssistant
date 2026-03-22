# Stage 5 Hardening Task Pack

## Name

Stage 5 Hardening and Cost Control

## Goal

Tighten Stage 5 so it stays a reliable ingestion substrate with lower hidden cost and fewer ambiguous runtime behaviors.

This sprint is not a redesign of extraction quality.
It is a hardening pass.

## Why This Sprint

Stage 5 is already usable, but several things should be cleaned up before further scaling:

- expensive pass should no longer behave like a half-active default path
- embedding model usage should respect config consistently
- summary generation cost should be made more explicit and controllable
- edit-diff analysis should be clearly gated as optional

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [STAGE5_HARDENING_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\STAGE5_HARDENING_TASK_PACK.md)

Also inspect the real server-side code in:

- `repo_inspect/src/TgAssistant.Intelligence/Stage5/*`
- `repo_inspect/src/TgAssistant.Processing/Media/*`
- `repo_inspect/src/TgAssistant.Core/Configuration/Settings.cs`
- `repo_inspect/src/TgAssistant.Host/appsettings.json`

## Scope

In scope:

- expensive-pass policy hardening
- embedding model config cleanup
- summary cost-control behavior cleanup
- edit-diff gating review
- Stage 5 verification and observability improvements

Out of scope:

- rewriting cheap extraction prompt from scratch
- changing the primary cheap model choice
- full media/audio redesign
- Stage 6 reasoning changes

## Product Rules To Respect

Stage 5 should remain:

- cheap-first
- reliable
- predictable

Expensive pass should be:

- exceptional
- opt-in by config/policy
- not a hidden default cost center

Summary generation should be:

- explicit
- controllable
- not silently duplicated across multiple paths without need

Config should remain the source of truth for model routing.

## Required Deliverables

### 1. Expensive Pass Hardening

Make expensive pass behavior explicit and safe.

Requirements:

- if expensive pass is effectively disabled by policy, Stage 5 should not keep acting as if it is a normal default lane
- config/default behavior should make this obvious
- logs should clearly show whether expensive pass is active or inactive

It is acceptable to keep the code path for future use, but it should be operationally dormant by default.

### 2. Embedding Config Consistency

Remove hardcoded embedding model assumptions where config should decide the model.

At minimum:

- summary historical retrieval should not hardcode `text-embedding-3-small` if `EmbeddingSettings` already exists
- Stage 5 and embedding usage should align with configured model routing

### 3. Summary Cost-Control Cleanup

Clarify and tighten summary behavior.

Requirements:

- identify where summary is generated inside the main Stage 5 worker
- make the behavior explicit through config and logs
- avoid ambiguous "worker disabled but summary still happens" confusion

Do not remove useful summaries if downstream depends on them.
Make the cost behavior clearer and safer.

### 4. Edit-Diff Gating Review

Review whether edit-diff analysis should remain active by default.

Requirements:

- ensure it is clearly optional
- ensure logs/config make that obvious
- do not let it behave like a silent token sink

### 5. Stage 5 Operational Visibility

Improve observability enough to answer:

- which Stage 5 subpaths are active
- which models are currently used by which subpaths
- whether expensive pass is effectively running
- whether summary generation is happening in the main worker and/or worker service

This can be done through startup logs or lightweight diagnostics.

### 6. Verification Path

Add a verification path such as:

- `--stage5-smoke`

That proves:

- Stage 5 config resolves coherently
- expensive pass policy state is visible
- embedding model is taken from config where expected
- summary behavior is coherent

## Verification Required

Codex must verify:

1. build success
2. startup/runtime wiring success
3. stage5 smoke success
4. expensive-pass state is explicit
5. embedding model routing is config-driven where expected
6. summary behavior is explicit and non-confusing

## Definition of Done

This sprint is complete only if:

1. Stage 5 is cheaper and more predictable operationally
2. there are no obvious hidden-cost ambiguities left in Stage 5 routing
3. Stage 5 is easier to reason about before future A/B and cost testing

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how Stage 5 behavior is now clearer/safer
4. what verification was run
5. remaining limitations
