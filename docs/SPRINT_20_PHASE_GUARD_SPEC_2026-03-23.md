# Sprint 20 Phase Guard Spec (Prep Only)

## Goal

Define explicit per-chat phase ownership for backfill/slice/Stage5 to prevent mixed execution.

## Phase set

- `backfill_ingest`
- `slice_build`
- `stage5_process`
- `tail_reopen`

## Canonical transitions

Allowed:
- `backfill_ingest -> slice_build`
- `slice_build -> stage5_process`
- `stage5_process -> tail_reopen` (bounded only)
- `tail_reopen -> stage5_process` (bounded only)

Forbidden:
- `backfill_ingest -> stage5_process`
- `backfill_ingest -> tail_reopen`
- `slice_build -> backfill_ingest`
- any unknown source/target pair

## Deny matrix

- active `backfill_ingest`: deny `slice_build`, deny `stage5_process` start from other owners
- active `slice_build`: deny `stage5_process`
- active `stage5_process`: deny backfill start for same chat unless explicit maintenance lock and verified idle
- `tail_reopen`: requires bounded reopen window + operator reason + audit id

## Required denial payload

When transition is denied, return/log:
- `chat_id`
- `requested_phase`
- `current_phase`
- `deny_code`
- `deny_reason`
- `observed_at_utc`

## Integration points

- phase acquisition/release in backfill flow
- stage5 claim/dequeue pre-check
- scoped repair/recompute preflight gate

This spec is prep only and does not enable runtime enforcement yet.
