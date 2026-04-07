# Task-By-Task Junior Hardening (DTP + PHB)

Date: `2026-04-06`
Status: `execution overlay`
Source review roles: `architect-reviewer`, `business-analyst`, `backend-developer`

Rule:
- This artifact is a strict execution overlay for weak executors.
- If this artifact conflicts with task-pack wording, this artifact wins for preflight/split/verification precision.

## DTP Execution Order

`DTP-001 -> DTP-002A -> DTP-002B -> DTP-003 -> DTP-004 -> DTP-005 -> DTP-006A -> DTP-006B -> DTP-008A -> DTP-008B -> DTP-007 -> DTP-009A -> DTP-009B -> DTP-010 -> DTP-011 -> DTP-012A -> DTP-012B -> DTP-013A -> DTP-013B -> DTP-014 -> DTP-015A -> DTP-015B`

## DTP Task-by-Task

1. `DTP-001` Split: `No`
Preflight: verify `ResolutionInterpretationLoopSettings` keys exist and are bound; else `BLOCKED`.
DoD: no listed loop/model runtime constant remains hard-coded.
Verify: `dotnet build TelegramAssistant.sln` and `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate`.
Artifact: `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json`.

2. `DTP-002A` Split: `Yes` (`usage/audit fields`)
Preflight: no schema change beyond response/audit usage fields.
DoD: each model round carries prompt/completion/total/cost usage keys, null preserved as null.
Verify: same as `DTP-001`.
Artifact: validation report includes usage keys per round.

3. `DTP-002B` Split: `Yes` (`claim uncertainty enforcement`)
Preflight: use only existing claim model fields.
DoD: each surfaced claim has evidence refs or explicit uncertainty type (`inference`/`hypothesis`).
Verify: same as `DTP-001`.
Artifact: validation report rows proving no unsupported certainty.

4. `DTP-003` Split: `No`
Preflight: enforce check precedence `input -> output -> total -> cost -> usage_unavailable -> invalid_budget_configuration`.
DoD: deterministic fallback reason is exact and round-2 skipped after first-round breach.
Verify: same as `DTP-001`.
Artifact: happy path report without budget-failure reason.

5. `DTP-004` Split: `No`
Preflight: disabled path must not call interpretation loop service.
DoD: fallback reason exactly `loop_disabled`; mandatory audit keys present with null where non-applicable.
Verify: `dotnet build TelegramAssistant.sln` and `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-control-detail-proof`.
Artifact: `src/TgAssistant.Host/artifacts/resolution-interpretation-loop/runtime-control-detail-bounded-proof.json`.

6. `DTP-005` Split: `No`
Preflight: create one shared failure-reason constant container.
DoD: every listed exception/fallback path maps to one shared failure reason; `audit_status` remains lifecycle status.
Verify: `DTP-001` validate + runtime-control proof command.
Artifact: both proof outputs pass with shared taxonomy.

7. `DTP-006A` Split: `Yes` (`proof matrix`)
Preflight: list required cases and induction seam inventory; if any seam missing then `BLOCKED`.
DoD: artifact has one pass/fail row per required fallback case.
Verify: runtime-control proof command.
Artifact: runtime-control proof with full case matrix.

8. `DTP-006B` Split: `Yes` (`reproducibility/bootstrap`)
Preflight: record exact seed/replay command used.
DoD: proof artifact contains seed/replay metadata for every case.
Verify: runtime-control proof command.
Artifact: runtime-control proof with reproducibility metadata.

9. `DTP-008A` Split: `Yes` (`persistence contract`)
Preflight: one write path for `offline:save`, one for `offline:save-final`.
DoD: draft->saved behavior preserves same record id when draft exists; recording_ref normalization exact.
Verify: `dotnet build TelegramAssistant.sln` and `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-007-b1-smoke`.
Artifact: OPINT-007 smoke report with draft/save-final rows.

10. `DTP-008B` Split: `Yes` (`session/no-write rejection`)
Preflight: rejection path must not mutate repository/session state.
DoD: invalid input leaves record+session unchanged and emits explicit reject note.
Verify: `--opint-007-b3-smoke`.
Artifact: OPINT-007 smoke report rejection row.

