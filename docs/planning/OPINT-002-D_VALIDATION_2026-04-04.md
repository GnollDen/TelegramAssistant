# OPINT-002-D Validation 2026-04-04

## Scope analyzed

- Seeded bounded operator scope: `chat:885574984`
- Tracked person: `80c384f3-a79f-4356-b832-45e636353f59` (`Гайнутдинова Алёна Амировна`)
- Contract surface:
  - `/api/operator/tracked-persons/query`
  - `/api/operator/tracked-persons/select`
  - `/api/operator/resolution/queue/query`
  - `/api/operator/resolution/detail/query`
  - `/api/operator/resolution/actions`

## Evidence and findings

- Initial blocker confirmed: migration `0052_operator_resolution_actions.sql` had not been applied in the active local Postgres instance, so `operator_resolution_actions` and `operator_audit_events` were missing before validation.
- Minimal mitigation applied: start the ops-only host with `--operator-schema-init` and run a bounded validator script that seeds one temporary `runtime_defect`-backed resolution item inside `chat:885574984`, exercises read/write contracts, verifies persistence, and deletes its own rows afterward.
- Validation artifact added: [scripts/opint-002-resolution-contract-validate.sh](/home/codex/projects/TelegramAssistant/scripts/opint-002-resolution-contract-validate.sh)
- Latest concrete run report: [logs/opint-002-d-validation-report.json](/home/codex/projects/TelegramAssistant/logs/opint-002-d-validation-report.json)

## Validated

- Normal read path:
  - tracked-person query `200`
  - tracked-person selection `200`
  - resolution queue query `200`
  - resolution detail query `200`
- Normal write path:
  - `approve` action `200`
  - one `operator_resolution_actions` row persisted
  - one accepted `operator_audit_events` row persisted
- Failure path:
  - `reject` without explanation returns `400`
  - failure reason `explanation_required`
  - zero `operator_resolution_actions` rows persisted
  - one denied `operator_audit_events` row persisted
- Auth-denied path:
  - queue query with expired session returns `401`
  - failure reason `session_expired`
  - action with mismatched active tracked person returns `403`
  - failure reason `session_active_tracked_person_mismatch`
  - zero `operator_resolution_actions` rows persisted
  - one denied `operator_audit_events` row persisted
- Session audit edge:
  - tracked-person switch persisted one session audit row during the run
- Cleanup verification:
  - temporary `runtime_defect`, `operator_resolution_actions`, and `operator_audit_events` rows were removed after the run

## Residual risk

- This gate covers bounded live backend contracts on one seeded scope only; it does not validate OPINT-003 recompute behavior, Telegram/UI rendering, or multi-item queue interactions.
- Startup still depends on valid runtime gateway configuration even for ops-only validation runs; the script works around that with a non-placeholder local OpenRouter key override so the host can boot without invoking LLM traffic.
