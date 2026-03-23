# Sprint 20 Safe Prep - Insertion Points (No Rollout)

## Purpose

Translate Sprint 20 design into concrete insertion points in current code, without enabling runtime-affecting guardrails during active Stage 5 tail.

## Phase Guards: concrete insertion points

1. Backfill gate
- File: `src/TgAssistant.Processing/Archive/HistoryBackfillService.cs`
- Insert before per-chat backfill acquisition/start.
- Gate: deny `slice_build` and `stage5_process` while `backfill_ingest` is active for chat.

2. Listener eligibility/handover gate
- File: `src/TgAssistant.Telegram/Listener/TelegramListenerService.cs`
- File: `src/TgAssistant.Infrastructure/Database/ChatCoordinationService.cs`
- Insert at realtime-eligibility refresh and handover completion checks.
- Gate: enforce explicit transition `backfill_ingest -> handover_pending -> realtime_active` with denial reason when pending Stage 5 extractions exceed threshold.

3. Stage 5 dequeue/claim gate
- File: `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs`
- Insert at dequeue/claim boundary before processing session/message batch.
- Gate: deny processing when chat phase is `backfill_ingest` or `slice_build`.

4. Tail-reopen guard
- File: `src/TgAssistant.Infrastructure/Database/ChatCoordinationService.cs`
- Insert in phase transition writer path.
- Gate: allow `tail_reopen` only if reopen window is bounded and operator reason/identity are present.

## Backup Guardrail: concrete insertion points

1. Scoped repair command preflight
- File: `src/TgAssistant.Host/Stage5Repair/Stage5ScopedRepairCommand.cs`
- Insert before data-moving SQL path in dry-run/apply flow.
- Gate: require fresh backup metadata (`backup_id`, `created_at_utc`, `artifact_uri`, `checksum`, `scope`) or explicit approved override.

2. Future risky run entrypoints
- File: `src/TgAssistant.Processing/Archive/HistoryBackfillService.cs`
- File: `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs`
- Insert at explicit operator-triggered recompute/repair entrypoints when introduced.

## Integrity Preflight: concrete insertion points

1. Scoped repair analysis section
- File: `src/TgAssistant.Host/Stage5Repair/Stage5ScopedRepairCommand.cs`
- Insert after target-scope discovery and before apply branch.
- Checks:
  - duplicate/overlap by chat scope,
  - sequence holes in target span,
  - dual-source conflict summary,
  - write-volume sanity threshold.

2. Coordination safety checks
- File: `src/TgAssistant.Infrastructure/Database/ChatCoordinationService.cs`
- Insert as reusable repository/service API for `clean|warning|unsafe` preflight result.

## Data contract prep (design only)

Proposed preflight result contract:
- `result`: `clean | warning | unsafe`
- `scope`: operation scope metadata
- `checks[]`: `{ code, status, details }`
- `blocking_reasons[]`
- `generated_at_utc`

No active schema/runtime changes in this prep step.

## Rollout hold policy

Do not enable any of the above guard paths until all conditions are true:
1. current Stage 5 tail is completed,
2. fresh backup is confirmed,
3. dry-run preflight reviewed by operator,
4. rollback procedure tested.

## Acceptance mapping snapshot

- `Phase Guards`: insertion points identified, deny matrix defined, no rollout yet.
- `Backup Guardrail`: preflight gate placement identified, fail-closed policy defined.
- `Integrity Preflight`: check set and result contract defined, no destructive-path activation.
