# Planning Pack

## Purpose

This folder is the clean-slate planning entry point for the Person Intelligence System track.

Active planning authority is now anchored to the PRD, the reset boundary, and the execution backlog. Legacy Stage6/web/tgbot materials remain available only as historical context.

## Active Authority

Use this exact set as the current source of truth for planning and execution routing:

1. [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](./PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md)
2. [CLEANUP-001-A_VALID_CORE_INVENTORY_2026-04-02.md](./CLEANUP-001-A_VALID_CORE_INVENTORY_2026-04-02.md)
3. [CLEANUP-001-B_RESET_BOUNDARY_NOTE_2026-04-02.md](./CLEANUP-001-B_RESET_BOUNDARY_NOTE_2026-04-02.md)
4. [CLEANUP-002-A_PLANNING_AND_RUNBOOK_INVENTORY_2026-04-02.md](./CLEANUP-002-A_PLANNING_AND_RUNBOOK_INVENTORY_2026-04-02.md)
5. [tasks.json](../../tasks.json)
6. [task_slices.json](../../task_slices.json)

## Current Interpretation

- The PRD is the product-shape authority for the clean-slate track.
- The valid-core inventory defines what survives from the previous workspace baseline.
- The reset-boundary note freezes current Stage6/web/tgbot behavior as legacy, not target architecture.
- The backlog files define execution order and slice boundaries for current work.
- No active planning doc should treat the old N1/Stage6/web/tgbot path as current baseline authority.

## Historical / Archive Inputs

These remain useful context but are not execution authority:

- [N1_READINESS_BASELINE_2026-03-31.md](./N1_READINESS_BASELINE_2026-03-31.md)
- [N1_KNOWN_LIMITS_FOLLOWUP_2026-03-31.md](./N1_KNOWN_LIMITS_FOLLOWUP_2026-03-31.md)
- [N1_RUNTIME_OPERATOR_HANDOVER_2026-03-31.md](../runbooks/N1_RUNTIME_OPERATOR_HANDOVER_2026-03-31.md)
- [N1_OPERATOR_QUICKSTART_2026-03-31.md](../runbooks/N1_OPERATOR_QUICKSTART_2026-03-31.md)
- [S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md](./S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md)
- [stage6-rebuild-verification.md](../runbooks/stage6-rebuild-verification.md)
- [FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md](./FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md)
- [STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md](./STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md)
- [STAGE6_VERIFIED_BACKLOG_2026-03-30.md](./STAGE6_VERIFIED_BACKLOG_2026-03-30.md)
- [STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md](./STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md)
- [RUNTIME_TOPOLOGY_NOTE_2026-03-25.md](./RUNTIME_TOPOLOGY_NOTE_2026-03-25.md)
- `docs/planning/archive/*`
- historical `SPRINT_*` task packs and draft acceptance docs

## Notes

- Historical docs are preserved for traceability only.
- If a doc conflicts with the PRD or reset boundary, the clean-slate authority chain wins.
