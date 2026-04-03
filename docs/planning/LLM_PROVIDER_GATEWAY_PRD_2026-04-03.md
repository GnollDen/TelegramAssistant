# PRD: LLM Provider Gateway (Hybrid Codex LB + OpenRouter)

## Date

2026-04-03

## Status

Draft v1

## Purpose

Документ фиксирует продуктовые и архитектурные требования к модулю расширяемого провайдинга LLM в TelegramAssistant.

Целевой результат v1:
- текстовые LLM-запросы (`chat/text/tools`) идут через `codex-lb`;
- `embeddings` и `audio` остаются на OpenRouter;
- бизнес-логика Stage5/Stage6 не зависит от конкретного внешнего провайдера.

## Related Documents

- [LLM_PROVIDER_EXTENSION_OPTION_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/LLM_PROVIDER_EXTENSION_OPTION_2026-04-03.md)
- [PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md](/home/codex/projects/TelegramAssistant/docs/planning/PERSON_INTELLIGENCE_SYSTEM_PRD_2026-04-02.md)

## 1. Product Scope

### 1.1 Product Definition

`LLM Provider Gateway` это внутренний системный слой, который:
- принимает унифицированный LLM-запрос;
- выбирает провайдера по policy и конфигу;
- нормализует ответ в единый внутренний формат;
- логирует usage/latency/fallback события.

### 1.2 Core Goal

Убрать прямую привязку Stage5/Stage6 к OpenRouter transport-контракту и обеспечить конфигурируемый гибридный routing по modality.

### 1.3 v1 Goals

- Ввести единый интерфейс вызова LLM внутри приложения.
- Реализовать provider routing:
  - `text/chat/tools -> codex-lb`
  - `embeddings -> openrouter`
  - `audio -> openrouter`
- Включить конфигурируемый fallback для text-path при недоступности `codex-lb`.
- Сохранить текущие бюджетные и usage guardrails.

### 1.4 v1 Non-Goals

- Полная унификация всех vendor-specific параметров.
- Замена всех текущих сервисов за один релиз без feature flags.
- Поддержка внешнего публичного API для third-party клиентов.
- Миграция хранилищ данных или схем БД.

## 2. Current State and Constraints

### 2.1 Verified Runtime Facts (as of 2026-04-03)

- `codex-lb` локально отвечает на `POST http://127.0.0.1:2455/v1/chat/completions`.
- `codex-lb` не подтвержден для `embeddings` в текущем runtime (ожидается `openrouter`).
- текущий код Stage5/Media использует OpenRouter-style paths:
  - `/api/v1/chat/completions`
  - `/api/v1/embeddings`

### 2.2 Main Technical Constraint

Нельзя просто заменить `BaseUrl` глобально: это ломает части, завязанные на OpenRouter-специфичные маршруты и/или modality.

## 3. Functional Requirements

### 3.1 Unified Gateway Contract

- FR-1: Система должна иметь единый вход `ILlmGateway` для text/chat/tools/embeddings/audio.
- FR-2: Запрос должен содержать минимум:
  - `modality`
  - `task`
  - `model_hint` (optional)
  - payload (`messages` или `input`)
  - token/timeout limits
  - trace context.
- FR-3: Ответ должен возвращать:
  - `content/tool_calls/embeddings` (по modality)
  - `usage`
  - `provider`
  - `latency_ms`
  - `request_id`.

### 3.2 Routing and Provider Selection

- FR-4: Routing policy должна поддерживать выбор провайдера по modality.
- FR-5: Default policy v1:
  - `chat/text/tools -> codex-lb`
  - `embeddings/audio -> openrouter`
- FR-6: Policy должна быть изменяемой через конфиг без изменения кода.

### 3.3 Fallback Behavior

- FR-7: Для text-path должен существовать опциональный fallback `codex-lb -> openrouter` по retryable ошибкам.
- FR-8: Для embeddings/audio fallback в `codex-lb` в v1 не обязателен.
- FR-9: Fallback должен быть прозрачно отражен в telemetry.

### 3.4 Compatibility with Existing Services

- FR-10: Stage5 analysis path должен работать через gateway без изменения бизнес-результата протокола на уровне domain output.
- FR-11: Embedding и audio сервисы должны перейти на gateway-интерфейс, сохранив текущий провайдер (OpenRouter).
- FR-12: Бюджетные блокировки и quota-сигналы должны сохраняться.

