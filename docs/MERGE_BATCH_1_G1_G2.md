# Merge Batch 1

## Name

G1 + G2 Runtime And Repair Layer

## Scope

Reshape Batch 1 into a narrow but self-contained mergeable changeset:

- G1/G2 runtime + repair behavior
- minimal unavoidable schema/runtime substrate required for this behavior
- explicit exclusion of Sprint 21 composition-root decomposition

## Included Changes

### G1 Runtime Fixes

- backfill -> handover -> realtime coordination
- listener gating
- recovery heartbeat
- idempotent coordination state init
- budget ops upsert hardening

### G2 Repair Tooling

- Stage5 scoped repair command
- repair CLI entrypoints
- repair audit artifacts support
- integrity preflight preview support

### Minimal Unavoidable Dependencies (for working runtime/apply path)

- migration `0020` tables used by repair apply/guardrail path:
  - `ops_chat_phase_guards`
  - `ops_backup_evidence_records`
- EF mappings and repository contract/model support needed by `ChatCoordinationService`
- settings/env wiring for chat coordination and risky-operation safety

## Primary Files

- [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs) (G1/G2 hunks only; see split rules below)
- [Stage5ScopedRepairCommand.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Stage5Repair/Stage5ScopedRepairCommand.cs)
- [TelegramListenerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Telegram/Listener/TelegramListenerService.cs)
- [HistoryBackfillService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Processing/Archive/HistoryBackfillService.cs)
- [ChatCoordinationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/ChatCoordinationService.cs)
- [ChatCoordinationModels.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/ChatCoordinationModels.cs)
- [IRepositories.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Interfaces/IRepositories.cs)
- [DbRows.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Ef/DbRows.cs)
- [TgAssistantDbContext.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Ef/TgAssistantDbContext.cs)
- [BudgetOpsRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/BudgetOpsRepository.cs)
- [0018_sprint_19_backfill_realtime_coordination.sql](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Migrations/0018_sprint_19_backfill_realtime_coordination.sql)
- [0019_sprint_19_recovery_heartbeat.sql](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Migrations/0019_sprint_19_recovery_heartbeat.sql)
- [0020_sprint_20_phase_guards_and_backup_guardrail.sql](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Migrations/0020_sprint_20_phase_guards_and_backup_guardrail.sql)
- [Settings.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Configuration/Settings.cs)
- [appsettings.json](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/appsettings.json)
- [docker-compose.yml](/home/codex/projects/TelegramAssistant/docker-compose.yml)
- [.env.example](/home/codex/projects/TelegramAssistant/.env.example)
- [stage5_integrity_preflight_preview.sql](/home/codex/projects/TelegramAssistant/scripts/stage5_integrity_preflight_preview.sql)

## Program.cs Split Rules

Include in Batch 1:

- Stage5 scoped repair CLI args and validation (`--stage5-scoped-repair*`, `--risk-*`)
- Stage5 scoped repair execution branch and logging

Exclude from Batch 1 (move to Batch 2 / Sprint 21):

- runtime role parsing/decomposition (`RuntimeRoleParser.Parse(...)`)
- composition-root replacement (`services.AddTelegramAssistantCompositionRoot(...)`)
- any decomposition-only import/wiring tied to `src/TgAssistant.Host/Startup/*`

## Program.cs Hunk Ownership Map (Execution Prep Snapshot 2026-03-23)

Use these anchors for `git add -p`/cherry-pick safety in this working tree:

- include for Batch 1:
  - `using TgAssistant.Host.Stage5Repair;` at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:13)
  - Stage5 scoped repair + risk arg parsing block at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:72)
  - Stage5 scoped repair execution/apply/logging branch at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:359)
- exclude from Batch 1:
  - `using TgAssistant.Host.Startup;` at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:14)
  - runtime role parsing + composition root call at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:143)

Collision guard:
- if a patch hunk includes both `runStage5ScopedRepair*` and `RuntimeRoleParser.Parse(...)`, split hunk manually before staging.
- Batch 1 must not stage changes that touch `Startup/*` imports or runtime-role selection flow.

## Explicit Exclusions From Batch 1

- [DomainRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs)
- [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs)
- [HttpClientRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HttpClientRegistrationExtensions.cs)
- [InfrastructureRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/InfrastructureRegistrationExtensions.cs)
- [RuntimeRoleSelection.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs)
- [ServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/ServiceRegistrationExtensions.cs)
- [SettingsRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/SettingsRegistrationExtensions.cs)
- [AnalysisWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs) phase-guard hooks (Sprint 20 runtime rollout scope)

## Review Focus

- only the production-used runtime/repair logic
- verify that Batch 1 is self-contained with `0020` substrate included
- no Sprint 21 decomposition review here
- no Sprint 20 phase-guard rollout review here beyond substrate dependency

## Acceptance Target

- runtime fixes are safe
- repair tooling is safe-by-default
- Batch 1 can be reviewed/merged independently from Sprint 21 decomposition
