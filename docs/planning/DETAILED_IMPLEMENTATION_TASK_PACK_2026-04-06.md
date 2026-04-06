# Detailed Implementation Task Pack

Date: `2026-04-06`  
Mode: `single-active-agent sequential orchestration`

## Task Pack Scope And Rules

- Authority is limited to:
  - `docs/planning/README.md`
  - `docs/planning/MASTER_PROJECT_STATUS_2026-04-06.md`
  - `docs/planning/COMPACT_EXECUTION_CONTEXT_2026-04-06.md`
  - `docs/planning/ORCH_MASTER_PROJECT_STATUS_2026-04-06.md` (orchestration evidence only, not product authority)
  - `docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`
  - `docs/planning/OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md`
  - `docs/planning/AI_CENTRIC_CONTEXT_LOOP_ADDENDUM_2026-04-05.md`
  - `docs/planning/LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md` (remains listed in the planning authority index and is part of the active product authority chain for this pack)
  - `tasks.json`
  - `task_slices.json`
- Do not use `docs/planning/AI_CONFLICT_RESOLUTION_SESSION_DESIGN_2026-04-06.md` or any other proposed-only document as execution authority.
- Treat `docs/planning/PROJECT_AGENT_RULES_2026-04-06.md` as missing. Do not block this pack on that file unless a task explicitly proves it is required for compilation or runtime.
- Allowed implementation tracks are only:
  - `ResolutionInterpretationLoopV1`
  - `Loop Guardrails And Rollback`
  - `Offline Event Source Admission`
  - `Trust And Label Parity`
  - `Web Home And Dashboard Closure`
- Execution is strict linear order. Do not start the next task until the current task passes its acceptance criteria.
- Every task is bounded to the listed files/areas. If the task cannot be completed inside those bounds, stop and escalate instead of widening scope.
- Preserve current deterministic control-plane ownership:
  - no model-direct durable writes
  - no cross-scope retrieval
  - no MCP dependency
  - no reopening legacy Stage6 bot/web/operator paths
- Rollback rule:
  - if a task fails acceptance, revert only the edits from that task
  - do not carry partial changes into the next task
  - keep the last accepted task as the restore point
- Default owner mapping for execution:
  - `DTP-001` through `DTP-009`: backend/runtime engineer
  - `DTP-010` through `DTP-012`: backend plus operator-surface engineer
  - `DTP-013` through `DTP-015`: operator web engineer

## Dependency Order

Strict linear order:

1. `DTP-001`
2. `DTP-002`
3. `DTP-003`
4. `DTP-004`
5. `DTP-005`
6. `DTP-006`
7. `DTP-008`
8. `DTP-007`
9. `DTP-009`
10. `DTP-010`
11. `DTP-011`
12. `DTP-012`
13. `DTP-013`
14. `DTP-014`
15. `DTP-015`

Stop gates:

- After `DTP-003`, the canonical bounded loop must still pass `--resolution-interpretation-loop-v1-validate`.
- After `DTP-006`, rollback behavior must be proven before any offline-event source admission or surface work starts.
- After `DTP-009`, offline events must be queryable and inspectable as bounded operator data before parity work starts.
- After `DTP-012`, label/trust output must be stable before web home/dashboard work consumes it.

## Tasks

### Task 1

- `Task ID`: `DTP-001`
- `Title`: Bind ResolutionInterpretationLoopV1 services to runtime settings
- `Purpose`: Remove hard-coded LoopV1 runtime values so the active bounded loop obeys `ResolutionInterpretationLoopSettings` at runtime.
- `Track`: `ResolutionInterpretationLoopV1`
- `Dependencies`: `[]`
- `Exact Scope`: Wire `Enabled`, `CanonicalScopeOnly`, `CanonicalScopeKey`, retrieval limits, token limits, timeout, task key, and optional model hint from settings into the LoopV1 service and model classes. Do not change the output schema in this task.
- `Files/Areas`: `src/TgAssistant.Core/Configuration/Settings.cs`, `src/TgAssistant.Host/appsettings.json`, `src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs`, `src/TgAssistant.Infrastructure/Database/LlmResolutionInterpretationModel.cs`, `src/TgAssistant.Host/Launch/ResolutionInterpretationLoopValidationRunner.cs`
- `Step-by-Step Instructions`:
  1. Read `ResolutionInterpretationLoopSettings` and confirm every field needed by the current loop already exists.
  2. Inject `IOptions<ResolutionInterpretationLoopSettings>` into `ResolutionInterpretationLoopV1Service`.
  3. Replace local LoopV1 constants for canonical scope, retrieval round count, initial evidence count, and additional context counts with setting-backed values.
  4. Inject `IOptions<ResolutionInterpretationLoopSettings>` into `LlmResolutionInterpretationModel`.
  5. Replace the hard-coded task key, timeout, and optional model selection in `LlmResolutionInterpretationModel` with setting-backed values.
  6. Keep the structured output schema unchanged.
  7. Update validation runner setup only where hard-coded expectations must match the new setting-backed values.
  8. Leave default values in `appsettings.json` equal to the active addendum limits unless the current file already uses those exact values.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`.
  3. Open `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` and confirm the report still shows a passed bounded loop run.
- `Acceptance Criteria`:
  - `ResolutionInterpretationLoopV1Service` no longer uses private hard-coded runtime limits for scope key, retrieval rounds, or evidence window size.
  - `LlmResolutionInterpretationModel` no longer uses a private hard-coded task key or timeout.
  - Validation passes without widening the loop beyond the canonical scope.
- `Risks`: DI registration mismatch can break host startup; setting defaults can drift from the active addendum if copied incorrectly.
- `Do Not Do`: Do not add new settings fields in this task. Do not widen the loop to non-canonical scopes. Do not change the JSON contract.
- `Expected Artifacts`: Updated setting-backed runtime code and a passing `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json`.

### Task 2

- `Task ID`: `DTP-002`
- `Title`: Extend LoopV1 model responses with prompt, completion, and cost usage
- `Purpose`: Capture the full per-call budget facts required by the addendum instead of only total token count.
- `Track`: `ResolutionInterpretationLoopV1`
- `Dependencies`: [`DTP-001`]
- `Exact Scope`: Add prompt-token, completion-token, and cost fields to the LoopV1 model response path and carry them from `LlmGatewayResponse.Usage` into the loop service. Every loop audit record must include `context_manifest`, `retrieval_requests`, `retrieval_results`, `model_id`, `model_version`, `prompt_tokens`, `completion_tokens`, `total_tokens`, `cost_usd`, `normalization_status`, and `gate_decision` as structured metadata fields; free-text details may supplement them but do not satisfy the requirement, and validation fails closed if any required field is missing. If provider usage is null or missing, preserve `null` in the response model and leave downstream budget enforcement to DTP-003 rather than infer zero or bypass gating.
  Any surfaced claim must carry evidence refs or an explicit uncertainty signal; do not emit claims that imply certainty without one of those fields. For this task, the only explicit uncertainty signal that satisfies this requirement is a surfaced claim `type` set to the exact bounded value `inference` or `hypothesis`.
- `Files/Areas`: `src/TgAssistant.Core/Models/ResolutionModels.cs`, `src/TgAssistant.Infrastructure/Database/LlmResolutionInterpretationModel.cs`, `src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs`, `src/TgAssistant.Host/Launch/ResolutionInterpretationLoopValidationRunner.cs`
- `Step-by-Step Instructions`:
  1. Add new fields to `ResolutionInterpretationModelResponse` for `PromptTokens`, `CompletionTokens`, and `CostUsd`.
  2. Populate those fields in `LlmResolutionInterpretationModel` from `response.Usage`.
  3. Update LoopV1 logging so each model pass logs prompt tokens, completion tokens, total tokens, and cost when available.
  4. Update loop audit entries so the mandatory audit values are present as structured metadata fields; the details string may mirror them, but cannot be the only source.
  5. Update the validation runner report model if it must expose the additional budget facts.
  6. Keep backward compatibility for missing provider usage values by setting `PromptTokens`, `CompletionTokens`, `TotalTokens`, and `CostUsd` to `null` and require the validation artifact to render those missing values as `null`; downstream budget enforcement is handled in DTP-003.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`.
  3. Inspect `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` and confirm usage fields are present on both model rounds or explicitly recorded as unavailable.
- `Acceptance Criteria`:
  - LoopV1 can access prompt, completion, total, and cost usage for each model round.
  - The validation artifact records more than `total_tokens` alone and includes the full mandatory audit contract as structured metadata fields.
  - Null provider usage is preserved as null in the artifact.
  - Any surfaced claim must preserve evidence refs or explicit uncertainty instead of implying unsupported certainty, where the explicit uncertainty signal in this task is the exact surfaced claim `type` value `inference` or `hypothesis`.
- `Risks`: Changing the response model can break existing validation code; provider usage can be missing or partial.
- `Do Not Do`: Do not add database persistence in this task. Do not estimate cost with new custom formulas here; use gateway usage only.
- `Expected Artifacts`: Updated response model, updated loop audit/logging, updated validation artifact with expanded usage data.

