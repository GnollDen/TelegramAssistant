# ALIGN-203-A Legacy Bridge and Correction-Store Audit

## Status

Completed on 2026-04-03 as an evidence-backed boundary audit with one bounded runtime-wiring tighten.

## Operational Boundary Analyzed

- Default host composition and runtime-path DI registrations:
  - [ServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/ServiceRegistrationExtensions.cs)
  - [DomainRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs)
  - [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs)
  - [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs)
- Legacy-to-active trigger bridge:
  - [DomainReviewEventRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/DomainReviewEventRepository.cs)
  - [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs)
- Correction-store implementation and active-table side effects:
  - [IdentityMergeRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/IdentityMergeRepository.cs)

## Confirmed Findings

### 1. Legacy trigger bridge is explicit, not ambient

- `IDomainReviewEventRepository` is registered only inside `AddStage6LegacyRepositoryServices()`, not in the active baseline repository block.
- All observed callers of `IDomainReviewEventRepository` are legacy Stage6 services under `src/TgAssistant.Intelligence/Stage6/*`.
- The bridge remains real: when legacy diagnostic code writes a domain review event, [DomainReviewEventRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/DomainReviewEventRepository.cs) immediately forwards that event to the active [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs).
- This means the bridge is justified only for explicit legacy diagnostics. It is not part of the default runtime path.

### 2. Correction store was still ambient in default DI before this slice

- `IIdentityMergeRepository` had no active callers in the current codebase, but it was still registered in the default composition root through `AddCorrectionRepositoryServices()`.
- The implementation is not passive storage. [IdentityMergeRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/IdentityMergeRepository.cs) mutates person identity state, writes correction metadata, and directly enqueues `Stage8` recompute work into active queue tables.
- Because there was no explicit operator/API entrypoint and no default-runtime caller, keeping this repository ambient in baseline DI overstated it as a normal runtime dependency.

## Bounded Tightening Applied

- Default host composition no longer registers correction-store services automatically.
- `AddCorrectionRepositoryServices()` remains in code as an explicit-only seam, but it now requires an opt-in composition path instead of baseline wiring.
- This is the smallest safe change because it removes avoidable ambient exposure without deleting the reversible correction implementation or inventing a new operator surface inside this slice.

## Resulting Boundary State

- Default runtime:
  - active Stage8 recompute queue and outcome-gate services remain available
  - legacy domain review bridge is not available unless explicit legacy diagnostics are enabled
  - identity-merge correction storage is not available as a baseline runtime dependency
- Explicit legacy diagnostics:
  - still able to reach `IDomainReviewEventRepository`
  - can still bridge into active Stage8 recompute triggers
- Explicit correction path:
  - not yet promoted
  - implementation retained, but registration now requires a future explicit entrypoint

## Why This Option Was Preferred

- It restores a cleaner least-privilege runtime boundary without changing the data model or correction logic.
- It preserves rollback options because the repository implementation was not removed.
- It avoids architectural widening during an audit slice.

## Validation Expectations For ALIGN-203-B

- Decide whether the legacy diagnostic bridge should stay as-is, gain additional gating, or move behind a narrower correction-only/admission-checked path.
- If `IIdentityMergeRepository` is reintroduced, do it only behind an explicit operator/API mode with clear auditability and rollback semantics.
- Confirm any future correction entrypoint validates review/approval requirements before it can enqueue Stage8 recompute work.

## Live-Environment Gaps

- Repository data alone cannot prove whether operators or external tooling depend on ambient `IIdentityMergeRepository` resolution outside this codebase.
- Repository data alone cannot prove whether legacy diagnostic runs are still used operationally in any live environment.
- Any future reintroduction of correction entrypoints should be validated against live operator workflow, approval handling, and rollback drills.
