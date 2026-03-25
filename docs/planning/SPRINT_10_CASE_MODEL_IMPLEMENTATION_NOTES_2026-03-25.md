# Sprint 10 Case Model Implementation Notes (2026-03-25)

## What Was Implemented

Sprint 10 introduces a first-class Stage 6 unified case layer:

- `stage6_cases` as canonical lifecycle record for operator work.
- `stage6_case_links` for explicit linkage to source primitives and artifact targets.
- `stage6_user_context_entries` for source-separated user-supplied context input.

## Scope Semantics

- Existing `case_id` in legacy Stage 6 domain tables remains the analysis scope key.
- New actionable case record identity is `stage6_cases.id` (UUID).

This avoids overloading `case_id` semantics while preserving Sprint 1-9 behavior.

## Case Types And Statuses

Implemented statuses:
- `new`
- `ready`
- `needs_user_input`
- `resolved`
- `rejected`
- `stale`

Implemented case types (schema-valid):
- `needs_input`
- `needs_review`
- `risk`
- `state_refresh_needed`
- `dossier_candidate`
- `draft_candidate`
- clarification typed variants from Pre-Sprint 9.5 (`clarification_*`)
- user-context related typed variants (`user_context_*`)

## Unified Mapping

Existing primitives now map into the unified case layer:

- `domain_clarification_questions` -> typed `needs_input` / `clarification_*` cases.
- `domain_inbox_items` -> `needs_review` / `state_refresh_needed` / candidate cases.
- `domain_conflict_records` -> `risk` cases.

Clarification queue building now reads unified case state first (with legacy fallback), preventing a second queue model.

## User Context Integration

Clarification answer application now also persists user context entry in `stage6_user_context_entries` with:

- explicit source kind/type
- certainty
- applies-to references
- linkage to the corresponding Stage 6 case

This preserves source separation between observed evidence and user-reported context.
