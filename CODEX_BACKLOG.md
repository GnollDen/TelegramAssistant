# TelegramAssistant — Codex Backlog (clean)

## Scope
Operational backlog for current architecture state.
Only active, non-duplicate tasks are kept here.
Historical planning noise and outdated task descriptions were removed.

Last cleanup: 2026-03-17.

---

## Architecture baseline (locked on 2026-03-17)

1. Tentative facts use manual review gate only (no auto-approve).
2. MCP target is dual transport:
   - `stdio` for local/Codex
   - `SSE/HTTP` for remote clients (including Claude)
3. Session-first must not hard-block on missing previous summary.
   - Use bootstrap prompt for dialog start marker.
   - Allow optional pre-dialog context.
4. Redis ingestion SLA: at-least-once, with mandatory PEL reclaim.
5. `TestModeMaxSessionsPerChat` is test-only and must not affect production implicitly.
6. Summary policy:
   - generated after slice processing
   - manually triggerable from bot
   - no duplicate steady-state background re-generation

---

## Done / Closed (already implemented)

### D-01. Stage5 core decomposition is already in code

Implemented classes exist and are active:
- `src/TgAssistant.Intelligence/Stage5/ExtractionApplier.cs`
- `src/TgAssistant.Intelligence/Stage5/ExtractionRefiner.cs`
- `src/TgAssistant.Intelligence/Stage5/ExtractionValidator.cs`
- `src/TgAssistant.Intelligence/Stage5/ExpensivePassResolver.cs`
- `src/TgAssistant.Intelligence/Stage5/MessageContentBuilder.cs`

Status: closed as backlog item, continue only as targeted refactor (see `R` tasks below).

### D-02. Session-first + summary workers are already present

Implemented runtime components exist:
- `AnalysisWorkerService` (session-first orchestration)
- `DialogSummaryWorkerService` (summary pipeline)
- `DailyKnowledgeCrystallizationWorkerService` (cold-path finalization)

Status: closed as "build-from-zero" item; now managed as policy/hardening tasks (`P` tasks).

### D-03. MCP baseline server exists

Implemented MCP baseline exists:
- `src/TgAssistant.Mcp/index.ts`
- current mode: dual transport (`stdio` + `sse`), with `sse` default in compose.

Status: closed as scaffold; remaining work is productization (`P` tasks).

---

## Active backlog

## Closed in current cycle (2026-03-17)

- `P1` Remove auto-approve for tentative facts.
- `P2` Redis PEL reclaim + pending observability.
- `P3` Expensive backoff persistence per model.
- `P4` Session-first bootstrap fallback for missing previous summary.
- `P5` Summary ownership policy (post-slice + manual trigger).
- `P6` MCP dual transport (`stdio` + `sse`) + deploy integration.
- `P7` Separate test/prod session cap behavior.
- `P8` Documentation alignment pass.
- `O1` Observability expansion:
  - OpenRouter failure/cooldown trace logs enhanced.
  - Stage5 cheap-pass summary telemetry added.
  - Stage5 metrics delta logs added.
  - Runbooks added: `docs/runbooks/stage5-openrouter.md`, `docs/runbooks/stage5-redis-ingestion.md`.
- `R1` Targeted AnalysisWorker refactor:
  - incremental behavior-preserving extractions completed
  - session slicing stability fixes for uncapped mode completed
  - reviewer pass with no blocking findings

## Current active

- no active items in this file snapshot

---

## Sprint layout (non-blocking)

### S0 (0.5-1 day)
- lock architecture baseline
- assign file ownership per lane
- prepare branch + smoke policy

### S1 (P0 parallel)
- Lane A: `P1`
- Lane B: `P2`
- Lane C: `P3`
- Lane D: `P4`

Merge order: B -> C -> A -> D

### S2 (P1 parallel)
- Lane A: `P5`
- Lane B: `P6` (foundation: transport + parity read-tools)
- Lane C: `P7`

Merge order: C -> A -> B

### S3
- finish `P6` hardening (auth + deploy integration)
- execute `P8`

### S4+
- `R1`, `O1` in small non-blocking PRs

---

## Multi-agent orchestration rules

1. One branch per lane (`s1-lane-a-*`, ...), one owner per file-set.
2. Do not overlap write scopes inside the same sprint unless explicitly coordinated.
3. Required smoke checks per lane before merge:
   - C# touched: `dotnet build TelegramAssistant.sln`
   - MCP touched: MCP build command for active MCP project
4. Rebase open lanes after each merge in planned merge order.
5. Keep daily integration window for conflict resolution and end-to-end smoke run.
