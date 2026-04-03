# Sprint N1 Operator Quickstart

> Historical quickstart (N1).
> Legacy-only context; not active clean-slate runtime authority.

## First Surfaces to Use

1. Bot Stage 6 triad: `/state` -> `/timeline` -> `/profile`
2. Web review surfaces: `/queue`, `/case-detail`, `/artifact-detail`
3. MCP Stage 6 reads:
   - `get_current_state`
   - `get_strategy`
   - `get_profiles`
   - `get_periods`
   - `get_profile_signals`
   - `get_session_summaries`

## Scope Rule (Real vs Synthetic)

- Real/operator scope: real chats and cases used in normal monitored operation.
- Synthetic/debug scope: `chat_id >= 9000000000000`.
- Synthetic/debug scope is not normal operator flow and must not be used as default quality signal for accepted baseline.

## Fast "System Is Normal" Check

```bash
dotnet run --project src/TgAssistant.Host -- --liveness-check
dotnet run --project src/TgAssistant.Host -- --readiness-check
dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check
dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --stage6-execution-smoke
```

Optional 1:1 sender invariant check:

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as sender_id_zero_rows from messages where sender_id = 0;"
```

If non-zero and defect signature matches direct-chat repair rule, use:

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/repair_sender_id_zero_direct_chat.sql
```
