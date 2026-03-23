# Sprint 19 Runtime Repair

## Name

Backfill / Realtime Coordination Runtime Repair

## Goal

Fix the two production runtime defects found during the Sprint 19 reality check:

- concurrent/idempotency failure in coordination state initialization
- false degradation of active auto-recovery backfill into `degraded_backfill`

## Context

Sprint 19 coordination wiring is present and partially working:

- coordination states initialize
- listener gating works
- auto-catch-up starts

But reality check found two defects that keep Sprint 19 on hold.

## Read First

1. [S19_SPRINT.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\S19_SPRINT.md)
2. [S19_REPAIR.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\S19_REPAIR.md)
3. [S19_CHECK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\S19_CHECK.md)
4. [LAUNCH_READINESS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\LAUNCH_READINESS.md)

## Defect 1

### Coordination state init race

Observed behavior:

- `EnsureStatesAsync` can hit duplicate key on startup
- host can stop because of the exception
- container restarts

Required result:

- state initialization is idempotent and concurrency-safe
- startup does not crash on repeated or racing initialization

## Defect 2

### False degrade during auto-recovery

Observed behavior:

- active auto-recovery catch-up can be downgraded to `degraded_backfill`
- listener eligibility path appears to mark degradation using stale/manual-backfill assumptions

Required result:

- a real active auto-recovery backfill is not falsely marked degraded
- degradation only happens on real interruption/failure conditions

## In Scope

- minimal repair for the two runtime defects
- safe runtime verification after patch

## Out Of Scope

- broad redesign of coordination state machine
- new product behavior beyond fixing these defects
- unrelated Stage 5 changes

## Verification Minimum

- no duplicate-key startup failure during coordination state initialization
- no false degrade while auto-recovery backfill is genuinely active
- listener/backfill conflict still absent
- healthcheck and runtime wiring still pass

## Final Report

Report strictly:

1. What changed
2. Which files changed
3. How the state-init race was fixed
4. How false-degrade was fixed
5. What was verified
6. Whether Sprint 19 runtime hold is cleared
