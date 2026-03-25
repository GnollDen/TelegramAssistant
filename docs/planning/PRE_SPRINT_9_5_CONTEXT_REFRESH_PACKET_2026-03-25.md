# Pre-Sprint 9.5 Context Refresh Packet

## Purpose

Freeze one shared Stage 6 baseline for all implementation agents after Sprint 7-9.
This packet is contract-only and is the execution context for Sprint 10+ design and implementation.

## Source-of-Truth Inputs

`baseline_date`: `2026-03-25`

`authoritative_doc_refs[]`:
- `docs/planning/README.md`
- `docs/planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md`
- `docs/planning/STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md`
- `docs/planning/PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`
- `docs/planning/PRE_SPRINT0_STAGE6_V0_OPERATOR_CONTRACT_2026-03-25.md`
- `docs/planning/PRE_SPRINT0_DB_DATA_IDENTITY_CONTRACT_2026-03-25.md`
- `docs/CASE_ID_POLICY.md`

## Current System Reality

`implemented_modules[]`:
- persisted Stage 6 artifact layer (`stage6_artifacts`) with freshness and stale logic
- bot Stage 6 command surface (`/state`, `/draft`, `/review`, `/gaps`, `/answer`, `/timeline`, `/offline`)
- clarification subsystem (questions, answers, dependencies, conflict/recompute handling)
- web read/review/search/inbox/history surfaces
- profile/timeline/strategy/draft/review services with persistence and verification services

`deployable_surfaces[]`:
- bot interaction path via Stage 6 services
- web read/review renderer routes inside current host topology

## Locked Terms And Contracts

`locked_contracts[]`:
- planning authority is the planning pack; `docs/BACKLOG_STATUS.md` is not equal authority
- `case_id` stays the analysis scope key (not message/session/entity id)
- one canonical queue rule remains in force (bot/web are filtered views of one queue)
- Stage 6 artifact foundation from Sprint 7 remains additive and current-revision based
- Sprint 8-9 quality contracts remain mandatory: fact vs interpretation separation, explicit uncertainty/missing information, signal-strength markers, ethical strategy and fixed draft variant shape

## Known Gaps Reserved For Sprint 10-13

`known_gaps[]`:
- no first-class actionable `Case` aggregate yet (existing primitives are inbox/conflict/clarification objects)
- no canonical clarification-case contract mapped to Sprint 10 case statuses/types yet
- no frozen user-supplied context storage contract with explicit source typing and conflict/supersede rules
- behavioral profile exists as substrate, but no bounded first-wave artifact/consumer contract
- autonomous case generation workflow is not yet activated as a dedicated Stage 6 hosted runtime path

## Assumptions Agents Must Not Make

`forbidden_assumptions[]`:
- do not assume Sprint 10 starts from zero queue/case infrastructure
- do not introduce a second queue or competing case identity model
- do not silently convert user-reported context into observed evidence
- do not treat behavioral profile as clinical diagnosis or hard truth layer
- do not reinterpret already-finished Sprint 7-9 foundations unless explicitly required

## Sprint 10 Entry Conditions

`next_sprint_entry_points[]`:
- clarification case contract is explicit and implementation-ready
- user-supplied context contract is explicit and source-separated
- behavioral-profile first-wave plan is bounded and non-diagnostic
- Sprint 10-13 fit check is documented with narrow adjustments only
