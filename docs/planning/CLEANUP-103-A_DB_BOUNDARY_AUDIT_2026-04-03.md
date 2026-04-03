# CLEANUP-103-A DB Boundary Audit (2026-04-03)

Scope: discovery/classification first for `CLEANUP-103-A`, with only minimal code clarification changes to make the active versus frozen boundary explicit.

## Boundary Result

- `AddActiveRepositoryServices()` now contains only repository registrations still needed by the default runtime composition.
- `AddStage6LegacyRepositoryServices()` now owns the legacy-only `IEvalRepository` and `IDomainReviewEventRepository` registrations alongside the rest of the frozen Stage6/domain repository surface.
- `TgAssistantDbContext` now groups `budget_operational_states`, `ops_chat_coordination_states`, `ops_chat_phase_guards`, `backup_evidence_records`, and `external_archive_*` mappings with the active block because default-runtime services still write them.
- No schema/table mutation was applied in this slice.

## Evidence Commands

- `rg -n "AddSingleton<.*Repository" src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs`
- `rg -n "I(EvalRepository|DomainReviewEventRepository|ModelPassEnvelopeRepository|SummaryRepository|IdentityMergeRepository|BudgetOpsRepository|ExternalArchiveIngestionRepository)" src -g '*.cs'`
- `rg -n "ChatCoordinationStates|ChatPhaseGuards|BackupEvidenceRecords|BudgetOperationalStates|ExternalArchiveImportBatches|ExternalArchiveImportRecords|ExternalArchiveLinkageArtifacts" src -g '*.cs'`
- `rg -n "IPeriodRepository|IClarificationRepository|IOfflineEventRepository|IStateProfileRepository|IInboxConflictRepository|IStrategyDraftRepository|IDependencyLinkRepository|IStage6ArtifactRepository|IStage6CaseRepository|IStage6UserContextRepository|IStage6FeedbackRepository|IStage6CaseOutcomeRepository" src -g '*.cs'`

## Candidate Repository Classification

| Repository | Mapping / registration status | Active callers / evidence | Storage purpose | Decision | Justification |
| --- | --- | --- | --- | --- | --- |
| `IBudgetOpsRepository` | Active registration; table now grouped in active block | `BudgetGuardrailService` is registered in default runtime and injected by Stage5/media workers plus launch readiness | Budget guardrail operational state in `budget_operational_states` | keep | Not stale. Default-runtime budget checks still persist and read this state. |
| `IExternalArchiveIngestionRepository` | Active registration; tables now grouped in active block | Active callers: `ExternalArchiveIngestionService`, `ExternalArchiveVerificationService`; legacy spillover caller: `CompetingContextRuntimeService` | External archive import staging/linkage in `external_archive_import_*` and `external_archive_linkage_artifacts` | keep | Not stale. Active ingestion flow still depends on it even though one legacy diagnostic also reads it. |
| `IDomainReviewEventRepository` | Moved to legacy-only registration; table remains in frozen block | Callers are legacy Stage6 services only (`StrategyEngine`, `DraftReviewEngine`, `OutcomeService`, `CompetingContextRuntimeService`, verifiers) | Legacy review/audit trail in `domain_review_events` | isolate | Was an active-registration mismatch. No default-runtime caller requires it. |
| `IEvalRepository` | Moved to legacy-only registration; tables remain in frozen block | Callers are only `EvalHarnessService` and `EvalVerificationService`, both under legacy diagnostic registration | Legacy eval harness persistence in `eval_runs` and `eval_scenario_results` | isolate | Was an active-registration mismatch. Default runtime does not need eval storage. |
| `IPeriodRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 periodization/strategy/current-state/bot flows only | Frozen `domain_periods`, transitions, hypotheses lifecycle | isolate | Still used by retained diagnostics, but not baseline runtime. |
| `IClarificationRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 strategy/current-state/bot/clarification flows only | Frozen clarification question/answer workflow tables | isolate | Still needed for legacy diagnostics only. |
| `IOfflineEventRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 current-state/periodization/profile/bot flows only | Frozen offline-event and audio truth tables | isolate | No active baseline caller. |
| `IStateProfileRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 strategy/current-state/profile/draft flows only | Frozen state/profile snapshot tables | isolate | No active baseline caller. |
| `IInboxConflictRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 strategy/current-state/competing-context/draft flows only | Frozen inbox/conflict review tables | isolate | No active baseline caller. |
| `IStrategyDraftRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 strategy/draft/review/outcome/bot flows only | Frozen strategy/draft/outcome tables | isolate | No active baseline caller. |
| `IDependencyLinkRepository` | Legacy-only registration; frozen block | Callers are legacy clarification orchestration services only | Frozen dependency graph for legacy clarification flow | isolate | No active baseline caller. |
| `IStage6ArtifactRepository` | Legacy-only registration; frozen block | Callers are legacy Stage6 strategy/current-state/draft/bot/eval flows only | Frozen `stage6_artifacts` cache/tracking table | isolate | No active baseline caller. |
| `IStage6CaseRepository` | Legacy-only registration; frozen block | Callers are legacy autocase/bot/clarification/eval flows only | Frozen `stage6_cases` and links | isolate | No active baseline caller. |
| `IStage6UserContextRepository` | Legacy-only registration; frozen block | Callers are legacy bot/clarification flows only | Frozen Stage6 user-context entries | isolate | No active baseline caller. |
| `IStage6FeedbackRepository` | Legacy-only registration; frozen block | Callers are legacy bot/eval flows only | Frozen Stage6 feedback entries | isolate | No active baseline caller. |
| `IStage6CaseOutcomeRepository` | Legacy-only registration; frozen block | Caller is legacy `BotCommandService` only | Frozen Stage6 case-outcome table | isolate | No active baseline caller. |
| `IModelPassEnvelopeRepository` | Still in active registration block; active table block | No injectors found; active callers use `IModelPassAuditStore` / `IModelPassAuditService` instead | Direct CRUD wrapper over `model_pass_runs` | delete | Proven dead DI surface in current code. Storage is still active, but this repository is redundant with the audit store path. |
| `ISummaryRepository` | Still in active registration block; active table block | No injectors found; current summary flow uses `IChatDialogSummaryRepository` instead | Historical `daily_summaries` writes/reads | delete | Proven dead DI surface in current code. Table remains mapped because other code still updates `daily_summaries` indirectly during entity merge. |
| `IIdentityMergeRepository` | Still in active registration block; active table block | No injectors found | Identity correction history plus bounded recompute staging in `identity_merge_histories` and related active person/durable tables | isolate | Dormant but not obviously deletable. The implementation owns active correction/reversal behavior and bounded recompute side effects, so deleting it in discovery would be premature. |

## Non-Repository Mapping Clarifications

- `ChatCoordinationService` directly uses `ChatCoordinationStates`, `ChatPhaseGuards`, and `BackupEvidenceRecords`, and it is injected by `TelegramListenerService`, `HistoryBackfillService`, `AnalysisWorkerService`, and `Stage5ScopedRepairCommand`.
- Those tables were previously grouped with frozen mappings, but current code makes them active operational state, so the DbContext grouping was corrected in this slice.

## Recommended 103-B Follow-up

1. Remove `IModelPassEnvelopeRepository` registration/interface/implementation if no new caller is introduced.
2. Remove `ISummaryRepository` registration/interface/implementation if `daily_summaries` stays write-only/indirect.
3. Decide whether `IIdentityMergeRepository` should gain a real operator/API caller or move behind an explicit correction-only registration path.
