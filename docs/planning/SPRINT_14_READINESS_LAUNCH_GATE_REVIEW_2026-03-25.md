# Sprint 14 Readiness and Launch Gate Review (2026-03-25)

## Scope

Execution run for Sprint 14 launch gate as an integration pass across:
- foundation and Stage 5 safety baseline
- Stage 6 execution discipline
- artifacts + case model + auto case generation
- clarification and user-context intake
- bot/web operator workflows
- feedback/eval/economics visibility
- control-plane and runtime safety

This run is verify/integrate/tighten only; no broad redesign.

## Verification Commands

Executed on current `master` with local runtime dependencies (`tga-postgres`, `tga-redis`) and explicit local env overrides for DB/Redis:

- `dotnet build TelegramAssistant.sln`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --liveness-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --readiness-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --stage5-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --stage6-execution-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --auto-case-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --clarification-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --bot-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --web-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --ops-web-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --outcome-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --eval-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --budget-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --launch-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --network-smoke`

## Narrow Fix Applied In-Scope

### Fixed blocker

`stage6_case_links.metadata_json` upsert wrote plain text into `jsonb` column and caused repeated failures in clarification/bot/web/outcome/launch paths.

- Fix: cast upsert payload to `jsonb` in `Stage6CaseRepository.UpsertLinkAsync`.
- File: `src/TgAssistant.Infrastructure/Database/Stage6CaseRepository.cs`

Result after fix:
- `clarification-smoke`, `bot-smoke`, `web-smoke`, `ops-web-smoke`, `outcome-smoke`, `launch-smoke` now pass.

## Integrated Evidence Summary

### PASS

- Build: `dotnet build TelegramAssistant.sln` passed.
- Runtime probes:
  - `liveness-check` passed.
  - `runtime-wiring-check` passed.
- Stage 6 and operator usefulness surfaces:
  - `auto-case-smoke` passed.
  - `clarification-smoke` passed (after jsonb fix).
  - `bot-smoke` passed with `/state`, `/draft`, `/review`, `/gaps`, `/answer`, `/timeline`, `/offline` paths.
  - `web-smoke` and `ops-web-smoke` passed.
  - `outcome-smoke` passed.
  - `launch-smoke` passed with steps: foundation, web-read, web-review, web-search, ops-web, outcome, budget-visibility, eval.
- Quality/economics visibility:
  - `eval-smoke` passed and stage6 pack scenarios are persisted/visible.
  - `budget-smoke` passed.
- Stage 6 execution discipline:
  - `stage6-execution-smoke` passed on retest.

### FAIL / Open blockers

1. `readiness-check` fails.
- Failure reason: Stage5 managed prompt contract mismatch.
- Error: `Stage5 smoke failed: managed prompt 'stage5_cheap_extract_v10' version mismatch db='v1' code='v10'.`

2. `stage5-smoke` fails with the same prompt mismatch.
- This blocks a clean Stage 5 runtime-safety confirmation for launch gate.

3. `network-smoke` fails.
- Error: `Network smoke failed: nodes were not assembled.`
- This is a remaining integration gap for `/network` verification path.

## Control-Plane / Runtime Safety Notes

Additional architecture/SRE review found high-risk control-plane concerns requiring follow-up:
- readiness path initializes Redis queue and can mutate queue state (`InitializeAsync` in health probe path)
- liveness/readiness truthfulness is limited (short-lived probe process; no in-process worker heartbeat truth)
- role fail-closed is stronger syntactically than operationally (enabled-state/runtime-contract gaps)

These are tracked as launch-risk follow-ups even when smoke entrypoints pass.

## Final Verdict

`not ready yet`

Rationale:
- launch gate still has blocking red checks in required safety/foundation area (`readiness-check`, `stage5-smoke`)
- one integration smoke (`network-smoke`) remains red
- control-plane truthfulness risks remain open for runtime health semantics

## Follow-up Required (Narrow)

1. Resolve Stage5 managed prompt mismatch path so `stage5-smoke` and `readiness-check` are green without manual DB drift handling.
2. Fix `network-smoke` node assembly failure.
3. Harden health probe semantics to be read-only for queue state and align readiness/liveness truth with running host state.