### Task 3

- `Task ID`: `DTP-003`
- `Title`: Enforce total token and cost ceilings inside the bounded loop
- `Purpose`: Make LoopV1 stop or fall back when model usage exceeds the active addendum budgets.
- `Track`: `ResolutionInterpretationLoopV1`
- `Dependencies`: [`DTP-002`]
- `Exact Scope`: Add cumulative token and cost tracking in `ResolutionInterpretationLoopV1Service`. Enforce `PromptTokens <= MaxInputTokens` and `CompletionTokens <= MaxOutputTokens` on each model pass, while still enforcing `MaxTotalTokens` and `MaxCostUsdPerLoop` across the loop. If round one already exceeds any configured ceiling, skip round two and return deterministic fallback. If provider usage is null or missing for a model pass, return deterministic fallback with `usage_unavailable` rather than inferring zero or bypassing the budget check. If the final round exceeds any configured ceiling, mark the loop as fallback and record the failure reason. If a budget value is zero or negative because of bad configuration, treat that as `invalid_budget_configuration` and return fallback instead of running the loop.
- `Files/Areas`: `src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs`, `src/TgAssistant.Core/Models/ResolutionModels.cs`, `src/TgAssistant.Host/Launch/ResolutionInterpretationLoopValidationRunner.cs`
- `Step-by-Step Instructions`:
  1. Add local cumulative counters in `ResolutionInterpretationLoopV1Service` for total tokens and total cost across both passes.
  2. After the first model call, compare cumulative usage to `MaxTotalTokens` and `MaxCostUsdPerLoop`.
  3. If `PromptTokens` exceeds `MaxInputTokens`, `CompletionTokens` exceeds `MaxOutputTokens`, `TotalTokens` exceeds `MaxTotalTokens`, or `CostUsd` exceeds `MaxCostUsdPerLoop` after the first call, record an audit entry with `input_token_budget_exceeded`, `output_token_budget_exceeded`, `total_token_budget_exceeded`, or `cost_budget_exceeded` respectively and return deterministic fallback without issuing a retrieval round. If usage is missing, record `usage_unavailable` and return deterministic fallback instead of treating the call as budget-compliant.
  4. After the second model call, repeat the same budget checks and convert the result to deterministic fallback with the same granular reason set if any prompt, completion, total-token, or cost limit is exceeded.
  5. Use distinct failure reasons `input_token_budget_exceeded`, `output_token_budget_exceeded`, `total_token_budget_exceeded`, `cost_budget_exceeded`, and `usage_unavailable`.
  6. Update the validation runner so it asserts the normal happy path still stays inside ceilings.
  7. If a budget value is zero or negative because of bad configuration, treat that as `invalid_budget_configuration` and return fallback instead of running the loop.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`.
  3. Confirm `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` still passes and does not show any budget failure reason on the happy path. Rollback proof remains a separate DTP-006 concern.
- `Acceptance Criteria`:
  - LoopV1 refuses to exceed configured prompt, completion, total token, and cost ceilings.
  - Round two is skipped when round one already exhausts any allowed budget.
  - Happy-path validation still passes on the canonical scope.
  - Missing provider usage fails closed with `usage_unavailable` instead of bypassing budget enforcement.
  - Input-token, output-token, total-token, and cost overruns each map to distinct failure reasons.
  - Bad budget configuration fails closed with `invalid_budget_configuration`.
  - This task does not claim rollback proof; that is reserved for DTP-006.
- `Risks`: Budget fallback can accidentally suppress valid interpretations if usage is normalized incorrectly.
- `Do Not Do`: Do not widen the budget ceilings to make tests pass. Do not delete existing deterministic fallback logic.
- `Expected Artifacts`: Updated loop budget enforcement code and a passing canonical validation artifact.

### Task 4

- `Task ID`: `DTP-004`
- `Title`: Honor the LoopV1 global enable switch in resolution projection
- `Purpose`: Make the projection path fully reversible without code rollback by respecting the existing `Enabled` setting.
- `Track`: `Loop Guardrails And Rollback`
- `Dependencies`: [`DTP-003`]
- `Exact Scope`: Update `ResolutionReadProjectionService` so it does not call the loop when `ResolutionInterpretationLoopSettings.Enabled` is `false`. It must publish the current deterministic projection instead and set the fallback `failure_reason` to the exact string `loop_disabled`. The disabled fallback path must still emit an `InterpretationLoop` audit record with the mandatory audit keys from DTP-002 present, using `null` where a key is non-applicable because the loop was skipped. The rollback proof runner must be reproducible either through self-seeding/replay or by documenting the exact seed command before the proof command is executed.
- `Files/Areas`: `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`, `src/TgAssistant.Core/Configuration/Settings.cs`, `src/TgAssistant.Host/Launch/RuntimeControlDetailBoundedProofRunner.cs`
- `Step-by-Step Instructions`:
  1. Inject `IOptions<ResolutionInterpretationLoopSettings>` into `ResolutionReadProjectionService`.
  2. Add an early check before `InterpretAsync` that exits to deterministic projection when the setting is disabled.
  3. When the loop is skipped, set the deterministic fallback `failure_reason` to exactly `loop_disabled`.
  4. Keep existing heuristic projection fields unchanged when disabled.
  5. Update runtime-control bounded proof logic if it assumes the loop always runs on canonical scope.
  6. Make sure the disabled path is deny-safe, does not leave `InterpretationLoop` null when downstream code expects a result object, and preserves the mandatory audit-key shape with `null` values where the skipped path has no applicable runtime value.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-control-detail-proof`.
  3. Inspect `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json` and confirm the proof artifact includes a disabled-loop case with bounded projection output and `failure_reason=loop_disabled`; do not treat a manual "renders deterministic copy" check as sufficient verification for this task.
- `Acceptance Criteria`:
  - Setting `ResolutionInterpretationLoop:Enabled=false` disables the AI loop without breaking resolution detail rendering.
  - The deterministic projection remains available as the rollback path.
  - The disabled-path fallback reason is exactly `loop_disabled`.
  - The disabled path preserves a non-null `InterpretationLoop` audit record with the mandatory audit keys present, using `null` only where the skipped path has no applicable value.
  - Runtime-control proof still passes in the default enabled configuration.
- `Risks`: Null handling regressions can break detail rendering when the loop is skipped.
- `Do Not Do`: Do not remove the deterministic projection. Do not force-enable the loop anywhere else in the stack.
- `Expected Artifacts`: Enable-switch-aware projection code and a passing `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json`.

### Task 5

- `Task ID`: `DTP-005`
- `Title`: Standardize loop failure reasons and audit statuses
- `Purpose`: Make rollback and triage deterministic by using a fixed failure taxonomy instead of ad hoc strings.
- `Track`: `Loop Guardrails And Rollback`
- `Dependencies`: [`DTP-004`]
- `Exact Scope`: Define and apply a single failure-reason vocabulary for LoopV1 skip, scope, schema, usage, input budget, output budget, total budget, cost budget, retrieval, projection, and invalid-budget configuration exceptions. Keep audit step statuses separate from failure reasons; do not force `audit_status` to equal a failure-reason constant. For skip and exception fallback paths touched by this task, preserve a non-null `InterpretationLoop` audit record with the mandatory DTP-002 audit keys present, using `null` when a key is non-applicable to that fallback path.
- `Files/Areas`: `src/TgAssistant.Core/Models/ResolutionModels.cs`, `src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs`, `src/TgAssistant.Infrastructure/Database/LlmResolutionInterpretationModel.cs`, `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`
- `Step-by-Step Instructions`:
  1. Add a centralized set of failure-reason constants under the resolution models.
  2. Replace literal failure-reason strings in the loop service with those constants.
  3. Replace literal failure-reason strings in `ResolutionReadProjectionService` fallback handling with the same constants.
  4. Catch structured payload deserialization or schema mismatch failures in `LlmResolutionInterpretationModel` and map them to a schema-specific failure reason.
  5. Use the exact constants `loop_disabled`, `scope_rejected`, `schema_invalid`, `usage_unavailable`, `input_token_budget_exceeded`, `output_token_budget_exceeded`, `total_token_budget_exceeded`, `cost_budget_exceeded`, `invalid_budget_configuration`, `retrieval_failed`, and `projection_exception` for `failure_reason`, and retain step-level `audit_status` values such as `recorded`, `completed`, `skipped`, `rejected`, `empty`, and `model_error` independently so the audit trail remains semantically accurate.
  6. For skip and exception fallback paths touched here, keep the mandatory audit-key shape intact on the emitted `InterpretationLoop` audit record and use `null` only for non-applicable values rather than omitting keys.
  7. Update validation or proof runners only where the failure-reason text is asserted directly.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`.
  3. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-control-detail-proof`.
