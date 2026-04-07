# DTP-013A Owner-Exposure Gate

Date: `2026-04-07`
Task: `DTP-013A`
Status: `pass`

## Scope

Bounded gate inspection only (no endpoint implementation):
- `src/TgAssistant.Core/Configuration/Settings.cs`
- `src/TgAssistant.Host/appsettings.json`
- `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`
- `src/TgAssistant.Host/OperatorApi/OperatorAlertsProjectionBuilder.cs`
- `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`
- `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`

## Findings

1. Canonical owner exposure for `criticalUnresolvedCount` is now available in the bounded file set.
2. Backend home-summary read-model owner is explicitly exposed as a first-class contract field/property/method path.
3. `/api/operator/home/summary` is still intentionally out of scope for `DTP-013A` and remains for `DTP-013B`.

## Gate Decision

`PASS: canonical criticalUnresolvedCount owner exposed within DTP-013 files/areas`

## Evidence Anchors

- `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`: `OperatorHomeSummaryOwners.CriticalUnresolvedCount` plus `OperatorHomeSummaryReadModel.CriticalUnresolvedCountOwner`.
- `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`: `BuildOperatorHomeSummaryReadModel(...)` sets `CriticalUnresolvedCountOwner` and computes `CriticalUnresolvedCount` from canonical priority facets (`critical`) with deterministic fallback to critical-priority queue items.
- `src/TgAssistant.Core/Configuration/Settings.cs`: `OperatorHomeSummarySettings` includes `ForceDegradedSummary` verification override shape required for downstream DTP-013 validation.
- `src/TgAssistant.Host/appsettings.json`: `OperatorHomeSummary` section present with bounded override wiring.

## Next Action

Proceed to `DTP-013B` implementation using this canonical owner exposure as the source-of-truth for `criticalUnresolvedCount`.
