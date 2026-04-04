# OPINT-003-D Validation 2026-04-04

## Scope analyzed

- Bounded scope: `chat:885574984`
- Validation surface:
  - operator tracked-person query/select contracts
  - resolution queue/detail/action contracts
  - post-action recompute lifecycle persistence
  - related-conflict reevaluation and domain-review event emission
  - replayed lifecycle feedback projection artifact

## Key findings and evidence

- Added bounded runtime validator entrypoint `--opint-003-d-validate` via [Program.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Program.cs) and [Opint003LoopValidationRunner.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Launch/Opint003LoopValidationRunner.cs).
- Validation uncovered a real runtime defect in linked action lifecycle refresh: [Stage8RecomputeQueueRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/Stage8RecomputeQueueRepository.cs) was issuing `LIKE` against `operator_audit_events.details_json` while the column is `jsonb`, which fails in Postgres during recompute completion.
- Minimal fix applied: lifecycle refresh now narrows accepted action/audit candidates by scope and performs the queue-item-id string match client-side after materialization, avoiding invalid `jsonb LIKE` SQL while preserving the deduped queue-link behavior.
- Concrete report artifact: [logs/opint-003-d-validation-report.json](/home/codex/projects/TelegramAssistant/logs/opint-003-d-validation-report.json)

## Validated

- Normal path:
  - tracked-person query accepted
  - tracked-person selection accepted
  - resolution queue accepted with 2 seeded source items visible
  - `approve` action accepted on `missing_data:stage8_queue:*`
  - recompute projected `running -> done`
  - related-conflict reevaluation applied with `created=1`, `resolved=1`, `domain_review_events=2`
  - idempotent replay returned `done` lifecycle feedback with `last_result_status=result_ready`
- Degraded path:
  - second `approve` action accepted on a distinct seeded source item
  - recompute projected `running -> clarification_blocked`
  - related-conflict reevaluation skipped with `result_status_not_ready`
  - idempotent replay returned `clarification_blocked` lifecycle feedback with `last_result_status=need_operator_clarification`
- Cleanup:
  - residual action, audit, queue, conflict, domain-review-event, metadata, and tracked-person rows all returned to zero after the run

## Commands run

```bash
dotnet build TelegramAssistant.sln
POSTGRES_PASSWORD=... Runtime__Role=ops Database__ConnectionString='Host=127.0.0.1;Database=tgassistant;Username=tgassistant;Password=...' Redis__ConnectionString='127.0.0.1:6379' LlmGateway__Providers__openrouter__ApiKey='or-live-opint003dvalidationkey' dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --operator-schema-init --opint-003-d-validate --opint-003-d-validate-output=/home/codex/projects/TelegramAssistant/logs/opint-003-d-validation-report.json
```

## Residual risk

- This gate validates the bounded operator recompute loop on one synthetic tracked person inside `chat:885574984`; it does not validate Stage7 model execution quality, Telegram rendering, or OPINT-004+ UI behavior.
- Operator tracked-person query still emits EF ordering warnings because `Distinct` erases the upstream ordering before `Take`; that did not block this slice but remains a separate query hygiene issue.
