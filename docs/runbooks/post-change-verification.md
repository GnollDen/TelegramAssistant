# Post Change Verification Runbook

Use this to decide the minimum verification required before commit and push.

## Baseline

Run these according to the touched scope:

- any C# change: `dotnet build TelegramAssistant.sln`
- any TypeScript change in `src/TgAssistant.Mcp/`: `npm run build`

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

When prompt contracts change (Sprint 4 scope), also verify:

- `prompt_templates` has explicit `version` and `checksum` values for managed prompts.
- no-op restarts do not mutate managed prompt rows when prompt content is unchanged.
- session summary prompt is sourced from one managed contract (`stage5_session_summary_v1`) across inline and worker paths.
- semantic validation rejects contract drift (invalid category/key/relationship type, or missing `reason` when `requires_expensive=true`).

## Verification Reporting

Before commit/push, be able to state:

1. what was verified
2. what passed
3. what failed
4. whether failure is code-related or environment-only

## Minimum Standard

Do not finalize a coding pass without at least:

- successful build for the changed language/runtime
- targeted runtime verification when startup/runtime was touched
