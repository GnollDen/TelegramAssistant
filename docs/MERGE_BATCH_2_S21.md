# Merge Batch 2

## Name

Sprint 21 Composition Root Decomposition

## Scope

Review and prepare merge for the composition-root refactor after Batch 1 is stabilized.

## Included Changes

- modular DI registration
- startup registration extensions
- role-aware startup selection
- Program.cs decomposition

## Primary Files

- [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs)
- [ServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/ServiceRegistrationExtensions.cs)
- [SettingsRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/SettingsRegistrationExtensions.cs)
- [InfrastructureRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/InfrastructureRegistrationExtensions.cs)
- [DomainRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs)
- [HttpClientRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HttpClientRegistrationExtensions.cs)
- [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs)
- [RuntimeRoleSelection.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs)

## Program.cs Hunk Ownership Map (Execution Prep Snapshot 2026-03-23)

Use these anchors for Sprint 21-only staging/cherry-pick:

- include for Batch 2:
  - `using TgAssistant.Host.Startup;` at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:14)
  - runtime role selection initialization + parse/composition root call at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:143)
  - all `Startup/*` files listed above
- exclude from Batch 2:
  - `using TgAssistant.Host.Stage5Repair;` at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:13)
  - Stage5 scoped repair/risk args parse block at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:72)
  - Stage5 scoped repair execution branch at [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs:359)

Collision guard:
- if a staged `Program.cs` hunk contains `--stage5-scoped-repair`/`--risk-` symbols, reject it from Batch 2.
- Batch 2 must remain a composition-root batch (`Program.cs` decomposition + `Startup/*`) with no runtime-repair branch staging.

## Review Focus

- dropped/duplicated registrations
- role default behavior
- startup order implications
- composition-root clarity

## Acceptance Target

- Sprint 21 becomes a clean, reviewable, mergeable batch independent of Batch 1 hotfix logic
