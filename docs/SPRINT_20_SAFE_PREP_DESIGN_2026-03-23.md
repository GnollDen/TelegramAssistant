# Sprint 20 Safe Prep Design (No Rollout)

## Scope

Design-only + safe prep for:
- phase guards
- backup guardrail
- integrity preflight

This document intentionally does not enable runtime guards in the active Stage 5 path.

## Phase Guard Model

Per-chat phase ownership state:
- `backfill_ingest`
- `slice_build`
- `stage5_process`
- `tail_reopen` (restricted window only)

Guard matrix:
- `backfill_ingest` active -> deny `slice_build`, deny `stage5_process`
- `slice_build` active -> deny `stage5_process`
- `tail_reopen` only for configured bounded window and explicit operator reason

State transition requirements:
- explicit `from_phase -> to_phase`
- monotonic timestamps
- operator-visible denial reason
- deny-by-default for unknown phase combinations

## Backup Guardrail Design

Risky operations (repair/recompute/backfill reseed) must pass backup preflight:
- require fresh backup metadata (`backup_id`, `created_at_utc`, `scope`, `artifact_uri`, `checksum`)
- freshness policy configurable (example: <= 6h)
- explicit override requires operator identity + reason + approval token

Fail-closed behavior:
- if metadata missing/stale/invalid, operation is blocked
- block reason logged and emitted to operator-facing channel

## Integrity Preflight Design

Preflight checks before risky run:
- duplicate interval overlap by `chat_id`
- hole detection in expected sequence span
- mixed-source conflict check for target scope
- planned write volume sanity threshold

Output contract:
- `clean`, `warning`, `unsafe`
- machine-readable summary + human-readable explanation
- unsafe result blocks destructive path by default

## Safe Insertion Points (Current Runtime)

Identified insertion points for future guarded rollout:
- backfill start gate near `HistoryBackfillService` scope acquisition
- slice/session preparation gate before Stage 5 eligibility handoff
- Stage 5 worker gate at dequeue/claim boundary in analysis path
- repair/recompute command path preflight in host command handlers

Current status:
- insertion points documented only
- active behavior unchanged until Stage 5 tail completion + explicit rollout window

## Rollout Policy

Do not enable runtime-affecting guards until:
1. active Stage 5 tail is fully completed
2. fresh full backup is confirmed
3. dry-run preflight output is reviewed
4. rollback path is verified

Recommended post-tail order:
1. backup guardrail
2. phase guards
3. integrity preflight hard-block mode

## Verification Plan (Post-tail)

- backfill-active chat cannot enter slicing/Stage 5
- slicing-active chat cannot enter Stage 5
- risky command fails closed without fresh backup metadata
- override path is explicit and auditable
- integrity preflight blocks unsafe scopes
