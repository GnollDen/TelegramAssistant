# LLM Provider Extension Option (2026-04-03)

## Status

Archive-only (superseded by [LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/LLM_PROVIDER_GATEWAY_PRD_2026-04-03.md)).

## Authority Note

This note is retained only as pre-PRD option history. Active gateway requirements and positioning now live in the gateway PRD.

## Контекст

Текущее состояние:
- основной LLM-клиент в проекте ориентирован на OpenRouter-совместимые маршруты (`/api/v1/chat/completions`, `/api/v1/embeddings`);
- `codex-lb` на локальном хосте (`127.0.0.1:2455`) подтвержденно принимает `POST /v1/chat/completions`;
- `embeddings` через `codex-lb` в текущем состоянии не готовы к использованию (метод недоступен).

Требуемый вариант развития:
- `text/chat` запросы вести через `codex-lb` (модели OpenAI);
- `embeddings` и `audio` оставить на OpenRouter;
- реализовать это не точечными if/else, а как расширяемый модуль провайдинга LLM с собственным endpoint-входом в системе и отдельным конфигом.

## Цель

Сформировать внутри TelegramAssistant единый слой маршрутизации LLM-запросов, где:
- доменная логика не знает о конкретном вендоре API;
- выбор провайдера задается политикой (по modality/use-case/model);
- добавление нового провайдера не требует переписывать Stage5/Stage6 сервисы.

## Предлагаемая архитектура

### 1. Внутренний модуль провайдинга

Добавить внутренний модуль (рабочие имена):
- `ILlmGateway` (единая точка вызова для text/chat/tools/embeddings/audio);
- `ILlmProviderClient` (адаптер конкретного провайдера);
- `ILlmRoutingPolicy` (правила выбора провайдера);
- `ILlmRequestNormalizer` / `ILlmResponseNormalizer` (приведение DTO к внутреннему контракту).

### 2. Внутренний endpoint системы

Ввести внутренний endpoint-контракт внутри приложения (не публичный интернет API), например:
- logical endpoint: `llm://gateway/complete`
- реализация на уровне сервиса/шины вызовов (или HTTP-loopback, если нужно изоляция).

Задача endpoint:
- принимать унифицированный запрос (`modality`, `task`, `model_hint`, `messages/input`, `limits`, `trace tags`);
- передавать его в routing policy;
- возвращать унифицированный ответ и usage-метрики.

### 3. Адаптеры провайдеров (первый этап)

- `CodexLbChatProvider`
  - отвечает только за `chat/text` (OpenAI-like `/v1/chat/completions`);
  - используется для текстовых этапов.
- `OpenRouterProvider`
  - отвечает за `embeddings` и `audio`;
  - может также оставаться fallback для chat на случай деградации codex-lb.

## Routing policy (первый вариант)

Базовые правила:
- `modality = text|chat|tools` -> `codex-lb`
- `modality = embeddings` -> `openrouter`
- `modality = audio|vision_audio` -> `openrouter`

Fallback:
- если `codex-lb` недоступен/возвращает retryable ошибку:
  - fallback в OpenRouter для non-critical text path (опционально, по конфигу);
  - для critical path может быть `fail-fast` (по конфигу).

## Конфигурация (предложение)

Добавить отдельную секцию, например `LlmGateway`:

```json
{
  "LlmGateway": {
    "Enabled": true,
    "DefaultProvider": "openrouter",
    "Routing": {
      "TextProvider": "codex-lb",
      "EmbeddingsProvider": "openrouter",
      "AudioProvider": "openrouter"
    },
    "Providers": {
      "codex-lb": {
        "BaseUrl": "http://127.0.0.1:2455",
        "ChatCompletionsPath": "/v1/chat/completions",
        "ApiKey": "__REQUIRED__",
        "TimeoutSeconds": 120
      },
      "openrouter": {
        "BaseUrl": "https://openrouter.ai/api/v1",
        "ChatCompletionsPath": "/api/v1/chat/completions",
        "EmbeddingsPath": "/api/v1/embeddings",
        "ApiKey": "__REQUIRED__",
        "TimeoutSeconds": 120
      }
    },
    "Fallback": {
      "EnableTextFallbackToOpenRouter": true,
      "RetryableStatusCodes": [408, 429, 500, 502, 503, 504]
    }
  }
}
```

Для `.env`:
- `LLM_GATEWAY_ENABLED=true`
- `LLM_TEXT_PROVIDER=codex-lb`
- `LLM_EMBEDDINGS_PROVIDER=openrouter`
- `LLM_AUDIO_PROVIDER=openrouter`
- `CODEX_LB_BASE_URL=http://127.0.0.1:2455`
- `CODEX_LB_API_KEY=...`

## Интеграция в текущие сервисы

Перевод на модульный слой:
- `OpenRouterAnalysisService` -> использовать `ILlmGateway` для text/chat;
- `OpenRouterEmbeddingService` -> через `ILlmGateway` с modality `embeddings` (фактически маршрутизируется в OpenRouter);
- `OpenRouterVoiceParalinguisticsAnalyzer` и media processor -> через `ILlmGateway` с modality `audio`.

На первом шаге допускается адаптерная совместимость:
- оставить текущие классы и заменить только транспорт/вызов внутри них на gateway.

## Этапы реализации

1. Ввести контракты `ILlmGateway`, `LlmRequest`, `LlmResponse`, `LlmUsage`.
2. Реализовать `RoutingPolicy` + конфиг `LlmGatewaySettings`.
3. Реализовать `CodexLbChatProvider` (только chat/text).
4. Подключить Stage5 text paths к gateway.
5. Переключить embeddings/audio на gateway без изменения фактического провайдера.
6. Добавить метрики:
   - `llm_gateway_requests_total{provider,modality,status}`
   - `llm_gateway_latency_ms{provider,modality}`
   - `llm_gateway_fallback_total{from,to,reason}`
7. Включить rollout через feature flag per modality.

## Риски и ограничения

- Различия DTO/response schema между провайдерами (особенно tools/function-call).
- Разные модели токенизации и usage полей.
- Потеря части OpenRouter-специфичных опций при унификации (provider order/fallback hints).
- Нужна четкая политика таймаутов и retry, чтобы не создать двойные ретраи.

## Критерии готовности (DoD)

- text/chat проходит через `codex-lb` в продовом рантайме;
- embeddings/audio стабильно идут через OpenRouter;
- переключение провайдеров возможно через конфиг без изменений кода бизнес-логики;
- есть метрики маршрутизации и fallback;
- при падении `codex-lb` система не блокирует весь pipeline.
