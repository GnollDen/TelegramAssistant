# Detailed Implementation Task Pack (Phase B)

Date: `2026-04-06`  
Mode: `single-active-agent sequential orchestration`

Status: `pre-execution consistency gate required` (`docs/planning/PROJECT_AGENT_RULES_2026-04-06.md` restored in Git: `e068bcb`, `2026-04-06`)

## Scope and Rules

- Junior-execution overlay for this pack:
  - `docs/planning/artifacts/task-by-task-junior-hardening-2026-04-06.md`
  - Use this overlay as authoritative for split/preflight/verification precision.
- For the current run, `Phase A` (`DTP-001..015`) is not executed and must be treated as prerequisite execution chain, not completed baseline.
- This pack covers only missing AI-centric requirements `B1..B6`.
- Active product authority for this pack:
  - `PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`
  - `LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md`
  - `OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md`
  - `AI_CENTRIC_CONTEXT_LOOP_ADDENDUM_2026-04-05.md`
- Planning input used to generate this pack:
  - `AI_CENTRIC_REQUIREMENTS_SUPPLEMENT_2026-04-06.md` (not direct execution authority by itself)
- Non-authority for direct execution decisions:
  - `AI_CONFLICT_RESOLUTION_SESSION_DESIGN_2026-04-06.md` (reference-only)
- Global invariants:
  - deterministic layer is sole durable writer
  - model output never writes durable truth directly
  - scope-local retrieval and writes only
  - Telegram stays compact; web is deep-analysis surface
  - publication honesty is mandatory (`insufficient evidence`, `escalation-only`, `manual review required`)
  - execution remains gated until authority-consistency recheck is completed with restored rules file

## Pre-Execution Gate

- `Gate Owner`: the single active executor assigned to this Phase B pack.
- `Blocking Artifact`: `docs/planning/artifacts/phase-b-2026-04-06-pre-execution-gate.md`
- `External Prerequisite Gate`: `docs/planning/artifacts/dtp-2026-04-06-pre-execution-gate.md` must be `status: pass` before this Phase-B gate can be set to `pass`.
- `Artifact Required Fields`: `pack_id`, `rules_file_path`, `rules_git_commit`, `authority_docs_checked`, `dependency_order_confirmed`, `proof_command_inventory_checked`, `owner`, `completed_at_utc`, `status`.
- `Execution Authority Note`: for this Phase-B run, `tasks.json` and `task_slices.json` are historical baseline evidence only; do not use `IMPLEMENT-*` backlog items to select, sequence, or validate execution. Execute only `PHB-001..PHB-018` from this pack.
- `Gate Checklist`:
  1. `docs/planning/PROJECT_AGENT_RULES_2026-04-06.md` is present and reviewed at Git commit `e068bcb` or later.
  2. All four listed authority docs were checked against this pack and no unresolved contradiction remains.
  3. Strict dependency order `PHB-001..PHB-018` is acknowledged as sequential with no parallel task execution.
  4. Verification command markers were reviewed: `existing`, `existing; extend in this task`, `to be added by this task`.
  5. Stop/escalate conditions were acknowledged by the named gate owner.
- `Release Condition`: `PHB-001` may start only after the blocking artifact is written and marked `status: pass` by the gate owner.
- `Cross-Pack Release Condition`: `PHB-001` may start only after:
  1. `DTP-001..015` execution chain is completed and recorded as `status: pass` in `docs/planning/artifacts/dtp-2026-04-06-pre-execution-gate.md`.
  2. This Phase-B gate artifact is also `status: pass`.

## Strict Dependency Order

1. `PHB-001`
2. `PHB-002`
3. `PHB-003`
4. `PHB-004`
5. `PHB-005`
6. `PHB-006`
7. `PHB-007`
8. `PHB-008`
9. `PHB-009`
10. `PHB-010`
11. `PHB-011`
12. `PHB-012`
13. `PHB-013`
14. `PHB-014`
15. `PHB-015`
16. `PHB-016`
17. `PHB-017`
18. `PHB-018`

## Tasks

Execution precondition for every task in this section:
- Do not execute `PHB-*` tasks until authority-consistency recheck is completed with `docs/planning/PROJECT_AGENT_RULES_2026-04-06.md` included and the blocking artifact `docs/planning/artifacts/phase-b-2026-04-06-pre-execution-gate.md` is present with `status: pass`.

Verification command markers used below:
- `(existing)`: command/CLI switch exists in the repo as of `2026-04-06`.
- `(existing; extend in this task)`: command exists, but this task must expand its output contract or proof matrix before rerun.
- `(to be added by this task)`: task must add the command/CLI switch before verification can run.

### Task 1

- `Task ID`: `PHB-001`
- `Title`: Define Stage6/7/8 semantic ownership contract
- `Purpose`: Freeze stage ownership to prevent semantic blending.
- `Track`: `WS-B6`
- `Dependencies`: []
- `Deterministic Writer / Read Owner`: `read-only contract task` (no durable write owner introduced)
- `Exact Scope`: Add shared contract for stage-owned output families, accepted input families, handoff reasons, and one explicit mapping layer into existing routing surfaces; Stage6 is transient enrichment-only, while Stage7 owns durable dossier/profile, pair-dynamics, timeline, and case-formation outputs. Contract-only task.
- `Files/Areas`: new `src/TgAssistant.Core/Models/StageSemanticContractModels.cs`; `src/TgAssistant.Core/Models/Stage6BootstrapModels.cs`; `src/TgAssistant.Core/Models/Stage7DossierProfileModels.cs`; `src/TgAssistant.Core/Models/Stage7PairDynamicsModels.cs`; `src/TgAssistant.Core/Models/Stage7TimelineModels.cs`; `src/TgAssistant.Core/Models/Stage8RecomputeQueueModels.cs`
- `Step-by-Step Instructions`:
  1. Add shared constants/types for `stage`, `owned_output_family`, `accepted_input_family`, `handoff_reason`.
  2. Encode Stage6 ownership as bootstrap-graph and discovery-pool outputs only.
  3. Encode Stage7 ownership as durable profile, pair-dynamics, timeline, and case formation outputs only.
  4. Encode Stage8 ownership as recompute/reintegration outputs only.
  5. Add helper validation methods for Stage6->Stage7 and Stage7->Stage8 handoffs and one shared mapping layer from semantic families to existing `Stage7DurableObjectFamilies` and `Stage8RecomputeTargetFamilies`.
  6. Add explicit canonical values:
     - `owned_output_family`: `stage6_bootstrap_graph`, `stage6_discovery_pool`, `stage7_durable_profile`, `stage7_pair_dynamics`, `stage7_durable_timeline`, `stage7_case_pool`, `stage8_recompute_request`, `stage8_reintegration_update`.
     - `accepted_input_family`: `stage6_seed_scope`, `stage6_bootstrap_graph`, `stage6_discovery_pool`, `stage7_case_pool`, `stage7_durable_profile`, `stage7_pair_dynamics`, `stage7_durable_timeline`, `stage8_recompute_request`.
     - `handoff_reason`: `bootstrap_complete`, `durable_ready`, `needs_recompute`, `recompute_applied`, `stage_contract_violation`.
  7. Add explicit semantic-to-routing mappings:
     - `stage7_durable_profile` -> Stage7 durable families `dossier`, `profile`; Stage8 recompute target `dossier_profile`
     - `stage7_pair_dynamics` -> Stage7 durable family `pair_dynamics`; Stage8 recompute target `pair_dynamics`
     - `stage7_durable_timeline` -> Stage7 durable families `event`, `timeline_episode`, `story_arc`; Stage8 recompute target `timeline_objects`
     - `stage7_case_pool` -> semantic contract family only in this Phase-B pack; no direct runtime family expansion is required in PHB-001.
