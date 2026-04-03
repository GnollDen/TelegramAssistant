# Planning Authority Index

## Status

Active authority entrypoint for planning as of 2026-04-03.

## Authority Chain

Top-level routing:

1. `README.md` (repository overview, points here for planning authority)
2. `docs/planning/README.md` (this file, active planning index)
3. PRD authority documents listed below
4. Backlog authority files (`tasks.json`, `task_slices.json`)

## PRD Authority

- [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md)
- [LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md)

## Planning Doc Classification

### PRD docs

- Active: [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md)
- Active: [LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md)

### Gateway prep docs

- Reference-only (track-prep/audit-design note, not runtime completion authority): [LLM_GATEWAY_TRACK_PREP_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/LLM_GATEWAY_TRACK_PREP_2026-04-03.md)

### Cleanup and audit notes

- Reference-only baseline evidence: [CLEANUP-101-A_SAFE_DELETE_INVENTORY_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/CLEANUP-101-A_SAFE_DELETE_INVENTORY_2026-04-03.md)
- Reference-only boundary audit: [CLEANUP-103-A_DB_BOUNDARY_AUDIT_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/CLEANUP-103-A_DB_BOUNDARY_AUDIT_2026-04-03.md)

### Option docs

- Historical/archive-only (merged into gateway PRD): [LLM_PROVIDER_EXTENSION_OPTION_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/LLM_PROVIDER_EXTENSION_OPTION_2026-04-03.md)

## Backlog Authority

- [tasks.json](/home/codex/projects/TelegramAssistant/tasks.json)
- [task_slices.json](/home/codex/projects/TelegramAssistant/task_slices.json)

## Runtime and Baseline Authority

- Current runtime baseline date: `2026-04-03`
- Default compose role set: `ingest,stage5,maintenance,ops`
- [README.md](/home/codex/projects/TelegramAssistant/README.md)
- [src/TgAssistant.Host/Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs)
- [src/TgAssistant.Host/appsettings.json](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/appsettings.json)
- [docker-compose.yml](/home/codex/projects/TelegramAssistant/docker-compose.yml)
- [deploy/PIPELINE.md](/home/codex/projects/TelegramAssistant/deploy/PIPELINE.md)

## Cleanup Baseline Evidence

- [CLEANUP-101-A_SAFE_DELETE_INVENTORY_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/CLEANUP-101-A_SAFE_DELETE_INVENTORY_2026-04-03.md)
- [CLEANUP-103-A_DB_BOUNDARY_AUDIT_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/CLEANUP-103-A_DB_BOUNDARY_AUDIT_2026-04-03.md)

## Routing Rule

Active planning authority routes through this index and the two PRDs above.
Treat gateway prep and cleanup/audit notes as reference-only context.
Treat option notes as historical/archive-only unless explicitly promoted by a newer PRD update.
