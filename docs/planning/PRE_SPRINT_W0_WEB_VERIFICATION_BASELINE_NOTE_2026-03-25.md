# Pre-Sprint W0 Web Verification Baseline Note

## Date

2026-03-25

## Purpose

Зафиксировать обязательный verification baseline для web track: что автоматизируется, что проверяется вручную, и какие launch criteria применяются.

## Automated Baseline (Current)

Для web track обязательны:
1. `dotnet build TelegramAssistant.sln` для любого изменения C# в web track.
2. Web smoke entrypoints (worker-host based):
- `--web-smoke`
- `--web-review-smoke`
- `--ops-web-smoke`
- `--search-smoke`
3. Runtime probe baseline:
- `--liveness-check`
- `--runtime-wiring-check`

Условно-обязательные (track risk indicators):
- `--readiness-check` (важно для runtime truth)
- `--network-smoke` (интеграционный индикатор, сейчас исторически нестабилен)

## Manual Baseline (Current)

До W1 (до появления hosted web app):
1. Manual review planning/code truth consistency для web scope.
2. Проверка, что web scope не смешан с backend broad sprint.

Начиная с W1:
1. cold start hosted web runtime
2. shell load and navigation sanity
3. queue/detail/action loop in browser
4. clarification answer from browser path

## Evidence Capture Contract

Для каждого web sprint gate фиксировать:
1. Список выполненных команд проверки.
2. Pass/fail по каждому smoke/probe.
3. Явный список известных red checks.
4. Краткое решение: ready / not ready + почему.

## Launch Criteria: Web MVP (after W2)

Web MVP launch допускается, если одновременно:
1. W1/W2 scope выполнен полностью.
2. Обязательные automated checks green.
3. Manual browser walkthrough проходит базовые operator flows.
4. Нет критичных ambiguity по env/runtime prerequisites.

## Launch Criteria: Normal Working Web (after W4)

Normal working web допускается, если:
1. W3/W4 scope выполнен.
2. Deep-review/clarification manual scenarios устойчиво проходят.
3. Web-specific smoke/manual baseline стабилен в repeated runs.
4. Оставшиеся риски классифицированы как non-blocking для internal daily use.

## Explicit Non-Goal of Verification Baseline

Baseline не требует на этом этапе:
- публичного product-grade QA matrix
- cross-browser/device certification for external release
- полной автоматизации UI regression suite до стабилизации W1-W4 surfaces
