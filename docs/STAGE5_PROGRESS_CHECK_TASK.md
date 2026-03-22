# Stage 5 Progress Check

Цель: снять первый осмысленный status report по текущему Stage 5 rerun в prod.

Это не product sprint.
Это operational monitoring pass.

## Нужно ответить на 4 вопроса

1. Какая текущая динамика прогресса?
2. Какой сейчас burn rate по cost?
3. Какой примерный ETA до завершения baseline rerun?
4. Какую долю в нагрузке/стоимости дают media-related paths?

## Scope

Проверять только текущий runtime и persisted operational data.

Без:

- изменения reasoning logic
- больших конфиг-правок
- destructive cleanup
- product-quality анализа

## Что проверить

### 1. Progress

Нужно оценить:

- сколько archive messages уже прошли analysis
- сколько ещё осталось
- есть ли backlog по sessions / chunks / pending processing
- есть ли stalled indicators

Использовать:

- app logs
- `messages`
- `chat_sessions`
- `message_extractions`
- `analysis_state`
- `stage5 metrics`

Если возможно, дать:

- processed count
- remaining count
- throughput за последний интервал

### 2. Cost / burn

Нужно оценить:

- cost so far for current rerun window
- tokens so far
- burn rate per hour
- top expensive phases/models

Использовать:

- `analysis_usage_events`
- app logs if helpful

Разбить минимум по:

- cheap analysis
- summary
- embeddings
- vision
- audio / paralinguistics
- other notable paths if they exist

### 3. ETA

Нужно дать practical ETA:

- optimistic
- likely
- if current throughput degrades

Не нужна математическая идеальность.
Нужна operator-useful оценка.

### 4. Media share

Нужно понять:

- сколько media messages уже встретилось
- что реально сейчас включено/выключено
- идет ли media processing в этом rerun
- если media path выключен, явно так и написать
- если media path активен, оценить cost/load contribution

### 5. Guardrails / risk state

Проверить:

- появились ли новые budget operational states
- близко ли text budget к soft/hard limit
- есть ли quota/cooldown/block signs

## Expected output

Финальный отчет строго в формате:

1. Текущий прогресс
2. Текущий расход и burn rate
3. Примерный ETA
4. Что происходит с media/audio paths
5. Есть ли budget/blocker риски
6. Verdict: stable progress / watch closely / intervention needed

