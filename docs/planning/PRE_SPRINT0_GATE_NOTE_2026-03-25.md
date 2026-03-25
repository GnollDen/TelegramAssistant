# Pre-Sprint 0 Gate Note

## Date

2026-03-25

## Gate Scope

This gate covers PS0-1 through PS0-7:
- authority normalization
- runtime role/health contracts
- Stage 6 v0 operator contract
- DB/data identity contract
- acceptance normalization
- final sprint-start readiness note

## PS0 Checklist

- PS0-1 Authority normalization: `pass`
  - authority is fixed to planning pack baseline
  - `BACKLOG_STATUS.md` is historical-only
- PS0-2 Runtime role contract: `pass`
  - runtime role matrix and allowed combinations documented
- PS0-3 Health contract: `pass`
  - startup/readiness/liveness/degraded rules documented
- PS0-4 Stage 6 v0 operator contract: `pass`
  - dossier/internal-vs-operator/chat-vs-case/one-queue rules fixed
- PS0-5 DB/data identity contract: `pass`
  - canonical IDs, dedupe, reopen, watermark ownership documented
- PS0-6 Acceptance normalization: `pass`
  - manual review pack contract and measurable thresholds added
- PS0-7 Consolidated gate note: `pass`
  - this document

## Authority Set After PS0

Sprint-start authority:
- `docs/planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md` (execution baseline)
- `docs/planning/STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md` (product contract baseline)
- `docs/planning/README.md` (navigation only)

Contract appendices:
- `docs/planning/PRE_SPRINT0_RUNTIME_ROLE_HEALTH_CONTRACT_2026-03-25.md`
- `docs/planning/PRE_SPRINT0_STAGE6_V0_OPERATOR_CONTRACT_2026-03-25.md`
- `docs/planning/PRE_SPRINT0_DB_DATA_IDENTITY_CONTRACT_2026-03-25.md`

Historical-only:
- `docs/BACKLOG_STATUS.md`

## Sprint 1 Start Decision

Gate decision: `ready-to-start-sprint-1`

Rationale:
- no competing execution authority remains in baseline docs
- runtime and health assumptions are explicit
- Stage 6 v0 and DB identity assumptions are no longer implicit
- acceptance wording is operational enough for gate tracking

## Residual Follow-Up (Non-Blocking for Sprint 1 Start)

- enforce these contracts in code and tests during Sprint 1-3
- keep supporting older plan docs as context only, not authority
