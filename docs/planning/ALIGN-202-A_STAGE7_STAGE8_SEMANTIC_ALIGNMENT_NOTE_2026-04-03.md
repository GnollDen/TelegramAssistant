# ALIGN-202-A Read-Only Prep: Stage7/Stage8 Semantic Alignment Note

## Date

2026-04-03

## Status

Read-only prep artifact for ALIGN-202-A.

This note does not change runtime wiring, gateway track implementation, DI contracts, or Stage7/Stage8 code.

## Authority Context

- PRD authority: [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md)
- Runtime/planning index: [README.md](/home/codex/projects/TelegramAssistant/README.md), [docs/planning/README.md](/home/codex/projects/TelegramAssistant/docs/planning/README.md)

## Code Surfaces Reviewed

- Stage7 formation services:
  - [Stage7DossierProfileFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs)
  - [Stage7PairDynamicsFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs)
  - [Stage7TimelineFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs)
- Stage8 recompute/control surfaces:
  - [Stage8RecomputeQueueService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs)
  - [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs)
  - [Stage8RecomputeWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeWorkerService.cs)
  - [RuntimeControlStateService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/RuntimeControlStateService.cs)
  - [Stage8OutcomeGateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Stage8OutcomeGateRepository.cs)
- Contracts and registrations:
  - [Stage8RecomputeQueueModels.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/Stage8RecomputeQueueModels.cs)
  - [Stage7DossierProfileModels.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/Stage7DossierProfileModels.cs)
  - [DomainRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs)
  - [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs)

## Current Semantics (Code-Backed)

### Stage7 durable formation (current implementation)

- Stage7 is implemented as three deterministic formation services over Stage6 bootstrap output:
  - `dossier_profile`
  - `pair_dynamics`
  - `timeline_objects`
- Each Stage7 path:
  - builds a `ModelPassEnvelope` with `Stage = stage7_durable_formation` and `TruthLayer = derived_but_durable`;
  - normalizes/persists through `IModelPassAuditService`;
  - materializes durable objects only when result status is `result_ready`;
  - returns non-formed results for blocked/need-more-data statuses.
- Stage7 durable families persisted in current model surface:
  - `dossier`, `profile`, `pair_dynamics`, `event`, `timeline_episode`, `story_arc`.

### Stage8 crystallization/recompute (current implementation)

- Stage8 is a queue-driven recompute orchestrator with worker loop execution.
- Trigger path:
  - `Stage8RecomputeTriggerService` derives scope and enqueues target families based on signal/object/action heuristics.
- Execution path:
  - `Stage8RecomputeQueueService` leases queue items and executes scoped recompute;
  - calls Stage6 bootstrap, then (for Stage7 families) re-runs Stage7 formation services;
  - applies outcome gate via `IStage8OutcomeGateRepository`.
- Outcome gating currently controls promotion/truth-layer state on durable metadata:
  - `promoted` (canonical truth),
  - `promotion_blocked`,
  - `clarification_blocked`.
- Runtime control can force safety modes (`safe_mode`, `budget_protected`, `review_only`, `promotion_blocked`, `degraded`) and defer or restrict Stage8 execution.

## PRD Mapping

### Aligned with PRD intent

- Promotion is controlled by application control-plane logic, not by raw model prose.
- Stage7 performs first durable object formation with explicit status gating.
- Stage8 is trigger-driven and includes clarification/promotion gates.
- Durable object families and truth-layer transitions are explicit in metadata updates.

### Semantic Drift / Narrowed Claims Needed

- `Stage8` in current code is primarily recompute + outcome gating over Stage6/Stage7 families, not a broad set of specialized crystallizer workers described in PRD target language.
- Trigger heuristics still reference several legacy object/action taxonomies, so trigger semantics are partially legacy-shaped.
- Timeline formation currently centers on bootstrap-derived anchor/bundle outputs and may underrepresent richer timeline/story-arc semantics implied by PRD target wording.
- Stage5 daily crystallization worker is explicitly disabled in active hosted-service registration, so operational crystallization breadth is narrower than a naive PRD reading might imply.

## Bounded Remediation Direction (Analysis-Only)

- For documentation/backlog truthfulness, describe current Stage8 as `recompute-and-gate crystallization control plane` until specialized crystallizers are implemented.
- Keep claims that promotion is control-plane gated and status-driven.
- Avoid claiming full PRD Stage8 specialization completeness in active docs/backlog until dedicated crystallizer families exist beyond the current Stage6/Stage7 rerun model.

## Non-Interference Statement

This artifact is analysis-only and safe for parallel execution with the active gateway track:
- no code edits,
- no startup/runtime wiring changes,
- no provider/gateway contract changes,
- no shared control-plane seam mutations.
