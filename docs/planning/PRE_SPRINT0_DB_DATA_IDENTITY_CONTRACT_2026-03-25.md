# Pre-Sprint 0 DB and Data Identity Contract

## Date

2026-03-25

## Purpose

This note fixes PS0-5 for sprint start.
It defines canonical identities and control-state ownership assumptions required before Sprint 3 implementation.

## Canonical Identity

### Message Identity

A logical message is uniquely identified by:
- `(source_platform='telegram', chat_id, telegram_message_id)`

Redelivery or edit of the same Telegram message must update the same logical message identity and must not create another logical message row.

### Artifact Identity

A logical Stage 6 artifact is uniquely identified by:
- `(scope_kind='chat', scope_id, artifact_type)`

Each materialization has immutable `artifact_revision_id`.
At most one revision per logical artifact key may be marked current.

### Case Identity

A logical case is uniquely identified by:
- `(scope_kind='chat', scope_id, case_type, driver_key)`

`driver_key` must be stable for dedupe-safe auto-creation.

## Dedupe Rules

- Artifact dedupe:
  - same logical artifact key plus same freshness basis should reuse/idempotently upsert current revision
- Case dedupe:
  - no more than one active case per `(scope_kind, scope_id, case_type, driver_key)`
  - new evidence for same active case should update in place

## Reopen Semantics

- Active statuses: `new`, `ready`, `needs_user_input`
- System superseded: `stale`
- Terminal for current evidence basis: `resolved`, `rejected`

Reopen is allowed only when freshness basis advanced beyond close point.
For `rejected`, reopen additionally requires material driver change or explicit operator override.

## Freshness Basis and Ownership

Freshness basis is:
- `(source_message_watermark, context_version)`

Where:
- `source_message_watermark` tracks Stage 5 substrate progress
- `context_version` tracks offline/manual operator context changes

Ownership:
- Stage 5 owns and advances `source_message_watermark` only after durable substrate commit
- Stage 6 reads Stage 5 watermarks but does not advance them
- Stage 6 owns artifact/case control-state versions and advances only after durable write commit

## Monotonicity Rule

All watermark and control-state versions must be monotonic and CAS-safe.
No component may move version backward or acknowledge non-durable work.

## Sprint 3 Enforcement Map (Implementation)

Implemented in Sprint 3 pass:

- Message identity:
  - DB uniqueness: `(chat_id, telegram_message_id)` via `0022_sprint_3_db_identity_integrity.sql`
  - repository lookup/dedupe no longer relies on source-scoped identity in `MessageRepository`
- Fact/relationship uniqueness and upserts:
  - DB uniqueness:
    - current facts: `(entity_id, category, key, value) where is_current=true`
    - relationships: `(from_entity_id, to_entity_id, type)`
  - repository upserts moved to `INSERT ... ON CONFLICT ... DO UPDATE`
- Watermarks:
  - `AnalysisStateRepository.SetWatermarkAsync` is monotonic (`GREATEST`) and blocks backward writes
  - explicit backward reset path remains `ResetWatermarksIfExistAsync`
- Stage 6 queue/artifact layer (current DB substrate):
  - queue identity for `domain_inbox_items`: `(case_id, item_type, source_object_type, source_object_id)`
  - conflict identity for `domain_conflict_records`: symmetric object-pair natural key per `(case_id, conflict_type, object-pair)`
