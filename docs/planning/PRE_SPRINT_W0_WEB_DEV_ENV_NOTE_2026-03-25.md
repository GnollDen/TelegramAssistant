# Pre-Sprint W0 Web Dev Environment Note

## Date

2026-03-25

## Purpose

Зафиксировать повторяемый локальный runtime baseline для web track до старта Sprint W1.

## Local Runtime Entry Assumption

До W1 web-track запускается через существующий worker host (`src/TgAssistant.Host`) и web smoke/verification entrypoints, а не через браузерный web host.

## Runtime Role Baseline

Разрешенные role-комбинации включают:
- `web`
- `web,ops`

Рекомендация для baseline web development:
- использовать `--runtime-role=web` для чистых web read/review/search smoke paths
- использовать `--runtime-role=web,ops` только когда нужен ops/web workflow

## Required Dependencies (Current Code)

Независимо от `web` роли startup требует:
- валидный `Database:ConnectionString` (не placeholder)
- валидный `Redis:ConnectionString`

Также до smoke-paths всегда выполняются:
- `DatabaseInitializer.InitializeAsync()`
- `RedisMessageQueue.InitializeAsync()` (создает Redis stream/group при необходимости)

## Required Config Surface

Минимально обязательное для web role:
- `Runtime:Role` или `--runtime-role=web`
- `Database:ConnectionString`
- `Redis:ConnectionString`
- Redis stream/group settings (`Redis:StreamName`, `Redis:ConsumerGroup`)

Дополнительно для `web,ops`:
- `Telegram:BotToken`
- `BotChat:OwnerId` или `Telegram:OwnerUserId`

## Docker / Network Assumptions

По текущему compose:
- app container работает с `network_mode: "host"`
- для app используются `127.0.0.1:5432` и `127.0.0.1:6379`
- MCP container отдельный, для PostgreSQL использует service-name `postgres`

Для локального web track это значит:
- PostgreSQL и Redis должны быть доступны в expected адресах runtime
- web verification paths опираются на те же DB/Redis runtime assumptions, что и Stage6 worker

## Known Local Blockers / Risks

1. Placeholder DB/secret values блокируют startup через `RuntimeStartupGuard`.
2. Web smoke не равен browser-host verification (до W1 это ожидаемо).
3. Из последнего launch review:
- `network-smoke` оставался red
- `stage5-smoke`/`readiness-check` имели prompt-version drift risk

Эти пункты нужно учитывать как baseline-risks, не подменяя ими W1 scope.

## Local Start Contract for W1 Entry

W1 стартует с assumption, что:
- web service/renderer substrate доступен
- DB/Redis локально доступны
- runtime role/config contract явный
- скрытых требований к env для web track не осталось
