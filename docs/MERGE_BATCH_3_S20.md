# Merge Batch 3

## Name

Sprint 20 Hardening Layer

## Scope

Review and merge the runtime hardening layer only after Batch 1 and Batch 2 are clean.

## Explicit Dependencies

Batch 3 is valid only on top of:

- Batch 1 (runtime/repair hotfix layer)
- Batch 2 (Sprint 21 composition-root decomposition)
- Sprint 19/20 schema/runtime foundation (`0018` coordination states + `0019` listener heartbeat + `0021` phase guard lease recovery)

## Included Changes

- phase guards
- backup guardrail
- integrity preflight
- related schema/config/runtime hooks

## Primary Files

- [AnalysisWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs)
- [ChatCoordinationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/ChatCoordinationService.cs)
- [IRepositories.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Interfaces/IRepositories.cs)
- [DbRows.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Ef/DbRows.cs)
- [TgAssistantDbContext.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Ef/TgAssistantDbContext.cs)
- [0020_sprint_20_phase_guards_and_backup_guardrail.sql](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Migrations/0020_sprint_20_phase_guards_and_backup_guardrail.sql)
- [0021_sprint_20_phase_guard_lease_recovery.sql](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Migrations/0021_sprint_20_phase_guard_lease_recovery.sql)
- [Settings.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Configuration/Settings.cs)
- [appsettings.json](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/appsettings.json)
- [docker-compose.yml](/home/codex/projects/TelegramAssistant/docker-compose.yml)
- [.env.example](/home/codex/projects/TelegramAssistant/.env.example)

## Review Focus

- phase exclusivity rules
- backup evidence enforcement
- integrity preflight correctness
- rollout safety
- explicit transition matrix and deny-by-default semantics
- global exclusivity policy is opt-in and narrow (active only for explicit global backfill windows)

## Explicit Exclusions From Batch 3

Do not include:

- unrelated Stage5 data-repair/dedup logic not required for Sprint 20 hardening
- broad composition-root refactors outside Batch 2
- opportunistic repository/runtime fixes that are not phase-guard or backup-preflight related

## Acceptance Target

- Sprint 20 is mergeable as a hardening batch, not mixed with older emergency/runtime changes
