# Sprint N1 Readiness Baseline (Source of Truth)

## Date

2026-03-31

## Authority

This is the authoritative readiness/handover baseline for current `master` dev workspace after Stage 5 / Stage 6 remediation-rebuild-acceptance cycle.

This document is **readiness state capture**, not a rebuild plan.

## Baseline Status

### Completed (included in baseline)

- Stage 5 completed.
- Stage 6 rebuild completed.
- Stage 6 accepted on real scopes.
- Synthetic scope isolation implemented.
- BotChat non-command recovery implemented.
- Timeline quality cleanup implemented.
- MCP Stage 6 ergonomics cleanup implemented.
- 1:1 ingestion `sender_id=0` defect fixed and repaired.

### Accepted For Operation

Accepted means normal operator/developer flow can rely on:

- Stage 6 artifacts via bot/web/MCP without raw DB-first workflow.
- Synthetic scopes excluded from normal operator flow by default.
- Non-command bot replies degrade safely when rich context is unavailable.
- Timeline output suppresses unresolved/no-signal noise by default.
- MCP profile-signal and not-found behavior is operator-usable.

## Current Working Dev Baseline

Treat this as current working baseline on `master`:

1. Stage 5 substrate is production-like and should not be reinterpreted as "rebuild in progress".
2. Stage 6 is rebuilt and accepted; normal checks are health + operator smokes, not reset/rebuild loops.
3. Runtime role selection is explicit and limited to allowed combinations (see `RuntimeRoleSelection`).
4. MCP remains a separate TypeScript service/container and is part of normal operator stack.

## Non-Blocking Constraints

These are known constraints but not blockers for normal operation:

- Stage 6 rebuild runbook remains required only when doing an explicit future rebuild/reset.
- Runtime roles are intentionally constrained; not every role can be combined in one host process.
- Synthetic smoke scope (`chat_id >= 9000000000000`) remains reserved for drills and must stay out of normal operator interpretation.

Follow-up classification is tracked in:

- [N1_KNOWN_LIMITS_FOLLOWUP_2026-03-31.md](./N1_KNOWN_LIMITS_FOLLOWUP_2026-03-31.md)

## Runtime/Handover Pointers

- Runtime/operator handover: [../runbooks/N1_RUNTIME_OPERATOR_HANDOVER_2026-03-31.md](../runbooks/N1_RUNTIME_OPERATOR_HANDOVER_2026-03-31.md)
- Operator quickstart: [../runbooks/N1_OPERATOR_QUICKSTART_2026-03-31.md](../runbooks/N1_OPERATOR_QUICKSTART_2026-03-31.md)
- Post-change verification baseline: [../runbooks/post-change-verification.md](../runbooks/post-change-verification.md)
