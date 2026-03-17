# STAGE5_EVOLUTION_MANIFESTO.md

> Historical manifesto (planning artifact), not runtime source-of-truth.
> Current runtime documentation: `README.md` and `docs/stage5-extraction-algorithm.txt`.

## 🎯 Общая цель
Завершить архитектурную трансформацию Stage 5 до версии **v10 Hybrid**. Система должна стать максимально экономной (исключение мусорных запросов), точной (строгое соблюдение v10-схемы) и интеллектуальной (гибридный режим обработки сессий: Hot/Cold paths).

---

## 🛠 Протокол работы агентов (Chain of Thought)

При выполнении каждой задачи Кодекс обязан разделять контекст на три роли:

1.  **Архитектор (Architect):** Анализирует зависимости и проектирует изменения. Создает краткий план (design-doc) перед правкой кода. Гарантирует, что новые механизмы (Idle-timeout) не конфликтуют с существующей логикой БД.
2.  **Бэкенд (C# Developer):** Реализует патчи. Принцип: **Zero-Waste Coding**. Минимум новых зависимостей, переиспользование инфраструктуры Stage 5. Весь код должен соответствовать стандартам Clean Code.
3.  **Ревьюер (QA/Reviewer):** Проводит финальную проверку. Критерии: отсутствие латиницы в саммари, корректный маппинг `TrustFactor`, отсутствие активных "зомби-воркеров" в DI.

---

## 📋 Беклог задач (Priority Order)

### Phase A: Зачистка и Стабилизация (Срочно)
- **[A1] Удаление Зомби-кода:**
    - Полностью удалить `ChatSessionSlicerWorkerService.cs`.
    - Убрать регистрацию `AddHostedService<ChatSessionSlicerWorkerService>` в `Program.cs`.
    - Вычистить из репозиториев неиспользуемые методы, специфичные только для старого слайсера.
- **[A2] Рефакторинг Рефинера:**
    - Упростить `ExtractionRefiner.cs` до "пассивного нормализатора".
    - Оставить только: маппинг полей v10 (`TrustFactor = confidence ?? 0`), приведение типов и базовую чистку строк.
    - **Удалить** любую логику "умного" объединения сущностей или эвристического исправления данных за LLM.
- **[A3] Изоляция Stage 6:**
    - Разорвать DI-связи для `ContinuousRefinementWorkerService`. Код пометить как `[Deprecated]`.

### Phase B: Гибридная Суммаризация (Hot Path)
- **[B1] Idle-Timeout (Охлаждение):**
    - В `DialogSummaryWorkerService` добавить проверку "остывания" диалога.
    - Если с момента последнего сообщения в сессии прошло менее **15 минут** — пропускать суммаризацию (ждем накопления контекста).
- **[B2] Hot Summary:**
    - Реализовать генерацию "чернового" саммари только для сессий, прошедших 15-минутный порог тишины.
- **[B3] Trash Filter:**
    - В `MessageContentBuilder` добавить жесткую блокировку: не отправлять в LLM сессии, состоящие только из техшума/пустых команд, даже если они помечены как `Processed`.

### Phase C: Ночная Агрегация (Cold Path / Cron)
- **[C1] Cron Infrastructure:**
    - Настроить планировщик (Cron) для запуска задач обслуживания Stage 5.
- **[C2] Daily Aggregator (Ночной Крон):**
    - В 03:00 (локальное время) проходить по всем "коротким" сессиям за прошедшие сутки и объединять их в крупные смысловые эпизоды (агрегация по дням/темам).
- **[C3] Final Summary:**
    - Генерация финального, глубокого саммари для агрегированных сессий. Установка флага `IsFinalized = true` для исключения дальнейших правок.

---

## 📈 Схема данных v10 (Reference)

- **Extractions:** Всегда использовать `trust_factor` и `needs_clarification`. Fallback для старых данных: `confidence`.
- **Language:** Строго **Russian** для всех выходных текстовых полей (Summary, Claims, Facts).
- **Session queue:** `chat_sessions.is_analyzed=false` + idle gate.
- **Session checkpoints:** `stage5:session_chunk_checkpoint:{chatId}:{sessionIndex}`.
- **Summary checkpoints:** `stage5:summary:session:{chatId}:{sessionIndex}`.
- **Legacy summary watermarks:** `stage5:summary_watermark` и `stage5:summary_extraction_watermark` не используются в активном runtime-flow.

---

## 🚀 Инструкция по запуску для Кодекса

1.  Используй этот манифест как исторический план и контекст, но не как runtime source-of-truth.
2.  Создай/обнови файл `docs/backlog/stage5_hybrid_evolution.md` на основе этого списка.
3.  **Приступи к выполнению Phase A**, действуя по протоколу **Архитектор -> Бэк -> Ревью**.
4.  После завершения зачистки (Phase A) представь детальный план реализации Гибридного режима (Phase B).
