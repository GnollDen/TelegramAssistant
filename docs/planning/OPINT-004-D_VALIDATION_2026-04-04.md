# OPINT-004-D Validation 2026-04-04

## Scope analyzed

- Bounded Telegram/operator scope: `chat:885574984`
- Validation surface:
  - Telegram mode selection + explicit tracked-person context
  - Telegram authorization allowlist enforcement and denied audit visibility
  - bounded operator action persistence and deny-path behavior
  - post-action recompute lifecycle and related-conflict feedback

## Key findings and evidence

- Telegram bounded smoke succeeded with explicit owner allowlist and unauthorized deny checks via [logs/opint-004-d-smoke-report.json](/home/codex/projects/TelegramAssistant/logs/opint-004-d-smoke-report.json).
  - mode card rendered with explicit mode selection
  - tracked-person context switched twice and persisted in session snapshot (`resolution_queue`)
  - unauthorized `/start` was denied and audited (`UnauthorizedDeniedCount=1`)
- Action persistence + authorization deny checks succeeded on bounded seeded scope via [logs/opint-002-d-validation-report.json](/home/codex/projects/TelegramAssistant/logs/opint-002-d-validation-report.json).
  - normal `approve` action persisted one action row + one accepted audit row
  - explanation-required failure returned `400` and persisted denied audit without action row writes
  - tracked-person mismatch returned `403` and persisted denied audit without action row writes
- Recompute lifecycle and related-conflict feedback succeeded via [logs/opint-004-d-recompute-report.json](/home/codex/projects/TelegramAssistant/logs/opint-004-d-recompute-report.json).
  - normal scenario: `running -> done`, `last_result_status=result_ready`, related conflicts `created=1`, `resolved=1`
  - degraded scenario: `running -> clarification_blocked`, `last_result_status=need_operator_clarification`, reevaluation skipped with `result_status_not_ready`

## Commands run

```bash
dotnet build TelegramAssistant.sln
POSTGRES_PASSWORD=$(docker inspect tga-postgres --format '{{range .Config.Env}}{{println .}}{{end}}' | awk -F= '$1=="POSTGRES_PASSWORD"{print $2}')

Runtime__Role=ops Telegram__OwnerUserId=885574984 \
Database__ConnectionString="Host=127.0.0.1;Database=tgassistant;Username=tgassistant;Password=${POSTGRES_PASSWORD}" \
Redis__ConnectionString='127.0.0.1:6379' \
LlmGateway__Providers__openrouter__ApiKey='or-live-opint004dvalidationkey' \
dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- \
--operator-schema-init --opint-004-a-smoke \
--opint-004-a-smoke-output=/home/codex/projects/TelegramAssistant/logs/opint-004-d-smoke-report.json

POSTGRES_PASSWORD="${POSTGRES_PASSWORD}" scripts/opint-002-resolution-contract-validate.sh

Runtime__Role=ops \
Database__ConnectionString="Host=127.0.0.1;Database=tgassistant;Username=tgassistant;Password=${POSTGRES_PASSWORD}" \
Redis__ConnectionString='127.0.0.1:6379' \
LlmGateway__Providers__openrouter__ApiKey='or-live-opint003dvalidationkey' \
dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- \
--operator-schema-init --opint-003-d-validate \
--opint-003-d-validate-output=/home/codex/projects/TelegramAssistant/logs/opint-004-d-recompute-report.json
```

## Residual risk

- Telegram validation remains bounded to synthetic seeded scope behavior and does not replace multi-operator or production-traffic soak validation.
- EF warnings on tracked-person query ordering (`Distinct` + row limiting) remain visible during smokes and should be addressed separately for query determinism hygiene.
