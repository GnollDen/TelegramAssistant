# Pre-Sprint W0 Web Baseline Context Note

## Date

2026-03-25

## Purpose

Зафиксировать фактический code truth по web-слою на старте web track и явно отделить существующий код от planning intent.

## Authority

- `docs/planning/README.md`
- `docs/planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md`
- `docs/planning/WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md`
- `docs/planning/STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md`
- `docs/planning/PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`

## Current Web Code Truth (Implemented)

1. В коде есть отдельный web domain/read слой в `src/TgAssistant.Web/Read/`:
- `WebReadService`, `WebReviewService`, `WebOpsService`, `WebSearchService`
- `WebRouteRenderer`
- модели read/review/search/ops

2. Есть DI-регистрация web-сервисов и web verification-сервисов:
- `src/TgAssistant.Host/Startup/DomainRegistrationExtensions.cs`

3. Есть smoke/verification entrypoints через worker host CLI:
- `--web-smoke`
- `--web-review-smoke`
- `--ops-web-smoke`
- `--search-smoke`
- `--network-smoke`
- `--launch-smoke` (включает `web-read`, `web-review`, `web-search`, `ops-web` шаги)

4. Есть route/render слой с внутренними маршрутами (рендер HTML-строк), включая:
- `/dashboard`, `/search`, `/dossier`, `/inbox`
- `/case-detail`, `/artifact-detail`, `/case-action`, `/clarification-answer`, `/artifact-action`
- `/state`, `/timeline`, `/network`, `/profiles`, `/clarifications`, `/strategy`, `/drafts-reviews`, `/outcomes`
- `/review`, `/review-action`, `/review-edit-period`
- `/ops-budget`, `/ops-eval`, `/ops-ab-candidates`

## Current Web Code Truth (Not Implemented Yet)

1. Нет реального hosted web app слоя:
- нет ASP.NET Core web host / HTTP pipeline / `MapGet`/controllers
- `src/TgAssistant.Web/TgAssistant.Web.csproj` использует `Microsoft.NET.Sdk` (не `Microsoft.NET.Sdk.Web`)
- `src/TgAssistant.Host/TgAssistant.Host.csproj` использует `Microsoft.NET.Sdk.Worker`

2. Runtime role `web` сейчас не поднимает отдельные hosted services:
- `AddWebHostedServices(...) => services` (no-op)

3. Нет зафиксированного HTTP API контракта и браузерного shell/UI runtime.

4. Нет auth/session/access слоя для web-host сценария.

## Code Truth vs Planning Intent

- Code truth now: есть зрелый внутренний service+renderer+smoke substrate для web use-cases.
- Planning intent (W1+): превратить этот substrate в реальный hosted internal web app.
- Следствие: service-layer existence не равен hosted web readiness.

## Gap to Real Hosted Web App

До реального hosted web app отсутствуют обязательные элементы:
- web host runtime и HTTP entrypoints
- операторский shell/navigation как браузерный слой
- explicit transport contract (HTTP API or server-rendered routing contract)
- auth/session gate даже для single-operator internal режима
- browser-level smoke path

## W0 Boundary

Этот документ фиксирует baseline и gap only.
Он не открывает implementation W1+.