- `Verification Steps`: `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`:
  - Shared contract exists and compiles.
  - Stage ownership can be validated by constants/helpers, not ad hoc strings.
  - `stage6_discovery_pool` and `stage7_pair_dynamics` are represented explicitly in the contract and mapping layer.
- `Risks`: Overcomplicated contract model.
- `Do Not Do`: No DB changes, no runners, no UI/API changes.
- `Expected Artifacts`: Contract file and clean build.

### Task 2

- `Task ID`: `PHB-002`
- `Title`: Enforce Stage6/7/8 contract in stage services
- `Purpose`: Fail closed on invalid stage-family routing.
- `Track`: `WS-B6`
- `Dependencies`: [`PHB-001`]
- `Deterministic Writer / Read Owner`: `Stage6BootstrapService`, `Stage7*FormationService`, `Stage8RecomputeQueueService` (contract-enforcement only)
- `Exact Scope`: Replace literal stage/family routing with the shared contract and mapping layer; preserve happy path across dossier/profile, pair-dynamics, timeline, and recompute routing.
- `Files/Areas`: `src/TgAssistant.Core/Models/StageSemanticContractModels.cs`; `src/TgAssistant.Intelligence/Stage6Bootstrap/Stage6BootstrapService.cs`; `src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs`; `src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs`; `src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs`; `src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs`; `src/TgAssistant.Infrastructure/Database/ResolutionRecomputePlanner.cs`; `src/TgAssistant.Infrastructure/Database/ClarificationBranchStateRepository.cs`
- `Step-by-Step Instructions`:
  1. Replace stage/family literals with shared constants.
  2. Enforce Stage6 output family constraints, including explicit handling for `stage6_discovery_pool`.
  3. Enforce Stage7 output family constraints only for the explicitly listed runtime seams in `Files/Areas` (dossier/profile, pair-dynamics, timeline).
  4. Enforce Stage8 input-family acceptance through the shared mapping layer and deterministic reject reason.
  5. Keep scope-local recompute routing unchanged, but require pair-dynamics routing to resolve through the same shared mapping layer as dossier/profile and timeline.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage6-bootstrap-smoke` `(existing)`
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage7-dossier-profile-smoke` `(existing)`
  4. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage7-pair-dynamics-smoke` `(existing)`
  5. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage7-timeline-smoke` `(existing)`
  6. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage8-recompute-smoke` `(existing)`
- `Acceptance Criteria`: Invalid handoff families and unmapped semantic-family routes are rejected deterministically; all existing stage smokes pass.
- `Risks`: Over-tight constraints blocking valid flows.
- `Do Not Do`: No new target families; no API contract changes.
- `Expected Artifacts`: Contract-enforced stage services and passing smokes.

### Task 3

- `Task ID`: `PHB-003`
- `Title`: Add Stage6/7/8 semantic contract proof runner
- `Purpose`: Produce artifact-backed proof of stage ownership enforcement.
- `Track`: `WS-B6`
- `Dependencies`: [`PHB-002`]
- `Deterministic Writer / Read Owner`: `read-only proof task` (no durable writer)
- `Exact Scope`: Add one proof runner and CLI switch with accepted/rejected handoff cases.
- `Files/Areas`: new runner in `src/TgAssistant.Host/Launch`; `src/TgAssistant.Host/Program.cs`
- `Step-by-Step Instructions`:
  1. Add `StageSemanticContractProofRunner`.
  2. Reuse existing stage seams/stubs.
  3. Include required case matrix:
     - `s6_to_s7_valid`
     - `s7_to_s8_valid`
     - `s8_input_family_invalid`
     - `s8_invalid_input_sets_blocked_status`
  4. Write single artifact at `src/TgAssistant.Host/artifacts/phase-b/stage-semantic-contract-proof.json` with fields: `case_id`, `input_family`, `expected_decision`, `actual_decision`, `expected_status`, `actual_status`, `reason`, `passed`.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage-semantic-contract-proof` `(to be added by this task)`
- `Acceptance Criteria`: Command fails non-zero on contract breach; artifact contains per-case decision, status, and reason.
- `Risks`: Superficial proof bypassing real logic.
- `Do Not Do`: No live model dependency.
- `Expected Artifacts`: CLI switch, runner, proof artifact.

### Task 4

- `Task ID`: `PHB-004`
- `Title`: Define temporal person-state model contract
- `Purpose`: Introduce explicit temporal/supersession state shape.
- `Track`: `WS-B1`
- `Dependencies`: [`PHB-003`]
- `Deterministic Writer / Read Owner`: `read-only contract task` (no durable write owner introduced)
- `Exact Scope`: Contract-only temporal state model.
- `Files/Areas`: new `src/TgAssistant.Core/Models/TemporalPersonStateModels.cs`; `src/TgAssistant.Core/Models/Stage7DossierProfileModels.cs`; `src/TgAssistant.Core/Models/Stage7TimelineModels.cs`
- `Step-by-Step Instructions`:
  1. Add `subject_ref`, `fact_type`, `value`, `valid_from_utc`, `valid_to_utc`, `confidence`, `evidence_refs`, `state_status`, `supersedes_state_id`, `superseded_by_state_id`.
  2. Add categories: stable, temporal, event-conditioned, contested.
  3. Add helpers for current-open vs historical-closed states.
  4. Add shared classifier `TemporalSingleValuedFactFamilies` in `TemporalPersonStateModels.cs` with explicit starter list: `profile_status`, `profile_location`, `relationship_state`, `timeline_primary_activity`; this classifier becomes the only allowed source for single-valued uniqueness checks in `PHB-005` and `PHB-006`.
- `Verification Steps`: `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`:
  - Contract includes `subject_ref`, `fact_type`, `value`, `valid_from_utc`, `valid_to_utc`, `confidence`, `evidence_refs`, `state_status`, `supersedes_state_id`, and `superseded_by_state_id`.
  - Helpers distinguish current-open states from historical-closed states without overwriting prior state rows in the contract shape.
- `Risks`: Mixing conditional logic too early.
- `Do Not Do`: No DB or UI/API work.
- `Expected Artifacts`: Temporal contract model.

### Task 5

