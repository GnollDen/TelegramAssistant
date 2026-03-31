# Sprint N1 Runtime and Operator Handover

## Date

2026-03-31

## Purpose

Practical handover note for operating current accepted Stage 5/Stage 6 baseline.

## Runtime Topology (Current)

- `app` host process/container: C# runtime with explicit `Runtime__Role`.
- `mcp` process/container: TypeScript MCP server (`src/TgAssistant.Mcp`), separate from host roles.
- `postgres` and `redis`: required dependencies.

Current allowed host role combinations:

- `ingest`
- `ingest,ops`
- `stage5`
- `stage5,maintenance`
- `stage6`
- `web`
- `web,ops`
- `ingest,stage5,maintenance,ops`

## Roles That Should Be Up Together

Normal dev operational baseline:

1. Core substrate runtime: `ingest,stage5,maintenance,ops`
2. MCP service: up in parallel
3. Stage 6 operator runtime (`stage6`): run as dedicated process when validating/operating Stage 6 bot surface
4. Web operator runtime (`web` or `web,ops`): run as dedicated process when validating/operating web surface

Note: `stage6` and `web` are single-role processes in current parser contract, so run them as dedicated role runs, not as one merged role string.

## Normal Dev Mode

Minimum startup for baseline operation:

```bash
docker compose up -d postgres redis app mcp
```

Baseline assumptions:

- `Runtime__Role=ingest,stage5,maintenance,ops` for always-on substrate mode.
- `Analysis__ArchiveOnlyMode=false`.
- `Analysis__ArchiveCutoffUtc` empty (or not suppressing fresh live messages).
- `BudgetGuardrails__Enabled=true`.

## Deterministic Smokes (Operator)

Use these as deterministic handover checks:

```bash
dotnet run --project src/TgAssistant.Host -- --liveness-check
dotnet run --project src/TgAssistant.Host -- --readiness-check
dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check
dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --stage6-execution-smoke
```

Stage6 execution smoke pass interpretation:

- optional-path state may be `soft_limited` or `hard_paused` depending on current rolling budget saturation;
- both are acceptable pass states when command exits `0` and reports discipline success.

## Env and Budget Assumptions (Current Normal)

- Budget guardrails are enabled and expected in normal operation.
- Default dev budget baseline (from current config templates):
  - daily: `0.5`
  - import: `0.2`
  - text: `0.25`
  - embeddings: `0.1`
  - vision: `0.1`
  - audio: `0.1`
- Synthetic scope reserved for drills: `chat_id >= 9000000000000`.
- 1:1 sender repair script is available at `scripts/repair_sender_id_zero_direct_chat.sql`.
