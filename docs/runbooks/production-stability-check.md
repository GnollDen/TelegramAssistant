# Production Stability Check

Use this after deploy or when a risky runtime change must be validated in production.

## Baseline Boundary

Affected path:

- service: `app` container and its preserved Stage5/background workloads
- legacy diagnostics: retired Stage6/web/tgbot probes, only when explicitly invoked for investigation
- environment: production Docker Compose stack on the VPS
- dependency path: GitHub Actions deploy -> immutable GHCR image -> PostgreSQL migrations/state -> Redis stream and reclaim loop

## Core Checks

1. container/process status
2. application healthcheck
   - liveness (process viability)
   - readiness (critical dependency admission checks)
3. runtime wiring check
4. preserved baseline verification checks when relevant
5. pipeline liveliness in logs
6. integrity snapshot in PostgreSQL
7. Redis PEL aging/reclaim sample when ingest path is in scope
8. one bounded recovery drill when a new recovery path was changed or is newly relied on

## Typical Evidence

- `docker compose ps`
- app logs over a recent window
- `dotnet TgAssistant.Host.dll --liveness-check`
- `dotnet TgAssistant.Host.dll --readiness-check`
- `dotnet TgAssistant.Host.dll --healthcheck` (compatibility alias to readiness)
- `dotnet TgAssistant.Host.dll --runtime-wiring-check`
- preserved baseline verification checks
- legacy diagnostic entrypoints only when explicitly validating retired Stage6/web/tgbot paths
- Redis queue/group sanity
- Stage5/backfill/listener liveliness
- `schema_migrations` latest rows
- `analysis_state` watermark snapshot
- processed-without-apply / duplicate anomaly query output

## Repeatable Validation Sequence

1. Validate the normal path.
   ```bash
   docker compose ps
   docker compose exec -T app dotnet TgAssistant.Host.dll --liveness-check
   docker compose exec -T app dotnet TgAssistant.Host.dll --readiness-check
   docker compose exec -T app dotnet TgAssistant.Host.dll --runtime-wiring-check
   docker compose logs --since=15m app | rg -i "Application terminated unexpectedly|Readiness failed" || true
   docker compose logs --since=15m app | rg -i "Stage5 operational signals|Stage5 metrics snapshot" | tail -n 20 || true
   ```
2. Confirm the deployed schema edge is sane.
   ```bash
   docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select id, applied_at from schema_migrations order by id desc limit 10;"
   ```
   - Pass condition: expected latest migration ids are present, and there are no startup checksum mismatch errors in app logs.
3. Capture integrity evidence.
   ```bash
   docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as processed_without_apply from messages m left join message_extractions me on me.message_id = m.id where m.processing_status = 1 and me.id is null;"
   docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select chat_id, telegram_message_id, count(*) as duplicate_rows from messages group by chat_id, telegram_message_id having count(*) > 1 order by duplicate_rows desc, chat_id, telegram_message_id limit 20;"
   docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select key, value, updated_at from analysis_state where key like '%watermark%' order by key;"
   ```
   - Pass condition: `processed_without_apply = 0`, duplicate query returns `0` rows, watermark values are present for active paths and no recent `Blocked non-monotonic watermark update` log appears unless intentionally injected.
4. If realtime ingest or reclaim is in scope, validate the Redis edge twice 5 minutes apart.
   ```bash
   docker compose exec -T redis redis-cli XPENDING tg-messages batch-workers
   docker compose logs --since=15m app | rg -i "Redis stream pending status|Redis pending reclaim executed|Redis reclaimed messages delivered" || true
   ```
   - Pass condition: `XPENDING` is stable or decreasing, and reclaimed entries are delivered after restart/reclaim rather than accumulating on one dead consumer.
5. Run one bounded recovery drill when the change touched recovery logic.
   - Preferred drill for live production: use only synthetic smoke chats (`chat_id >= 9000000000000`) and the existing cleanup hook.
     ```bash
     docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select chat_id, session_index, last_message_at from chat_sessions where chat_id >= 9000000000000 and not is_analyzed and not is_finalized order by chat_id, session_index;"
     docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5_synthetic_smoke_cleanup.sql
     docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as remaining_synthetic_pending from chat_sessions where chat_id >= 9000000000000 and not is_analyzed and not is_finalized;"
     ```
   - Legacy diagnostic only for isolated maintenance windows: `docker compose exec -T app dotnet TgAssistant.Host.dll --stage6-execution-smoke`
   - Pass condition: the drill exercises a known fault/recovery path, recovery completes, and only the intended bounded scope changes.

## Acceptance Lens

Confirm:

- latest messages are being processed
- slicing/Stage5 paths are active or correctly idle
- no startup exception loops
- no obvious race symptoms
- no growing lag that indicates hidden stall
- no integrity anomalies above operator threshold:
  - `processed_without_apply > 0` is a `hold`
  - any duplicate message identity row is a `hold`
  - Redis `XPENDING` growth across two samples is at least a `healthy with warnings`, and a `hold` if paired with no reclaim progress

## Evidence Checklist

Record these artifacts in the change ticket or deploy log:

- deploy SHA / image tag
- `docker compose ps` output timestamp
- liveness, readiness, and runtime wiring results
- latest `schema_migrations` rows
- processed-without-apply query result
- duplicate anomaly query result
- watermark snapshot and recent watermark/reclaim log sample
- Redis `XPENDING` samples if ingest path was in scope
- recovery drill commands, bounded scope, and before/after counts

## Hold and Rollback

Declare `hold` and prepare rollback when any of these are true:

- readiness or runtime wiring fails
- app logs contain startup crash loops or checksum mismatch
- `processed_without_apply` is non-zero
- duplicate identity query returns rows
- Redis pending grows across repeated samples with no reclaim progress

Rollback notes:

- rollback should redeploy the previous known-good immutable image tag; do not edit or rename historical migration files
- if a recovery drill touched only synthetic smoke rows, rollback is usually not required; record the exact chat ids that were exercised
- if a stateful fault-injection smoke was run outside synthetic scope, verify cleanup or residual smoke rows before closing the incident

## Reporting

Final production status should be one of:

- stable and healthy
- healthy with warnings
- degraded
- hold
