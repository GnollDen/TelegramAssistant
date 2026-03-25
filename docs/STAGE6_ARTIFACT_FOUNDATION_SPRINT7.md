# Sprint 7: Stage 6 Artifact Foundation Contract

Date: 2026-03-25

## Scope

This document records the Sprint 7 additive contract for persisted Stage 6 artifacts.

Covered artifact types:
- `dossier`
- `current_state`
- `strategy`
- `draft`
- `review`
- `clarification_state`

## Persistence Model

New table: `stage6_artifacts`

Each record is a persisted artifact revision with:
- identity: `artifact_type`, `case_id`, `chat_id`, `scope_key`
- payload linkage: `payload_object_type`, `payload_object_id`, `payload_json`
- freshness: `generated_at`, `refreshed_at`, `stale_at`, `is_stale`, `stale_reason`
- reuse tracking: `reuse_count`
- provenance: `source_type`, `source_id`, `source_message_id`, `source_session_id`
- basis: `freshness_basis_hash`, `freshness_basis_json`

Current-revision rule:
- only one `is_current=true` record per `(artifact_type, case_id, chat_id, scope_key)`
- replacing current revision is additive (old record remains, `is_current=false`)

## Freshness/Stale Rules (Foundation)

Artifact is stale when at least one is true:
- underlying evidence timestamp is newer than `generated_at`
- `stale_at` TTL is exceeded
- artifact was explicitly marked stale

First-wave evidence basis includes (as applicable):
- latest processed message timestamp
- latest clarification question/update timestamp
- latest clarification answer timestamp
- latest offline event timestamp
- dossier additionally considers hypothesis/conflict updates

## Reuse vs Regeneration

Read path behavior for bot/web foundation:
1. Read current artifact revision.
2. Evaluate stale/fresh against evidence basis and TTL.
3. If fresh: reuse persisted artifact and increment `reuse_count`.
4. If stale or missing: regenerate/persist new revision and set it current.

No broad redesign is introduced; existing domain tables and review events remain intact.
