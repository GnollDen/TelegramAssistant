# ALIGN-202-A Stage7/Stage8 Semantic Alignment Note

## Date

2026-04-03

## Status

Completed analysis artifact for `ALIGN-202-A`.

This note is evidence-backed and read-only. It does not change runtime wiring, DI, gateway scope, or Stage7/Stage8 behavior.

## Authority Context

- PRD authority: [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md#L128), [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md#L324)
- Planning authority index: [README.md](/home/codex/projects/TelegramAssistant/README.md), [README.md](/home/codex/projects/TelegramAssistant/docs/planning/README.md)

## Exact Scope Reviewed

- Stage7 durable formation services:
  - [Stage7DossierProfileFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs#L24)
  - [Stage7PairDynamicsFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs#L24)
  - [Stage7TimelineFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs#L24)
- Stage8 recompute and control plane:
  - [Stage8RecomputeQueueService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs#L62)
  - [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs#L81)
  - [Stage8RecomputeWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeWorkerService.cs#L25)
  - [Stage8OutcomeGateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Stage8OutcomeGateRepository.cs#L36)
- Contracts and active runtime registration:
  - [Stage8RecomputeQueueModels.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/Stage8RecomputeQueueModels.cs#L3)
  - [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs#L54)

## Confirmed Code Semantics

### Stage7 current implementation

- `Stage7` exists as three pass families only: `dossier_profile`, `pair_dynamics`, and `timeline_objects` via [Stage7DossierProfileFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs#L74), [Stage7PairDynamicsFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs#L78), and [Stage7TimelineFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs#L78).
- Each service builds a `ModelPassEnvelope` with `Stage = "stage7_durable_formation"` and `TruthLayer = derived_but_durable`, then sends a locally generated `RawModelOutput` into `IModelPassAuditService.NormalizeAndPersistAsync(...)` before any durable write via [Stage7DossierProfileFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs#L31), [Stage7PairDynamicsFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs#L31), and [Stage7TimelineFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs#L31).
- Durable writes only occur when the normalized result status is `result_ready`; otherwise the services return `Formed = false` and stop before repository upsert via [Stage7DossierProfileFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs#L39), [Stage7PairDynamicsFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs#L39), and [Stage7TimelineFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs#L39).
- Confirmed durable families are `dossier`, `profile`, `pair_dynamics`, `event`, `timeline_episode`, and `story_arc` via [Stage8OutcomeGateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Stage8OutcomeGateRepository.cs#L12).

### Stage7 important narrowing

- Current Stage7 is not a broad second reasoning worker that calls a separate model runtime per family. The services synthesize structured `RawModelOutput` directly from Stage6 bootstrap counts, identities, ambiguity pools, contradictions, and closure heuristics inside the service code itself via [Stage7DossierProfileFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7DossierProfileFormationService.cs#L154), [Stage7PairDynamicsFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7PairDynamicsFormationService.cs#L146), and [Stage7TimelineFormationService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage7Formation/Stage7TimelineFormationService.cs#L144).
- This still qualifies as first durable formation, but the semantic depth is narrower than PRD language that describes a second intelligence run reconciling prepared corpus and Stage6 outputs.

### Stage8 current implementation

- `Stage8` target families are limited to `stage6_bootstrap`, `dossier_profile`, `pair_dynamics`, and `timeline_objects` via [Stage8RecomputeQueueModels.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/Stage8RecomputeQueueModels.cs#L3).
- Trigger ingestion is heuristic and queue-based. `Stage8RecomputeTriggerService` maps clarification answers, edits, deletes, and selected high-signal object/action pairs to the three Stage7 families via [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs#L130).
- Execution is scoped recompute, not specialized crystallizer fan-out. `Stage8RecomputeQueueService` runs Stage6 bootstrap and then re-runs one of the existing Stage7 services for the requested family via [Stage8RecomputeQueueService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs#L351).
- The Stage8 control plane adds useful production behavior beyond queueing: runtime-state deferral, blocked-branch short-circuiting, retry/reschedule, and outcome gating via [Stage8RecomputeQueueService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs#L146), [Stage8RecomputeQueueService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeQueueService.cs#L191), and [Stage8OutcomeGateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Stage8OutcomeGateRepository.cs#L135).
- Promotion remains application-controlled, matching the PRD truth model. `result_ready` can become `promoted` and `canonical_truth`, contradictions force `promotion_blocked` and `conflicted_or_obsolete`, and clarification blocks stay explicit via [Stage8OutcomeGateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Stage8OutcomeGateRepository.cs#L141).
- Operationally, active hosted runtime includes the Stage8 recompute worker under `ops`, while the old daily Stage5 crystallization worker remains disabled via [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs#L64) and [HostedServiceRegistrationExtensions.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs#L70).

## PRD Alignment

### Confirmed alignment

- The PRD defines a `crystallizer` as a specialized Stage8 worker and requires truth promotion to remain in the control plane via [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md#L128) and [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md#L179).
- Current code does satisfy the control-plane part: Stage8 outcome gating, runtime safety states, and clarification blocking are application-owned, not model-owned.
- The PRD defines Stage7 as first durable object formation and Stage8 as trigger-based scoped refinement via [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md#L324) and [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md#L349). Current code does implement first durable writes plus trigger-driven scoped reruns.

### Confirmed semantic drift

- PRD Stage8 says it owns "all specialized crystallizers" and profile/history crystallization. Current code does not expose specialized crystallizer workers; it exposes a recompute queue that replays Stage6 plus existing Stage7 families.
- PRD Stage7 language implies richer knowledge reconciliation than the current deterministic service-generated outputs support. Current implementation is closer to audited durable materialization from bootstrap-derived signals than to a broader second intelligence pass.
- Stage8 trigger heuristics still depend on legacy object/action taxonomies such as `period`, `state_snapshot`, `strategy_record`, and `draft_outcome` via [Stage8RecomputeTriggerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage8Recompute/Stage8RecomputeTriggerService.cs#L18). That is compatible with queueing, but it is not yet a clean PRD-native trigger vocabulary.
- Timeline formation currently emits one bundled event/episode/story-arc family from bootstrap-scoped heuristics, which is materially narrower than full PRD wording around reviewed timeline, local episodes, and broader story development.

## Smallest Bounded Follow-Up For ALIGN-202-B

- Narrow active docs and backlog language so current Stage8 is described as `triggered scoped recompute and outcome-gating control plane`, not as completed specialized crystallizer coverage.
- Narrow active docs and backlog language so current Stage7 is described as `first durable formation from Stage6 bootstrap outputs with audited deterministic synthesis`, not as a broader finished semantic reconciliation layer.
- Preserve the existing positive claim that control-plane promotion, clarification blocking, and bounded rerun behavior are implemented.
- Do not rewrite Stage7/Stage8 runtime architecture in `ALIGN-202-B`; this slice only justifies claim narrowing and bounded semantics correction.

## Residual Risk

- Without wording changes in `ALIGN-202-B`, active docs and backlog can still overstate Stage8 completeness and understate how deterministic current Stage7 formation is.
- No blocker was found for bounded doc/backlog narrowing. The gap is semantic truthfulness, not a required architecture redesign inside this slice.