11. `DTP-007` Split: `No`
Preflight: loader method enforces scope tuple `(tracked_person_id, scope_key)`.
DoD: admit only `saved` events, sorted `saved_at_utc desc, id desc`, capped to 5.
Verify: `--opint-007-b3-smoke`.
Artifact: evidence admission proof row listing admitted offline-event ids.

12. `DTP-009A` Split: `Yes` (`API/order hardening`)
Preflight: shared auth/session/scope/record-load sequence used by `/detail|/refine|/timeline-linkage`.
DoD: single-item envelope exact; pre-load fail returns bounded envelope with null record fields and correct scope flags.
Verify: build + exact POST matrix from task pack.
Artifact: API contract proof rows for `200/400/401/403/404`.

13. `DTP-009B` Split: `Yes` (`web consumption`)
Preflight: web panel consumes only bounded API envelope.
DoD: web refinement/linkage flow preserves bounded behavior and failure semantics.
Verify: host run + operator web exercise on offline-events panel.
Artifact: web smoke notes tied to same response envelope.

14. `DTP-010` Split: `No`
Preflight: helper outputs only `normalizedLabel` and `int? trustPercent`.
DoD: allowed labels fixed (`Fact|Inference|Hypothesis|Recommendation`), no local duplicate normalization remains.
Verify: `--opint-006-b1-smoke`.
Artifact: assistant smoke output confirms normalized label+percent behavior.

15. `DTP-011` Split: `No`
Preflight: do not modify LLM structured output schema.
DoD: DTOs explicitly carry `DisplayLabel` and nullable `TrustPercent` only when bounded confidence exists.
Verify: validate loop + `POST /api/operator/resolution/detail/query`.
Artifact: detail response sample with label/trust nullability checks.

16. `DTP-012A` Split: `Yes` (`Telegram parity`)
Preflight: render consumes upstream fields only.
DoD: no raw float trust rendering in Telegram.
Verify: validate loop + Telegram operator resolution flow.
Artifact: parity sample for Telegram.

17. `DTP-012B` Split: `Yes` (`Web parity`)
Preflight: no local web re-derivation of label/trust.
DoD: web rendering matches Telegram semantics.
Verify: validate loop + `/operator/resolution?...` render check.
Artifact: parity sample for web.

18. `DTP-013A` Split: `Yes` (`owner-exposure gate`)
Preflight: prove canonical owner values already exposed; otherwise `BLOCKED` and stop `DTP-014..015`.
DoD: explicit owner-exposure check recorded.
Verify: file inspection + recorded gate outcome.
Artifact: blocked/pass owner-exposure record.

19. `DTP-013B` Split: `Yes` (`home summary endpoint + degraded override`)
Preflight: authenticated operator session required.
DoD: GET `/api/operator/home/summary` returns exact 6-field contract; forced degraded override returns ordered all-null section contract.
Verify: build + host run + authenticated GET checks.
Artifact: endpoint contract evidence for happy/degraded/partial-failure.

20. `DTP-014` Split: `No`
Preflight: requires `DTP-013B` pass.
DoD: `/operator` shows live summary; degraded state keeps navigation visible and marker ids mounted.
Verify: host run + `/operator` happy/degraded checks.
Artifact: UI evidence with `data-state='degraded'` and required marker ids.

21. `DTP-015A` Split: `Yes` (`API smoke`)
Preflight: dedicated CLI switch added.
DoD: smoke asserts happy-path shape and approved route allow-list.
Verify: `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-home-dashboard-smoke`.
Artifact: `src/TgAssistant.Host/artifacts/operator-home/opint-home-dashboard-smoke.json`.

22. `DTP-015B` Split: `Yes` (`web/degraded smoke`)
Preflight: if deterministic partial-failure injection absent, mark partial path `not_exercised` explicitly.
DoD: degraded contract fully asserted with exact ordered `degradedSources`.
Verify: same smoke command.
Artifact: same smoke report with partial-failure section status.

## PHB Execution Order

`PHB-001 -> PHB-002 -> PHB-003 -> PHB-004 -> PHB-005 -> PHB-006A -> PHB-006B -> PHB-006C -> PHB-007 -> PHB-008A -> PHB-008B -> PHB-009 -> PHB-010A -> PHB-010B -> PHB-011A -> PHB-011B -> PHB-012A -> PHB-012B -> PHB-013 -> PHB-014A -> PHB-014B -> PHB-015 -> PHB-016A -> PHB-016B -> PHB-016C -> PHB-017 -> PHB-018A -> PHB-018B`

