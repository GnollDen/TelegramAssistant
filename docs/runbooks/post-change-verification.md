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