- `Task ID`: `PHB-005`
- `Title`: Add deterministic temporal-state persistence and supersession repository
- `Purpose`: Add bounded durable store for temporal states.
- `Track`: `WS-B1`
- `Dependencies`: [`PHB-004`]
- `Deterministic Writer / Read Owner`: `TemporalPersonStateRepository` (deterministic durable writer)
- `Exact Scope`: Add row mappings, migration, repository, and interface wiring for temporal states.
- `Files/Areas`: `src/TgAssistant.Core/Interfaces/IRepositories.cs`; new `src/TgAssistant.Infrastructure/Database/TemporalPersonStateRepository.cs`; `src/TgAssistant.Infrastructure/Database/Ef/DbRows.cs`; `src/TgAssistant.Infrastructure/Database/Ef/TgAssistantDbContext.cs`; new `src/TgAssistant.Infrastructure/Database/Migrations/0056_temporal_person_states.sql`
- `Step-by-Step Instructions`:
  1. Add temporal-state DB row and migration.
  2. Add repository methods for insert, scoped query, open-state lookup, supersession update.
  3. Require scope key and deterministic trigger metadata on writes.
  4. Enforce no duplicate active-open row per `scope + subject + fact_type` for single-valued fact families only unless supersession chain is explicit, using only `TemporalSingleValuedFactFamilies` from `PHB-004`.
  5. Treat multi-valued conditional families as out-of-scope for this task and route them to `PHB-016` conditional storage.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`:
  - Migration and repository support scope-local temporal-state insert, scoped query, open-state lookup, and supersession update with explicit supersession links.
  - Duplicate-open active rows for the same key tuple are rejected by repository constraint/validation and queued for proof validation in `PHB-006`.
  - Scope of uniqueness in this task is explicitly limited to single-valued fact families from `TemporalSingleValuedFactFamilies`; multi-valued conditional families are deferred to `PHB-016`.
- `Risks`: Duplicate-open state bugs.
- `Do Not Do`: No operator endpoint in this task.
- `Expected Artifacts`: Migration + repository + interface changes.

### Task 6

- `Task ID`: `PHB-006`
- `Title`: Integrate temporal writes into Stage7/8 and add proof
- `Purpose`: Make temporal state updates deterministic and historically safe.
- `Track`: `WS-B1`
- `Dependencies`: [`PHB-005`]
- `Deterministic Writer / Read Owner`: `Stage7*Repository` via `TemporalPersonStateRepository`; `Stage8RecomputeTriggerService` is post-apply recompute trigger/validation only in this task
- `Exact Scope`: Wire Stage7 dossier/profile and timeline outputs into temporal repository, keep Stage7 pair-dynamics outside temporal-person-state writes in this task, constrain Stage8 touchpoints to deterministic post-apply recompute trigger/validation only, and add proof for state-change history.
- `Files/Areas`: `src/TgAssistant.Infrastructure/Database/Stage7DossierProfileRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7TimelineRepository.cs`; `src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs`; `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`; `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`; new `src/TgAssistant.Host/Launch/TemporalPersonStateProofRunner.cs`; new `src/TgAssistant.Host/Launch/PersonHistoryProofRunner.cs`; `src/TgAssistant.Host/Program.cs`
- `Step-by-Step Instructions`:
  1. Persist temporal states from Stage7 durable profile and timeline outputs only.
  2. Keep Stage7 pair-dynamics on its existing durable-object path and do not write pair-dynamics rows into `TemporalPersonStateRepository` in this task.
  3. On new accepted value, close prior active row with `valid_to_utc` and link supersession.
  4. Keep historical rows queryable.
  5. If this task touches Stage8 recompute linkage, keep it limited to deterministic post-apply recompute trigger/validation keyed by the exact temporal boundary `scope_key + subject_ref + fact_type`; do not allow Stage8 to perform temporal durable writes or broad family-only/subject-only matching.
  6. Add proof runner with event-conditioned presence/absence and preference-change cases.
  7. Write artifact at `src/TgAssistant.Host/artifacts/phase-b/temporal-person-state-proof.json` with fields: `case_id`, `scope_key`, `subject_ref`, `fact_type`, `previous_state_id`, `new_state_id`, `expected_decision`, `actual_decision`, `supersedes_state_id`, `superseded_by_state_id`, `reason`, `passed`.
  8. Add required negative-path proof cases:
     - `duplicate_open_active_row_rejected`
     - `supersession_link_required_for_replacement`
  9. Add person-history read output surface and proof in this task scope:
     - Add API query `/api/operator/person-workspace/person-history/query`.
     - Return `TemporalPersonHistoryRow` entries with required fields: `state_id`, `scope_key`, `tracked_person_id`, `subject_ref`, `fact_type`, `value`, `valid_from_utc`, `valid_to_utc`, `state_status`, `supersedes_state_id`, `superseded_by_state_id`, `evidence_refs`, `publication_state`.
     - Add proof runner output artifact `src/TgAssistant.Host/artifacts/phase-b/person-history-proof.json` with fields: `case_id`, `scope_key`, `tracked_person_id`, `state_id`, `expected_publication_state`, `actual_publication_state`, `expected_history_order`, `actual_history_order`, `reason`, `passed`.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --temporal-person-state-proof` `(to be added by this task)`
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --person-history-proof` `(to be added by this task)`
- `Acceptance Criteria`:
  - Proof shows preserved history plus current state without destructive overwrite.
  - Artifact contains explicit pass/fail rows for `duplicate_open_active_row_rejected` and `supersession_link_required_for_replacement`.
  - Person-history output is queryable through `/api/operator/person-workspace/person-history/query` and exposes the required `TemporalPersonHistoryRow` fields with evidence-linked publication state.
  - `person-history-proof.json` includes pass/fail rows for ordered history reconstruction and current/open vs historical/closed state separation.
  - Pair-dynamics remains explicitly out of temporal-person-state write scope in this task.
  - Temporal durable writes in this task occur only through `Stage7*Repository -> TemporalPersonStateRepository`; `Stage8RecomputeTriggerService` remains post-apply recompute trigger/validation only.
  - Single-valued fact-family uniqueness constraint from `PHB-005` is enforced using `TemporalSingleValuedFactFamilies`; conditional multi-valued families remain out-of-scope here and enter via `PHB-016`.
- `Risks`: Over/under-supersession.
- `Do Not Do`: No global backfill, no web UI exposure.
- `Expected Artifacts`: Integrated temporal flow + temporal/history proof artifacts.

### Task 7

- `Task ID`: `PHB-007`
- `Title`: Define iterative carry-forward case identity/status contract
- `Purpose`: Stabilize case identity across passes.
- `Track`: `WS-B2`
- `Dependencies`: [`PHB-006`]
- `Deterministic Writer / Read Owner`: `read-only contract task` (no durable writer introduced)
- `Exact Scope`: Contract-only shared model for carry-forward case identity, reintegration-ledger entries, and statuses on the active non-legacy reintegration boundary between Stage7 case formation outputs and Stage8 targeted recompute; do not depend on legacy Stage6 case tables/models in this task.
- `Files/Areas`: new `src/TgAssistant.Core/Models/IterativeCaseModels.cs`; new `src/TgAssistant.Core/Models/ReintegrationLedgerModels.cs`; `src/TgAssistant.Core/Models/ResolutionModels.cs`; `src/TgAssistant.Core/Models/OperatorResolutionModels.cs`
- `Step-by-Step Instructions`:
  1. Add `carry_forward_case_id`, `reintegration_entry_id`, and `origin_source_kind`.
  2. Add statuses: `open`, `resolving_ai`, `resolved_by_ai`, `needs_more_context`, `needs_operator`, `deferred_to_next_pass`, `superseded`.
  3. Add predecessor/successor and resolution linkage fields.
  4. Add reintegration-ledger fields for `scope_key`, `scope_item_key`, `recompute_target_family`, `recompute_target_ref`, `previous_status`, `next_status`, and `recorded_at_utc`; define closed enum `origin_source_kind` with allowed values `stage7_durable_profile`, `stage7_pair_dynamics`, `stage7_durable_timeline`, `stage8_recompute_request`, `resolution_action`.
  5. Add allowed transition helpers.
  6. Add explicit allowed transition matrix:
     - `open -> resolving_ai | needs_more_context | needs_operator | deferred_to_next_pass | superseded`
     - `resolving_ai -> resolved_by_ai | needs_more_context | needs_operator | deferred_to_next_pass | superseded`
     - `needs_more_context -> resolving_ai | deferred_to_next_pass | superseded`
      - `needs_operator -> resolving_ai | deferred_to_next_pass | superseded`
      - `deferred_to_next_pass -> resolving_ai | superseded`
      - `resolved_by_ai -> superseded`
- `Verification Steps`: `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`:
  - `carry_forward_case_id` is distinct from conflict session ID and scope item ID.
  - Contract defines closed `origin_source_kind` enum with only `stage7_durable_profile`, `stage7_pair_dynamics`, `stage7_durable_timeline`, `stage8_recompute_request`, and `resolution_action`.
  - Contract defines the full status set `open`, `resolving_ai`, `resolved_by_ai`, `needs_more_context`, `needs_operator`, `deferred_to_next_pass`, and `superseded`.
  - Transition helpers/matrix allow the listed transitions and reject unlisted transitions.
  - Reintegration contract is explicitly non-legacy and does not require `Stage6Case*` tables/models to satisfy `PHB-001` stage ownership.
- `Risks`: Status drift from legacy names.
- `Do Not Do`: No DB changes yet.
- `Expected Artifacts`: Shared iterative-case contract.

### Task 8

- `Task ID`: `PHB-008`
- `Title`: Persist reintegration ledger and targeted recompute linkage
- `Purpose`: Make carry-forward residue and accepted reintegration durable.
- `Track`: `WS-B2`
- `Dependencies`: [`PHB-007`]
- `Deterministic Writer / Read Owner`: `ResolutionCaseReintegrationService` + `ResolutionCaseReintegrationLedgerRepository` (deterministic durable writer/validator)
- `Exact Scope`: Add bounded non-legacy reintegration ledger with linkage to actions/sessions/recompute targets through one deterministic reintegration-service boundary; do not reuse legacy Stage6 case repositories/tables.
- `Files/Areas`: new `src/TgAssistant.Infrastructure/Database/ResolutionCaseReintegrationService.cs`; new `src/TgAssistant.Infrastructure/Database/ResolutionCaseReintegrationLedgerRepository.cs`; `src/TgAssistant.Core/Interfaces/IRepositories.cs`; `src/TgAssistant.Infrastructure/Database/ClarificationBranchStateRepository.cs`; `src/TgAssistant.Infrastructure/Database/ResolutionActionCommandService.cs`; `src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs`; `src/TgAssistant.Host/Launch/ResolutionRecomputeContractSmokeRunner.cs`; new `src/TgAssistant.Infrastructure/Database/Migrations/0057_resolution_case_reintegration_ledger.sql`
- `Step-by-Step Instructions`:
  1. Add `ResolutionCaseReintegrationService` as the single deterministic owner for ledger writes and targeted recompute-link validation.
  2. Add `ResolutionCaseReintegrationLedgerRepository` keyed by `carry_forward_case_id`; do not persist reintegration state in `Stage6Case*` tables.
  3. Persist status transitions and linkage IDs.
  4. Persist unresolved residue for next pass.
  5. Persist targeted recompute linkage after accepted outcomes using exact validation tuple `scope_key + carry_forward_case_id + recompute_target_family + recompute_target_ref`.
  6. Preserve superseded history.
  7. Extend `--resolution-recompute-contract-smoke` so it executes through `ResolutionCaseReintegrationService -> ResolutionCaseReintegrationLedgerRepository`, writes to the real repository-backed ledger, and fails if validation only passes with in-memory doubles.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-recompute-contract-smoke` `(existing; extend in this task to prove repository-backed reintegration-ledger writes/readback through ResolutionCaseReintegrationService)`
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage8-recompute-smoke` `(existing)`
- `Acceptance Criteria`:
  - Reintegration ledger persists `carry_forward_case_id`, status transitions, linkage IDs, unresolved residue, and targeted recompute linkage through `ResolutionCaseReintegrationService` as the sole write boundary.
  - Cross-scope or stale linkage writes are rejected by deterministic validation and queued for proof validation in `PHB-009`.
  - Verification in this task proves repository-backed reintegration-ledger persistence/readback; in-memory-only success does not satisfy acceptance.
  - `PHB-009` proof must execute through `ResolutionCaseReintegrationService`, not direct repository seeding.
  - No legacy `Stage6Case*` repository/table remains an ownership dependency for this task.
- `Risks`: ID churn, stale linkage.
- `Do Not Do`: Do not reuse conflict-session tables as reintegration ledger.
- `Expected Artifacts`: Ledger schema and integrated linkage.

### Task 9

- `Task ID`: `PHB-009`
- `Title`: Add iterative reintegration multi-pass proof
- `Purpose`: Prove stable case identity and status transitions across passes.
- `Track`: `WS-B2`
- `Dependencies`: [`PHB-008`]
- `Deterministic Writer / Read Owner`: `read-only proof task` (no durable writer)
- `Exact Scope`: New proof runner with unresolved/deferred and resolved-by-AI scenarios executed through the non-legacy reintegration service/repository boundary from `PHB-008`.
- `Files/Areas`: new runner in `src/TgAssistant.Host/Launch`; `src/TgAssistant.Host/Program.cs`
- `Step-by-Step Instructions`:
  1. Seed unresolved-to-pass2 case through `ResolutionCaseReintegrationService`.
  2. Seed resolved-by-ai-in-pass2 case through `ResolutionCaseReintegrationService`.
  3. Assert stable `carry_forward_case_id`.
  4. Assert expected status transitions and recompute linkage.
  5. Add required negative-path proof cases:
     - `cross_scope_linkage_rejected`
     - `stale_recompute_linkage_rejected`
  6. Write artifact at `src/TgAssistant.Host/artifacts/phase-b/iterative-pass-reintegration-proof.json` with fields: `case_id`, `pass_index`, `scope_key`, `scope_item_key`, `carry_forward_case_id`, `ledger_entry_id`, `previous_status`, `next_status`, `recompute_target_family`, `recompute_target_ref`, `expected_decision`, `actual_decision`, `reason`, `passed`.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --iterative-reintegration-proof` `(to be added by this task)`