- `Acceptance Criteria`:
  - LoopV1 and projection fallback code use shared failure-reason constants.
  - Schema mismatch, disabled, scope rejection, missing-usage fallback, invalid budget configuration, input budget overrun, output budget overrun, total budget overrun, cost overrun, retrieval failure, and projection exception each have distinct failure reasons.
  - Audit status values remain step-level lifecycle states and are not collapsed into failure-reason constants.
  - Skip and exception fallback paths touched by this task preserve a non-null `InterpretationLoop` audit record with the mandatory DTP-002 audit keys present, using `null` only where a value is non-applicable.
  - Existing proof commands still pass.
- `Risks`: Renaming failure strings can break proof runners or any downstream filtering code.
- `Do Not Do`: Do not add human-language messages as machine failure identifiers. Do not collapse distinct failure cases into one generic value.
- `Expected Artifacts`: Shared failure-reason constants and updated code paths that consume them.

### Task 6

- `Task ID`: `DTP-006`
- `Title`: Add rollback proof coverage for disabled and budget-exceeded loop paths
- `Purpose`: Prove that the rollback path works before downstream tasks depend on the loop output.
- `Track`: `Loop Guardrails And Rollback`
- `Dependencies`: [`DTP-005`]
- `Exact Scope`: Extend the existing `--runtime-control-detail-proof` runner so the canonical proof command exercises at least these cases: enabled happy path, disabled deterministic fallback, usage_unavailable fallback, invalid_budget_configuration fallback, input-token, output-token, total-token, and cost budget fallback coverage, schema-invalid fallback, scope-rejected fallback, retrieval_failed fallback, projection_exception fallback, and writes the single proof artifact to `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json`. Induce those failure modes only through runner-local stubs or runner-local settings overrides inside the proof command; do not widen production seams or add broader test-only hooks in this task. If an existing runner-local seam is absent for a required case, stop and escalate instead of extending scope. The proof must be reproducible either by self-seeding/replay or by recording the exact seed command in the proof output or artifact before the proof command runs.
- `Files/Areas`: `src/TgAssistant.Host/Launch/ResolutionInterpretationLoopValidationRunner.cs`, `src/TgAssistant.Host/Launch/RuntimeControlDetailBoundedProofRunner.cs`, `src/TgAssistant.Host/Program.cs`
- `Step-by-Step Instructions`:
  1. Use `--runtime-control-detail-proof` as the canonical proof command and keep the existing runner as the base. Do not create a second overlapping proof path.
  2. Add a disabled-loop case that proves deterministic projection is returned.
  3. Add budget-fallback coverage for `usage_unavailable`, `invalid_budget_configuration`, input-token, output-token, total-token, and cost overruns using runner-local stubs or runner-local settings overrides that force each failure mode, and assert the shared `usage_unavailable`, `invalid_budget_configuration`, `input_token_budget_exceeded`, `output_token_budget_exceeded`, `total_token_budget_exceeded`, and `cost_budget_exceeded` reasons. If a required case cannot be induced through an existing runner-local seam, stop and escalate instead of adding broader wiring.
  4. Add schema-invalid, scope-rejected, retrieval_failed, and projection_exception fallback cases.
  5. Keep the happy-path case intact.
  6. Require audit-record presence checks and mandatory audit-field checks for every case in the proof acceptance: `context_manifest`, `retrieval_requests`, `retrieval_results`, `model_id`, `model_version`, `prompt_tokens`, `completion_tokens`, `total_tokens`, `cost_usd`, `normalization_status`, and `gate_decision`. For fallback cases, including `usage_unavailable`, those keys must still be present and may be `null` only when the fallback path makes the field non-applicable.
  7. Write each case result into `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json` with clear pass/fail fields.
  8. If a new CLI switch is added, register it in `Program.cs`.
  9. Fail the runner if any case returns null interpretation data, breaks scope bounds, or fails to return a deterministic bounded projection.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-control-detail-proof`.
  3. Inspect `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json` and confirm all cases are represented, including `invalid_budget_configuration` alongside the other rollback reasons, and that the artifact or proof output also records replay metadata or the exact seed command used for the run.
- `Acceptance Criteria`:
  - The canonical `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-control-detail-proof` command writes a single proof artifact covering happy path, loop_disabled, usage_unavailable, invalid_budget_configuration, input_token_budget_exceeded, output_token_budget_exceeded, total_token_budget_exceeded, cost_budget_exceeded, schema_invalid fallback, scope_rejected fallback, retrieval_failed fallback, and projection_exception fallback to `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json`.
  - Each rollback case shows a deterministic fallback reason and a bounded projection, not an unhandled exception.
  - The proof checks that a per-run audit record exists for every case and that `context_manifest`, `retrieval_requests`, `retrieval_results`, `model_id`, `model_version`, `prompt_tokens`, `completion_tokens`, `total_tokens`, `cost_usd`, `normalization_status`, and `gate_decision` are present as the mandatory audit fields, with `null` allowed only when a fallback path, including `usage_unavailable`, makes a field non-applicable.
  - The proof output or artifact includes either self-seeding/replay metadata or the exact seed command used for the run.
  - The command exits non-zero if any rollback case fails.
- `Risks`: Test-only wiring can accidentally leak into production registration.
- `Do Not Do`: Do not add a proof that depends on live external model calls. Do not skip the disabled case because the default config is enabled. Do not add broader production seams or non-runner-local failure injection hooks just to satisfy this proof.
- `Expected Artifacts`: Updated proof runner, updated CLI switch registration if needed, and a proof artifact covering rollback cases.

### Task 8

- `Task ID`: `DTP-008`
- `Title`: Finalize offline-event save states for downstream evidence admission
- `Purpose`: Ensure Telegram capture and save flows produce the exact persisted fields required by the new evidence-admission path.
- `Track`: `Offline Event Source Admission`
- `Dependencies`: [`DTP-006`]
- `Exact Scope`: Treat `offline:save` as a draft write and `offline:save-final` as a delta hardening over the existing OPINT-007 contract: both commands operate on the same offline-event record, preserve the existing OPINT-007 persisted metadata baseline, and keep direct `offline:save-final` behavior intact. `offline:save` must upsert a record with `status='draft'`, `saved_at_utc` null, and the baseline persisted fields `tracked_person_id`, `scope_key`, `summary`, `clarification_payload`, `confidence`, and `recording_ref`; these baseline fields are the minimum required set, and any additional existing OPINT-007 metadata columns or values must be preserved rather than stripped. Canonical `recording_ref` normalization for this task is: trim surrounding whitespace, persist `null` when the trimmed value is empty or whitespace-only, and otherwise persist the trimmed value without alternate rewriting. `offline:save-final` must persist the same baseline fields, then transition that same record to `status='saved'` with non-null `saved_at_utc`; if a persisted draft id exists, the final save may update that record in place, otherwise it may create the saved record. If validation fails at any point, write no repository changes, keep the pre-existing record and session state unchanged, leave the conversation in offline-event mode, and emit an explicit save-rejected note. Keep the pinned stop_reason persistence and derivation contract intact for DTP-009 consumption.
- `Files/Areas`: `src/TgAssistant.Telegram/Operator/TelegramOperatorWorkflowService.cs`, `src/TgAssistant.Infrastructure/Database/OperatorOfflineEventRepository.cs`, `src/TgAssistant.Core/Models/OperatorOfflineEventModels.cs`, `src/TgAssistant.Host/Launch/Opint007OfflineEventCaptureSmokeRunner.cs`, `src/TgAssistant.Host/Launch/Opint007OfflineEventClarificationOrchestrationSmokeRunner.cs`
- `Step-by-Step Instructions`:
  1. Trace the `offline:save` and `offline:save-final` callback paths in `TelegramOperatorWorkflowService`.
  2. Confirm `offline:save` writes a draft record with `status='draft'`, `saved_at_utc = null`, and the required minimum persisted fields listed in the exact scope.
  3. Confirm `offline:save-final` either updates that same record in place when a persisted draft id exists or, when no draft id exists, creates the saved record through the direct `offline:save-final` path; in both cases it changes only `status` to `saved` and `saved_at_utc` to the current UTC timestamp and does not alter `tracked_person_id`, `scope_key`, `summary`, `clarification_payload`, `confidence`, or `recording_ref`.
  4. Confirm the clarification payload, confidence, and recording reference survive both draft save and final save without being dropped or rewritten to alternate shapes.
  5. Normalize `recording_ref` canonically by trimming surrounding whitespace and persisting `null` when the trimmed value is empty or whitespace-only; preserve non-empty trimmed values without alternate rewriting so downstream evidence mapping does not need special cases.
  6. Update both OPINT-007 smoke runners to assert the draft-save and final-save persistence rules, including the no-write-on-validation-failure case.
  7. If any save step fails validation, leave the repository unchanged, keep the session in offline-event mode, and render the same offline-event context with an explicit save-rejected note.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-007-b1-smoke`.
  3. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-007-b3-smoke`.
- `Acceptance Criteria`:
  - Draft save produces a `draft` record with `saved_at_utc = null`, the required minimum persisted fields present, and existing OPINT-007 metadata preserved.
  - Final save mutates that same record in place when a draft id exists, or creates the saved record when only the saved path exists, with non-null `saved_at_utc` and no removal of the other persisted fields or metadata.
  - Empty or whitespace-only `recording_ref` inputs are trimmed and persisted as `null`, while non-empty values persist in trimmed form.
  - Validation failure produces no repository write, leaves the offline-event session state unchanged, and records an explicit save-rejected note.
  - Both OPINT-007 smoke commands pass.
- `Risks`: Changing save-state handling can break existing draft flows or session-state recovery.
- `Do Not Do`: Do not redesign the offline Telegram workflow. Do not add new operator questions in this task.
- `Expected Artifacts`: Updated Telegram save behavior, updated repository mapping if needed, and passing OPINT-007 smoke artifacts.

### Task 7

- `Task ID`: `DTP-007`
- `Title`: Admit saved offline events into bounded resolution evidence reads
- `Purpose`: Make offline events a first-class evidence source on the active tracked-person scope instead of a separate side record only.
- `Track`: `Offline Event Source Admission`
- `Dependencies`: [`DTP-008`]
- `Exact Scope`: Add bounded offline-event evidence loading to `ResolutionReadProjectionService` for the same `tracked_person_id` and `scope_key`. Admit only offline events with `status = saved`, order the admitted offline-event candidates deterministically by `saved_at_utc` descending and `id` descending, hard-cap the admitted offline-event candidate list at 5 items before merge, and then pass those rows through the existing final sort-and-truncate path without reserving a dedicated slot or adding an offline-event-specific post-merge reorder. This task remains saved-event admission only; do not reintroduce draft-only verification paths. Require the saved-event path to be exercised through the exact `--opint-007-b3-smoke` command with `Telegram__OwnerUserId` configured; do not substitute an ad hoc seed path. No other semantic relevance filter is used in this task.
- `Files/Areas`: `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`, `src/TgAssistant.Infrastructure/Database/OperatorOfflineEventRepository.cs`, `src/TgAssistant.Core/Models/OperatorOfflineEventModels.cs`, `src/TgAssistant.Core/Models/ResolutionModels.cs`
- `Step-by-Step Instructions`:
  1. Add a small helper in the read path to fetch only saved offline events within the current `tracked_person_id` and `scope_key`.
  2. Order the candidate events by `saved_at_utc` descending, then `id` descending, and stop after 5 admitted items.
  3. Map each admitted offline event to `ResolutionEvidenceSummary` with a stable `source_label` such as `offline_event`.
  4. Use a stable `source_ref` format that includes the offline event id.
  5. Populate summary text from the saved offline-event summary and include clarification-derived context only if it already exists in persisted payload.
  6. Merge the capped offline-event rows into the existing final sort-and-truncate path without reserving a dedicated slot and without adding an offline-event-specific post-merge reorder; the deterministic `saved_at_utc` descending then `id` descending order applies to the admitted offline-event candidate list before merge.
  7. Keep the merge scope-local. Do not read offline events from other tracked persons or scopes.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-007-b3-smoke` with `Telegram__OwnerUserId` configured before the host starts, then read `SavedOfflineEventId` from `logs/opint-007-b3-smoke-report.json` or the matching `offline_event_id=` host log line and reuse that id for the resolution-detail checks below.
  3. With the authenticated operator session established by the local operator login flow, confirm the seeded resolution detail output admits saved offline-event evidence only, caps the admitted offline-event candidate list at 5 items, and excludes draft or cross-scope offline events from resolution evidence.
  4. Reuse the same seeded data to repeat the same bounded resolution-detail read and confirm the observable offline-event evidence rows remain stable across repeated reads after the `--opint-007-b3-smoke` run; do not require a non-observable pre-merge assertion unless this task adds a bounded proof artifact for it.
