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
- current mode: `stdio` transport with read tools.

Status: closed as scaffold; remaining work is productization (`M` tasks).

---

## Active backlog

## P0 — Reliability and correctness (do first)

### P1. Remove auto-approve for tentative facts

**Why:** prevents silent quality degradation.

**Scope:**
- stop automatic enqueue of `approve` for tentative facts
- keep explicit manual approve/reject command flow

**Files:**
- `src/TgAssistant.Intelligence/Stage5/ExtractionApplier.cs`
- fact review command flow (worker + MCP/bot write path)

**Acceptance criteria:**
- tentative facts never become confirmed without explicit manual action.

### P2. Add Redis PEL reclaim + pending observability

**Why:** avoid stream deadlock after worker crash.

**Scope:**
- reclaim pending entries (`XPENDING` + `XAUTOCLAIM`/`XCLAIM`)
- startup + periodic reclaim
- pending count/age metrics/logs

**Files:**
- `src/TgAssistant.Infrastructure/Redis/RedisMessageQueue.cs`
- ingestion worker metrics/logging

**Acceptance criteria:**
- read-unacked messages are recovered after restart.

### P3. Persist expensive backoff per model

**Why:** isolate model failures and preserve restart behavior.

**Scope:**
- per-model keys in `analysis_state`
- load state at startup

**Acceptance criteria:**
- one model can be blocked while another still processes expensive pass.

### P4. Session-first bootstrap fallback for missing previous summary

**Why:** prevent session chain deadlocks and extraction loss.

**Scope:**
- remove hard stop on missing previous summary
- inject bootstrap prompt with boundary marker
- optional pre-dialog context block

**Files:**
- `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs`
- summary context/prompt builder

**Acceptance criteria:**
- downstream sessions continue processing even if previous summary is empty.

---

## P1 — Policy and platform completion

### P5. Enforce summary ownership policy (post-slice + manual trigger)

**Scope:**
- keep mandatory post-slice summary generation
- keep/add manual re-summary command
- prevent duplicate steady-state summary generation

**Files:**
- `AnalysisWorkerService`
- `DialogSummaryWorkerService`
- bot command handlers

### P6. MCP dual transport (`stdio` + `sse`)

**Scope:**
- env-selectable transport: `MCP_TRANSPORT=stdio|sse`
- same tool registry in both modes
- sse auth + localhost bind

**Files:**
- MCP project (`src/TgAssistant.Mcp` now; move to `mcp/` only if explicitly chosen)
- deploy/docker integration

### P7. Separate test/prod session cap behavior

**Scope:**
- no implicit production use of `TestModeMaxSessionsPerChat`
- explicit prod-safe limit flag if needed
- telemetry for skip/quarantine caused by limits

---

## P2 — Alignment and maintainability

### P8. Documentation alignment pass

**Scope:**
- align `README.md`, `AGENTS.md`, this backlog
- document runtime keys/topology and summary ownership
- remove contradictory docs

### R1. Continue targeted AnalysisWorker refactor (incremental)

**Scope:**
- small behavior-preserving extractions only where it reduces risk
- keep orchestration readable and DI minimal

### O1. Observability expansion (after P0/P1)

**Scope:**
- OpenRouter broadcast trace enhancements
- coverage/alerts/runbook improvements

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
