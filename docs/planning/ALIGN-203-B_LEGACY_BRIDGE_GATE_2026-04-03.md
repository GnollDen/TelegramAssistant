# ALIGN-203-B Legacy Bridge Gate

## Status

Completed on 2026-04-03 as bounded runtime hardening for the retained legacy Stage6 diagnostic bridge.

## Operational Boundary Analyzed

- CLI admission and runtime composition:
  - [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs)
  - [ServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/ServiceRegistrationExtensions.cs)
  - [DomainRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs)
- Retained legacy-to-active bridge:
  - [DomainReviewEventRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/DomainReviewEventRepository.cs)
  - [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs)

## Confirmed Issue

- After `ALIGN-203-A`, default DI no longer exposed the correction store, but any explicit legacy Stage6 diagnostic smoke still admitted the retained `DomainReviewEventRepository` bridge into active `Stage8` recompute behavior with no extra admission check.
- That meant the bridge was legacy-only but not separately operator-acknowledged.

## Hardening Applied

- Legacy Stage6 diagnostic entrypoints now require an additional explicit CLI admission flag: `--allow-legacy-stage8-bridge`.
- Without that flag, the process fails before host construction and before any runtime dependencies, hosted services, or bridge-capable legacy services are activated.
- With that flag, the retained bridge remains available only for intentional bounded diagnostics.

## Why This Option Was Preferred

- It is the smallest coherent change that narrows the remaining bridge without redesigning or deleting legacy diagnostic paths.
- It preserves rollback options because the legacy diagnostic services and bridge implementation remain intact.
- It keeps the default runtime unchanged while making the exceptional path explicit and auditable from process arguments.

## Resulting Boundary State

- Default runtime and normal preserved verification entrypoints do not expose the legacy Stage6 bridge.
- Legacy diagnostic entrypoints remain available only when both of the following are true:
  - a legacy diagnostic smoke switch is requested
  - `--allow-legacy-stage8-bridge` is also present
- Correction-store repository wiring remains non-baseline and unpromoted.

## Validation Performed

- Confirmed failure path: legacy diagnostic entrypoint without `--allow-legacy-stage8-bridge` now stops immediately with an admission error before host build.
- Confirmed normal preserved path: baseline runtime wiring still resolves without legacy bridge admission.
- Confirmed recovery/explicit path: legacy diagnostic entrypoint with `--allow-legacy-stage8-bridge` proceeds past admission into the legacy diagnostic runtime path.

## Live-Environment Gaps

- Repository evidence still cannot prove whether operators rely on legacy diagnostic invocations in production-like environments.
- Any real operational use of the retained bridge should still be validated against live credentials, audit expectations, and incident rollback drills.
