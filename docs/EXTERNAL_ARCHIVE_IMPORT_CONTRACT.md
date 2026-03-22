# External Archive Import Contract

## Purpose

Formal contract for sidecar external archive ingestion foundation.

## Contract Envelope

`external_archive_import_request` must include:

- `case_id` (required, analysis scope)
- `source_class` (required)
- `source_ref` (required, stable external source descriptor)
- `imported_at_utc` (required)
- `records` (required, non-empty)

## Allowed Source Classes

- `supporting_context_archive`
- `mutual_group_archive`
- `indirect_mention_archive`
- `competing_relationship_archive`

## Record Contract

Each record should include:

- `record_id` (required, stable within source)
- `occurred_at_utc` (required)
- `record_type` (required: `message`, `event`, `relationship_signal`, `clarification_input`, `note`)
- `text` (optional)
- `subject_actor_key` (optional)
- `target_actor_key` (optional)
- `chat_id` (optional canonical anchor)
- `source_message_id` (optional canonical anchor)
- `source_session_id` (optional canonical anchor)
- `evidence_refs` (optional list)
- `confidence` (required, range `0..1`)
- `raw_payload_json` (required)

## Provenance Contract

Each record must produce `provenance` with:

- `truth_layer` (`observed_from_chat`, `observed_from_audio`, `user_reported`, `user_confirmed`, `model_inferred`, `model_hypothesis`)
- `source_class`
- `source_ref`
- `import_batch_id`
- `payload_hash`

## Weighting Contract

Each record must produce `weighting` with:

- `base_weight`
- `confidence_multiplier`
- `corroboration_multiplier`
- `final_weight`
- `needs_clarification`
- `weighting_reason`

## Linkage Contract

Each record should produce zero or more linkage entries:

- `graph_link` with node/edge target ids
- `period_link` with `period_id` or temporal match reason
- `event_link` with communication/offline event target id
- `clarification_link` with generated or referenced question id

Every link entry carries:

- `link_type`
- `target_type`
- `target_id`
- `link_confidence`
- `reason`

## Contract Validation Rules

Reject request if:

- `case_id <= 0`
- unsupported `source_class`
- empty `source_ref`
- empty `records`
- any record with invalid `confidence`
- any record missing `record_id`, `record_type`, `occurred_at_utc`, or `raw_payload_json`

## Integration Status

Current status: foundation-only contract.

Not included yet:

- host wiring
- worker orchestration
- production persistence flow
- runtime auto-link execution
