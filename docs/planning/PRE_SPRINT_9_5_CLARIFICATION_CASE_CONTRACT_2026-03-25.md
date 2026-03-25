# Pre-Sprint 9.5 Clarification Case Contract

## Purpose

Define system-detected clarification cases as explicit operator work items before Sprint 10 case-model implementation.

## Scope And Non-Goals

In scope:
- case-level contract for clarification triggers, fields, lifecycle, and hand-off

Out of scope:
- broad case-system implementation
- queue redesign
- replacement of existing clarification subsystem internals

## Case Type Definition

`case_type`: `needs_input` (clarification subtype)

`clarification_kind` allowed values:
- `missing_data`
- `ambiguity`
- `evidence_interpretation_conflict`
- `next_step_blocked`

Case must be created when at least one is true:
- data required for stable state/strategy/dossier is missing
- multiple plausible interpretations remain unresolved
- evidence and current interpretation conflict materially
- operator input is required for the next useful action

## Lifecycle

`status` values:
- `new`
- `ready`
- `needs_user_input`
- `resolved`
- `rejected`
- `stale`

Lifecycle mapping to current clarification objects:
- primary lifecycle owner is the case-level record
- `ClarificationQuestion` remains the question payload object linked to the case
- `ClarificationAnswer` remains answer payload and never replaces the case lifecycle record

## Evidence And Provenance Rules

- every clarification case must include evidence basis (`evidence_refs[]`) and short operator reason
- provenance fields must preserve `source_type`, `source_id`, `source_message_id`, `source_session_id` where available
- clarification case is actionable only when evidence basis and affected targets are explicit

## Bot Vs Web Intake

- bot is primary fast intake (`/gaps`, `/answer`) for short responses
- web is expanded intake/review for evidence inspection and longer answers
- both surfaces operate on one canonical queue model

## Minimum Data Contract

Required fields for Sprint 10 implementation:
- `case_id`
- `case_type`
- `status`
- `priority`
- `chat_id`
- `scope_type`
- `clarification_kind`
- `question_text`
- `reason_summary`
- `evidence_refs[]`
- `subject_refs[]`
- `response_mode`
- `response_channel_hint`
- `target_artifact_types[]`
- `created_at_utc`
- `resolved_at_utc?`
- `reopen_trigger_rules[]`

## Resolution And Reopen Rules

Resolution allowed when:
- answer is recorded with explicit source metadata
- answer applicability to target artifacts is recorded

Reopen allowed when:
- new evidence materially changes the prior basis
- answer is superseded/corrected by operator input
- downstream artifacts are stale against updated context basis

Reject allowed when:
- signal is non-actionable/noisy or no longer relevant

## Sprint Hand-off

Sprint 10 should implement this contract by mapping existing clarification question/answer objects into first-class case lifecycle records without creating a second queue.