## PHB Task-by-Task

1. `PHB-001` Split: `No`
Preflight: mapping one-way only (`semantic family -> existing runtime family`).
DoD: exact constants/enums/helpers from task text exist and compile.
Verify: `dotnet build TelegramAssistant.sln`.
Artifact: compile proof.

2. `PHB-002` Split: `No`
Preflight: explicit deterministic reject reason/status constant defined and used.
DoD: stage services call shared validators and reject unmapped families deterministically.
Verify: build + stage6/stage7/stage8 smokes from task.
Artifact: smoke pass set.

3. `PHB-003` Split: `No`
Preflight: proof runner must call shared runtime validators, not local stubs.
DoD: artifact rows for all required valid/invalid cases with expected vs actual decision/status.
Verify: `--stage-semantic-contract-proof`.
Artifact: `src/TgAssistant.Host/artifacts/phase-b/stage-semantic-contract-proof.json`.

4. `PHB-004` Split: `No`
Preflight: clarify scope ownership fields location (`semantic DTO` vs `persistence row`).
DoD: temporal contract + helpers + `TemporalSingleValuedFactFamilies` exact starter set.
Verify: build.
Artifact: compile proof.

5. `PHB-005` Split: `No`
Preflight: uniqueness enforcement at repository plus DB constraint/index where feasible.
DoD: insert/query/open/supersession methods with single-valued-family uniqueness only.
Verify: build.
Artifact: migration `0056_temporal_person_states.sql` + compile proof.

6. `PHB-006A` Split: `Yes` (`Stage7 temporal writes`)
Preflight: `Stage8RecomputeTriggerService` remains trigger/read-only for this task.
DoD: only dossier/profile and timeline write temporal rows; pair-dynamics excluded.
Verify: build + `--temporal-person-state-proof`.
Artifact: temporal proof.

7. `PHB-006B` Split: `Yes` (`person-history API/query`)
Preflight: authenticated scope-bounded endpoint only.
DoD: `/api/operator/person-workspace/person-history/query` returns exact row shape from task.
Verify: build + `--person-history-proof`.
Artifact: person-history proof.

8. `PHB-006C` Split: `Yes` (`negative proofs`)
Preflight: include duplicate-open and missing-supersession rejection rows.
DoD: both proof artifacts contain positive+negative matrix.
Verify: same two proof commands.
Artifact: two proof reports with rejection rows.

9. `PHB-007` Split: `No`
Preflight: invariant `carry_forward_case_id` never equals `scope_item_key` or conflict-session id.
DoD: closed status/origin enums and transition helpers reject illegal transitions.
Verify: build.
Artifact: compile proof.

10. `PHB-008A` Split: `Yes` (`ledger schema/repo/service`)
Preflight: all write/readback flows go through `ResolutionCaseReintegrationService` public methods.
DoD: migration + repo + service wired; no legacy `Stage6Case*` storage.
Verify: build + `--resolution-recompute-contract-smoke`.
Artifact: recompute smoke proves repository-backed ledger round-trip.

11. `PHB-008B` Split: `Yes` (`smoke extension`)
Preflight: smoke seeds only through service boundary.
DoD: smoke asserts real persistence/readback, not in-memory shortcuts.
Verify: `--stage8-recompute-smoke`.
Artifact: stage8 smoke evidence for ledger linkage.

12. `PHB-009` Split: `No`
Preflight: pass-1/pass-2 setup only via service methods.
DoD: same `carry_forward_case_id` across passes with expected transition matrix.
Verify: `--iterative-reintegration-proof`.
Artifact: `src/TgAssistant.Host/artifacts/phase-b/iterative-pass-reintegration-proof.json`.

13. `PHB-010A` Split: `Yes` (`settings + audit contract`)
Preflight: ops prereq commands are execution preconditions, not coding scope.
DoD: session knobs externalized to `ConflictResolutionSessionSettings`; audit contract includes required keys/null handling.
Verify: build + readiness + existing conflict-session proof command.
Artifact: updated proof output and prereq artifact.

14. `PHB-010B` Split: `Yes` (`structured verdict contract`)
Preflight: closed enums only for decision/publication state.
DoD: `ConflictResolutionStructuredVerdict` validated with exact required fields.
Verify: same proof command.
Artifact: proof rows for valid/invalid structured verdicts.

