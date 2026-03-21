# VPS / Project Baseline — 2026-03-21

## Git
- Current branch: `master`
- Uncommitted changes: `yes`
- Last commit: `1b0f799 (HEAD -> master, origin/master, origin/HEAD) Update docs after archive and scripts cleanup`

`git status --short`:
```text
 M src/TgAssistant.Core/Interfaces/IRepositories.cs
 M src/TgAssistant.Host/Program.cs
 M src/TgAssistant.Infrastructure/Database/Ef/DbRows.cs
 M src/TgAssistant.Infrastructure/Database/Ef/TgAssistantDbContext.cs
?? docs/AGENT_ROLES.md
?? docs/CODEX_TASK_PACKS.md
?? docs/IMPLEMENTATION_BACKLOG.md
?? docs/OPEN_QUESTIONS.md
?? docs/PRODUCT_DECISIONS.md
?? docs/PROJECT_SKILL.md
?? docs/SPRINT_01_1_CANONICAL_MAPPING.md
?? docs/SPRINT_01_1_REPAIR_ACCEPTANCE.md
?? docs/SPRINT_01_1_REPAIR_TASK_PACK.md
?? docs/SPRINT_01_ACCEPTANCE.md
?? docs/SPRINT_01_TASK_PACK.md
?? docs/SPRINT_AB_TESTS.md
?? docs/data-format.md
?? skills/
?? src/TgAssistant.Core/Models/ClarificationModels.cs
?? src/TgAssistant.Core/Models/DomainReviewEvent.cs
?? src/TgAssistant.Core/Models/OfflineAudioModels.cs
?? src/TgAssistant.Core/Models/PeriodModels.cs
?? src/TgAssistant.Core/Models/ReviewQueueModels.cs
?? src/TgAssistant.Core/Models/StateProfileModels.cs
?? src/TgAssistant.Core/Models/StrategyDraftModels.cs
?? src/TgAssistant.Infrastructure/Database/ClarificationRepository.cs
?? src/TgAssistant.Infrastructure/Database/DependencyLinkRepository.cs
?? src/TgAssistant.Infrastructure/Database/DomainReviewEventRepository.cs
?? src/TgAssistant.Infrastructure/Database/FoundationDomainVerificationService.cs
?? src/TgAssistant.Infrastructure/Database/InboxConflictRepository.cs
?? src/TgAssistant.Infrastructure/Database/Migrations/0011_domain_foundation_schema.sql
?? src/TgAssistant.Infrastructure/Database/Migrations/0012_sprint_01_1_repair_alignment.sql
?? src/TgAssistant.Infrastructure/Database/Migrations/0013_domain_dependency_link_reason.sql
?? src/TgAssistant.Infrastructure/Database/OfflineEventRepository.cs
?? src/TgAssistant.Infrastructure/Database/PeriodRepository.cs
?? src/TgAssistant.Infrastructure/Database/StateProfileRepository.cs
?? src/TgAssistant.Infrastructure/Database/StrategyDraftRepository.cs
```

## Runtime
- `docker compose` is used in current runtime.
- `docker compose ps` (running now):
  - `tga-app` — Up 3 days
  - `tga-mcp` — Up 3 days
  - `tga-postgres` — Up 5 days (healthy)
  - `tga-redis` — Up 7 days (healthy)
- Services defined in compose but not currently running: `postgres-exporter`, `redis-exporter`, `prometheus`, `grafana`.

## Migrations
- Source of truth: table `schema_migrations` (`id`, `checksum`, `applied_at`).
- Applied migrations (`id`, ordered):
  - `0001_initial_schema.sql`
  - `0002_intelligence_foundation.sql`
  - `0003_fact_decay.sql`
  - `0003_stage5_context_summaries.sql`
  - `0004_enable_pgvector.sql`
  - `0004_episodic_memory_sessions.sql`
  - `0005_human_trust_and_slicer.sql`
  - `0006_stage5_cold_path_finalization.sql`
  - `0007_phase_c_cold_path_sessions.sql`
  - `0008_session_first_analysis.sql`
  - `0009_message_extractions_quarantine.sql`
  - `0010_media_enrichment_state.sql`
  - `0011_domain_foundation_schema.sql`
  - `0011_foundation_domain_layer.sql`
  - `0012_sprint_01_1_repair_alignment.sql`
  - `0013_domain_dependency_link_reason.sql`
- Current latest migration (by `id`): `0013_domain_dependency_link_reason.sql` (applied at `2026-03-21 12:38:20+00`).

## App Sanity
- App container state: `running=true`, `status=running`, `started=2026-03-17T19:21:34Z`, `restart_count=0`.
- Baseline check conclusion: app was already started and is currently running; no restart loop detected.
- Recent high-signal app logs (last 24h):
  - `Telegram bot polling error` with `Request timed out` (`HttpClient.Timeout 100s`).
  - No fatal/unhandled crash lines in sampled excerpt.
- `telegram.session` path issue:
  - No explicit `telegram.session/session path/session file` errors found in sampled recent logs.
  - Session volume mount is present (`/opt/tgassistant/data/telegram-session:/app/data`).
  - Status: issue not observed in this baseline snapshot.

## Paths
Operational paths (from compose/config):
- Postgres data: `/opt/tgassistant/data/postgres` -> container `/var/lib/postgresql/data`
- Redis data: `/opt/tgassistant/data/redis` -> container `/data`
- App media storage: `/opt/tgassistant/data/media` -> container `/data/media`
- Telegram session storage: `/opt/tgassistant/data/telegram-session` -> container `/app/data`
- App logs: `/opt/tgassistant/logs` -> container `/app/logs`
- Archive input mount: `/home/codex/projects/TelegramAssistant/archive` -> container `/data/archive`
- Main compose file: `/home/codex/projects/TelegramAssistant/docker-compose.yml`
- App config file: `/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/appsettings.json`

## Known Risks
- Working tree is dirty with many tracked/untracked changes; high risk of accidental drift between local state and committed baseline.
- Monitoring stack services are defined but currently down (`prometheus`, `grafana`, exporters), reducing observability for regressions.
- Migration naming has duplicate numeric prefixes (`0003`, `0004`, `0011`), which increases risk of operator confusion/manual ordering mistakes.
- Telegram polling timeout already observed in logs; potential intermittent network/API availability risk.
- App has no compose healthcheck; container can stay `Up` while parts of runtime are degraded.