- `Acceptance Criteria`:
  - Resolution detail can show up to 5 saved offline events as bounded evidence for the matching tracked person and scope.
  - Only `saved` records with matching `tracked_person_id` and `scope_key` are admitted; draft or cross-scope offline events do not leak into resolution evidence.
  - The admitted offline-event candidate list is ordered by `saved_at_utc` descending then `id` descending, capped at 5 items before merge, and the existing final sort-and-limit behavior remains intact after merge.
  - Verification uses the saved-event path only and explicitly requires `Telegram__OwnerUserId`.
  - Verification stays bounded to observable saved-event evidence output unless this task also adds a proof artifact that exposes the pre-merge candidate list.
- `Risks`: Unbounded evidence loading can flood the detail view or accidentally cross scopes.
- `Do Not Do`: Do not query offline events without both `tracked_person_id` and `scope_key`. Do not promote offline-event text directly into durable truth in this task.
- `Expected Artifacts`: Updated resolution evidence loader and resolution detail output that can include offline-event evidence rows.

### Task 9

- `Task ID`: `DTP-009`
- `Title`: Expose bounded offline-event detail and timeline linkage in operator APIs and web
- `Purpose`: Let the operator inspect and correct admitted offline events through the approved operator surfaces.
- `Track`: `Offline Event Source Admission`
- `Dependencies`: [`DTP-007`]
- `Exact Scope`: Complete the existing offline-event API and web detail flow as a contract-hardening delta over OPINT-007 so the operator can query offline-event detail, inspect clarification history count, and update timeline linkage within the same tracked-person scope. `/api/operator/offline-events/query` remains a collection endpoint and must stay compatible with the existing `offlineEvents` list contract. The single-item envelope `{ accepted, failure_reason, session, offline_event }` is pinned only to `/detail`, `/refine`, and `/timeline-linkage`. Each single-item endpoint in this task must enforce scope checks in the same order: resolve operator auth/session, resolve the active tracked person, reject mismatched tracked-person/session scope before touching repository data with the canonical `session_scope_item_mismatch` reason, load the offline-event record only inside the resolved `tracked_person_id` plus `scope_key`, and only then perform endpoint-specific detail, refinement, or linkage work. Treat that pre-repository ordering as a bounded implementation and code-review invariant; verification for this task is limited to externally observable contract results rather than internal call-order tracing. `/api/operator/offline-events/query` keeps its existing bounded collection contract and is not converted to the single-item record-load sequence or envelope. For `/timeline-linkage`, the endpoint-specific validation order after that shared sequence is fixed: validate `linkage_status`, then `target_family`, then `target_ref`, and only then resolve the linkage target inside the same `tracked_person_id` plus `scope_key` before any persistence attempt. For `/detail`, `/refine`, and `/timeline-linkage`, keep the canonical JSON envelope for both success and failure: `{ accepted, failure_reason, session, offline_event }`. All detail fields for those single-item endpoints live inside `offline_event`, not at the top level, and the nested detail shape is the same on success and failure: `id`, `tracked_person_id`, `scope_key`, `summary`, `confidence`, `clarification_history_count`, `stop_reason`, `linkage_target_family`, `linkage_target_ref`, `scope_bound`, and `found`. This task does not add a `clarification_history` array or payload; clarification inspection is bounded to `clarification_history_count` only. Keep the failure envelope deterministic: before the bounded offline-event record load runs, `offline_event.id`, `offline_event.tracked_person_id`, `offline_event.scope_key`, `offline_event.summary`, `offline_event.confidence`, `offline_event.clarification_history_count`, `offline_event.stop_reason`, `offline_event.linkage_target_family`, and `offline_event.linkage_target_ref` must all be `null`, with `offline_event.scope_bound=false` and `offline_event.found=false`; after the scoped record load runs, `offline_event.scope_bound=true`, and `offline_event.found` must then reflect whether the scoped record was found. `failure_reason` is top-level only. Preserve the current auth-session ordering and the canonical mismatch reason unless the consumer is updated in this same task. Keep the envelope deterministic and compatible. Keep the deterministic `stop_reason` persistence/derivation contract intact by returning the persisted `stop_reason` verbatim rather than re-deriving alternate values in the API or web path.
- `Files/Areas`: `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`, `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`, `src/TgAssistant.Infrastructure/Database/OperatorOfflineEventRepository.cs`, `src/TgAssistant.Core/Models/OperatorOfflineEventModels.cs`
- `Step-by-Step Instructions`:
  1. Verify the existing `/api/operator/offline-events/query` collection endpoint continues to use the existing `offlineEvents` list contract, while `/detail`, `/refine`, and `/timeline-linkage` keep the externally observable bounded contract and failure-envelope behavior described in the exact scope. Treat the pre-repository auth/session then scope-check ordering as a code-review invariant for the bounded implementation rather than a runtime verification assertion.
  2. Return `/detail`, `/refine`, and `/timeline-linkage` responses with the fields `id`, `tracked_person_id`, `scope_key`, `summary`, `confidence`, `clarification_history_count`, `stop_reason`, `linkage_target_family`, `linkage_target_ref`, `scope_bound`, and `found` inside `offline_event`, and keep `failure_reason` only at the top level. Do not move these detail fields to the top level on success or failure. Keep `/query` on the collection shape and do not wrap it in the single-item envelope.
  3. For timeline linkage updates, after the shared auth/session, active-tracked-person, scope-mismatch, and bounded offline-event-load sequence from the exact scope, validate `linkage_status` first, then `target_family`, then `target_ref`, and only then resolve the target within the same `tracked_person_id` and `scope_key` before any persistence attempt. Only `resolution`, `person`, and `alert` are valid `target_family` values, and `target_ref` must be non-empty before any target resolution or persistence attempt.
  4. Make the web detail panel show summary, confidence, clarification history count, stop reason, and current linkage state from the same bounded response object used by the API for `/detail`, `/refine`, and `/timeline-linkage`. Do not add or require a `clarification_history` payload in this task.
  5. Map failure classes to fixed HTTP behavior: `401` for session/auth rejection, `403` for `session_scope_item_mismatch`, `404` for `offline_event_not_found`, and `400` for invalid input including `invalid_target_family`, `invalid_target_ref`, and `unsupported_offline_event_timeline_linkage_status`. `/detail`, `/refine`, and `/timeline-linkage` must preserve the single-item envelope shape with `accepted=false`, top-level `failure_reason`, the current `session`, and an `offline_event` object whose nested detail fields remain present. For `401`, `403`, and any other failure that occurs before the bounded offline-event record load, the record-backed nested fields stay `null` with `scope_bound=false` and `found=false`. For failures after the bounded scoped record load, `scope_bound=true`, and `found` reflects whether that scoped record was found.
  6. Keep UI behavior deny-safe: if detail load fails, keep the page interactive and show an explicit bounded error state that reflects the returned `failure_reason`.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Seed a known offline-event id with `--opint-007-b3-smoke`, then start the host with `Telegram__OwnerUserId` configured and a normal operator auth/session.
  3. On the happy path, use these exact HTTP POST requests for the seeded tracked-person scope and offline-event id: `/api/operator/offline-events/query` with `{ "trackedPersonId": "<seededTrackedPersonId>", "statuses": ["saved"], "sortBy": "updated_at", "sortDirection": "desc", "limit": 100 }`; `/api/operator/offline-events/detail` with `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>" }`; `/api/operator/offline-events/refine` with `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>", "summary": "<bounded verification summary>", "recordingReference": null, "clearRecordingReference": true, "refinementNote": "verification", "submittedAtUtc": "<current-utc-iso>" }`; and `/api/operator/offline-events/timeline-linkage` with `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>", "linkageStatus": "unlinked", "targetFamily": null, "targetRef": null, "linkageNote": "verification", "submittedAtUtc": "<current-utc-iso>" }`. Confirm `/query` still returns the existing `offlineEvents` collection contract, while `/detail`, `/refine`, and `/timeline-linkage` keep the `accepted`, top-level `failure_reason`, `session`, and `offline_event` envelope, the nested offline-event fields stay in one place on both success and failure, clarification inspection remains bounded to `clarification_history_count` only, and the success path returns the persisted `stop_reason`.
  4. POST `/api/operator/offline-events/detail` with `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>" }` and no authenticated operator session, and confirm it returns `401` with the bounded failure envelope where all record-backed `offline_event` fields are `null`, `scope_bound=false`, and `found=false`.
  5. POST `/api/operator/offline-events/detail` with an authenticated operator session but a mismatched `trackedPersonId`, while keeping `offlineEventId` set to the seeded item id, and confirm it returns `403` with `session_scope_item_mismatch` and the same pre-load bounded failure envelope (`scope_bound=false`, `found=false`, and all record-backed `offline_event` fields `null`). Treat repository call-order as a code-review invariant rather than an externally asserted verification step.
  6. POST `/api/operator/offline-events/detail` with the authenticated scoped session, the seeded `trackedPersonId`, and a different bounded-scope `offlineEventId` value that does not exist in that resolved tracked-person scope, and confirm it returns `404` with `offline_event_not_found` and the bounded failure envelope where `scope_bound=true`, `found=false`, and the record-backed nested fields remain `null`.
  7. POST `/api/operator/offline-events/timeline-linkage` after the shared auth/session and bounded record-load sequence with isolated negative cases that hold later fields valid while one earlier field fails: first use `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>", "linkageStatus": "invalid_status", "targetFamily": "resolution", "targetRef": "resolution:validation", "linkageNote": "verification", "submittedAtUtc": "<current-utc-iso>" }` and expect `400` with `unsupported_offline_event_timeline_linkage_status`; then use `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>", "linkageStatus": "linked", "targetFamily": "invalid_family", "targetRef": "resolution:validation", "linkageNote": "verification", "submittedAtUtc": "<current-utc-iso>" }` and expect `400` with `invalid_target_family`; then use `{ "trackedPersonId": "<seededTrackedPersonId>", "offlineEventId": "<seededOfflineEventId>", "linkageStatus": "linked", "targetFamily": "resolution", "targetRef": "", "linkageNote": "verification", "submittedAtUtc": "<current-utc-iso>" }` and expect `400` with `invalid_target_ref`. These isolated cases must prove the fixed validation order `linkage_status`, then `target_family`, then `target_ref`, and only then target resolution/persistence.
