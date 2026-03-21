# AGENTS.md — TelegramAssistant

## Project overview

Personal Telegram assistant with intelligence/dossier building pipeline.
Monorepo with C# .NET 8 backend (`src/`) and TypeScript MCP server (`src/TgAssistant.Mcp`).

## Repository layout

```
src/
  TgAssistant.Core/           — domain models, interfaces, configuration
  TgAssistant.Infrastructure/  — PostgreSQL (EF Core), Redis, database migrations
  TgAssistant.Telegram/        — WTelegramClient MTProto listener
  TgAssistant.Processing/      — batch worker, archive import, media processing
  TgAssistant.Intelligence/    — Stage 5 analysis, entity merge, embeddings, Neo4j sync
  TgAssistant.Web/             — placeholder for future web UI
  TgAssistant.Host/            — entry point, DI wiring
  TgAssistant.Mcp/             — TypeScript MCP server
mcp/                           — reserved for legacy/auxiliary MCP docs
deploy/                        — Dockerfile, docker-compose, monitoring, shell scripts
scripts/                       — minimal operational SQL helpers
```

## Tech stack

- **Backend:** C# .NET 8 Worker Service
- **ORM:** Entity Framework Core 8 with `IDbContextFactory` (singleton repositories)
- **Database:** PostgreSQL 16 (migrations via embedded SQL in `Infrastructure/Database/Migrations/`)
- **Queue:** Redis 7 Streams
- **LLM:** OpenRouter API (DeepSeek, GPT-4o-mini, Claude 3.5 Sonnet, Qwen VL)
- **MCP server:** TypeScript, `@modelcontextprotocol/sdk`, `pg` driver
- **Container:** Docker Compose, `network_mode: "host"` for app container
- **CI/CD:** GitHub Actions → GHCR → SSH deploy

## Working agreements

- Run `dotnet build TelegramAssistant.sln` after modifying any C# file. Fix all compiler errors before committing.
- Run `cd src/TgAssistant.Mcp && npm run build` after modifying any TypeScript file in `src/TgAssistant.Mcp/`.
- Never modify files in `src/TgAssistant.Infrastructure/Database/Migrations/` — create new migration files instead.
- Database schema changes go in new files: `0003_*.sql`, `0004_*.sql`, etc. in `Infrastructure/Database/Migrations/`.
- All repository classes are **singletons** using `IDbContextFactory<TgAssistantDbContext>` — never inject `TgAssistantDbContext` directly.
- All new classes must be registered in `src/TgAssistant.Host/Program.cs` DI container.
- Configuration settings go in `Settings.cs`, `appsettings.json`, `docker-compose.yml` env section, and `.env.example`.
- Use `CancellationToken ct` parameter on all async methods.
- Prefer `async/await` over `.Result` or `.Wait()`.
- Log via `ILogger<T>`, not `Console.WriteLine`.
- Commit messages in English, imperative mood: "Split AnalysisWorkerService into focused classes".

## Code style

- C#: follow existing patterns in the codebase. `var` for local variables. Expression-bodied members where concise.
- TypeScript: ESM imports, strict mode, `async/await`, no `any` unless unavoidable.
- SQL: lowercase keywords, snake_case column names.

## Testing

- No test project exists yet. If you create one, name it `TgAssistant.Tests` and add it to the solution.
- At minimum, verify that `dotnet build` succeeds after changes.
- For MCP server: `npm run build` must succeed.

## Docker

- App container uses `network_mode: "host"` — connects to PostgreSQL/Redis on 127.0.0.1.
- MCP container is on the default Docker network — connects to PostgreSQL via service name `postgres`.
- Never expose ports publicly (bind to `127.0.0.1` only).

## Key domain concepts

- **Entity:** person, organization, place, pet, or event. Identified by `actor_key` (chat_id:sender_id) or name.
- **Fact:** durable information about an entity (e.g., "monthly_income=5000"). Has `is_current`, `confidence`, `status`.
- **Observation:** message-local signal (e.g., "availability_update"). Stored in `intelligence_observations`.
- **Claim:** atomic dossier-ready statement. Stored in `intelligence_claims`.
- **Extraction:** LLM output per message. Stored as JSON in `message_extractions` (cheap_json, expensive_json).
- **Watermark:** progress cursor in `analysis_state` table, key `stage5:watermark`.

## Backlog reference

See `CODEX_BACKLOG.md` in repository root for active tasks and acceptance criteria.
Active task groups are prefixed `P*` (policy/platform), `R*` (targeted refactor), and `O*` (observability).
Follow the sprint/merge order documented in that file.

## Codex delegated agent profiles

Use these profiles when spawning sub-agents for focused tasks in this repository.

- **system-design-expert**
  - Purpose: design and review system architecture for Stage5/Stage6 pipeline and MCP integration.
  - Responsibilities:
    - define service boundaries, data flow, and failure domains;
    - review throughput/latency bottlenecks, backpressure, and idempotency;
    - validate consistency between C# worker topology, PostgreSQL schema usage, Redis queueing, and MCP read/write contracts;
    - produce pragmatic migration/refactor plans with rollback strategy.
  - Inputs: current architecture docs (`README.md`, `docs/stage5-extraction-algorithm.txt`), relevant code modules, operational constraints from `deploy/`.
  - Output format:
    - architecture decision summary;
    - tradeoff table (option, pros, cons, risk);
    - implementation plan split into small executable steps;
    - explicit validation checklist (build, runtime, data correctness).
  - Rules:
    - no speculative redesigns disconnected from current code;
    - prioritize incremental changes over big-bang rewrites;
    - every proposal must include observability and rollback notes.
