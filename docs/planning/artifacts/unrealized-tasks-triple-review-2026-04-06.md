# Unrealized Tasks Triple Review

Date: `2026-04-06`  
Mode: `strict sequential review`  
Review lenses: `architect-reviewer`, `business-analyst`, `backend-developer`

## Task Universe Reviewed

- `DTP-001..015` (authoritative execution truth: not executed)
- `PHB-001..018` (not started, gate closed)

Total unreleased tasks reviewed: `33`.

Evidence basis:
- task packs:
  - `docs/planning/DETAILED_IMPLEMENTATION_TASK_PACK_2026-04-06.md`
  - `docs/planning/DETAILED_IMPLEMENTATION_TASK_PACK_PHASE_B_2026-04-06.md`
- status/context:
  - `docs/planning/MASTER_PROJECT_STATUS_2026-04-06.md`
  - `docs/planning/COMPACT_EXECUTION_CONTEXT_2026-04-06.md`
- reproducibility checks run in workspace (`dotnet build`, selected smoke/proof commands)

---

## Section 1. Architect Reviewer

Verdict: `FAIL`

Critical findings:
1. Baseline-boundary contradiction:
   - Phase B pack states `Phase A (DTP-001..015) is completed baseline` while execution truth is `DTP not executed`.
   - Evidence:
     - [DETAILED_IMPLEMENTATION_TASK_PACK_PHASE_B_2026-04-06.md:10](/home/codex/projects/TelegramAssistant/docs/planning/DETAILED_IMPLEMENTATION_TASK_PACK_PHASE_B_2026-04-06.md:10)
     - [MASTER_PROJECT_STATUS_2026-04-06.md:40](/home/codex/projects/TelegramAssistant/docs/planning/MASTER_PROJECT_STATUS_2026-04-06.md:40)
2. Hidden scope drift risk across packs:
   - PHB assumes prerequisites from Phase A are already present; with DTP unexecuted, PHB scope silently expands into prerequisite closure work.
3. Two-pack execution ambiguity at architecture level:
   - DTP pack remains a full executable sequence and PHB pack remains a full executable sequence, but there is no single active execution chain declaration for current run when DTP is unexecuted.
4. Context inconsistency still present in compact context:
   - Same file contains both `DTP not executed` and prior decision text treating Phase A as completed baseline, which weakens authority clarity for weak executors.
   - Evidence:
     - [COMPACT_EXECUTION_CONTEXT_2026-04-06.md:28](/home/codex/projects/TelegramAssistant/docs/planning/COMPACT_EXECUTION_CONTEXT_2026-04-06.md:28)
     - [COMPACT_EXECUTION_CONTEXT_2026-04-06.md:36](/home/codex/projects/TelegramAssistant/docs/planning/COMPACT_EXECUTION_CONTEXT_2026-04-06.md:36)

Required fixes:
1. Replace PHB line `Phase A completed baseline` with explicit prerequisite gate wording: `PHB blocked until DTP baseline execution gate passes`.
2. Declare one active chain for this run: either `DTP -> PHB` or `PHB-only with explicit accepted risk waiver`.
3. Remove conflicting legacy decision lines from compact context.

---

## Section 2. Business Analyst

Verdict: `FAIL`

Key mismatches:
1. Delivery narrative ambiguity for weak executors:
   - Current docs can be read as “DTP done + start PHB”, while operator truth is “DTP not done.”
2. Missing explicit release path to target outputs:
   - No single plain execution statement connects current state to the required target outputs (person history, current world approximation, temporal/conditional knowledge) under the new `DTP not executed` truth.
3. Conflicting backlog semantics:
   - `tasks.json/task_slices.json done` and historical status can be misread as engineering completion without runtime proof.
4. Verification preconditions are not explicit in user-facing task wording:
   - Some commands require runtime role and safe credentials; this is not clearly surfaced at every affected task.
5. Gate communication gap:
   - PHB pre-execution gate does not currently include a mandatory `DTP baseline executed/proven` checkpoint.

Required wording fixes:
1. Add top-level “Current Run Execution Truth” block in both packs:
   - `DTP-001..015 = not executed`
   - `PHB-001..018 = blocked until DTP gate decision`
2. Add explicit path statement:
   - `To reach B-goals, execute DTP baseline first, then PHB in strict order`.
3. Add explicit “verification prerequisites” mini-section for commands requiring runtime role/credentials.

---

## Section 3. Backend Developer

Verdict: `FAIL`

Blockers:
1. Command reproducibility inconsistency across required verification:
   - `--runtime-control-detail-proof` fails without runtime role and valid DB secret.
   - `--opint-007-b1-smoke` / `--opint-007-b2-smoke` fail with runtime-role requirement (and then DB placeholder guard).
   - Evidence:
     - [RuntimeRoleSelection.cs:59](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs:59)
     - [RuntimeStartupGuard.cs:24](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeStartupGuard.cs:24)
2. DTP acceptance-to-code drift remains for critical contracts:
   - Loop service has hard-coded constants where DTP expects settings-driven wiring.
   - Loop model response shape does not expose prompt/completion/cost fields required by DTP contract text.
   - Evidence:
     - [ResolutionInterpretationLoopV1Service.cs:9](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/ResolutionInterpretationLoopV1Service.cs:9)
     - [ResolutionModels.cs:323](/home/codex/projects/TelegramAssistant/src/TgAssistant.Core/Models/ResolutionModels.cs:323)
3. DTP web-home contract appears unimplemented in current snapshot:
   - No mapped `/api/operator/home/summary` endpoint in current operator API map.
   - Evidence:
     - [OperatorApiEndpointExtensions.cs:25](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/OperatorApi/OperatorApiEndpointExtensions.cs:25)
4. Dependency order risk:
   - PHB linear dependencies are internally clear, but external prerequisite (DTP baseline) is unresolved, so PHB order is not executable in practice.

Required backend fixes:
1. Add explicit preflight for each proof/smoke command:
   - required runtime role, required secrets, expected env.
2. Close DTP contract drifts (or update DTP contract to observed implementation and re-approve).
3. Add/restore missing home-summary endpoint contract if DTP-013..015 remains required.
4. Re-run DTP verification chain from `DTP-001` only after preflight conditions are satisfied.

---

## Section 4. Verdict (GO / NO-GO)

`NO-GO`

Reason:
- All three lenses return `FAIL` for current unrealized-task execution readiness.
- Main gate blocker is prerequisite truth mismatch: PHB assumes completed DTP baseline while authoritative run truth is `DTP not executed`.

---

## Section 5. Required Fixes (for NO-GO)

1. Normalize authority text:
   - remove any remaining “Phase A completed baseline” wording for current run.
2. Publish one active execution chain decision:
   - recommended: `DTP-001..015` first, then PHB gate rerun, then `PHB-001..018`.
3. Add verification preflight table to DTP/PHB packs for runtime-role/secret dependencies.
4. Execute strict DTP run with evidence artifacts, then mark each DTP `confirmed`.
5. Re-run strict PHB sanity gate (`architect-reviewer -> business-analyst -> backend-developer`) only after DTP confirmation.