- `Acceptance Criteria`:
  - `/api/operator/offline-events/query` remains a collection response compatible with the existing `offlineEvents` list contract, and only `/detail`, `/refine`, and `/timeline-linkage` use the exact single-item envelope `{ accepted, failure_reason, session, offline_event }`.
  - Offline-event detail is viewable in web through the bounded API path for `/detail`, `/refine`, and `/timeline-linkage`, with all detail fields nested under `offline_event`.
  - Clarification inspection in this task is bounded to `clarification_history_count` only; no `clarification_history` array or payload is added to the single-item envelope.
  - Timeline linkage applies the shared endpoint sequence first, then rejects invalid family/ref combinations in the fixed `linkage_status`, `target_family`, `target_ref`, target-resolution order, and cannot persist a target outside the active tracked-person scope.
  - Each failure class returns its pinned HTTP status code, `failure_reason`, and envelope shape instead of a generic 500. `401` and `403` failures keep all record-backed `offline_event` fields `null` with `scope_bound=false` and `found=false`; scoped not-found failures keep record-backed fields `null` with `scope_bound=true` and `found=false`.
  - The returned `stop_reason` is the persisted authoritative value, not a fresh derivation.
  - UI shows bounded failure states instead of blank panels.
  - Existing consumer-facing shapes and mismatch reasons remain compatible unless the same task updates the consumer.
  - Verification uses the exact HTTP POST request shapes above for `/detail`, `/refine`, and `/timeline-linkage`, and isolated negative `/timeline-linkage` cases prove the fixed validation order without relying on compound invalid payloads.
- `Risks`: Loose validation on linkage targets can create bad references; UI JS changes can silently fail.
- `Do Not Do`: Do not add bulk editing or cross-person offline-event search. Do not add raw JSON editors for capture payloads.
- `Expected Artifacts`: Working offline-event detail API responses and working web detail/linkage panel behavior.

### Task 10