- `Acceptance Criteria`:
  - Artifact proves stable IDs + valid transitions.
  - Artifact contains explicit pass/fail rows for `cross_scope_linkage_rejected` and `stale_recompute_linkage_rejected`.
  - Proof executes through `ResolutionCaseReintegrationService` against `ResolutionCaseReintegrationLedgerRepository`, not direct repository seeding or legacy Stage6 case storage.
- `Risks`: Runner bypassing real repos.
- `Do Not Do`: No one-pass-only proof.
- `Expected Artifacts`: `iterative-pass-reintegration-proof.json`.

### Task 10

- `Task ID`: `PHB-010`
- `Title`: Externalize conflict-session budgets and full audit contract
- `Purpose`: Settings-backed bounded session control and full audit shape.
- `Track`: `WS-B3`
- `Dependencies`: [`PHB-009`]
- `Deterministic Writer / Read Owner`: `OperatorResolutionApplicationService` (deterministic session state writer)
- `Exact Scope`: Move budget/runtime constants to settings and add usage/audit fields.
- `Files/Areas`: `src/TgAssistant.Core/Configuration/Settings.cs`; `src/TgAssistant.Host/appsettings.json`; `src/TgAssistant.Host/Startup/SettingsRegistrationExtensions.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; `src/TgAssistant.Infrastructure/Database/LlmConflictResolutionSessionModel.cs`; `src/TgAssistant.Core/Models/OperatorResolutionModels.cs`; `src/TgAssistant.Host/Launch/AiConflictResolutionSessionV1ProofRunner.cs`
- `Step-by-Step Instructions`:
  1. Add/extend `ConflictResolutionSessionSettings`.
     - Required keys: `ConflictResolutionSession:Enabled`, `CanonicalScopeOnly`, `CanonicalScopeKey`, `SessionTtlMinutes`, `MaxModelCalls`, `MaxRetrievalRounds`, `MaxOperatorTurns`, `MaxInputTokens`, `MaxOutputTokens`, `MaxTotalTokens`, `MaxCostUsdPerSession`, `ModelTaskKey`, `ModelTimeoutMs`, `ModelHint`.
  2. Replace hard-coded scope, ttl, call limits, and budgets.
  3. Carry prompt/completion/total/cost usage through response models.
  4. Add structured audit keys with explicit `null` when unavailable: `context_manifest`, `retrieval_requests`, `retrieval_results`, `model_id`, `model_version`, `prompt_tokens`, `completion_tokens`, `total_tokens`, `cost_usd`, `normalization_status`, `gate_decision`.
  5. Define closed structured verdict contract `ConflictResolutionStructuredVerdict` with required fields: `verdict_id`, `scope_key`, `scope_item_key`, `carry_forward_case_id`, `decision`, `publication_state`, `claim_rows`, `uncertainty_rows`, `normalization_plan`, `evidence_refs`, `created_at_utc`. Allowed `decision`: `apply`, `defer`, `escalate`, `reject_scope`. Allowed `publication_state`: `publishable`, `insufficient_evidence`, `escalation_only`, `manual_review_required`.
  6. Bind and register `ConflictResolutionSessionSettings` through `SettingsRegistrationExtensions`, then consume those bound values from both `OperatorResolutionApplicationService` and `LlmConflictResolutionSessionModel`.
  7. Keep deterministic apply path unchanged.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. Use prerequisite artifact `docs/planning/artifacts/phase-b-2026-04-06-ai-conflict-session-proof-prereqs.md` (owner: `ops proof executor`) to record the exact command/operator used for the checks below and the canonical seed contract source `docs/planning/BOUNDED_BASELINE_PROOF_CHAT_885574984_2026-04-03.md`.
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --readiness-check` `(existing)`
  4. If readiness fails because schema/bootstrap prerequisites are missing, run `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --operator-schema-init` `(existing)` and rerun Step 3 before continuing.
  5. If canonical scope `chat:885574984` is missing or stale, run these exact bootstrap commands and rerun Step 3:
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --seed-bootstrap-scope --seed-dry-run`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --seed-bootstrap-scope --seed-apply`
  6. If no eligible contradiction/review item exists for that scope, run the exact bounded pre-run commands below and rerun Step 3:
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --stage5-scoped-repair`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --stage5-scoped-repair --stage5-scoped-repair-apply` (only if approved backup metadata is available)
  7. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --ai-conflict-session-v1-proof --ai-conflict-session-v1-proof-output=src/TgAssistant.Host/artifacts/resolution-interpretation-loop/ai-conflict-resolution-session-v1-proof.json` `(existing; extend in this task)`
- `Operational Prerequisites`:
  - Before this task starts, `docs/planning/artifacts/phase-b-2026-04-06-ai-conflict-session-proof-prereqs.md` must be marked `status: pass`.
  - At task start, the artifact must include non-empty prep fields: `executed_by`, `completed_at_utc`, `readiness_command`, `readiness_result`, `seed_or_repair_commands`, `seed_or_repair_result`.
  - After Step 7 completes, the same artifact must be updated with non-empty `proof_command` and `proof_result`.
- `Acceptance Criteria`:
  - No private budget constants remain in bounded session path.
  - Structured verdict payload is closed and explicit (`ConflictResolutionStructuredVerdict`) with only allowed decision/publication enums accepted.
  - Proof output and persisted session audit expose the full listed audit contract, and every listed key is present with explicit `null` when unavailable rather than omitted.
- `Risks`: config drift and usage-null handling.
- `Do Not Do`: No scope widening. Do not change unrelated Phase-A runtime code paths outside the `Files/Areas` listed in this task; environment/data prep commands listed in Verification Steps are allowed only as proof prerequisites.
- `Expected Artifacts`: Settings-backed session control + updated proof output.

### Task 11

- `Task ID`: `PHB-011`
- `Title`: Execute session-local retrieval and bounded operator follow-up
- `Purpose`: Implement real bounded B3 execution flow.
- `Track`: `WS-B3`
- `Dependencies`: [`PHB-010`]
- `Deterministic Writer / Read Owner`: `OperatorResolutionApplicationService` + session entry repository
- `Exact Scope`: Whitelisted tool execution and follow-up budget enforcement in conflict session.
- `Files/Areas`: `src/TgAssistant.Core/Interfaces/IRepositories.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; `src/TgAssistant.Infrastructure/Database/LlmConflictResolutionSessionModel.cs`; `src/TgAssistant.Infrastructure/Database/MessageRepository.cs`; `src/TgAssistant.Infrastructure/Database/ResolutionReadProjectionService.cs`; `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`; `src/TgAssistant.Core/Models/OperatorResolutionModels.cs`; `src/TgAssistant.Host/Launch/AiConflictResolutionSessionV1ProofRunner.cs`
- `Step-by-Step Instructions`:
  1. Add typed tool request/response contracts.
  2. Allow only the whitelisted tools: `get_neighbor_messages`, `get_evidence_refs`, `get_durable_context`, `ask_operator_question`.
     - Deterministic reject reason for any other tool: `tool_not_allowed`.
  3. Route tool handlers only through deterministic retrieval seams already scoped by repository/service contracts; no ad hoc DB access from model session code.
  4. Enforce scope-item bounds and request caps.
  5. Persist request/response manifests with fields `tool_name`, `request_scope`, `request_items`, `decision`, `response_refs`.
  6. Enforce one follow-up question + one answer budget and fail closed on excess.
  7. Keep verdict generation in same lifecycle and require output to conform to `ConflictResolutionStructuredVerdict`.
  8. Extend proof matrix with required negative-path rows:
     - `tool_not_allowed_rejected`
     - `cross_scope_tool_request_rejected`
     - `followup_budget_exceeded_rejected`
  9. Define all new WS-B3 tool reject/status reasons in shared constants at `src/TgAssistant.Core/Models/OperatorResolutionModels.cs` and consume those constants in service + proof runner outputs.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. Use prerequisite artifact `docs/planning/artifacts/phase-b-2026-04-06-ai-conflict-session-proof-prereqs.md` (owner: `ops proof executor`) and confirm it records a passing `--readiness-check`, the canonical seed contract source `docs/planning/BOUNDED_BASELINE_PROOF_CHAT_885574984_2026-04-03.md`, and whether these prep commands were rerun:
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --seed-bootstrap-scope --seed-dry-run`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --seed-bootstrap-scope --seed-apply`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --stage5-scoped-repair`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --stage5-scoped-repair --stage5-scoped-repair-apply`
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --readiness-check` `(existing)`
  4. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --ai-conflict-session-v1-proof --ai-conflict-session-v1-proof-output=src/TgAssistant.Host/artifacts/resolution-interpretation-loop/ai-conflict-resolution-session-v1-proof.json` `(existing; extend in this task)`
