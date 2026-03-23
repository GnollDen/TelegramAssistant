# Sprint 19 Repair

## Name

Backfill / Realtime Coordination Repair

## Goal

Close the two acceptance gaps that kept Sprint 19 on hold:

- new monitored chat must not enter realtime before historical catch-up is established
- recovery after downtime must have an explicit and safe catch-up activation path

## Context

Sprint 19 implementation already introduced:

- per-chat coordination state
- listener gating
- handover semantics
- degraded_backfill state

But acceptance is still on hold because onboarding and recovery behavior are incomplete.

## Read First

1. [S19_SPRINT.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\S19_SPRINT.md)
2. [S19_CHECK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\S19_CHECK.md)
3. [S19_READY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\S19_READY.md)
4. [LAUNCH_READINESS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\LAUNCH_READINESS.md)

## Repair Targets

### 1. New Chat Onboarding

Required behavior:

- a newly monitored chat must not silently default to `realtime_active`
- if historical coverage is not established, it must enter the historical coordination path first
- any safe legacy fallback must be explicit, narrow, and justified

### 2. Recovery After Downtime

Required behavior:

- there must be an explicit catch-up activation path after downtime/restart
- affected chats must not rely only on manual env choreography to enter catch-up
- the system must have a clear rule or marker for when catch-up is required

## In Scope

- repair of onboarding policy
- repair of downtime recovery policy
- minimal runtime and state-machine changes needed to satisfy acceptance
- verification for these two scenarios

## Out Of Scope

- broad ingestion redesign
- new unrelated features
- destructive cleanup/rerun

## Verification Minimum

- build passes
- runtime wiring passes
- onboarding path no longer defaults an unready monitored chat to `realtime_active`
- downtime recovery path exists and is explicit in runtime behavior
- hold conditions from Sprint 19 are cleared

## Final Report

Report strictly:

1. What changed
2. Which files changed
3. How onboarding now works
4. How recovery after downtime now works
5. What was verified
6. Whether Sprint 19 hold conditions are cleared
