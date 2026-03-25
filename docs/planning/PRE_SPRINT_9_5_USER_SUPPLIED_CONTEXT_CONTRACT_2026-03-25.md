# Pre-Sprint 9.5 User-Supplied Context Contract

## Purpose

Freeze a first-wave contract for user-provided context so Stage 6 can use it without corrupting evidence semantics.

## Evidence Separation Rule

Hard rule:
- observed evidence, user-reported context, and system inference must remain separately typed

Implications:
- user-reported context is never silently persisted as observed fact
- system inference is never written as observed evidence without explicit evidence source

## Allowed Input Shapes

- short clarification answer (`/answer`) linked to a question/case
- long-form contextual correction/addition from web
- offline-event contextual note with explicit source type

## Minimum Data Contract

Required fields:
- `context_entry_id`
- `case_id?`
- `chat_id`
- `source_kind`
- `clarification_question_id?`
- `content_text`
- `structured_payload_json?`
- `applies_to_refs[]`
- `entered_via`
- `user_reported_certainty`
- `created_at_utc`
- `supersedes_context_entry_id?`
- `conflicts_with_refs[]`

## Linking To Cases And Artifacts

- context entries should link to affected case and target artifacts (`current_state`, `dossier`, `strategy`, `clarification_state` as applicable)
- linkage must be explicit; inferred linkage without references is not allowed for blocking decisions

## Correction And Conflict Handling

- corrections are additive and reference `supersedes_context_entry_id`
- conflicts with observed evidence remain explicit (`conflicts_with_refs[]`), not auto-overwritten
- contradictory context can trigger clarification reopen or conflict review

## Bot Vs Web Intake Rules

- bot intake: quick, short, high-frequency updates
- web intake: long-form details, structured payloads, correction trails
- both channels write the same source-separated context model

## Rendering And Storage Rules

Operator-facing surfaces must label each statement source as one of:
- `observed_evidence`
- `user_reported_context`
- `system_inference`

Storage rules:
- preserve original text/value and certainty label
- preserve provenance and supersede chain
- never coerce source type during refresh/recompute

## Sprint Hand-off

Sprint 10-11 should introduce explicit storage/read models for this contract and wire them to clarification-case resolution and artifact refresh logic.