- `Operational Prerequisites`:
  - Before this task starts, `docs/planning/artifacts/phase-b-2026-04-06-ai-conflict-session-proof-prereqs.md` must remain `status: pass`.
  - The artifact must show that the same canonical scope key `chat:885574984` and the same seed/pre-run lineage used in `PHB-010` are still valid for this run.
- `Acceptance Criteria`:
  - Session performs bounded retrieval/follow-up only.
  - Session verdict output conforms to `ConflictResolutionStructuredVerdict` and rejects invalid decision/publication enum values.
  - Proof output contains explicit pass/fail rows for `tool_not_allowed_rejected`, `cross_scope_tool_request_rejected`, and `followup_budget_exceeded_rejected`.
- `Risks`: Validation gaps leaking scope.
- `Do Not Do`: No raw DB tools, no recursive loop.
- `Expected Artifacts`: Bounded tool execution and expanded proof cases.

### Task 12

- `Task ID`: `PHB-012`
- `Title`: Finalize deterministic session normalization handoff and fallback proof
- `Purpose`: Guarantee advisory-to-deterministic handoff and honest fallback publication.
- `Track`: `WS-B3`
- `Dependencies`: [`PHB-011`]
- `Deterministic Writer / Read Owner`: `ResolutionActionCommandService` (sole durable apply owner)
- `Exact Scope`: Add deterministic validator/normalizer between AI verdict and action apply path; expand fallback matrix in proof.
- `Files/Areas`: `src/TgAssistant.Host/Startup/SettingsRegistrationExtensions.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; `src/TgAssistant.Infrastructure/Database/ResolutionActionCommandService.cs`; `src/TgAssistant.Host/Launch/AiConflictResolutionSessionV1ProofRunner.cs`; `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`; `src/TgAssistant.Host/Program.cs`; `src/TgAssistant.Core/Models/OperatorResolutionModels.cs`
- `Step-by-Step Instructions`:
  1. Validate verdict schema/evidence/operator inputs/normalization proposal.
  2. Downgrade unsupported retained claims to uncertainties.
  3. Force unresolved/manual-review state when publishable claims are empty.
  4. Keep all durable writes through `ResolutionActionCommandService`.
  5. Add explicit violation-to-outcome mapping:
     - `schema_invalid -> fallback_manual_review_required`
     - `budget_exceeded -> fallback_escalation_only`
     - `scope_rejected -> fallback_scope_rejected`
     - `publication_honesty_block -> fallback_insufficient_evidence`
  6. Add fallback proof rows for each mapping above.
  7. Define all new WS-B3 fallback/status reasons in shared constants at `src/TgAssistant.Core/Models/OperatorResolutionModels.cs` and consume those constants in service + proof runner outputs.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. Use prerequisite artifact `docs/planning/artifacts/phase-b-2026-04-06-ai-conflict-session-proof-prereqs.md` (owner: `ops proof executor`) and confirm it records the same canonical scope bootstrap/pre-run state used in `PHB-010` and `PHB-011`; do not substitute a different seeded scope for fallback verification. Allowed prep commands are only:
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --seed-bootstrap-scope --seed-dry-run`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --seed-bootstrap-scope --seed-apply`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --stage5-scoped-repair`
     - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --stage5-scoped-repair --stage5-scoped-repair-apply`
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --readiness-check` `(existing)`
  4. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --ai-conflict-session-v1-proof --ai-conflict-session-v1-proof-output=src/TgAssistant.Host/artifacts/resolution-interpretation-loop/ai-conflict-resolution-session-v1-proof.json` `(existing; extend in this task)`
- `Operational Prerequisites`:
  - Before this task starts, `docs/planning/artifacts/phase-b-2026-04-06-ai-conflict-session-proof-prereqs.md` must remain `status: pass`.
  - The artifact must explicitly confirm the same canonical scope bootstrap and proof lineage as `PHB-010` and `PHB-011`; if lineage changed, task is blocked and prereqs must be rerun.
- `Acceptance Criteria`:
  - Invalid/honesty-violating verdicts cannot be applied; no model-direct write path exists.
  - Validator rejects verdicts that violate `ConflictResolutionStructuredVerdict` field presence or closed enum constraints.
- `Risks`: Over-strict filtering blocking useful outcomes.
- `Do Not Do`: No bypass of action command path.
- `Expected Artifacts`: deterministic validator + expanded proof.

### Task 13

- `Task ID`: `PHB-013`
- `Title`: Define current-world approximation contract and bounded read model
- `Purpose`: Add temporal current-world read layer required by B4.
- `Track`: `WS-B4`
- `Dependencies`: [`PHB-012`]
- `Deterministic Writer / Read Owner`: `CurrentWorldApproximationReadService` (read-only projector; no durable writer introduced)
- `Exact Scope`: Contract + read service only; no write path and no cache that can outlive supersession/recompute events.
- `Files/Areas`: new `src/TgAssistant.Core/Models/CurrentWorldApproximationModels.cs`; new `src/TgAssistant.Infrastructure/Database/CurrentWorldApproximationReadService.cs`; `src/TgAssistant.Core/Interfaces/IRepositories.cs`; `src/TgAssistant.Infrastructure/Database/TemporalPersonStateRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7TimelineRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7PairDynamicsRepository.cs`
- `Step-by-Step Instructions`:
  1. Define exact DTO contract set:
     - `CurrentWorldApproximationSnapshot`: `snapshot_id`, `scope_key`, `tracked_person_id`, `as_of_utc`, `publication_state`, `uncertainty_refs`.
     - `ActivePersonRow`: `person_row_id`, `snapshot_id`, `subject_ref`, `state_label`, `valid_from_utc`, `valid_to_utc`, `evidence_refs`, `source_ref_ids`.
     - `InactivePersonRow`: `inactive_person_row_id`, `snapshot_id`, `subject_ref`, `inactive_reason`, `dropped_out_flag`, `last_seen_utc`, `valid_from_utc`, `valid_to_utc`, `evidence_refs`, `source_ref_ids`.
     - `RelationshipStateRow`: `relationship_row_id`, `snapshot_id`, `subject_ref`, `related_subject_ref`, `relationship_state`, `valid_from_utc`, `valid_to_utc`, `evidence_refs`, `source_ref_ids`.
     - `ActiveConditionRow`: `condition_row_id`, `snapshot_id`, `subject_ref`, `condition_type`, `condition_value`, `valid_from_utc`, `valid_to_utc`, `evidence_refs`, `source_ref_ids`.
     - `RecentChangeRow`: `change_row_id`, `snapshot_id`, `subject_ref`, `change_type`, `change_summary`, `changed_at_utc`, `evidence_refs`, `source_ref_ids`.
     - Every DTO above must carry `publication_state` as a required field.
  2. Build scope-local assembler from temporal states + pair + timeline only.
  3. Treat Stage7/8 supersession commits and Stage8 recompute completions as refresh triggers for the next read; `CurrentWorldApproximationReadService` owns recomputation on read and must not depend on a stale cache in B4.
  4. When temporal, pair-dynamics, or timeline surfaces disagree, return `unresolved` or `insufficient_evidence` with refs; do not deterministically pick a semantic winner.
  5. Require refs for surfaced statements.
  6. Return explicit insufficient-evidence state when publishable content is empty.
  7. Define WS-B4 publication/read-state constants in `src/TgAssistant.Core/Models/CurrentWorldApproximationModels.cs` and require read service + proof outputs to use those constants.
- `Verification Steps`: `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`:
  - Temporal/revisable read-only current-world model exists.
  - Contract includes `CurrentWorldApproximationSnapshot`, `ActivePersonRow`, `InactivePersonRow`, `RelationshipStateRow`, `ActiveConditionRow`, and `RecentChangeRow` with required IDs, refs, publication state, and timestamps.
  - Contract explicitly supports inactive and dropped-out people state representation for B4.
  - Current-world reads refresh from latest durable state after supersession/recompute triggers.
  - Disagreement across contributing surfaces results in `unresolved` or `insufficient_evidence`, not deterministic semantic arbitration.
  - In this task, `active conditions` are temporal/timeline-derived only; B5 conditional-rule states are integrated starting from `PHB-016`.
- `Risks`: Heuristic overreach in read assembly.
- `Do Not Do`: No UI/API in this task; no legacy current-state reuse as authority.
- `Expected Artifacts`: Current-world model and read service.

### Task 14

- `Task ID`: `PHB-014`
- `Title`: Surface current-world approximation and add honesty proof
- `Purpose`: Expose B4 output in web person workspace with explicit uncertainty behavior.
- `Track`: `WS-B4`
- `Dependencies`: [`PHB-013`]
- `Deterministic Writer / Read Owner`: `CurrentWorldApproximationReadService` + deterministic read seams (`TemporalPersonStateRepository`, `Stage7TimelineRepository`, `Stage7PairDynamicsRepository`) are read composition owners; `OperatorResolutionApplicationService` is adapter/population-only for response shaping; no durable writer
- `Exact Scope`: Add web API query + web section + proof runner; read-only. `CurrentWorldApproximationReadService` owns current-world composition, while `OperatorResolutionApplicationService` may only adapt/populate the response shape returned to API/web consumers.
- `Files/Areas`: `src/TgAssistant.Core/Interfaces/IRepositories.cs`; `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`; `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`; `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`; `src/TgAssistant.Infrastructure/Database/CurrentWorldApproximationReadService.cs`; `src/TgAssistant.Infrastructure/Database/TemporalPersonStateRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7TimelineRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7PairDynamicsRepository.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; new runner in `src/TgAssistant.Host/Launch`; `src/TgAssistant.Host/Program.cs`
- `Step-by-Step Instructions`:
  1. Add `/api/operator/person-workspace/current-world/query`.
  2. Enforce operator auth/session and tracked-person scope before load.
  3. Compose current-world data only through `CurrentWorldApproximationReadService` and its deterministic read seams; `OperatorResolutionApplicationService` may only shape/populate DTO fields and must not own read composition or semantic winner selection.
  4. Render active and inactive/dropped-out people plus relations/conditions/changes/uncertainties in web.
  5. Show degraded/insufficient-evidence state explicitly when needed.
  6. Add proof with publishable, disagreement-unresolved, and insufficient-evidence cases.
  7. Add required negative-path proof case for unauthenticated or cross-scope request rejection on `/api/operator/person-workspace/current-world/query`.
  8. Write artifact at `src/TgAssistant.Host/artifacts/phase-b/current-world-approximation-proof.json` with fields: `case_id`, `scope_key`, `tracked_person_id`, `as_of_utc`, `expected_http_status`, `actual_http_status`, `expected_publication_state`, `actual_publication_state`, `active_person_count`, `inactive_person_count`, `dropped_out_person_count`, `active_relation_count`, `active_condition_count`, `recent_change_count`, `reason`, `passed`.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --current-world-approximation-proof` `(to be added by this task)`
  3. Proof output must include explicit pass/fail for `unauth_or_cross_scope_rejected` with expected/actual HTTP status.
- `Acceptance Criteria`:
  - Scope-bound read-only current-world UI/API exists with honesty enforcement, and disagreement cases surface as unresolved/insufficient-evidence rather than a selected winner.
  - `CurrentWorldApproximationReadService` remains the read composition owner, while `OperatorResolutionApplicationService` is limited to adapter/population-only response shaping.
  - Inactive and dropped-out people are explicitly surfaced in B4 output.
  - Proof artifact contains per-case publication state and pass/fail rows for publishable, disagreement-unresolved, insufficient-evidence, inactive-or-dropped-out-person coverage, and unauthenticated-or-cross-scope rejection scenarios.
- `Risks`: active/inactive collapse in UI.
- `Do Not Do`: no Telegram deep-analysis work.
- `Expected Artifacts`: API+web section and proof artifact.

### Task 15

- `Task ID`: `PHB-015`
- `Title`: Define conditional knowledge and phase-marker contract
- `Purpose`: Introduce bounded conditional representation for B5.
- `Track`: `WS-B5`
- `Dependencies`: [`PHB-014`]
- `Deterministic Writer / Read Owner`: `read-only contract task` (no durable writer introduced)
- `Exact Scope`: Contract-only baseline/exception/condition/phase-marker model.
- `Files/Areas`: new `src/TgAssistant.Core/Models/ConditionalKnowledgeModels.cs`; `src/TgAssistant.Core/Models/DossierFieldRegistryModels.cs`; `src/TgAssistant.Core/Models/TemporalPersonStateModels.cs`
- `Step-by-Step Instructions`:
  1. Define required `ConditionClause` fields: `condition_clause_id`, `subject_ref`, `attribute`, `operator`, `value`, `valid_from_utc`, `valid_to_utc`, `confidence`, `evidence_refs`, `source_ref_ids`, `linked_temporal_state_ids`.
  2. Define closed operator enum: `eq`, `neq`, `contains`, `in`, `gte`, `lte`.
  3. Define baseline rule model fields: `rule_id`, `subject_ref`, `fact_family`, `baseline_value`, `valid_from_utc`, `valid_to_utc`, `confidence`, `evidence_refs`.
  4. Define exception rule model fields: `exception_id`, `rule_id`, `condition_clause_ids`, `exception_value`, `valid_from_utc`, `valid_to_utc`, `confidence`, `evidence_refs`.
  5. Define style-drift and phase-marker models with validity windows and evidence refs.
  6. Add links to temporal state IDs and source refs.
- `Verification Steps`: `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`: Contract defines `ConditionClause`, baseline rule, exception rule, style-drift, and phase-marker models with the listed IDs, validity windows, confidence/evidence refs, and temporal/source links.
- `Risks`: rule-engine creep.
- `Do Not Do`: No free-form rule interpreter.
- `Expected Artifacts`: Conditional contract models.

### Task 16

- `Task ID`: `PHB-016`
- `Title`: Persist conditional rules and integrate into Stage7/8 outputs
- `Purpose`: Add durable, evidence-backed conditional state flow.
- `Track`: `WS-B5`
- `Dependencies`: [`PHB-015`]
- `Deterministic Writer / Read Owner`: `ConditionalKnowledgeRepository` (deterministic durable writer)
- `Exact Scope`: Add conditional storage + Stage7/8 integration for affected families: `profile_preference`, `behavior_pattern`, `style_drift`, `phase_marker`.
- `Files/Areas`: `src/TgAssistant.Core/Interfaces/IRepositories.cs`; new `src/TgAssistant.Infrastructure/Database/ConditionalKnowledgeRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7DossierProfileRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7TimelineRepository.cs`; `src/TgAssistant.Infrastructure/Database/Stage7PairDynamicsRepository.cs`; `src/TgAssistant.Infrastructure/Database/CurrentWorldApproximationReadService.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; `src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs`; `src/TgAssistant.Host/Launch/Stage7DossierProfileSmokeRunner.cs`; `src/TgAssistant.Host/Launch/Stage7TimelineSmokeRunner.cs`; `src/TgAssistant.Host/Launch/Stage8RecomputeSmokeRunner.cs`; new `src/TgAssistant.Infrastructure/Database/Migrations/0058_conditional_knowledge.sql`
- `Step-by-Step Instructions`:
  1. Add scope-local conditional-rule and phase-marker storage.
  2. Persist baseline/exception only when refs exist.
  3. Attach drift/phase to timeline windows.
  4. Supersede only affected conditional rows on accepted Stage8 changes.
  5. Feed active-now conditional states to current-world reads.
  6. Extend `--stage7-dossier-profile-smoke` so it persists and reads back repository-backed conditional rows for `profile_preference`/`behavior_pattern`; in-memory-only success is not sufficient.
  7. Extend `--stage7-timeline-smoke` so it persists and reads back repository-backed `style_drift`/`phase_marker` rows through `ConditionalKnowledgeRepository`.
  8. Extend `--stage8-recompute-smoke` to prove accepted Stage8 change performs targeted conditional supersession for only affected rows.
  9. Add one verification assertion through `CurrentWorldApproximationReadService` showing affected conditional rows are superseded while unaffected conditional rows remain open.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage7-dossier-profile-smoke` `(existing; extend in this task to assert repository-backed conditional round-trip through ConditionalKnowledgeRepository)`
  3. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage7-timeline-smoke` `(existing; extend in this task to assert repository-backed conditional round-trip through ConditionalKnowledgeRepository)`
  4. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --stage8-recompute-smoke` `(existing; extend in this task to assert targeted conditional supersession only for affected rows)`
  5. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --current-world-approximation-proof` `(existing from PHB-014; include assertion that affected conditional rows are superseded and unaffected rows remain open in read output)`
