# Post Change Verification Runbook

Use this to decide the minimum verification required before commit and push.

## Sprint 6 Boundary

Affected path:

- control plane: GitHub Actions deploy pipeline and migration ordering gate
- data plane: PostgreSQL schema/integrity state, Redis reclaim behavior, Stage5/Stage6 runtime probes
- dependency edges: container image build, PostgreSQL, Redis

## Baseline

Run these according to the touched scope:

- any C# change: `dotnet build TelegramAssistant.sln`
- any TypeScript change in `src/TgAssistant.Mcp/`: `npm run build`
- any migration file change: expect `migration_guard` in `.github/workflows/deploy.yml` to pass; new files must append after the current max prefix and keep `NNNN_slug.sql` naming

## Add Runtime Checks When Needed

Also run targeted checks when changes touch:

- `Program.cs`
- `Startup/*`
- hosted services
- configuration wiring
- compose/runtime environment
- database migrations
- coordination, listener, backfill, Stage5, repair flows

Typical commands:

- `dotnet run --project src/TgAssistant.Host -- --liveness-check`
- `dotnet run --project src/TgAssistant.Host -- --readiness-check`
- `dotnet run --project src/TgAssistant.Host -- --healthcheck` (Sprint 1 compatibility alias to readiness)
- `dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=ingest --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage5 --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage5 --stage5-smoke`
- `dotnet run --project src/TgAssistant.Host -- --budget-smoke` (Sprint 6 bounded quota/degrade/pause fault injection)
- `dotnet run --project src/TgAssistant.Host -- --stage6-execution-smoke` (Stage 6 cancellation/cooldown/degrade execution discipline)

When prompt contracts change (Sprint 4 scope), also verify:

- `prompt_templates` has explicit `version` and `checksum` values for managed prompts.
- no-op restarts do not mutate managed prompt rows when prompt content is unchanged.
- session summary prompt is sourced from one managed contract (`stage5_session_summary_v1`) across inline and worker paths.
- semantic validation rejects contract drift (invalid category/key/relationship type, or missing `reason` when `requires_expensive=true`).

## Sprint 6 Integrity and Recovery Sequence

Use this full sequence when changes touch deploy wiring, migrations, runtime startup, Redis reclaim, or recovery tooling.

1. Build and probe the normal path.
   - `dotnet build TelegramAssistant.sln`
   - `dotnet run --project src/TgAssistant.Host -- --liveness-check`
   - `dotnet run --project src/TgAssistant.Host -- --readiness-check`
   - `dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check`
2. Verify one failure path with a bounded smoke hook.
   - `dotnet run --project src/TgAssistant.Host -- --budget-smoke`
   - `dotnet run --project src/TgAssistant.Host -- --stage6-execution-smoke`
   - Pass condition: the commands exit `0` and log `Budget smoke passed` / `Stage6 execution discipline smoke passed`.
3. Capture integrity evidence from PostgreSQL.
   - Scoped preflight snapshot:
     ```bash
     docker compose exec -T postgres psql -U tgassistant -d tgassistant -v chat_id=885574984 -f scripts/stage5_integrity_preflight_preview.sql
     ```
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
4. If Redis intake or reclaim changed, sample the dependency edge.
   - `docker compose exec -T redis redis-cli XPENDING tg-messages batch-workers`
   - `docker compose logs --since=15m app | rg -i "Redis stream pending status|Redis pending reclaim executed|Redis reclaimed messages delivered|Blocked non-monotonic watermark update" || true`
   - Pass condition: no sustained `XPENDING` growth across two samples 5 minutes apart, and no recent non-monotonic watermark block unless it was intentionally injected.
5. If synthetic smoke backlog exists, verify one recovery path with the SQL cleanup hook.
   - Scope restriction: only synthetic smoke chats (`chat_id >= 9000000000000`).
   - Preview target rows first:
     ```sql
     select chat_id, session_index, last_message_at
     from chat_sessions
     where chat_id >= 9000000000000
       and not is_analyzed
       and not is_finalized
     order by chat_id, session_index;
     ```
   - Recovery hook:
     ```bash
     docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/stage5_synthetic_smoke_cleanup.sql
     ```
   - Recovery evidence: pending synthetic sessions decrease to `0`, and no non-synthetic chat rows were touched.

## Evidence Checklist

Before push, capture enough evidence to answer all of these:

- `migration_guard` passed if any migration file changed
- build, liveness, readiness, and runtime wiring all passed
- fault-injection hook used, observed fail-state, and observed recovery signal
- `processed_without_apply = 0`
- duplicate message identity query returned `0` rows
- watermark snapshot captured, with no unexpected regression log
- Redis `XPENDING` either stayed `0` or was stable/decreasing after reclaim
- any skipped drill is called out explicitly with reason (`no synthetic scope`, `no Redis impact`, `env-only blocker`)

## Verification Reporting

Before commit/push, be able to state:

1. what was verified
2. what passed
3. what failed
4. whether failure is code-related or environment-only
5. what recovery path was exercised
6. what evidence was saved for rollback or follow-up

## Minimum Standard

Do not finalize a coding pass without at least:

- successful build for the changed language/runtime
- targeted runtime verification when startup/runtime was touched
- `migration_guard` passing when migrations changed
- one integrity snapshot when deploy/runtime/recovery wiring changed
