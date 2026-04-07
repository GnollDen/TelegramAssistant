# Phase-B AI Conflict Session Proof Prereqs

- `owner`: `ops proof executor`
- `scope`: `PHB-010..PHB-012`
- `canonical_scope_key`: `chat:885574984`
- `seed_contract_source`: `docs/planning/BOUNDED_BASELINE_PROOF_CHAT_885574984_2026-04-03.md`
- `seed_runbook`: `docs/runbooks/bounded-seeded-pre-run-chat-885574984.md`
- `required_services`: `postgres`, `redis`, `llm_gateway`
- `required_env_vars`: `ConnectionStrings__Database`, `ConnectionStrings__Redis`, `LlmGateway__BaseUrl`, `LlmGateway__ApiKey`, `OpenAi__ApiKey`

## Prep Commands

1. `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --readiness-check`
2. If schema/bootstrap prerequisites are missing:
   - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --operator-schema-init`
   - rerun readiness check.
3. If canonical scope `chat:885574984` is missing/stale:
   - run exact `--seed-bootstrap-scope --seed-dry-run` command form from the runbook.
   - run exact `--seed-bootstrap-scope --seed-apply` command form from the runbook.
   - rerun readiness check.
4. If contradiction/review item is still missing for canonical scope:
   - run exact bounded `--stage5-scoped-repair` command form from the runbook.
   - apply form only if approved backup metadata is available.
   - rerun readiness check.
5. Final proof command:
   - `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --ai-conflict-session-v1-proof --ai-conflict-session-v1-proof-output=src/TgAssistant.Host/artifacts/resolution-interpretation-loop/ai-conflict-resolution-session-v1-proof.json`

## Verification Checklist

- `readiness_check_passed`: `yes`
- `schema_init_ran`: `yes/no`
- `seed_bootstrap_ran`: `yes/no`
- `stage5_scoped_repair_ran`: `yes/no`
- `canonical_scope_confirmed`: `yes`
- `proof_output_written`: `yes`

## Required Execution Record (must be filled before PHB-010 start)

- `executed_by`: `ralph-lite`
- `completed_at_utc`: `2026-04-07T14:43:00Z`
- `readiness_command`: `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --readiness-check`
- `readiness_result`: `pass`
- `proof_command`: `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --runtime-role=ops --ai-conflict-session-v1-proof --ai-conflict-session-v1-proof-output=src/TgAssistant.Host/artifacts/resolution-interpretation-loop/ai-conflict-resolution-session-v1-proof.json`
- `proof_result`: `pass`
- `seed_or_repair_commands`:
  - `<none>`
  - `<none>`
- `seed_or_repair_result`: `pass`
- `execution_record_status`: `pass`

## Status

- `completed_at_utc`: `2026-04-07T14:43:00Z`
- `status`: `pass`
- `notes`: `Readiness and canonical-scope conflict-session proof commands passed in this run with output written to /home/codex/projects/TelegramAssistant/src/TgAssistant.Host/artifacts/resolution-interpretation-loop/ai-conflict-resolution-session-v1-proof.json.`
