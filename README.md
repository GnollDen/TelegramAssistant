# TelegramAssistant

Personal Telegram assistant monorepo focused on message ingestion, processing, and intelligence extraction.

## Start Here: Runtime Requires Docker

Runtime/proof/smoke verification is container-first and must run via Docker Compose.

- Use `docker compose` for behavior checks (`--*-smoke`, `--*-proof`, readiness/liveness, runtime commands).
- Non-container runs (for example, `dotnet build`) are code checks only.
- Baseline policy: `docs/runbooks/container-first-testing-policy.md`

Quick start:

```bash
docker compose build app
docker compose run --rm app --list-smokes
```

## Authority Chain

Use a single planning authority entrypoint:

1. `docs/planning/README.md` (planning authority index)
2. PRD authority from that index:
   - `docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`
   - `docs/planning/LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md`
3. Execution backlogs:
   - `tasks.json` (L1 orchestration backlog)
   - `task_slices.json` (L2 execution backlog)

## Repository Layout

- `src/TgAssistant.Core` — domain models, contracts, settings
- `src/TgAssistant.Infrastructure` — PostgreSQL/Redis integration, EF Core context, repositories, migrations runner
- `src/TgAssistant.Telegram` — Telegram ingestion (MTProto via WTelegram)
- `src/TgAssistant.Processing` — processing pipeline and media/archive flows
- `src/TgAssistant.Intelligence` — Stage5 substrate plus Stage6/7/8 implementation track (legacy Stage6 operator flows remain quarantined)
- `src/TgAssistant.Host` — application entry point and DI/runtime composition
- `src/TgAssistant.Mcp` — TypeScript MCP server
- `deploy` — Docker and deployment artifacts
- `scripts` — operational SQL/helper scripts

## Tech Stack

- .NET 8 (C#)
- PostgreSQL 16
- Redis 7
- TypeScript MCP server (`@modelcontextprotocol/sdk`, `pg`)
- Docker Compose

## Build

### Backend

```bash
dotnet build TelegramAssistant.sln
```

## Testing Policy

Container-first testing is mandatory for runtime behavior checks.

- Policy: `docs/runbooks/container-first-testing-policy.md`
- Short rule: rebuild `app` image and run behavior tests through `docker compose run --rm app ...`
- Non-container runs are code checks only and cannot be used to confirm application behavior.

### MCP server

```bash
cd src/TgAssistant.Mcp
npm install
npm run build
```

## Runtime

Current runtime baseline (as of 2026-04-03):

- Default compose services: `postgres`, `redis`, `app`, `mcp`, `postgres-exporter`, `redis-exporter`, `prometheus`, `grafana`
- Default compose role set: `ingest,stage5,maintenance,ops`
- Default startup path does not run schema-changing DDL. Use explicit operator-only mode `--operator-schema-init` when migrations must be applied.
- `--seed-bootstrap-scope` is an operator command and can run under `Runtime__Role=ops`; that bounded path needs DB/Redis baseline only and does not require Telegram ingest admission.
- Legacy Stage6 diagnostic smokes are not baseline runtime behavior and now require explicit admission flag `--allow-legacy-stage8-bridge` because they retain a bounded legacy-to-active Stage8 bridge.
- Primary runtime composition: `src/TgAssistant.Host/Program.cs`
- Runtime settings baseline: `src/TgAssistant.Host/appsettings.json`
- Local orchestration baseline: `docker-compose.yml`

Gateway migration is an active track. Do not treat gateway-prep notes as runtime-complete implementation status.

## Notes

- Legacy planning and historical runbook documents were intentionally removed from active branch.
- Historical materials are kept only in local/off-branch archives or git history.
