# TelegramAssistant

Personal Telegram assistant monorepo focused on message ingestion, processing, and intelligence extraction.

## Active Authority

- Product authority (PRD): `docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`
- Execution backlog: `tasks.json`
- Task slicing: `task_slices.json`

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

### MCP server

```bash
cd src/TgAssistant.Mcp
npm install
npm run build
```

## Runtime

Primary runtime composition is in `src/TgAssistant.Host/Program.cs`.
Use `docker-compose.yml` for local service orchestration.

## Notes

- Legacy planning and historical runbook documents were intentionally removed from active branch.
- Historical materials are kept only in local/off-branch archives or git history.
