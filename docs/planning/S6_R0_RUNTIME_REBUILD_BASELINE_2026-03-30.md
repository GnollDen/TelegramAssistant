# S6-R0 Runtime and Rebuild Baseline Note

> Status note (2026-03-31): this remains the rebuild/reset preparation baseline.
> For normal accepted operation baseline, use `N1_READINESS_BASELINE_2026-03-31.md`.

## Date

2026-03-30

## Purpose

This note freezes the dev runtime/env baseline required before Stage 6 rebuild execution.

This is a documentation baseline artifact for `Pre-Sprint S6-R0`.
It is not rebuild execution.

## Scope Boundary

In scope:

- runtime topology for Stage 5 completion gate, Stage 6 rebuild, and post-rebuild verification
- required env/provider prerequisites
- Stage 6 reset/rebuild data boundary
- explicit sequencing before/after rebuild

Out of scope:

- Stage 5 or Stage 6 code changes
- running rebuild commands
- product expansion work

## Runtime Topology Baseline (Dev)

Runtime role combinations allowed by current host runtime parser include:

- `ingest`
- `ingest,ops`
- `stage5`
- `stage5,maintenance`
- `stage6`
- `web`
- `web,ops`
- `ingest,stage5,maintenance,ops`

For S6-R0 prep and later execution:

1. Stage 5 completion gate monitoring:
   - primary role: `stage5` or `ingest,stage5,maintenance,ops`
2. Stage 6 rebuild execution:
   - primary role: `stage6`
3. Operator verification:
   - bot checks: `stage6`
   - web checks: `web` or `web,ops`
   - MCP checks: MCP service (`src/TgAssistant.Mcp`) running and connected to PostgreSQL

Do not treat `web,ops` as a substitute for Stage 5 processing roles.

## Env and Provider Prerequisites

Before Stage 6 rebuild execution:

1. PostgreSQL reachable with write access for Stage 6 artifacts/cases.
2. Redis reachable for runtime coordination where applicable.
3. LLM provider credentials present and valid for configured runtime paths.
4. Runtime role explicit (`Runtime__Role` or `--runtime-role=...`), not implicit by old defaults.
5. Stage 5 mode sanity for post-rebuild live operation:
   - `Analysis__ArchiveOnlyMode=false`
   - `Analysis__ArchiveCutoffUtc` is empty or intentionally set so fresh messages are not filtered out.

## Stage 6 Reset/Rebuild Boundary

After Stage 5 full rerun completion, pre-existing Stage 6 materialized outputs are not trustworthy if mixed with new substrate.

Must be treated as rebuild scope:

- persisted Stage 6 artifacts
- Stage 6 cases derived from old Stage 5 substrate
- Stage 6 read models that materially depend on old artifact/case state

Must be preserved:

- raw messages and core Stage 5 substrate produced by completed rerun
- infrastructure/config state not tied to obsolete Stage 6 materialization

## Sequencing Baseline

1. Confirm Stage 5 completion gate evidence.
2. Complete must-fix remediation implementation (`S6-R1` to `S6-R4`) or explicitly waive.
3. Execute Stage 6 reset/rebuild (`S6-R5`).
4. Run post-rebuild verification checklist (bot/web/MCP/runtime/archive-mode).
5. Start post-rebuild expansion only after verification (`S6-R6`).

## Operator Verification Baseline

Verification checklist is in:

- [stage6-rebuild-verification.md](../runbooks/stage6-rebuild-verification.md)

Minimum proof categories after rebuild:

- runtime/health
- Stage 6 artifacts and cases readable
- bot flows (`/state`, `/timeline`, `/profile` when available)
- web scope and case/artifact reads
- MCP Stage 6 read coverage
- archive-mode sanity (`ArchiveOnlyMode` and cutoff checks)

## Historical/Supporting Notes

The following Stage 5 rerun docs are supporting operational history and must not override S6-R0 authority docs:

- `docs/STAGE5_PROD_RERUN_PREP_TASK.md`
- `docs/STAGE5_STRICT_RERUN_EXECUTION_TASK.md`
- `docs/STAGE5_BASELINE_COMMAND_PACK.md`
