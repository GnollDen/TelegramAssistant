# Bounded Seeded Pre-Run for `chat:885574984`

Short operator runbook for local/compose pre-run on the bounded main chat.

## Scope

- Chat is hard-bounded to `885574984` for Stage5 scoped repair.
- Seed scope key should be `chat:885574984` unless there is an explicit alternate scope contract.
- Do not combine seed/apply work with normal runtime startup changes.

## Env Checklist

Before touching runtime or DB, confirm the compose env has at least:

```bash
export POSTGRES_PASSWORD='...'
export TG_API_ID='...'
export TG_API_HASH='...'
export TG_PHONE='...'
export TG_OWNER_ID='...'
export TG_MONITORED_CHATS='885574984'
export GEMINI_API_KEY='...'
export CLAUDE_API_KEY='...'
export MCP_SSE_AUTH_TOKEN='...'
export GRAFANA_ADMIN_PASSWORD='...'
```

Quick sanity check:

```bash
env | rg '^(POSTGRES_PASSWORD|TG_API_ID|TG_API_HASH|TG_PHONE|TG_OWNER_ID|TG_MONITORED_CHATS|GEMINI_API_KEY|CLAUDE_API_KEY|MCP_SSE_AUTH_TOKEN|GRAFANA_ADMIN_PASSWORD)='
```

Operator-only:

- `--operator-schema-init`, `--seed-bootstrap-scope`, and Stage5 scoped repair apply are operator actions.
- Do not run apply without current backup evidence and an audit id.
- Do not target any other chat id for Stage5 scoped repair; code blocks it.

## Docker Up

Start only substrate services first (keep runtime workers quiescent during seed/repair):

```bash
docker compose up -d postgres redis
```

Basic status:

```bash
docker compose ps
```

Optional schema init, only when migrations must be applied:

```bash
docker compose run --rm -e Runtime__Role=ops app --operator-schema-init
```

## Readiness

Container health for substrate:

```bash
docker compose ps postgres redis
```

Connectivity sanity for operator role path:

```bash
docker compose run --rm -e Runtime__Role=ops app --liveness-check
```

## Seed Dry-Run

Fill in the operator/tracked identity fields from the approved seed contract. Keep optional identity hints explicit; do not invent them.

```bash
docker compose run --rm -e Runtime__Role=ops app \
  --seed-bootstrap-scope \
  --seed-dry-run \
  --seed-scope-key=chat:885574984 \
  --seed-chat-id=885574984 \
  --seed-operator-full-name='OPERATOR_FULL_NAME' \
  --seed-tracked-full-name='TRACKED_FULL_NAME' \
  --seed-operator-canonical-name='operator canonical name' \
  --seed-tracked-canonical-name='tracked canonical name' \
  --seed-operator-telegram-user-id=123456789 \
  --seed-operator-telegram-username='operator_username' \
  --seed-tracked-telegram-user-id=987654321 \
  --seed-tracked-telegram-username='tracked_username' \
  --seed-source-message-id=12345
```

Expected output is a `bootstrap_scope_seed_report` with:

- `contract_status: seeded_and_bootstrap_ready` or a clear validation error
- `bootstrap_ready: yes` before proceeding to apply

## Seed Apply

Run only after the dry-run report is clean:

```bash
docker compose run --rm -e Runtime__Role=ops app \
  --seed-bootstrap-scope \
  --seed-apply \
  --seed-scope-key=chat:885574984 \
  --seed-chat-id=885574984 \
  --seed-operator-full-name='OPERATOR_FULL_NAME' \
  --seed-tracked-full-name='TRACKED_FULL_NAME' \
  --seed-operator-canonical-name='operator canonical name' \
  --seed-tracked-canonical-name='tracked canonical name' \
  --seed-operator-telegram-user-id=123456789 \
  --seed-operator-telegram-username='operator_username' \
  --seed-tracked-telegram-user-id=987654321 \
  --seed-tracked-telegram-username='tracked_username' \
  --seed-source-message-id=12345
```

Operator-only:

- `--seed-bootstrap-scope` requires runtime role including `ops`.
- Do not combine it with `--operator-schema-init`.
- If the report says `seeded_but_still_missing_prerequisite`, stop and fix the contract first.

## Bounded Pre-Run

Dry-run the bounded Stage5 repair/pre-run for the main chat and keep the audit artifact:

```bash
docker compose run --rm -e Runtime__Role=ops app \
  --stage5-scoped-repair \
  --stage5-scoped-repair-chat-id=885574984 \
  --stage5-scoped-repair-audit-dir=/app/logs/stage5-repair
```

The command writes a JSON audit under `/app/logs/stage5-repair` and logs the predicted impact.

Apply only with backup evidence and explicit operator metadata:

```bash
docker compose run --rm -e Runtime__Role=ops app \
  --stage5-scoped-repair \
  --stage5-scoped-repair-apply \
  --stage5-scoped-repair-chat-id=885574984 \
  --stage5-scoped-repair-audit-dir=/app/logs/stage5-repair \
  --risk-backup-id='BACKUP_ID' \
  --risk-backup-created-at-utc='2026-04-03T16:00:00Z' \
  --risk-backup-scope='stage5_scoped_repair:chat_id=885574984' \
  --risk-backup-artifact-uri='BACKUP_URI' \
  --risk-backup-checksum='BACKUP_CHECKSUM' \
  --risk-operator='OPERATOR_IDENTITY' \
  --risk-reason='bounded_seeded_pre_run' \
  --risk-audit-id='AUDIT_ID'
```

Operator-only:

- Apply path is fail-closed on backup guardrail or tail-reopen policy denial.
- Override flags exist in code, but this runbook does not treat override as baseline.
- If apply is denied, stop and inspect the dry-run audit JSON plus container logs before retrying.

## Post-Run Checks

Start the long-running app only after seed + bounded repair steps:

```bash
docker compose up -d app
```

Re-run readiness:

```bash
docker compose exec app dotnet TgAssistant.Host.dll --readiness-check
```

Review recent app logs:

```bash
docker compose logs --since=10m app
```