- `Task ID`: `DTP-010`
- `Title`: Create a shared truth-label and trust-percent display helper
- `Purpose`: Stop each surface from formatting Fact/Inference/Hypothesis/Recommendation and trust percent differently.
- `Track`: `Trust And Label Parity`
- `Dependencies`: [`DTP-009`]
- `Exact Scope`: Add one shared helper or contract utility for truth-label normalization and trust-percent formatting. Use it only on operator surfaces covered by active authority.
- `Files/Areas`: `src/TgAssistant.Core/Models/OperatorAssistantModels.cs`, `src/TgAssistant.Core/Models/ResolutionModels.cs`, `src/TgAssistant.Telegram/Operator/TelegramOperatorWorkflowService.cs`, `src/TgAssistant.Host/OperatorApi/OperatorAlertsContracts.cs`, `src/TgAssistant.Infrastructure/Database/OperatorAssistantResponseGenerationService.cs`, `src/TgAssistant.Host/Launch/Opint006AssistantResponseSmokeRunner.cs`
- `Step-by-Step Instructions`:
  1. Identify all current operator-surface label and trust formatting code paths, including the formatter path exercised by `src/TgAssistant.Host/Launch/Opint006AssistantResponseSmokeRunner.cs` and `src/TgAssistant.Infrastructure/Database/OperatorAssistantResponseGenerationService.cs`.
  2. Add a shared helper for:
     - supported labels
     - label normalization
     - trust percent conversion from `0..1` floats
     - consistent string formatting where strings are needed
  3. Replace duplicated trust formatting code in Telegram and any operator API contract builders that currently derive percent independently.
  4. Keep label casing exactly aligned with the PRD: `Fact`, `Inference`, `Hypothesis`, `Recommendation`.
  5. Keep trust display in percent only.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-006-b1-smoke`.
  3. Inspect the `--opint-006-b1-smoke` output and the operator-assistant contract payloads exercised by that smoke to confirm trust is displayed as percent and labels keep the same casing.
- `Acceptance Criteria`:
  - One shared helper controls truth-label validity and trust-percent formatting.
  - Telegram and operator API code no longer duplicate percent conversion logic.
  - Existing assistant smoke still passes.
- `Risks`: Changing shared formatting can cause wide UI churn if a surface expected old text.
- `Do Not Do`: Do not invent new labels. Do not switch trust from percent to decimals anywhere.
- `Expected Artifacts`: Shared formatting helper and updated operator-surface consumers.

### Task 11

- `Task ID`: `DTP-011`
- `Title`: Attach label and trust fields to resolution interpretation claims and recommendations
- `Purpose`: Make resolution surfaces publish the same label/trust structure that the assistant surface already uses.
- `Track`: `Trust And Label Parity`
- `Dependencies`: [`DTP-010`]
- `Exact Scope`: Extend resolution interpretation contracts so each claim and review recommendation has an explicit display label and trust percent. DisplayLabel is required. TrustPercent is allowed only where bounded claim-level or recommendation-level confidence exists; otherwise it must be `null`. Do not derive trust from unrelated item-level or loop-level confidence unless that confidence is explicitly added in the same task. Do not change the model-facing JSON schema in this task unless required by internal normalization. For this task, the only explicit uncertainty signal that satisfies the surfaced-claim requirement is `DisplayLabel` set to the exact operator-facing value `Inference` or `Hypothesis`.
- `Files/Areas`: `src/TgAssistant.Core/Models/ResolutionModels.cs`, `src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs`, `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`
- `Step-by-Step Instructions`:
  1. Add `DisplayLabel` and nullable `TrustPercent` on claim output and operator-facing recommendation output only.
  2. Map `fact`, `inference`, and `hypothesis` claim types to the shared label casing.
  3. Map review recommendation output to `Recommendation` only when it is rendered as an operator-facing recommendation object.
  4. Derive `TrustPercent` as `round(Confidence * 100)` when bounded claim-level or recommendation-level `Confidence` exists, else `null`, with no other derivation.
  5. Make `ResolutionReadProjectionService` populate the new fields.
  6. Keep the current structured LLM contract stable unless an internal normalization step requires an additional field outside the model boundary.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`.
  3. Read `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` and reuse its `TrackedPersonId`, `ScopeKey`, and `ScopeItemKey` values as the canonical seed contract for the remaining checks in this task.
  4. Start the host, establish the normal operator auth/session, call HTTP POST `/api/operator/resolution/detail/query` with `{ "trackedPersonId": "<artifact.TrackedPersonId>", "scopeItemKey": "<artifact.ScopeItemKey>", "evidenceLimit": 5, "evidenceSortBy": "observed_at", "evidenceSortDirection": "desc" }`, and confirm claim and recommendation labels use `Fact`, `Inference`, `Hypothesis`, or `Recommendation` with trust percent only where bounded confidence is available.
- `Acceptance Criteria`:
  - Resolution detail can emit explicit operator-facing labels for interpretation claims and review recommendation objects.
  - Recommendations also carry the explicit display label and trust contract where bounded confidence exists.
  - No surface has to infer label casing from lowercase model values.
  - Validation still passes after the contract extension.
  - Every surfaced claim preserves evidence refs or an explicit uncertainty signal; unsupported certainty must not be implied. In this task, the explicit uncertainty signal is `DisplayLabel="Inference"` or `DisplayLabel="Hypothesis"`.
  - Verification reuses the canonical seed contract from `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` instead of an implicit host/UI seed assumption.
- `Risks`: Contract changes can break existing web or Telegram rendering if fields are renamed unexpectedly.
- `Do Not Do`: Do not fabricate trust values for claims that do not have a defensible source. Do not relabel runtime-control fallback text as `Fact`.
- `Expected Artifacts`: Updated resolution interpretation contracts and populated claim/recommendation display fields.

### Task 12

