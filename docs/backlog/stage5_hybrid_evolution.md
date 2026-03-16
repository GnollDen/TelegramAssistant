# Stage 5 Hybrid Evolution (v10)

Источник: `STAGE5_EVOLUTION_MANIFESTO.md`

## Цель
Довести Stage 5 до v10 Hybrid: экономия токенов, строгая v10-нормализация экстракций, гибридный контур Hot/Cold.

## Протокол выполнения
1. Архитектор: проверка зависимостей и рисков перед изменениями.
2. Бэкенд: минимальные и точечные патчи без лишних зависимостей.
3. Ревью: проверка trust-factor mapping, DI, сборки и регрессий.

## Backlog

### Phase A: Зачистка и стабилизация (срочно)
- [x] A1. Удаление зомби-кода слайсера
  - [x] `ChatSessionSlicerWorkerService` отсутствует в `src/`.
  - [x] Регистрация `AddHostedService<ChatSessionSlicerWorkerService>` отсутствует в `Program.cs`.
  - [x] Явных slicer-watermark ссылок в runtime-коде нет.
- [x] A2. Рефакторинг `ExtractionRefiner` в пассивный нормализатор
  - [x] Удалены эвристики подстановки generic entity токенов (`self/sender/я/...`).
  - [x] Оставлены нормализация строк, базовая фильтрация полей, приведение типов.
  - [x] Сохранен v10 mapping `trust_factor` с fallback к `confidence`.
- [x] A3. Изоляция Stage 6
  - [x] `ContinuousRefinementWorkerService` не зарегистрирован как hosted service.
  - [x] Добавлена явная пометка `[Obsolete]` как deprecated.

### Phase B: Гибридная суммаризация (Hot Path)
- [x] B1. Idle-timeout 15 минут в `DialogSummaryWorkerService`.
- [x] B2. Hot summary только для «остывших» сессий.
- [x] B3. Trash filter в `MessageContentBuilder` для чистого техшума/пустых команд.
- [x] Контроль языка: warning, если summary для вероятно русскоязычной сессии не содержит кириллицы.

### Phase C: Ночная агрегация (Cold Path / Cron)
- [x] C1. Cron-инфраструктура задач Stage 5.
- [x] C2. Daily Aggregator на 03:00 local time.
- [x] C3. Final summary + `IsFinalized = true`.

## Инварианты v10
- Единый мастер-watermark: `stage5:summary_watermark`.
- Поля `trust_factor` и `needs_clarification` обязательны в экстракциях/нормализации.
- Все текстовые выходы Stage 5 в production-контуре: на русском языке.

## План после Phase A
1. Внедрить B1 (idle-timeout) с защитой от ранней суммаризации.
2. Внедрить B3 как precheck до LLM-вызова.
3. Внедрить B2 и привязать к обновлению watermark без лишних вызовов.
4. После Hot path подготовить C1-C3 отдельным инкрементом.
