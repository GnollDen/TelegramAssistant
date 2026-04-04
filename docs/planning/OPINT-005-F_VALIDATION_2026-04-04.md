# OPINT-005-F Validation 2026-04-04

## Scope analyzed

- Bounded web/operator scope: `chat:885574984`
- Validation surface:
  - `/operator/resolution` route + bootstrap contract checks
  - tracked-person selection + queue/detail/evidence read path
  - bounded web actions: `clarify` / `approve` / `reject` / `defer`
  - denied-action path and degraded recompute lifecycle evidence

## Key findings and evidence

- Web P0 resolution loop passed with deterministic queue/detail/evidence/action progression and clean operator contracts.
  - artifact: [logs/opint-005-f-web-validation-report.json](/home/codex/projects/TelegramAssistant/logs/opint-005-f-web-validation-report.json)
  - route checks confirm `/operator/resolution` references clean `/api/operator/*` contracts and includes explicit no-legacy Stage6 queue/case note
  - queue/detail/evidence checks passed (`200`) with bounded tracked-person context and seeded scope items
  - action checks passed:
    - `reject` without explanation denied (`400`, `explanation_required`)
    - `approve`, `defer`, `clarify` accepted (`200`) with recompute lifecycle payloads (`running`)
    - denied path captured deterministically (`403`, `session_scope_item_mismatch`)
- Degraded recompute lifecycle path passed and is recorded via:
  - [logs/opint-005-f-recompute-degraded-report.json](/home/codex/projects/TelegramAssistant/logs/opint-005-f-recompute-degraded-report.json)
  - degraded scenario `degraded_clarification_blocked` shows replay lifecycle `clarification_blocked` and `need_operator_clarification`

## Commands run

```bash
scripts/opint-005-web-resolution-validate.sh
dotnet build TelegramAssistant.sln
```

## Residual risk

- Web validation is bounded to seeded scope behavior and contract-level web/API execution; it does not replace broader multi-operator traffic/soak validation.
- EF warnings about `Distinct` + row limiting query ordering remain visible during tracked-person query and should be handled separately for deterministic ordering hygiene.
