# Sprint 19 Verification

## Purpose

Verify that Sprint 19 safely introduced coordinated `backfill -> handover -> realtime` behavior.

## Acceptance Checklist

## Coordination

- per-chat readiness state exists and is used
- not-ready chats do not start realtime listening
- backfill mode can run without listener conflict
- handover to realtime is explicit and gated

## Scenarios

- new monitored chat onboarding works through historical catch-up first
- recovery after downtime uses catch-up before normal realtime
- interrupted catch-up lands in a recoverable degraded state

## Runtime Safety

- no silent mix of backfill and realtime for the same not-ready chat
- no crash-loop over shared Telegram session state
- current ready chats are not regressed

## Verification

- build passes
- startup passes
- runtime wiring passes
- scenario verification passes for onboarding and recovery

## Hold Conditions

Hold Sprint 19 if any of these are true:

- a new monitored chat can enter realtime before historical catch-up is complete
- backfill and listener still compete for the same session/runtime path
- handover criteria are unclear, implicit, or not persisted
- recovery after interruption is ambiguous or unsafe

## Pass Condition

Sprint 19 passes if:

- the product now enforces a clean and recoverable `historical catch-up first, realtime second` model per chat
