# DTP Baseline Reconciliation

Date: `2026-04-06`  
Mode: `evidence-backed, workspace-current`

## Scope

This reconciliation checks whether `DTP-001..015` can be treated as confirmed baseline in the current workspace snapshot.

Authority used:
- `docs/planning/DETAILED_IMPLEMENTATION_TASK_PACK_2026-04-06.md`
- current code and local artifacts only

## Confirmation Rule

A DTP item is marked `confirmed` only if both conditions hold:
1. Current code shape is materially aligned with the current DTP acceptance contract.
2. Declared verification path is reproducible in the current workspace/runtime preconditions.

Otherwise status is `unconfirmed`.

## Executed Evidence

Commands executed:
1. `dotnet build TelegramAssistant.sln -v minimal` -> `PASS`.
2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate` -> `PASS`.
3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-control-detail-proof` -> `FAIL` (`Runtime role is required`).
4. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --runtime-control-detail-proof` -> `FAIL` (`Database:ConnectionString contains placeholder or unsafe secret material`).

Key evidence anchors:
- Hard-coded LoopV1 constants still present: [ResolutionInterpretationLoopV1Service.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs:9)
- Loop response model still exposes only `TotalTokens`: [ResolutionModels.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/ResolutionModels.cs:323)
- Settings include fields expected by DTP but LoopV1 service is not proven to consume them end-to-end: [Settings.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Configuration/Settings.cs:340)
- Runtime role hard requirement: [RuntimeRoleSelection.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs:59)
- Runtime startup guard requires non-placeholder DB connection: [RuntimeStartupGuard.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeStartupGuard.cs:24)
- Operator API map does not expose `GET /api/operator/home/summary` in current snapshot: [OperatorApiEndpointExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs:25)

## Reconciliation Matrix

1. `DTP-001`: `unconfirmed`  
Reason: DTP contract is settings-driven runtime wiring, while LoopV1 service still uses hard-coded constants (`EnabledScopeKey`, evidence limits, retrieval limits).
2. `DTP-002`: `unconfirmed`  
Reason: DTP requires prompt/completion/cost fields in loop response path; current `ResolutionInterpretationModelResponse` exposes only `TotalTokens`.
3. `DTP-003`: `unconfirmed`  
Reason: DTP requires explicit per-pass input/output/total/cost ceiling enforcement and fail-closed usage handling; this is not proven by current executable evidence and is not directly visible as accepted contract output in current validation artifact.
4. `DTP-004`: `unconfirmed`  
Reason: DTP requires deterministic fallback reason `loop_disabled`; current observed loop fallback uses `scope_not_enabled` in loop core and no direct proof for the DTP-004 specific disabled-switch projection contract.
5. `DTP-005`: `unconfirmed`  
Reason: DTP requires centralized failure taxonomy (`loop_disabled`, `schema_invalid`, `usage_unavailable`, budget-specific constants, etc.); current observed failure reasons differ and taxonomy closure is not proven.
6. `DTP-006`: `unconfirmed`  
Reason: verification path is not reproducible as-is in this workspace snapshot due runtime role and guarded DB preconditions.
7. `DTP-007`: `unconfirmed`  
Reason: no current run artifact proving offline-event evidence admission contract exactly as stated by DTP-007.
8. `DTP-008`: `unconfirmed`  
Reason: offline save/save-final delta contract not re-proven in current rerun; code exists but acceptance is not re-verified.
9. `DTP-009`: `unconfirmed`  
Reason: offline-events API/web paths exist, but DTP-009 acceptance contract (including deterministic failure envelope shape) is not re-proven in current rerun.
10. `DTP-010`: `unconfirmed`  
Reason: no current run artifact proving unified assistant-response parity contract across Telegram/API surfaces in this rerun.
11. `DTP-011`: `unconfirmed`  
Reason: no current run artifact proving label/trust interpretation contract changes as specified.
12. `DTP-012`: `unconfirmed`  
Reason: no current run artifact proving stable parity consumption across Telegram/Web/API outputs.
13. `DTP-013`: `unconfirmed`  
Reason: no `/api/operator/home/summary` endpoint found in current API map; DTP contract target endpoint is not evidenced.
14. `DTP-014`: `unconfirmed`  
Reason: DTP depends on DTP-013 home summary endpoint contract; upstream contract not evidenced, therefore UI consumption cannot be confirmed.
15. `DTP-015`: `unconfirmed`  
Reason: no dedicated home summary smoke runner/entrypoint evidence in current launch map; DTP-015 acceptance path is not evidenced.

## Verdict

- Reconciliation verdict: `NO (baseline not fully confirmed)`.
- Planning statement "`DTP-001..015 completed baseline` remains lineage metadata, not full present-tense implementation proof.
- Gap risk exists if Phase B assumptions rely on literal completion of all current DTP acceptance contracts without this reconciliation.
