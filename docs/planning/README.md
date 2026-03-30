# Planning Pack

## Purpose

This folder is the authoritative planning entry point for the current dev baseline.

This pass is synchronized for `Pre-Sprint S6-R0: Runtime and Rebuild Prep` and aligns planning docs with the code-verified Stage 6 backlog.

## Current Authority (Dev Source of Truth)

Use this exact set as authoritative for Stage 6 remediation planning:

1. [README.md](./README.md)
2. [FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md](./FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md)
3. [STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md](./STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md)
4. [STAGE6_VERIFIED_BACKLOG_2026-03-30.md](./STAGE6_VERIFIED_BACKLOG_2026-03-30.md)
5. [STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md](./STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md)

## S6-R0 Runtime/Rebuild Docs

For operational prep before rebuild execution:

- [S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md](./S6_R0_RUNTIME_REBUILD_BASELINE_2026-03-30.md)
- [stage6-rebuild-verification.md](../runbooks/stage6-rebuild-verification.md)

These docs are execution support for S6-R0 and do not replace the authority set above.

## Sequencing Baseline

Current sequencing rule:

1. Complete Stage 5 rerun and confirm completion evidence.
2. Execute `S6-R1` to `S6-R4` must-fix remediation.
3. Run Stage 6 reset/rebuild and verification in `S6-R5`.
4. Run post-rebuild expansion (`S6-R6`) only after rebuilt verification is complete.

Do not start Stage 6 rebuild from ambiguous runtime or provider state.

## Stage 5 Completion Mode (For Stage 6 Planning)

For Stage 6 planning on dev, treat Stage 5 as:

- a required completion gate before Stage 6 reset/rebuild
- not a redesign track inside S6 remediation sprints
- a source substrate whose old/new mixed Stage 6 artifacts are untrusted after rerun

Operational check before post-rebuild verification:

- `Analysis__ArchiveOnlyMode=false`
- `Analysis__ArchiveCutoffUtc` does not suppress current live messages

## Supporting (Not Authority)

These documents remain useful, but are supporting context, not the primary entry point:

- [STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md](./STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md)
- [STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md](./STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md)
- [PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md](./PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md)
- [RUNTIME_TOPOLOGY_NOTE_2026-03-25.md](./RUNTIME_TOPOLOGY_NOTE_2026-03-25.md)
- [INITIAL_CHRONOLOGY_RECONSTRUCTION_PLAN_2026-03-26.md](./INITIAL_CHRONOLOGY_RECONSTRUCTION_PLAN_2026-03-26.md)
- [WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md](./WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md)

## Historical / Archive Inputs

Do not use these as sprint entry-point authority:

- `docs/planning/archive/*`
- [FULL_REVIEW_BACKLOG_2026-03-25.md](./archive/FULL_REVIEW_BACKLOG_2026-03-25.md)
- [PROJECT_REVIEW_BACKLOG_2026-03-25.md](./archive/PROJECT_REVIEW_BACKLOG_2026-03-25.md)
- [WORKING_SPRINTS_2026-03-25.md](./archive/WORKING_SPRINTS_2026-03-25.md)

## Web W0 Packet (Historical Supporting Only)

The W0 web pre-sprint packet stays as historical/supporting evidence for that track:

- `PRE_SPRINT_W0_WEB_BASELINE_CONTEXT_NOTE_2026-03-25.md`
- `PRE_SPRINT_W0_WEB_DEV_ENV_NOTE_2026-03-25.md`
- `PRE_SPRINT_W0_WEB_W1_W2_SCOPE_NOTE_2026-03-25.md`
- `PRE_SPRINT_W0_WEB_W3_W4_SCOPE_NOTE_2026-03-25.md`
- `PRE_SPRINT_W0_WEB_VERIFICATION_BASELINE_NOTE_2026-03-25.md`
