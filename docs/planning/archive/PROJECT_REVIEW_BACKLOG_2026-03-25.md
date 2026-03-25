# Project Review Backlog

## Date

2026-03-25

## Purpose

This backlog captures the actionable follow-up from deep review findings across:
- data ingest and brokers
- prompt lifecycle and validation
- database integrity and lifecycle

It is intended for execution by future agents and should be treated as a prioritized remediation plan rather than a general ideas list.

## Scope Summary

Review findings indicate that the highest-risk areas are not product polish but:
- delivery semantics
- state transitions
- queue/reclaim behavior
- database identity and consistency
- prompt lifecycle drift

## P0 Must Fix

### P0.1 Remove Stage 5 silent-loss window

Risk:
- A message can become `Processed` before extraction apply is fully durable.
- On exception, the message may leave the active work path without effective requeue.

Primary owners/files:
- [AnalysisWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs)
- [MessageRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/MessageRepository.cs)

Required outcome:
- `MarkProcessed` happens only after successful effective apply.
- Partial-failure paths do not silently drop work.
- Retry/requeue semantics stay explicit and observable.

Control points:
- `processed_messages_without_effective_extraction_apply`
- processed-but-no-extraction deltas

### P0.2 Fix Redis reclaim and poison-entry semantics

Risk:
- Reclaim may redeliver the same pending entries without dedupe on stream id.
- Poison entries may loop forever if deserialize fails and they are neither quarantined nor acknowledged.

Primary owners/files:
- [RedisMessageQueue.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Redis/RedisMessageQueue.cs)

Required outcome:
- dedupe guard on reclaim buffer
- DLQ/quarantine plus ack path for poison entries
- unique `Redis__ConsumerName` per instance

Control points:
- Redis PEL `pending_count`
- `max_idle_ms`
- repeat-claim rate
- deserialize failure count
- repeated reclaimed same stream id

### P0.3 Lock down message identity contract

Risk:
- DB uniqueness and runtime dedupe semantics can diverge.
- Future duplicate or split-identity regressions remain possible if message natural identity is not unified.

Primary owners/files:
- [0001_initial_schema.sql](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Migrations/0001_initial_schema.sql)
- [MessageRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/MessageRepository.cs)

Required outcome:
- one explicit natural identity contract for messages
- DB constraints aligned with repository behavior
- runtime dedupe aligned with canonical DB rule

Control points:
- duplicate-rate on messages
- identity mismatch incidents between repo and DB

### P0.4 Make watermark updates monotonic and CAS-safe

Risk:
- `analysis_state` blind overwrite can move watermarks incorrectly and hide regressions.

Primary owners/files:
- [AnalysisStateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/AnalysisStateRepository.cs)

Required outcome:
- monotonic watermark updates
- compare-and-set or equivalent safe write semantics
- alarms on regressions/non-monotonic movement

Control points:
- watermark monotonicity alarms
- suspicious backward/flat movement where progress is expected

## P1 Should Fix Next

### P1.1 Add prompt version/checksum lifecycle

Risk:
- Prompt templates are effectively create-once and may drift from code.

Primary owners/files:
- [AnalysisWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs)
- Stage 5 prompt-related files

Required outcome:
- prompt template version/checksum
- update-if-changed semantics
- drift detection between DB/runtime/code

Control points:
- prompt checksum mismatch
- validation reject rate after prompt changes

### P1.2 Make summary prompt single-source

Risk:
- Summary behavior may diverge because different prompt variants exist in different services.

Primary owners/files:
- [AnalysisWorkerService.Prompts.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.Prompts.cs)
- [DialogSummaryWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/DialogSummaryWorkerService.cs)

Required outcome:
- one source of truth for summary prompt contract
- explicit ownership of summary template lifecycle

Control points:
- summary consistency across inline path and worker path

### P1.3 Add DB-safe uniqueness and upsert for facts/relationships

Risk:
- Natural keys are not strongly protected.
- Read-then-write semantics can race and create silent inconsistency.

Primary owners/files:
- [FactRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/FactRepository.cs)
- relationship repositories/migrations in the same layer

Required outcome:
- DB uniqueness on natural identity
- conflict-safe upsert
- reduced race windows

Control points:
- duplicate-rate on facts
- duplicate-rate on relationships

## P2 Hardening and Tooling

### P2.1 Add CI guard for migration ordering and naming

Risk:
- Lexicographic migration loading plus duplicate prefixes can create fragile ordering.

Primary owners/files:
- [DatabaseInitializer.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/DatabaseInitializer.cs)
- CI scripts/pipeline

Required outcome:
- migration order collision check
- naming convention enforcement

### P2.2 Strengthen semantic validation

Risk:
- Prompt contracts may require canonical/allowlist behavior, but runtime validators only partially enforce it.

Primary owners/files:
- [ExtractionValidator.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/ExtractionValidator.cs)
- [ExtractionSchemaValidator.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/ExtractionSchemaValidator.cs)

Required outcome:
- semantic validator for allowlist/canonical rules
- less silent quality loss on schema-fail/degrade paths

### P2.3 Add integrity/fault-injection jobs

Risk:
- Recovery, reclaim, and corruption behavior may regress without explicit stress coverage.

Required outcome:
- crash/reclaim/corrupt payload tests
- periodic DB integrity job for:
  - duplicates
  - cursor regressions
  - processed-without-apply anomalies

## Safe Implementation Order

Recommended execution order:

1. P0.1 silent-loss window
2. P0.2 Redis reclaim/poison semantics
3. P0.3 message identity contract
4. P0.4 watermark CAS/monotonicity
5. P1.1 prompt version/checksum lifecycle
6. P1.2 summary prompt single-source
7. P1.3 fact/relationship uniqueness and upsert
8. P2.1 migration CI guard
9. P2.2 semantic validator
10. P2.3 integrity and fault-injection tooling

## Agent Execution Guidance

When delegating this backlog:
- keep fixes narrow and ordered
- do not combine P0 data correctness work with broad unrelated refactors
- prefer verification after each P0 item
- require explicit control-point evidence in final reports
- treat data safety and queue semantics as first-class acceptance criteria

## Current Overall Priority

The next most valuable work is not new feature breadth.
It is removal of silent data-loss and identity/queue consistency risk.
