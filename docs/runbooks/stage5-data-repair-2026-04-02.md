# Stage 5 Data Repair and Normalization (2026-04-02)

## Scope
- Stage 5 entity linkage and merge cleanup.
- Alias normalization and alias backfill.
- Evidence-level fact dedup.
- Relationship hygiene (self-loops and placeholder edge cleanup).

## Applied DB operations
- Repair script:
  - `scripts/stage5_data_repair_normalization.sql`
- Guardrail migration:
  - `src/TgAssistant.Infrastructure/Database/Migrations/0029_stage5_data_repair_guardrails.sql`

## Audit artifacts
- Before snapshot: `artifacts/stage5-repair/2026-04-02_before.txt`
- After snapshot: `artifacts/stage5-repair/2026-04-02_after.txt`
- DB audit tables:
  - `ops_stage5_repair_runs`
  - `ops_stage5_entity_merge_audit`
  - `ops_stage5_entity_backup`
  - `ops_stage5_fact_backup`
  - `ops_stage5_relationship_backup`

## Merge rules applied
- `canonical_name_duplicate`:
  - merge person entities sharing the same canonicalized name
  - canonical form: lowercase, `ё -> е`, punctuation-insensitive
  - winner preference: actor-key > telegram-id > fact count > updated-at
- `short_to_anchored_full`:
  - merge one-token short person into full-name entity if:
    - there is exactly one anchored full candidate (has actor key or telegram id),
    - short entity has no anchor,
    - evidence overlap by source-message facts is at least 3

## Guardrails for future ingestion
- Alias normalization uses canonical forms with `ё/е` normalization.
- Entity upsert now persists normalized alias rows.
- Full-name entities with strong identity anchor add first-name alias for short-form resolution.
- Intelligence claim persistence deduplicates `(entity,message,category,key,canonical_value)` within message.
- Fact upsert deduplicates evidence-equivalent facts by `(entity,source_message,category,key,canonical_value)`.
- Generic/ambiguous placeholder names are blocked earlier in Stage 5 extraction apply path.