## 4. Non-Functional Requirements

- NFR-1: p95 latency gateway overhead не более 30 ms поверх текущего провайдера.
- NFR-2: Ошибки провайдеров должны возвращаться в нормализованном типе, пригодном для retry-классификации.
- NFR-3: Gateway должен поддерживать cancellation token без деградации.
- NFR-4: Все ключевые действия gateway должны быть покрыты structured logs и метриками.
- NFR-5: Конфигурация секретов должна храниться только через существующую схему env/appsettings.

## 5. Architecture Requirements

### 5.1 Core Components

- `ILlmGateway`
- `LlmGatewayService`
- `ILlmRoutingPolicy`
- `ILlmProviderClient` (провайдер-адаптер)
- `CodexLbChatProviderClient`
- `OpenRouterProviderClient`
- request/response normalizers

### 5.2 Internal Endpoint Model

Внутри системы должен существовать единый логический endpoint вызова LLM (сервисный контракт, не внешний public API), который:
- принимает унифицированный контракт;
- применяет routing policy;
- вызывает конкретный provider client;
- возвращает унифицированный ответ.

### 5.3 Config Model

Требуется отдельная конфиг-секция `LlmGateway`:
- `enabled`
- routing map per modality
- provider settings (base url, api key, timeout, endpoints)
- fallback policy
- feature flags per modality.

## 6. Observability and Governance

### 6.1 Metrics

Минимальный набор:
- `llm_gateway_requests_total{provider,modality,status}`
- `llm_gateway_latency_ms{provider,modality}`
- `llm_gateway_fallback_total{from,to,reason}`
- `llm_gateway_failures_total{provider,error_type}`

### 6.2 Logging

Обязательные поля:
- `provider`
- `modality`
- `model`
- `request_id`
- `status_code`
- `latency_ms`
- `fallback_applied`

### 6.3 Budget Integration

Budget guardrails должны вызываться до outbound запроса и после ответа (для quota-like событий), как и в текущем pipeline.

## 7. Security Requirements

- SR-1: Ключи `codex-lb` и OpenRouter хранятся отдельно.
- SR-2: Логи не должны содержать raw secrets.
- SR-3: Внутренний endpoint не должен публиковаться наружу.
- SR-4: Должна поддерживаться безопасная ротация ключей через env/config reload policy.

## 8. Delivery Plan

### 8.1 Milestone A: Gateway Skeleton

- контракты и DTO;
- settings + DI;
- routing policy;
- no-op integration test.

### 8.2 Milestone B: Text Path Migration

- подключение Stage5 text/chat через gateway;
- `codex-lb` как primary text provider;
- optional fallback в OpenRouter.

### 8.3 Milestone C: Embeddings/Audio through Gateway

- migration оберток embeddings/audio на gateway contract;
- фактический provider остается OpenRouter.

### 8.4 Milestone D: Stabilization

- метрики/дашборды;
- error taxonomy;
- нагрузочная и отказоустойчивая проверка.

## 9. Acceptance Criteria (DoD)

- AC-1: Text/chat запросы Stage5 проходят через `codex-lb` в runtime при включенном флаге.
- AC-2: Embeddings и audio стабильно обслуживаются OpenRouter через gateway contract.
- AC-3: Переключение провайдера по modality выполняется конфигом без правок бизнес-кода.
- AC-4: Наличие telemetry по provider routing и fallback подтверждено.
- AC-5: При падении `codex-lb` система остается работоспособной в policy-режиме fallback (если флаг включен).

## 10. Testing Strategy

- Unit:
  - routing policy matrix
  - fallback classifier
  - request/response normalization
- Integration:
  - mock `codex-lb` chat success/failure
  - OpenRouter embeddings/audio success
- Runtime sanity:
  - startup config validation
  - stage5 smoke path with gateway enabled
- Regression:
  - сравнение domain output до/после migration для фиксированного sample набора сообщений.

## 11. Open Questions

- Нужен ли отдельный policy для tool-calling path vs обычный text path?
- Какая стратегия при частичной деградации `codex-lb` (rate-limit bursts): мгновенный fallback или bounded retries?
- Нужен ли sticky routing по chat/session для консистентности качества?
- Нужно ли в v1 сохранять provider-specific raw payload для диагностики, и где хранить retention?

