# Sprint 19 Task Pack

## Name

Backfill / Realtime Coordination

## Goal

Implement a clean coordination model where historical catch-up runs before realtime listening, instead of competing with it.

## Why This Sprint

Operationally confirmed during backfill work:

- listener and backfill can conflict over the same `telegram.session`
- new monitored chats should not immediately enter normal realtime mode
- recovery after downtime should not mix historical catch-up and live ingestion chaotically

The product needs a coherent per-chat activation flow.

## Target Model

Per-chat lifecycle:

- `historical_required`
- `backfill_active`
- `handover_pending`
- `realtime_active`
- `degraded_backfill` for interrupted/partial catch-up

## Product Rules

- historical catch-up comes before realtime for a not-ready chat
- realtime listener must stay disabled for chats not yet ready
- a new monitored chat must enter through historical catch-up mode
- recovery after downtime must use the same coordination model
- no broad redesign of Stage 5 or Telegram ingestion beyond what is needed for clean coordination

## Read First

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [BACKFILL_PHASE_PLAN.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\BACKFILL_PHASE_PLAN.md)
4. [BACKFILL_REPAIR_TASK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\BACKFILL_REPAIR_TASK.md)
5. [BACKFILL_FOLLOWTHROUGH_CHECK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\BACKFILL_FOLLOWTHROUGH_CHECK.md)
6. [LAUNCH_READINESS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\LAUNCH_READINESS.md)

## In Scope

- per-chat readiness state model
- startup detection of historical catch-up need
- listener gating for not-ready chats
- safe handover from backfill to realtime
- handling of partial/interrupted backfill
- new monitored chat onboarding path
- downtime recovery path
- verification and smoke coverage

## Out Of Scope

- full ingestion redesign
- broad Stage 5 redesign
- new expensive analysis defaults
- unrelated product features

## Deliverables

- runtime coordination implementation
- clear per-chat state persistence/markers
- listener gating wired into startup/runtime flow
- handover criteria from backfill to realtime
- verification path for:
  - new monitored chat onboarding
  - recovery after downtime

## Verification Minimum

- build passes
- startup/runtime wiring passes
- no listener/backfill session conflict during catch-up mode
- a not-ready chat stays out of realtime until catch-up completion
- a ready chat can enter realtime after handover

## Final Report

Report strictly:

1. What changed
2. Which files changed
3. How the coordination model now works
4. What was verified
5. What limitations remain
