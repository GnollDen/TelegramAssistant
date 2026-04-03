# Post Change Verification Runbook

Use this to decide minimum verification before commit and push.

## Current Baseline Reference

Current clean-slate baseline authority:

- `docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`
- `docs/planning/CLEANUP-001-B_RESET_BOUNDARY_NOTE_2026-04-02.md`
- `docs/planning/CLEANUP-005-A_RUNTIME_ROLE_AND_SMOKE_INVENTORY_2026-04-02.md`
- `docs/planning/README.md`
- `tasks.json`
- `task_slices.json`

Legacy Stage6 rebuild verification remains available only as legacy diagnostic context:

- `docs/runbooks/stage6-rebuild-verification.md` (legacy-only; use only for explicit rebuild/reset operations)

## Baseline Build Rules

Run these according to touched scope:

- any C# change: `dotnet build TelegramAssistant.sln`
- any TypeScript change in `src/TgAssistant.Mcp/`: `npm run build`
- any migration file change: expect `migration_guard` in `.github/workflows/deploy.yml` to pass; new files must append after current max prefix and keep `NNNN_slug.sql` naming

## Add Runtime Checks When Needed

Also run targeted checks when changes touch:

- `Program.cs`
- `Startup/*`
- hosted services
- configuration wiring
- compose/runtime environment
- database migrations
- coordination, listener, backfill, Stage5, Stage6Bootstrap/Stage7/Stage8, repair flows

Preserved baseline verification entrypoints:

- `dotnet run --project src/TgAssistant.Host -- --liveness-check`
- `dotnet run --project src/TgAssistant.Host -- --readiness-check`
- `dotnet run --project src/TgAssistant.Host -- --healthcheck` (compatibility alias to readiness)
- `dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=ingest --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage5 --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage5 --stage5-smoke`
- `dotnet run --project src/TgAssistant.Host -- --budget-smoke`
- `dotnet run --project src/TgAssistant.Host -- --launch-smoke`

## Integrity and Recovery Sequence (When Risky Paths Touched)

Use full sequence when changes touch deploy wiring, migrations, runtime startup, Redis reclaim, or recovery tooling.

1. Build and probe normal path.
   - `dotnet build TelegramAssistant.sln`
   - `dotnet run --project src/TgAssistant.Host -- --liveness-check`
   - `dotnet run --project src/TgAssistant.Host -- --readiness-check`
   - `dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check`
2. Verify one bounded verification path.
   - `dotnet run --project src/TgAssistant.Host -- --budget-smoke`
   - `dotnet run --project src/TgAssistant.Host -- --launch-smoke`
   - Pass condition: commands exit `0` and log preserved verification success messages.
3. Capture integrity evidence from PostgreSQL.
   - Processed-without-apply anomaly check:
     ```sql
     select count(*) as processed_without_apply
     from messages m
     left join message_extractions me on me.message_id = m.id
     where m.processing_status = 1
       and me.id is null;
     ```
   - Duplicate message identity check:
     ```sql
     select chat_id, telegram_message_id, count(*) as duplicate_rows
     from messages
     group by chat_id, telegram_message_id
     having count(*) > 1
     order by duplicate_rows desc, chat_id, telegram_message_id
     limit 50;
     ```
   - Watermark snapshot:
     ```sql
     select key, value, updated_at
     from analysis_state
     where key like '%watermark%'
     order by key;
     ```
4. If Redis intake/reclaim changed, sample dependency edge.
   - `docker compose exec -T redis redis-cli XPENDING tg-messages batch-workers`
   - `docker compose logs --since=15m app | rg -i "Redis stream pending status|Redis pending reclaim executed|Redis reclaimed messages delivered|Blocked non-monotonic watermark update" || true`
   - Pass condition: no sustained `XPENDING` growth across two samples 5 minutes apart.
5. If synthetic smoke backlog exists, verify one recovery path.
   - Scope restriction: only synthetic smoke chats (`chat_id >= 9000000000000`).
   - Recovery hook:
     ```bash
     docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5_synthetic_smoke_cleanup.sql
     ```
   - Recovery evidence: pending synthetic sessions decrease to `0`, no non-synthetic rows touched.

## Evidence Checklist

Before push, capture enough evidence to answer:

- `migration_guard` passed if migration files changed
- build, liveness, readiness, and runtime wiring passed
- fault-injection hook used, fail-state observed, recovery observed
- `processed_without_apply = 0`
- duplicate message identity query returned `0` rows
- watermark snapshot captured with no unexpected regression log
- Redis `XPENDING` stable/decreasing after reclaim when ingest path changed
- skipped drill explicitly called out (`no synthetic scope`, `no Redis impact`, `env-only blocker`)

## Verification Reporting

Before commit/push, state:

1. what was verified
2. what passed
3. what failed
4. whether failure is code-related or environment-only
5. what recovery path was exercised
6. what evidence was saved for rollback or follow-up

## Minimum Standard

Do not finalize coding pass without at least:

- successful build for changed language/runtime
- targeted runtime verification when startup/runtime touched
- `migration_guard` passing when migrations changed
- one integrity snapshot when deploy/runtime/recovery wiring changed
