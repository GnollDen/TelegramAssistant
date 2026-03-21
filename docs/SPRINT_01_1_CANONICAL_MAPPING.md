# Sprint 01.1 Canonical Mapping Audit

## Canonical Existing Layer (reused)

- `messages`: canonical communication substrate and evidence anchors.
- `entities`: canonical actor identity records.
- `relationships`: canonical relationship graph edges.
- `facts`: canonical durable truth records.
- `communication_events`: canonical message-derived events.
- `chat_sessions`: canonical runtime session segmentation.

## New Domain Layer (kept as first-class)

Kept because these represent orchestration/state objects not fully covered by canonical operational tables:

- `domain_periods`
- `domain_period_transitions`
- `domain_hypotheses`
- `domain_clarification_questions`
- `domain_clarification_answers`
- `domain_state_snapshots`
- `domain_profile_snapshots`
- `domain_profile_traits`
- `domain_strategy_records`
- `domain_strategy_options`
- `domain_draft_records`
- `domain_draft_outcomes`
- `domain_inbox_items`
- `domain_conflict_records`
- `domain_dependency_links`
- `domain_offline_events`
- `domain_audio_assets`
- `domain_audio_segments`
- `domain_audio_snippets`

## Explicit Canonical Anchoring Rules

To prevent parallel-truth drift, new domain records must anchor to canonical operational context when applicable:

- `chat_id` for chat/case scope disambiguation where relevant.
- `source_message_id` linking back to canonical `messages(id)` for evidence provenance.
- `source_session_id` linking back to canonical `chat_sessions(id)` for session-level provenance.

## Repair Decision Notes

- We do **not** create domain duplicates of `messages/entities/relationships/facts/communication_events/chat_sessions`.
- Domain layer remains focused on orchestration and decision-state objects.
- Lifecycle updates and review history are tracked via `domain_review_events` plus actor/reason fields on status-driven queue records.
