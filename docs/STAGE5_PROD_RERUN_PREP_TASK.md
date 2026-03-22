# Stage 5 Prod Rerun Prep

Цель: подготовить безопасный полный повторный Stage 5 прогон в prod-среде.

Это не product sprint.
Это operational preparation pass перед реальным ingest-run.

## Scope

Нужно сделать только:

1. Проверить prod env/config именно для Stage 5 запуска.
2. Подготовить план полной очистки данных для повторного Stage 5 прогона.
3. Проверить, что после очистки Stage 5 сможет стартовать.
4. Дать предварительную смету расхода именно на Stage 5.

Без:

- product-level quality analysis
- Stage 6+ evaluation
- больших рефакторов
- silent destructive действий без явного подтверждения

## Что проверить

### 1. Stage 5 only config

Проверить и явно зафиксировать:

- `.env`
- `docker-compose.yml`
- `src/TgAssistant.Host/appsettings.json`
- binding в `Settings.cs`

Особенно:

- `ArchiveImport__Enabled`
- `ArchiveImport__SourcePath`
- `ArchiveImport__MediaProcessingEnabled`
- `ArchiveImport__RequireCostConfirmation`
- `ArchiveImport__ConfirmProcessing`
- `Analysis__Enabled`
- `Analysis__ArchiveOnlyMode`
- `Analysis__CheapModel`
- `Analysis__ExpensivePassEnabled`
- `Analysis__MaxExpensivePerBatch`
- `Analysis__SummaryEnabled`
- `Analysis__SummaryWorkerEnabled`
- `Analysis__EditDiffEnabled`
- `Embedding__Enabled`
- `VoiceParalinguistics__Enabled`
- `BudgetGuardrails__*`

Нужно отдельно ответить:

- стартует ли именно Stage 5 path
- не мешают ли ему current budget limits
- не включены ли лишние optional cost centers

### 2. Full cleanup plan

Подготовить clear destructive plan для полного rerun Stage 5.

Нужно разделить:

- что обязательно чистить
- что желательно сохранить
- что optional

Ожидаемо:

- DB data for prior ingest/results
- Redis queues/state
- optional media-derived cache if it мешает чистому rerun
- не удалять auth/session material без явной причины

Важно:

- сначала только план и dry validation
- destructive cleanup выполнять только после явного вывода, что именно будет удалено

### 3. Startup validation after prep

Проверить minimum startup path:

- build
- compose config
- app/container startup sanity
- `--list-smokes`
- `--runtime-wiring-check`
- `--stage5-smoke`

Если есть отдельный безопасный способ проверить Stage 5 readiness без полного запуска ingest, использовать его.

### 4. Preliminary cost estimate

Дать practical estimate именно для Stage 5 rerun:

- text analysis
- summaries
- embeddings
- edit-diff
- media vision
- audio/paralinguistics

Разбить минимум на:

- text-only conservative run
- text + embeddings/summaries
- full media/audio-enriched run

Отдельно указать:

- какие budget guardrails нужно поднять или временно изменить
- что лучше выключить на first rerun

## Expected output

Финальный отчет строго в формате:

1. Что проверено
2. Какие файлы изменены
3. Какой cleanup plan предлагается
4. Какой Stage 5 config должен быть для первого rerun
5. Предварительная оценка расхода
6. Что заблокировано или требует ручного подтверждения
7. Verdict: ready for destructive rerun / needs fixes first

