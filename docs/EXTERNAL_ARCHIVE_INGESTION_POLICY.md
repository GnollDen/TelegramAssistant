# External Archive Ingestion Policy

## Purpose

Define a formal import contract for external archive data that can introduce competing relationship context signals.

This policy creates a stable handoff format for later runtime wiring.

## Import Envelope

Each import batch should provide:

- `batch_id`
- `case_id`
- `chat_id` (nullable only when chat linkage is genuinely unavailable)
- `source_system`
- `imported_at_utc`
- `records[]`

Each record should provide:

- `record_id`
- `source_type` (for example `external_archive`, `operator_note`, `clarification_answer`)
- `source_id`
- `observed_at_utc`
- `subject_actor_key`
- `competing_actor_key`
- `signal_type` (`graph`, `timeline`, `state`, `strategy`)
- `signal_subtype`
- `confidence` (0..1 input; clamped by interpreter)
- `evidence_refs[]`
- `metadata` (small auxiliary payload only)

## Case Scope Rule

- all records in one batch must belong to one `case_id`
- importer must reject cross-case mixed batches
- importer must retain canonical source ids for provenance

## Acceptance Rules

Accept a record only when:

- `case_id` matches batch scope
- `record_id` is present and unique within batch
- `signal_type` is supported
- at least one provenance anchor exists (`source_id`, `evidence_refs`, or canonical source id in metadata)

Reject a record when:

- it requests authoritative override semantics
- it is missing scope identity
- it is a duplicate replay with conflicting payload hash

## Interpretation Safety Rules

Imported competing-context records must be interpreted with:

- non-authoritative semantics
- additive-only output mode
- confidence clamp for competing hints (`max_effective_confidence <= 0.49`)
- explicit blocked-override alerts for unsafe intents

## Required Output Artifacts

Interpreter should emit:

- accepted/rejected import summary
- graph hints
- timeline annotations
- bounded current-state modifiers
- strategy constraints
- blocked override attempts
- review alerts

## Data Retention and Replay

- keep original raw record payload immutable for audit
- allow deterministic re-interpretation from raw payload
- do not mutate prior raw records in place

## Out of Scope

This policy does not define:

- full live runtime auto-application
- UI workflow details
- Stage 5 extraction budget/eval behavior