- `Acceptance Criteria`:
  - Durable conditional state exists with evidence-backed baseline/exception separation.
  - Conditional writes lacking required evidence refs are rejected by deterministic validation and queued for proof validation in `PHB-018`.
  - Active-now conditional states are available to the B4 current-world read path for affected families after deterministic writes complete, and proof covers this through `CurrentWorldApproximationReadService`.
  - Verification in this task proves persisted conditional rows are written and read back through `ConditionalKnowledgeRepository`; in-memory-only smoke coverage does not satisfy acceptance.
- `Risks`: noisy conditional rows from weak evidence.
- `Do Not Do`: no auto-promotion of proposal-only rules.
- `Expected Artifacts`: conditional schema/repo and integration hooks.

### Task 17

- `Task ID`: `PHB-017`
- `Title`: Extend operator read contracts for conditional modeling
- `Purpose`: Make operator responses represent baseline/exception/phase distinctly.
- `Track`: `WS-B5`
- `Dependencies`: [`PHB-016`]
- `Deterministic Writer / Read Owner`: `CurrentWorldApproximationReadService` + deterministic conditional read seams (`ConditionalKnowledgeRepository`, `TemporalPersonStateRepository`, `Stage7TimelineRepository`, `Stage7PairDynamicsRepository`) are read composition owners; `OperatorResolutionApplicationService` is adapter/population-only for response shaping
- `Exact Scope`: Extend API contracts and deterministic response population only; no web rendering in this task. `OperatorResolutionApplicationService` may only adapt/populate DTOs from deterministic read outputs and must not own conditional/current-world composition.
- `Files/Areas`: `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`; `src/TgAssistant.Infrastructure/Database/CurrentWorldApproximationReadService.cs`; `src/TgAssistant.Infrastructure/Database/ConditionalKnowledgeRepository.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`
- `Step-by-Step Instructions`:
  1. Add fields for baseline rules, exceptions, active-now conditionals, phase markers.
  2. Keep baseline and exception arrays separate.
  3. Add publication-state fields: `insufficient_evidence`, `escalation_only`, `manual_review_required`.
  4. Populate from `CurrentWorldApproximationReadService` and deterministic conditional read seams only; `OperatorResolutionApplicationService` may shape the response but must not perform semantic arbitration or direct repository composition.
  5. Ensure publication-state fields are emitted on every populated response shape, including active-now conditional rows.
  6. Define WS-B5 response publication/status constants in `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs` and require response population to use these constants consistently.
