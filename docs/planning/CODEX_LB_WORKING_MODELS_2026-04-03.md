# Codex LB Working Models Snapshot

## Date

2026-04-03

## Status

Reference-only runtime snapshot for gateway operations.

## Source

- Runtime probe endpoint: `http://127.0.0.1:2455/v1/models`
- Machine-readable artifact: [codex_lb_working_models_2026-04-03.json](/home/codex/projects/TelegramAssistant/artifacts/llm-gateway/codex_lb_working_models_2026-04-03.json)

## Confirmed Working Models

- `gpt-5.1`
- `gpt-5.4`
- `gpt-5.4-mini`
- `gpt-5.3-codex`
- `gpt-5.2-codex`
- `gpt-5.2`
- `gpt-5.1-codex-max`
- `gpt-5.1-codex`
- `gpt-5-codex`
- `gpt-5`
- `gpt-5.1-codex-mini`
- `gpt-5-codex-mini`

## Enforced Reasoning Levels

- `minimal`
- `low`
- `medium`
- `high`
- `xhigh`

## Note

This snapshot records models reported as available by `codex-lb` at probe time.
Per-model supported/default reasoning levels are stored in the JSON artifact.
It does not replace rollout-gate requirements for parity/fallback evidence.
