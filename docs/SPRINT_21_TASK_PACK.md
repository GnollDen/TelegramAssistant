# Sprint 21 Task Pack

## Name

Composition Root Decomposition

## Goal

Break the giant `Program.cs` composition root into modular service registration and role-aware startup boundaries.

## Why This Sprint

The current host wiring has grown too large and makes risky runtime changes harder than necessary.

This sprint reduces change risk before the container/workload split.

## Read First

1. [NEAR_TERM_BACKLOG_2026-03-23.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\NEAR_TERM_BACKLOG_2026-03-23.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [LAUNCH_READINESS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\LAUNCH_READINESS.md)

## In Scope

- modular DI/service registration methods
- separation by runtime responsibility:
  - ingest
  - Stage 5
  - Stage 6
  - web
  - MCP
  - ops/maintenance
- role-aware startup switches/registration
- reduced logic density in `Program.cs`

## Out Of Scope

- separate containers/workloads
- cross-process messaging redesign
- schema redesign

## Deliverables

- smaller `Program.cs`
- modular registration extensions
- role-aware startup model ready for later split

## Verification Minimum

- build passes
- runtime wiring passes
- healthcheck/startup still pass
- no existing role is silently dropped from registration

## Final Report

Report strictly:

1. What changed
2. Which files changed
3. How Program.cs was decomposed
4. How role-aware startup now works
5. What was verified
6. What limitations remain