- `Verification Steps`: `dotnet build TelegramAssistant.sln` `(existing)`
- `Acceptance Criteria`:
  - Contract can distinguish unconditional vs conditional vs phase-shift outputs.
  - Response population carries active-now conditionals needed by B4/B5 reads and always emits `insufficient_evidence`, `escalation_only`, and `manual_review_required`.
  - `OperatorResolutionApplicationService` remains adapter/population-only for response shaping; read composition ownership stays with `CurrentWorldApproximationReadService` and deterministic read seams.
- `Risks`: contract sprawl and parsing regressions.
- `Do Not Do`: no narrative synthesis in this task.
- `Expected Artifacts`: extended response contracts and population paths.

### Task 18

- `Task ID`: `PHB-018`
- `Title`: Surface conditional modeling with publication honesty proof
- `Purpose`: Expose B5 outputs in web without unsupported certainty.
- `Track`: `WS-B5`
- `Dependencies`: [`PHB-017`]
- `Deterministic Writer / Read Owner`: `CurrentWorldApproximationReadService` + deterministic conditional read seams (`ConditionalKnowledgeRepository`, `TemporalPersonStateRepository`, `Stage7TimelineRepository`, `Stage7PairDynamicsRepository`) are read composition owners; `OperatorResolutionApplicationService` is adapter/population-only for response shaping; no durable writer
- `Exact Scope`: web render + proof runner for baseline+exception, phase drift, and insufficient-evidence scenarios. Composition stays in deterministic read services; rendering consumes shaped read models only.
- `Files/Areas`: `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs`; `src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs`; `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs`; `src/TgAssistant.Infrastructure/Database/CurrentWorldApproximationReadService.cs`; `src/TgAssistant.Infrastructure/Database/ConditionalKnowledgeRepository.cs`; `src/TgAssistant.Infrastructure/Database/OperatorResolutionApplicationService.cs`; new runner in `src/TgAssistant.Host/Launch`; `src/TgAssistant.Host/Program.cs`
- `Step-by-Step Instructions`:
  1. Render baseline and exception as separate UI rows.
  2. Render phase markers with validity windows.
  3. Render explicit honesty state for non-publishable confidence/evidence.
  4. Source publishable/non-publishable conditional rows from `CurrentWorldApproximationReadService` and deterministic conditional read seams only; `OperatorResolutionApplicationService` may only adapt/populate response DTOs.
  5. Add proof runner with exactly these required cases:
     - `baseline_plus_exception_publishable`
     - `phase_drift_publishable`
     - `no_evidence_rule_rejected_with_honesty_state`
  6. Write artifact at `src/TgAssistant.Host/artifacts/phase-b/conditional-modeling-proof.json` with fields: `case_id`, `scope_key`, `tracked_person_id`, `rule_id`, `exception_id`, `phase_marker_id`, `expected_publication_state`, `actual_publication_state`, `expected_render_mode`, `actual_render_mode`, `reason`, `passed`.
  7. Use WS-B5 shared response/publication constants from `src/TgAssistant.Core/Models/OperatorResolutionApiModels.cs` in render and proof outputs.
