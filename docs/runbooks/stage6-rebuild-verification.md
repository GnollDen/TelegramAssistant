# Stage 6 Rebuild Verification Runbook

> Historical/legacy runbook only.
> Not part of the active runtime authority chain for current clean-slate operation.

Legacy-only diagnostic runbook.

Use this runbook only when an explicit Stage 6 reset/rebuild is planned or when you are intentionally investigating the retired Stage 6 path.

For current clean-slate baseline operations, use:

- `docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md`
- `docs/planning/CLEANUP-001-B_RESET_BOUNDARY_NOTE_2026-04-02.md`
- `docs/planning/CLEANUP-005-A_RUNTIME_ROLE_AND_SMOKE_INVENTORY_2026-04-02.md`
- `docs/planning/README.md`

## Preconditions

1. Stage 5 completion gate is confirmed for target scope.
2. Runtime/env prerequisites from `docs/planning/S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md` are satisfied.
3. Rebuild/reset boundary is explicitly approved for target scope.

## Rebuild Order

1. Record pre-reset evidence.
2. Reset Stage 6 materialized outputs in rebuild scope.
3. Run Stage 6 rebuild execution path.
4. Run post-rebuild operator and runtime verification.
5. Record result as `pass`, `pass_with_follow_up`, or `blocked`.

## Runtime and Mode Sanity

Before verification, confirm:

- runtime role is explicit and correct for the command being run
- `Analysis__ArchiveOnlyMode=false` for normal live operation checks
- `Analysis__ArchiveCutoffUtc` does not suppress current live intake

## Verification Checklist

### A. Runtime Health

- `dotnet run --project src/TgAssistant.Host -- --liveness-check`
- `dotnet run --project src/TgAssistant.Host -- --readiness-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check`

Pass condition:

- all checks exit `0`

### B. Stage 6 Legacy Diagnostic

- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --stage6-execution-smoke`

Deterministic behavior note for live-dev:

- smoke verifier is baseline-aware for optional embedding checks;
- when rolling usage already saturates hard limit, optional path may appear as `hard_paused`;
- when baseline is below hard limit, optional path may appear as `soft_limited`;
- both are acceptable if diagnostic exits `0` and reports discipline success.

### C. Bot Operator Surface

Run minimal bot-facing checks:

- `/state`
- `/timeline`
- `/profile`

Pass condition:

- commands resolve meaningful output or explicit safe "no data" status

### D. Web Operator Surface

Verify web runtime and reads:

- web shell opens
- queue and case detail open without runtime failure
- artifact views return readable payload for rebuilt scope

### E. MCP Stage 6 Read Surface

Verify Stage 6 read tools:

- `get_current_state`
- `get_strategy`
- `get_profiles`
- `get_periods`
- `get_profile_signals`
- `get_session_summaries`

Pass condition:

- tools return data or explicit safe absence messages; no raw DB spelunking required

### F. Data and Rebuild Boundary Evidence

Capture proof that rebuilt outputs are from rebuilt substrate, with no mixed old/new Stage 6 materialization in scope.

## Reporting Format

For each run, report:

1. what was executed
2. what passed
3. what failed
4. whether failure is code-related or environment/runtime-only
5. what follow-up is required
6. verdict: `completed`, `completed with follow-up`, or `blocked`
