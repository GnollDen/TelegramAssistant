# Sprint 20 Task Pack

## Name

Stage 5 Phase Guards And Backup Guardrail

## Goal

Prevent mixed processing phases and risky repair runs by enforcing per-chat phase ownership and pre-change backup guardrails.

## Why This Sprint

Recent incidents showed that the system currently allows too much implicit overlap between:

- backfill ingestion
- session slicing
- Stage 5 processing
- targeted repair/recompute

The system also lacks a formal backup gate before risky operations.

## Read First

1. [NEAR_TERM_BACKLOG_2026-03-23.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\NEAR_TERM_BACKLOG_2026-03-23.md)
2. [LAUNCH_READINESS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\LAUNCH_READINESS.md)
3. [STAGE5_PROGRESS_CHECK_TASK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\STAGE5_PROGRESS_CHECK_TASK.md)
4. [STAGE5_RUNTIME_OBSERVABILITY_TASK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\STAGE5_RUNTIME_OBSERVABILITY_TASK.md)

## Product Rules

- if `backfill_ingest` is active for a chat, `slice_build` and `stage5_process` must not run for that chat
- if `slice_build` is active for a chat, `stage5_process` must not run for that chat
- only a narrow tail window may be reopened after handover
- risky repair/recompute/backfill operations must require a fresh backup or an explicit approved override

## In Scope

- per-chat phase ownership model
- runtime phase guards for backfill vs slice vs Stage 5
- tail-only reopen policy
- backup preflight / guardrail
- integrity preflight checks before risky operations
- operator-visible logging/state for blocked phase transitions

## Out Of Scope

- full architecture split into separate workloads
- broad Stage 5 redesign
- unrelated Stage 6 product logic

## Deliverables

- phase-guard implementation
- backup guardrail implementation
- integrity preflight checks
- documentation/runbook updates

## Verification Minimum

- build passes
- startup/runtime wiring passes
- backfill-active chat does not enter slicing or Stage 5 processing
- slicing-active chat does not enter Stage 5 processing
- risky repair/backfill path fails closed without fresh backup metadata
- tail-only reopen is enforced

## Final Report

Report strictly:

1. What changed
2. Which files changed
3. How phase guards now work
4. How backup guardrail now works
5. What was verified
6. What limitations remain
