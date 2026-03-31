# Stage 6 Rebuild Verification Runbook

Use this after Stage 5 completion gate and during `S6-R5`.

This runbook defines execution and verification order for Stage 6 reset/rebuild readiness.
It does not authorize code changes by itself.

## Preconditions

1. Stage 5 rerun completion is confirmed.
2. `S6-R1` to `S6-R4` are completed or explicitly waived.
3. Runtime/env prerequisites from `docs/planning/S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md` are satisfied.

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

### B. Stage 6 Execution Discipline Smoke

- `dotnet run --project src/TgAssistant.Host -- --stage6-execution-smoke`

Deterministic live-dev smoke profile (standard override):

```bash
BudgetGuardrails__DailyBudgetUsd=100 \
BudgetGuardrails__ImportBudgetUsd=100 \
BudgetGuardrails__StageTextAnalysisBudgetUsd=0.01 \
BudgetGuardrails__StageEmbeddingsBudgetUsd=0.02 \
BudgetGuardrails__StageVisionBudgetUsd=1 \
BudgetGuardrails__StageAudioBudgetUsd=1 \
dotnet run --project src/TgAssistant.Host -- --stage6-execution-smoke
```

Why this override is required in live dev:

- `--stage6-execution-smoke` evaluates budget state using rolling 24h usage from `analysis_usage_events`.
- without overrides, ambient/dev traffic can push daily or stage limits into `hard_paused` before smoke-specific checks run, making result order-dependent and nondeterministic.
- this profile keeps daily/import caps high (to avoid ambient interference) and fixes stage smoke thresholds to deterministic values used by the verifier.

Policy note:

- this is an operational smoke profile only.
- do not keep these values as default runtime budget policy for normal Stage5/Stage6 operation.

Pass condition:

- exits `0` and reports execution-discipline success

### C. Bot Operator Surface

Run minimal bot-facing checks (or equivalent command-level diagnostics):

- `/state`
- `/timeline`
- `/profile` (when command is available in current sprint state)

Pass condition:

- commands resolve meaningful output or explicit safe "no data" status

### D. Web Operator Surface

Verify web runtime and reads:

- web shell opens
- queue and case detail open without runtime failure
- artifact views return readable payload for rebuilt scope

### E. MCP Stage 6 Read Surface

Verify MCP tools for Stage 6 reads according to sprint state:

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
