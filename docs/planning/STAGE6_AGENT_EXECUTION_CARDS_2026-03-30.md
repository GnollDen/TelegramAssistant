# Stage 6 Agent Execution Cards

## Date

2026-03-30

## Purpose

This document turns the verified Stage 6 backlog into agent-ready execution cards.

It exists to reduce agent overreach by making the following explicit:
- in-scope work
- out-of-scope work
- likely files
- expected deliverables
- concrete done criteria

Use this document together with:
- `STAGE6_VERIFIED_BACKLOG_2026-03-30.md`
- `STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md`

## Global Rule

Agents must not broaden the sprint goal.

If a task is not explicitly listed as in-scope, treat it as out-of-scope unless it is a narrow prerequisite required to make the in-scope change compile or run.

Agents must not:
- reopen Stage 5 work
- redesign Stage 6 architecture broadly
- invent new product scope
- mix pre-rebuild and post-rebuild tasks
- rewrite prompts or taxonomies outside the listed sprint scope

## Pre-Sprint S6-R0: Runtime and Rebuild Prep

### Goal

Produce an explicit and truthful Stage 6 rebuild baseline before implementation sprints start.

### Why this sprint exists

The team already knows Stage 5 needed multiple reruns and runtime fixes.
Stage 6 should not begin from hidden environment ambiguity or mixed artifact assumptions.

### In Scope

- identify the runtime topology required for:
  - Stage 5 completion monitoring
  - Stage 6 rebuild
  - bot/web verification after rebuild
- identify required env/provider prerequisites for Stage 6 rebuild
- document the Stage 6 reset/rebuild boundary:
  - what must be cleared
  - what must be preserved
  - what cannot be trusted after Stage 5 rebuild
- write a concrete Stage 6 rebuild runbook
- write a concrete verification checklist for:
  - rebuild success
  - bot checks
  - web checks
  - MCP checks
  - archive-mode sanity

### Out of Scope

- no Stage 6 code changes
- no Stage 6 rebuild execution
- no MCP implementation
- no bot command implementation
- no heuristic tuning
- no taxonomy changes

### Likely Files

- `docs/planning/STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md`
- `docs/planning/STAGE6_VERIFIED_BACKLOG_2026-03-30.md`
- `docs/planning/README.md`
- optionally one or more new notes in `docs/planning/`
- runtime/config references only as evidence:
  - `src/TgAssistant.Host/appsettings.json`
  - `docker-compose.yml`
  - Stage 6 worker/host registration files

### Expected Deliverables

1. Stage 6 rebuild runtime note
2. Stage 6 reset/rebuild boundary note
3. Stage 6 rebuild runbook
4. Stage 6 post-rebuild verification checklist
5. planning index update if a new doc is created

### Done Means

- another engineer can answer:
  - how to run Stage 6 rebuild
  - what to clear first
  - what not to clear
  - how to verify the rebuilt Stage 6
- no hidden runtime/env prerequisite remains implicit
- no implementation work was mixed into this sprint

### Forbidden Expansions

- “while here” code cleanup
- broad DevOps redesign
- changing runtime roles
- changing provider selection logic
- inventing new rebuild policies instead of documenting the existing required one

## Sprint S6-R1: MCP and Operator Read Surface

### Goal

Expose the minimum Stage 6 read surface required for agents and operators to inspect rebuilt Stage 6 output.

### Why this sprint exists

The verified audit found that:
- MCP only exposes Stage 5-oriented reads
- `/profile` is missing from bot commands
- Stage 6 engines exist, but are not fully inspectable through operator/agent surfaces

### In Scope

- add MCP read tools for:
  - current state
  - strategy
  - profiles
  - periods
  - profile signals
  - session summaries
- add bot `/profile` command
- reuse existing repositories/services where possible
- use existing persisted Stage 6 artifacts/snapshots when available
- trigger missing computations only when the current surface already does that elsewhere safely

### Out of Scope

- no Stage 6 semantic tuning
- no Stage 6 rebuild logic changes
- no bot prompt redesign
- no new web pages
- no strategy taxonomy changes
- no relationship-status redesign
- no LLM draft rewrite

### Likely Files

- `src/TgAssistant.Mcp/index.ts`
- `src/TgAssistant.Intelligence/Stage6/BotCommandService.cs`
- repository interfaces or read repositories only if required for narrow access
- possibly serialization/read-model helper files if a tiny helper is needed

### Expected Deliverables

1. MCP tools for:
  - `get_current_state`
  - `get_strategy`
  - `get_profiles`
  - `get_periods`
  - `get_profile_signals`
  - `get_session_summaries`
2. `/profile` command in bot
3. concise output shape for each new MCP tool
4. build verification
5. a short verification note showing each tool/command resolves data or explains absence safely

### Done Means

- operators can run `/state -> /timeline -> /profile`
- agents can inspect Stage 6 artifacts without DB spelunking
- new MCP tools return Stage 6-readable information, not raw table dumps only
- existing behavior outside the new tools/command is unchanged

### Forbidden Expansions

- adding unrelated MCP tools
- turning MCP into a broad query layer
- exposing write/mutate operations
- redesigning bot command architecture
- adding new Stage 6 artifact types
- refactoring repositories beyond what is strictly needed to read existing data

### Recommended Agent Split

Worker A:
- MCP tools only
- owns `src/TgAssistant.Mcp/index.ts`

Worker B:
- bot `/profile` only
- owns `src/TgAssistant.Intelligence/Stage6/BotCommandService.cs`

Shared rule:
- do not overlap write scopes unless a tiny shared helper is required

## Execution Rule For Agents

Before writing code, each agent should restate:
- exact in-scope deliverables
- exact files it owns
- what it will explicitly not change

If that restatement cannot stay short and concrete, the scope is still too broad and must be narrowed before implementation starts.