- `Verification Steps`:
  1. `dotnet build TelegramAssistant.sln` `(existing)`
  2. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --conditional-modeling-proof` `(to be added by this task)`
- `Acceptance Criteria`:
  - web surfaces differentiate rule/exception/phase and block unsupported strong claims.
  - `OperatorResolutionApplicationService` remains adapter/population-only for response shaping; `CurrentWorldApproximationReadService` plus deterministic conditional read seams remain read composition owners.
  - Proof artifact contains per-case publication/render state and explicit pass/fail rows for all required proof cases.
- `Risks`: UI collapsing exception into one sentence.
- `Do Not Do`: no Telegram UI in this task.
- `Expected Artifacts`: web conditional surfacing and proof artifact.

## Quality Gate

### Anti-Ambiguity Checks

1. Every task must identify one deterministic writer or explicit read-only owner.
2. Every task must include one bounded verification command and one artifact or compile outcome.
3. Every new failure/status reason must be centralized in one shared constant location per track.
4. Every surfaced claim/world/conditional output must have evidence refs or explicit insufficiency.

### Forbidden Vague Phrasing

1. `improve AI logic`
2. `make temporal modeling smarter`
3. `update related files`
4. `wire where needed`
5. `use best judgment on statuses`

### Stop / Escalate Conditions

1. Required changes exceed listed files/areas.
2. Task would require model-direct durable writes.
3. Task reopens Phase-A guardrail scope without explicit prerequisite link.
4. Proof cannot be induced via existing or runner-local seam.
5. Scope-local enforcement cannot be preserved.
6. Publication honesty cannot be preserved.
