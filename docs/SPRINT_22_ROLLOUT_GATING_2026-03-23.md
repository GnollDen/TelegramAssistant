# Sprint 22 Rollout Gating (Post Stage 5 Tail)

## Purpose

Define hard rollout gates for Modular Split M1 so workload separation does not destabilize active Stage 5 runtime.

## Gate 0: Stage 5 Tail Completion

Required before any runtime split activity:
- active Stage 5 tail is fully drained
- no in-flight destructive repair/recompute flow
- current watermark/state snapshot captured

If Gate 0 is not satisfied: rollout is blocked.

## Gate 1: Foundation Safety

- Sprint 20 phase guards/backup guardrail are already shipped and validated
- backup freshness evidence exists for rollback scope
- integrity preflight for planned split scope is clean or explicitly accepted

If Gate 1 is not satisfied: rollout is blocked.

## Gate 2: Startup Role Readiness

- role mapping is explicit per workload (`ingest`, `stage5`, `stage6`, `web`, `ops`, `maintenance`, `mcp`)
- default `all` behavior is preserved for fallback start mode
- ownership matrix has no ambiguous shared owners for Telegram session

If Gate 2 is not satisfied: rollout is blocked.

## Gate 3: Workload Isolation Validation

Staging-like verification must show:
- ingest workload is the only Telegram session owner
- Stage 5 workload runs without Stage 6/Web/MCP coupling
- Stage 6 iteration does not impact ingest uptime path
- web and mcp isolation does not break DB/read contracts

If Gate 3 is not satisfied: rollout is blocked.

## Gate 4: Cutover + Rollback Readiness

Before production cutover:
- cutover sequence is documented step-by-step
- rollback sequence is documented step-by-step
- operator on-call has runbook with clear stop conditions

If Gate 4 is not satisfied: rollout is blocked.

## Minimal Post-Cutover Checks

- all intended workloads started in correct roles
- no duplicate worker ownership
- healthchecks green across workloads
- message ingestion + Stage 5 progress remain stable
- operator visibility intact (logs/metrics/basic diagnostics)

## Explicit Non-Goals During Tail

Until Stage 5 tail completion:
- no production compose split activation
- no runtime role cutover in active path
- no ownership transfer for Telegram connectivity