- `Task ID`: `DTP-012`
- `Title`: Render label and trust parity across Telegram and web resolution surfaces
- `Purpose`: Consume the shared label/trust contract directly in operator UI instead of re-deriving it per surface.
- `Track`: `Trust And Label Parity`
- `Dependencies`: [`DTP-011`]
- `Exact Scope`: Update Telegram resolution rendering and web resolution/detail rendering to show shared labels and trust percent where the API now exposes them.
- `Files/Areas`: `src/TgAssistant.Telegram/Operator/TelegramOperatorWorkflowService.cs`, `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`, `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`
- `Step-by-Step Instructions`:
  1. Trace Telegram resolution-card and detail rendering methods that currently print trust or interpretation text.
  2. Replace any local label derivation with the shared display values from the resolution payload.
  3. Render each claim as `[DisplayLabel] [TrustPercent%]` before claim text in Telegram and web detail.
  4. Omit the percent token if `TrustPercent` is `null`.
  5. Queue and summary rows render no label unless payload has a claim object with `DisplayLabel`.
  6. Keep the UI compact. Do not redesign layout or navigation in this task.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`, then read `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` and reuse its `TrackedPersonId`, `ScopeKey`, and `ScopeItemKey` values as the canonical seed contract for the remaining checks in this task.
  3. Start the host with `Telegram__OwnerUserId` configured and use that canonical seed contract to open the same item in Telegram operator mode and in `/operator/resolution?trackedPersonId=<artifact.TrackedPersonId>&scopeItemKey=<artifact.ScopeItemKey>&activeMode=resolution_detail`; confirm labels and trust percent match, with `TrustPercent` omitted when null and no raw float shown.
- `Acceptance Criteria`:
  - Telegram and web resolution surfaces render the same label casing and trust-percent format for the same item.
  - Summary-only rows do not show fabricated labels.
  - No surface renders trust as a raw float.
  - Rendered claims preserve upstream evidence refs or explicit uncertainty instead of implying certain truth when the source is incomplete.
  - Verification reuses the canonical seed contract from `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json` before Telegram and web UI checks.
- `Risks`: UI code can drift if one surface still formats locally.
- `Do Not Do`: Do not add new UI sections. Do not show label chips for queue rows that do not have underlying claim objects.
- `Expected Artifacts`: Updated Telegram and web renderers that consume shared label/trust values.

### Task 13

- `Task ID`: `DTP-013`
- `Title`: Add a bounded operator home summary API
- `Purpose`: Replace the static operator home shell with a live bounded data source that matches the active web-home authority.
- `Track`: `Web Home And Dashboard Closure`
- `Dependencies`: [`DTP-012`]
- `Exact Scope`: Add one operator API HTTP GET endpoint at `/api/operator/home/summary` that returns only the home-page data required by the PRD: navigation counts, system status, critical unresolved count, active tracked-person count, and recent significant updates. The home navigation includes `Assistant` as a destination button, but authoritative counts are pinned only to `Resolution`, `Persons`, `Alerts`, and `Offline Events`. `criticalUnresolvedCount` must be read from the backend home summary read model's canonical critical-resolution-unresolved value, and that read model is the sole authoritative owner for this field in the API contract; do not derive, recompute, or substitute it from navigation counts, other aggregates, or client-side state. If no existing field, property, or method in the listed task files already exposes that canonical owner value, stop, record a blocked outcome for missing canonical owner exposure, and do not start `DTP-014` or `DTP-015` until that owner is made available within the bounded task files/areas. The response contract is fixed as `{ navigationCounts, systemStatus, criticalUnresolvedCount, activeTrackedPersonCount, recentUpdates, degradedSources }`, where `navigationCounts`, `systemStatus`, `criticalUnresolvedCount`, `activeTrackedPersonCount`, and `recentUpdates` are nullable sections and `degradedSources` is always present. `navigationCounts`, when present, must expose required integer fields `resolution`, `persons`, `alerts`, and `offlineEvents`. `navigationCounts.resolution`, `navigationCounts.persons`, `navigationCounts.alerts`, and `navigationCounts.offlineEvents` must each be read only from their own canonical bounded owner value already exposed within the listed task files/areas for that navigation destination; do not derive, recompute, or substitute any of them from sibling navigation counts, `criticalUnresolvedCount`, alert totals, recent updates, ad hoc aggregates, or client-side state. If any of those four canonical `navigationCounts` owner values is not already exposed through an existing field, property, or method in the listed task files, stop, record a blocked outcome for missing canonical `navigationCounts` owner exposure, and do not start `DTP-014` or `DTP-015` until that owner is made available within the bounded task files/areas. `systemStatus` must remain a nullable enum-backed contract rather than free-form text, and each `recentUpdates` item must be exactly `{ id, occurredAtUtc, summary, targetUrl }` with `targetUrl` bounded to this approved route allow-list only: `/operator`, `/operator/resolution`, `/operator/resolution?trackedPersonId=<guid>&scopeItemKey=<scopeItemKey>&activeMode=<resolution_queue|resolution_detail|assistant>`, `/operator/persons`, `/operator/person-workspace?trackedPersonId=<guid>`, `/operator/alerts`, and `/operator/offline-events`. For this task, the explicit uncertainty signal for an unavailable summary section is the pair of that section set to `null` and its exact section name present in `degradedSources`; `null` without the matching `degradedSources` entry, or a `degradedSources` entry without the matching `null`, does not satisfy the contract. In production partial-failure mode, set `degradedSources` to exactly the failed section names from the fixed full order `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]`, filtered to the failed sections only; set only those failed sections to `null`, and keep successful sections populated. Before calling the endpoint, establish operator auth/session through the normal operator login flow and use that authenticated session for the GET request. Provide the deterministic verification-only local override `OperatorHomeSummary:ForceDegradedSummary=true` through this task so degraded-summary behavior can be reproduced without ad hoc failure stubs or ambient network faults. When the override is set, every nullable section must be `null` and `degradedSources` must be exactly `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]` in that order.
- `Files/Areas`: `src/TgAssistant.Core/Configuration/Settings.cs`, `src/TgAssistant.Host/appsettings.json`, `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`, `src/TgAssistant.Host/OperatorApi/OperatorAlertsProjectionBuilder.cs`, `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`, `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`
- `Step-by-Step Instructions`:
  1. Define a small response contract for operator home summary data with these exact rules: `navigationCounts` is either `null` or an object with required integer fields `resolution`, `persons`, `alerts`, and `offlineEvents`; `systemStatus` is either `null` or a non-null enum-backed value; `criticalUnresolvedCount` and `activeTrackedPersonCount` are either `null` or non-negative integers; `recentUpdates` is either `null` or an array of non-null update objects with exact fields `id`, `occurredAtUtc`, `summary`, and `targetUrl`; and `degradedSources` is always a string array. `Assistant` remains a navigation destination, but its count is not required unless a separate authoritative source is explicitly defined. `targetUrl` is approved only when it equals one of these bounded routes: `/operator`, `/operator/resolution`, `/operator/resolution?trackedPersonId=<guid>&scopeItemKey=<scopeItemKey>&activeMode=<resolution_queue|resolution_detail|assistant>`, `/operator/persons`, `/operator/person-workspace?trackedPersonId=<guid>`, `/operator/alerts`, or `/operator/offline-events`. The fixed full `degradedSources` order is `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]`; production partial-failure responses must return the filtered subsequence of that full order containing only failed sections.
  2. Reuse existing bounded read services for:
     - resolution counts
     - active tracked-person counts
     - system/runtime state
     - recent significant alert or update items
     Read `navigationCounts.resolution`, `navigationCounts.persons`, `navigationCounts.alerts`, and `navigationCounts.offlineEvents` only from their respective canonical bounded owner values already exposed within the listed task files/areas for those navigation destinations; do not recalculate them from sibling navigation counts, `criticalUnresolvedCount`, alert totals, recent updates, ad hoc aggregates, or client-side state. If the listed task files do not already expose any one of those canonical owner values through an existing field, property, or method, stop with a blocked outcome and do not start `DTP-014` or `DTP-015` instead of adding a new substitute owner or aggregate in this task. Read `criticalUnresolvedCount` only from the backend home summary read model's canonical critical-resolution-unresolved value; do not recalculate it from navigation counts, alert totals, or client-side state. If the listed task files do not already expose that canonical owner value through an existing field, property, or method, stop with a blocked outcome and do not start `DTP-014` or `DTP-015` instead of adding a new substitute owner in this task.
  3. Add one `/api/operator/home/summary` HTTP GET endpoint that requires normal operator auth/session context.
  4. Keep the response bounded and shape it exactly as `{ navigationCounts, systemStatus, criticalUnresolvedCount, activeTrackedPersonCount, recentUpdates, degradedSources }` with no additional top-level fields.
  5. Cap `recentUpdates` at 5 items after sorting the candidate updates by `occurredAtUtc` descending and `id` descending, and keep the ordering deterministic in both happy and degraded paths.
  6. Return unresolved counts for the approved navigation targets only: `Resolution`, `Persons`, `Alerts`, and `Offline Events`. Keep `Assistant` as a navigation destination, but do not require a numeric count for it unless an authoritative source is explicitly defined.
  7. On the verification override path, return HTTP 200 with `navigationCounts`, `systemStatus`, `criticalUnresolvedCount`, `activeTrackedPersonCount`, and `recentUpdates` all set to `null`, keep `degradedSources` fixed to the ordered list from step 1, and keep `degradedSources` present even when the override is unset and the happy path is used. The explicit uncertainty signal for any unavailable section in this task is exactly the matching `null` section plus its matching entry in `degradedSources`.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Before starting the host, inspect the listed DTP-013 task files and confirm an existing field, property, or method already exposes the canonical owner values for `navigationCounts.resolution`, `navigationCounts.persons`, `navigationCounts.alerts`, `navigationCounts.offlineEvents`, and the backend home summary read model's canonical critical-resolution-unresolved value within this task's file bounds. If any `navigationCounts` owner exposure is absent, record `BLOCKED: canonical navigationCounts owner not exposed within DTP-013 files/areas`, stop verification immediately, and do not start `DTP-014` or `DTP-015`. If the `criticalUnresolvedCount` owner exposure is absent, record `BLOCKED: canonical criticalUnresolvedCount owner not exposed within DTP-013 files/areas`, stop verification immediately, and do not start `DTP-014` or `DTP-015`.
  3. If step 2 passes, start the host with `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj` with `OperatorHomeSummary:ForceDegradedSummary` unset, complete the normal operator login flow so the authenticated session is established, and call the HTTP GET endpoint on the happy path; confirm the JSON includes all six top-level fields, `recentUpdates` contains at most 5 items in descending recency order, every non-null `recentUpdates[*].targetUrl` matches the approved route allow-list from step 1, and happy-path `degradedSources` is empty.
  4. Stop the host, restart it with `OperatorHomeSummary:ForceDegradedSummary=true`, re-establish the operator auth/session, and call the same HTTP GET endpoint again; confirm the forced degraded response returns HTTP 200 with all nullable sections set to `null` and `degradedSources` in the fixed order from step 1. Do not rely on toggling the override without a restart/reload.
  5. If a production partial-failure path is exercised, confirm the response still returns HTTP 200, only the failed sections are null, `degradedSources` equals the filtered subsequence of `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]` containing exactly those failed sections, and successful sections remain populated.
- `Acceptance Criteria`:
  - A single bounded HTTP GET API endpoint provides all home-page summary data, with Assistant retained as a navigation destination and counts pinned only to `Resolution`, `Persons`, `Alerts`, and `Offline Events`.
  - `navigationCounts.resolution`, `navigationCounts.persons`, `navigationCounts.alerts`, and `navigationCounts.offlineEvents` are each sourced only from their own canonical bounded owner values already exposed within the listed task files/areas for those destinations and are not derived from sibling navigation counts, `criticalUnresolvedCount`, alert totals, recent updates, ad hoc aggregates, or client-side state.
  - If any canonical `navigationCounts` owner is not already exposed within the listed task files, the task stops with a blocked outcome and `DTP-014` plus `DTP-015` do not start rather than introducing a new owner, substitute aggregate, or fallback count.
  - Verification records `BLOCKED: canonical navigationCounts owner not exposed within DTP-013 files/areas` and halts `DTP-014` plus `DTP-015` when any `navigationCounts` owner exposure is absent.
  - `criticalUnresolvedCount` is sourced only from the backend home summary read model's canonical critical-resolution-unresolved value and is not derived from navigation counts, other aggregates, or client-side state.
  - If the canonical `criticalUnresolvedCount` owner is not already exposed within the listed task files, the task stops with a blocked outcome and `DTP-014` plus `DTP-015` do not start rather than introducing a new owner or substitute aggregate.
  - Verification records `BLOCKED: canonical criticalUnresolvedCount owner not exposed within DTP-013 files/areas` and halts `DTP-014` plus `DTP-015` when that owner exposure is absent.
  - The endpoint does not expose raw debug, admin, or cross-scope data.
  - In production partial-failure mode, only the failed sections are null, `degradedSources` equals the filtered subsequence of the fixed full order `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]`, and successful sections remain populated.
  - On the forced degraded response, every nullable section becomes `null`, `degradedSources` lists the exact section names in the fixed order, and the endpoint still returns HTTP 200.
  - `systemStatus` stays nullable and enum-backed, and `recentUpdates` items keep the exact bounded shape and deterministic cap/sort behavior.
  - `recentUpdates[*].targetUrl` is limited to the approved route allow-list from the exact scope and step 1, and section-level uncertainty is signaled only by the matching `null` section plus the matching `degradedSources` entry.
  - The verification-only `OperatorHomeSummary:ForceDegradedSummary=true` override deterministically exercises the degraded path.
- `Risks`: Combining multiple bounded sources can create accidental N+1 query patterns or brittle failure behavior.
- `Do Not Do`: Do not add free-form diagnostics or raw database views. Do not add counts for unapproved pages.
- `Expected Artifacts`: New operator home summary API contract and working endpoint.

### Task 14

- `Task ID`: `DTP-014`
- `Title`: Replace the static operator home page with live navigation and dashboard widgets
- `Purpose`: Close the PRD-required web home shape using the bounded summary API instead of hard-coded copy.
- `Track`: `Web Home And Dashboard Closure`
- `Dependencies`: [`DTP-013`]
- `Exact Scope`: Update `/operator` so it fetches the HTTP GET `/api/operator/home/summary` endpoint and renders navigation counts, critical blockers, system status, active tracked persons, and recent significant updates. Keep the home navigation aligned to the live summary contract and the active authority set only, including `Assistant` as a navigation destination. Before opening `/operator`, establish the normal operator auth/session flow so the page fetch runs with an authenticated operator session. Consume the verification-only local config override `OperatorHomeSummary:ForceDegradedSummary=true` defined by DTP-013 to induce the deterministic degraded summary behavior during verification runs; do not rely on ad hoc failure stubs or ambient network faults. When the summary response is successful but degraded because `navigationCounts == null` or `degradedSources` is non-empty, set `data-state='degraded'` on the home container while keeping the navigation links visible and leaving the `home-system-status`, `home-critical-unresolved`, `home-active-persons`, and `home-recent-updates` markers mounted with degraded fallback content. In production partial-failure cases, keep successful sections rendered from the live payload and only fall back the sections named in `degradedSources`. Keep fetch-failure degraded behavior too.
- `Files/Areas`: `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`
- `Step-by-Step Instructions`:
  1. Replace the current hard-coded home-page counts and snapshot text with client-side rendering backed by HTTP GET `/api/operator/home/summary`.
  2. Keep the existing route `/operator`.
  3. Render stable ids `home-system-status`, `home-critical-unresolved`, `home-active-persons`, and `home-recent-updates`.
  4. Render navigation buttons with live counts for `Resolution`, `Persons`, `Alerts`, and `Offline Events`. Keep `Assistant` as a navigation button, but do not require a numeric count for it unless the endpoint explicitly provides one.
  5. Add loading, degraded, and empty states, and on summary fetch failure set `data-state='degraded'` on the home container while keeping navigation links visible. Also set `data-state='degraded'` when the summary request succeeds but returns `navigationCounts == null` or a non-empty `degradedSources` array. When `degradedSources` is non-empty in production, keep the non-failed sections populated and only render degraded fallback content for the failed sections.
  6. Keep navigation-first layout. Do not turn home into a deep analysis page.
  7. If the fetch fails, show a bounded degraded-state card and keep navigation links visible.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Start the host with `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj`.
  3. Establish the normal operator auth/session flow, open `/operator` with `OperatorHomeSummary:ForceDegradedSummary` unset, and confirm the page loads live counts and dashboard data on the happy path.
  4. Restart with the temporary local config override `OperatorHomeSummary:ForceDegradedSummary=true`, and confirm the home page still renders degraded-state navigation instead of a blank page, with `data-state='degraded'` on the home container, navigation links still visible, and the `home-system-status`, `home-critical-unresolved`, `home-active-persons`, and `home-recent-updates` markers still rendered with degraded fallback content.
  5. If a production partial-failure path is exercised, confirm the page keeps successful sections rendered and only falls back the sections named in `degradedSources`.
- `Acceptance Criteria`:
  - `/operator` no longer relies on hard-coded operational counts.
  - The page renders all PRD-required dashboard items from live bounded data.
  - Navigation buttons show live counts when `navigationCounts` is present and show a bounded degraded state when it is `null` or when the summary response is otherwise marked degraded through a non-empty `degradedSources` array.
  - In production partial-failure cases, the page keeps successful summary sections visible and only falls back the sections named in `degradedSources`.
  - The DTP-013 verification override leaves a usable degraded home page with `data-state='degraded'`, navigation links still visible, and the `home-system-status`, `home-critical-unresolved`, `home-active-persons`, and `home-recent-updates` markers still rendered with degraded fallback content.
- `Risks`: Inline JS changes can break the operator home page silently if fetch or DOM wiring is wrong.
- `Do Not Do`: Do not redesign other operator pages. Do not add new navigation destinations.
- `Expected Artifacts`: Updated operator home HTML/JS and a working live dashboard backed by bounded API data.

### Task 15

- `Task ID`: `DTP-015`
- `Title`: Add a smoke runner for operator home and dashboard closure
- `Purpose`: Lock the new home/dashboard behavior before further UI work starts.
- `Track`: `Web Home And Dashboard Closure`
- `Dependencies`: [`DTP-014`]
- `Exact Scope`: Add one smoke runner that validates the HTTP GET home summary endpoint shape, bounded navigation URLs, live widget rendering markers, live unresolved counts on the `Resolution`, `Persons`, `Alerts`, and `Offline Events` buttons, and degraded-state fallback markers. Keep `Assistant` as a navigation button, but do not require a numeric count for it unless the API contract later defines one. Reuse the DTP-013 approved route allow-list exactly for both `recentUpdates[*].targetUrl` and rendered home navigation URLs: `/operator`, `/operator/resolution`, `/operator/resolution?trackedPersonId=<guid>&scopeItemKey=<scopeItemKey>&activeMode=<resolution_queue|resolution_detail|assistant>`, `/operator/persons`, `/operator/person-workspace?trackedPersonId=<guid>`, `/operator/alerts`, and `/operator/offline-events`. Keep the smoke aligned to the live summary GET contract and the same `OperatorHomeSummary:ForceDegradedSummary=true` verification flag defined by DTP-013 so the forced degraded path is deterministic. The smoke must assert the happy-path summary returns all six top-level fields, `degradedSources` is empty, the forced degraded path returns HTTP 200 with every nullable section set to `null` and `degradedSources` exactly `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]`, and any production partial-failure path returns only the failed sections as `null` while keeping successful sections populated. For production partial-failure assertions, `degradedSources` must equal the filtered subsequence of the fixed full order `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]` containing exactly the failed sections.
- `Files/Areas`: `src/TgAssistant.Host/Launch`, `src/TgAssistant.Host/Program.cs`, `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`, `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`
- `Step-by-Step Instructions`:
  1. Add a new home/dashboard smoke runner under `src/TgAssistant.Host/Launch`.
  2. Make the runner verify happy-path assertions separately from degraded-path assertions after the normal operator auth/session flow is established: happy-path checks cover HTTP GET summary endpoint bounded fields only, home HTML live widget markers, `recentUpdates[*].targetUrl` values and navigation URLs inside the exact DTP-013 approved route allow-list, and navigation buttons surfacing live unresolved counts when present for `Resolution`, `Persons`, `Alerts`, and `Offline Events`; `Assistant` remains a navigation button without a required count unless the API explicitly supplies one. Degraded-path checks use `OperatorHomeSummary:ForceDegradedSummary=true` and cover the exact null-section contract from DTP-013 plus the summary-unavailable fallback markers with the same `data-state='degraded'` contract as DTP-014. If a production partial-failure path is exercised, assert that only the failed sections are null, `degradedSources` equals the filtered subsequence of `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]` containing exactly those failures, and successful sections remain populated.
  3. Register the CLI switch `--opint-home-dashboard-smoke` in `Program.cs`.
  4. Write the smoke result to `src/TgAssistant.Host/artifacts/operator-home/opint-home-dashboard-smoke.json`.
  5. Fail the command when any happy-path marker `home-system-status`, `home-critical-unresolved`, `home-active-persons`, or `home-recent-updates` is missing in the happy-path render, or when the degraded-path checks are missing; require `data-state='degraded'` only in the degraded scenario. The happy-path count checks must cover only `Resolution`, `Persons`, `Alerts`, and `Offline Events`.
- `Verification Steps`:
  1. Run `dotnet build TelegramAssistant.sln`.
  2. Run the new smoke command, for example `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-home-dashboard-smoke`.
  3. Inspect the generated artifact and confirm the happy-path marker assertions pass separately from the degraded-path marker assertions.
- `Acceptance Criteria`:
  - The repository has a dedicated runnable smoke for the operator home/dashboard path.
  - The smoke covers both happy path and the forced degraded-state fallback.
  - The smoke asserts live unresolved counts on the approved navigation buttons `Resolution`, `Persons`, `Alerts`, and `Offline Events`, and the forced degraded branch when `navigationCounts` is null or `degradedSources` is non-empty and matches the fixed ordered list.
  - Any production partial-failure branch covered by the smoke keeps successful sections populated and returns only the failed sections in `degradedSources` using the filtered fixed full order `["navigationCounts", "systemStatus", "criticalUnresolvedCount", "activeTrackedPersonCount", "recentUpdates"]`.
  - The smoke fails on escaped URLs, missing widgets, or missing degraded-state markers.
- `Risks`: New smoke coverage can become brittle if it asserts cosmetic text instead of bounded behavior.
- `Do Not Do`: Do not assert unrelated page styling. Do not add network calls to external services in the smoke.
- `Expected Artifacts`: New home/dashboard smoke runner, new CLI switch registration, and a passing smoke artifact.