15. `PHB-011A` Split: `Yes` (`tool whitelist/retrieval`)
Preflight: model layer never calls repositories directly.
DoD: only allowed tools execute via deterministic scoped seams; others reject `tool_not_allowed`.
Verify: same conflict-session proof command.
Artifact: proof rows for allowed/disallowed tools.

16. `PHB-011B` Split: `Yes` (`follow-up budget`)
Preflight: one-question/one-answer budget fixed and explicit.
DoD: over-budget follow-up deterministically rejected.
Verify: same proof command.
Artifact: proof row `followup_budget_exceeded_rejected`.

17. `PHB-012A` Split: `Yes` (`validator/normalizer mapping`)
Preflight: one validator output contract `{ normalizedVerdict, fallbackReason, publicationState }`.
DoD: invalid verdicts map to exact fallback/publication outcomes before apply.
Verify: same conflict-session proof command.
Artifact: proof matrix for schema/budget/scope/publication mapping.

18. `PHB-012B` Split: `Yes` (`surface fallback state`)
Preflight: durable writes remain only through `ResolutionActionCommandService`.
DoD: web/API surface reflects deterministic fallback/publication state.
Verify: same proof command + targeted endpoint checks.
Artifact: conflict proof + surfaced fallback evidence.

19. `PHB-013` Split: `No`
Preflight: no persistent cache authority.
DoD: each request recomputes from latest durable rows; deterministic ordering for row sets.
Verify: build.
Artifact: compile proof.

20. `PHB-014A` Split: `Yes` (`API + proof`)
Preflight: `CurrentWorldApproximationReadService` is single composition entrypoint.
DoD: scoped API endpoint with auth/cross-scope rejects and honesty states.
Verify: `--current-world-approximation-proof`.
Artifact: `src/TgAssistant.Host/artifacts/phase-b/current-world-approximation-proof.json`.

21. `PHB-014B` Split: `Yes` (`web section`)
Preflight: no repository access from web/API layers.
DoD: web renders active + inactive/dropped-out + uncertainty states from API contract.
Verify: host run + web route checks.
Artifact: web proof rows referenced by current-world proof.

22. `PHB-015` Split: `No`
Preflight: declare phase-marker storage shape explicitly.
DoD: conditional contract includes IDs, validity windows, confidence, evidence refs, temporal/source links.
Verify: build.
Artifact: compile proof.

23. `PHB-016A` Split: `Yes` (`schema/repo`)
Preflight: exact reject reason for missing evidence refs defined.
DoD: conditional repository + migration `0058_conditional_knowledge.sql` ready.
Verify: build + stage7 smokes.
Artifact: migration + smoke evidence.

24. `PHB-016B` Split: `Yes` (`producer integration`)
Preflight: producer ownership per family fixed.
DoD: Stage7/8 write/supersession only for affected rows; unaffected rows remain open.
Verify: stage7 timeline/profile + stage8 recompute smokes.
Artifact: supersession evidence in smoke outputs.

25. `PHB-016C` Split: `Yes` (`read consumption/proofs`)
Preflight: current-world read proves active-now conditionals from repository.
DoD: repository-backed round-trip and targeted supersession proven.
Verify: `--current-world-approximation-proof`.
Artifact: current-world proof rows for conditional state.

26. `PHB-017` Split: `No`
Preflight: list exact DTO arrays/fields extended.
DoD: baseline/exception/active-now/phase-marker response contracts populated with WS-B5 publication constants.
Verify: build.
Artifact: compile proof.

27. `PHB-018A` Split: `Yes` (`web/API surfacing`)
Preflight: route/section/DOM markers fixed before implementation.
DoD: distinct render modes for baseline, exception, phase-marker, honesty-state rows.
Verify: build + conditional proof command.
Artifact: conditional proof includes render-mode evidence.

28. `PHB-018B` Split: `Yes` (`proof runner`)
Preflight: proof compares API state to web render state.
DoD: required cases pass: publishable baseline/exception, style-drift/phase shift, no-evidence honesty block.
Verify: `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --conditional-modeling-proof`.
Artifact: `src/TgAssistant.Host/artifacts/phase-b/conditional-modeling-proof.json`.
