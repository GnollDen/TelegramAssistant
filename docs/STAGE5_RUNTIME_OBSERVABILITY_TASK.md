# Stage 5 Runtime Observability

Цель: во время текущего baseline run снять практический runtime-report:

- ошибки
- warnings
- suspicious patterns
- метрики прогресса
- cost / burn
- budget / cooldown / pause признаки

Это не product sprint.
Это operational monitoring / issue-finding pass.

## Нужно проверить

### 1. Logs

Просмотреть логи `tga-app` за разумный интервал и выделить:

- errors
- warnings
- repeated warnings
- repeated retries
- budget/quota/cooldown signs
- media missing / path errors
- schema validation failures
- Stage5 loop failures
- archive media failures
- voice/paralinguistics failures

Нужно отделять:

- harmless informational noise
- реальные risk signals

### 2. Runtime metrics

Нужно снять:

- `messages`
- `message_extractions`
- `chat_sessions`
- `pending_archive_media`
- `voice_done`
- extraction growth over time
- usage/cost by phase/model

### 3. Budget / guardrails

Проверить:

- `ops_budget_operational_states`
- признаки soft/hard/quota-block
- projections to limit if current burn continues

### 4. Data-quality red flags

Искать early signals:

- слишком много failed/pending_review media
- suspiciously low extraction growth
- repeated stage restarts
- repeated Stage5 cheap chunk failures
- repeated media missing paths
- paralinguistics retry loops

### 5. Practical operator verdict

Нужно ответить:

- прогон идет нормально
- есть проблемы, но можно продолжать и наблюдать
- нужно вмешательство

## Финальный отчет строго

1. Какие errors найдены
2. Какие warnings/risk signals найдены
3. Текущие метрики прогресса
4. Текущий cost/budget status
5. Что выглядит нормальным шумом
6. Что требует вмешательства сейчас
7. Verdict: continue / watch closely / intervene now

