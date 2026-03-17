# Stage 5 Final Sprint — Codex Backlog

> Archive note (2026-03-17): this document is a historical sprint artifact.
> Current runtime source-of-truth is `README.md` + `docs/stage5-extraction-algorithm.txt`.

## Контекст

Stage 5 extraction pipeline работает. 5 итераций промпта на 300 сообщениях дали:
- 37 фактов, 8 связей, 6 entities из 212 обработанных сообщений
- Claims → Facts проекция работает
- Decay policy работает (instant/fast/slow/permanent)
- Relationship filtering работает (только Person/Org/Pet)
- Русский язык в значениях фактов — в основном работает

Оставшиеся задачи для полного завершения Stage 5.

---

## S5-1. Починить streaming JSON parser

**Файл:** `src/TgAssistant.Processing/Archive/ArchiveImportWorkerService.cs`, метод `ParseArchiveMessagesAsync`

**Баг:** `JsonReaderException: The input does not contain any JSON tokens` на любом валидном JSON файле.
Ошибка возникает на разных строках в зависимости от размера файла (2262, 2473, 2913).
Файлы проходят `python json.load()` без проблем.

**Причина:** streaming `Utf8JsonReader` с ручным управлением буфером. Баг на границе чанка —
когда JSON токен (объект сообщения) пересекает границу буфера, `JsonDocument.ParseValue` получает
неполный input и isFinalBlock=true.

**Задача:** Исправить буферную логику в `ParseArchiveMessagesAsync`. Возможные подходы:
- Увеличить буфер когда текущий токен не помещается (`buffer.Length * 2`)
- Правильно обрабатывать `isFinalBlock` — ставить true только когда stream реально закончился И буфер полностью consumed
- Или заменить на `JsonDocument.ParseAsync(stream)` с последующим обходом — для файлов до 30MB это допустимо

**Верификация:**
```bash
# Тестовый файл
python3 scripts/build-telegram-archive-window.py \
  /home/codex/projects/TelegramAssistant/archive/ChatExport_2026-03-07/result.json \
  /tmp/parser-test.json --count 300

# Запустить парсер
dotnet run --project src/TgAssistant.Host -- \
  ArchiveImport__Enabled=true \
  ArchiveImport__SourcePath=/tmp/parser-test.json \
  ArchiveImport__MediaBasePath=/home/codex/projects/TelegramAssistant/archive/ChatExport_2026-03-07 \
  Analysis__Enabled=false \
  Telegram__ApiId=0

# Должно быть в логах: "Archive import completed: 298 messages"
# НЕ должно быть: JsonReaderException
```

Также проверить на полном архиве (27MB):
```bash
# Проверить что полный файл тоже парсится
wc -c /home/codex/projects/TelegramAssistant/archive/ChatExport_2026-03-07/result.json
```

**Commit message:** `Fix streaming JSON parser buffer boundary handling in archive import`

---

## S5-2. Проверить обработку медиа

**Задача:** Убедиться что медиа-обработка (фото, голосовые, видео) работает корректно
при архивном импорте.

**Шаги:**
1. Проверить что медиа файлы существуют по путям из архива:
```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "
SELECT media_type, processing_status, COUNT(*)
FROM messages WHERE source = 1 AND media_type != 0
GROUP BY media_type, processing_status ORDER BY 1, 2;
"
```

2. Если есть status=0 (pending) медиа — включить обработку:
```env
ARCHIVE_MEDIA_PROCESSING_ENABLED=true
```

3. Проверить что media_description и media_transcription заполняются:
```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "
SELECT id, media_type, processing_status,
  LEFT(media_description, 80) AS description,
  LEFT(media_transcription, 80) AS transcription
FROM messages WHERE source = 1 AND media_type != 0
ORDER BY id LIMIT 20;
"
```

4. Если медиа-обработка работает — голосовые и фото дадут дополнительный контекст для Stage5.

**Commit message:** не требуется (конфиг + верификация)

---

## S5-3. Отключить expensive модель для архива

**Файл:** `.env`

**Задача:** Выключить expensive pass для архивного прогона. Cheap модель справляется.

**Изменения в .env:**
```env
ANALYSIS_CHEAP_MODEL=deepseek/deepseek-chat
ANALYSIS_CHEAP_MODEL_AB_ENABLED=false
ANALYSIS_MAX_EXPENSIVE_PER_BATCH=0
ANALYSIS_EXPENSIVE_DAILY_BUDGET_USD=0
```

**Commit message:** не требуется (конфиг)

---

## S5-4. Прогнать полный архив

**Зависит от:** S5-1 (parser fix), S5-3 (expensive off)

**Задача:** Импортировать и обработать весь архив ChatExport_2026-03-07.

**Шаги:**

1. Очистить предыдущие тестовые данные:
```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "
BEGIN;
TRUNCATE intelligence_claims, intelligence_observations, facts, relationships,
         communication_events, entity_aliases, message_extractions,
         extraction_errors, analysis_usage_events, stage5_metrics_snapshots,
         archive_import_runs, fact_review_commands RESTART IDENTITY CASCADE;
DELETE FROM entities;
DELETE FROM messages;
UPDATE analysis_state SET value = 0, updated_at = NOW();
COMMIT;
"
```

2. Настроить .env для полного архива:
```env
ARCHIVE_IMPORT_ENABLED=true
ARCHIVE_IMPORT_CONFIRM_PROCESSING=true
ARCHIVE_IMPORT_REQUIRE_CONFIRMATION=false
ARCHIVE_IMPORT_SOURCE_PATH=/data/archive/ChatExport_2026-03-07/result.json
ARCHIVE_IMPORT_MEDIA_BASE_PATH=/data/archive/ChatExport_2026-03-07
ARCHIVE_MEDIA_PROCESSING_ENABLED=true
ANALYSIS_ENABLED=true
ANALYSIS_CHEAP_MODEL=deepseek/deepseek-chat
ANALYSIS_CHEAP_MODEL_AB_ENABLED=false
ANALYSIS_MAX_EXPENSIVE_PER_BATCH=0
ANALYSIS_EXPENSIVE_DAILY_BUDGET_USD=0
ANALYSIS_BATCH_SIZE=24
EMBEDDING_ENABLED=false
MERGE_ENABLED=false
MONITORING_ENABLED=true
VOICE_PARALINGUISTICS_ENABLED=false
```

3. Запустить:
```bash
docker compose build app
docker compose up -d app
```

4. Мониторить прогресс:
```bash
# Каждые 5 минут
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "
SELECT
  (SELECT COUNT(*) FROM messages) AS total_messages,
  (SELECT COUNT(*) FROM messages WHERE processing_status = 1) AS processed,
  (SELECT COUNT(*) FROM message_extractions) AS extracted,
  (SELECT value FROM analysis_state WHERE key = 'stage5:watermark') AS watermark,
  (SELECT ROUND(SUM(cost_usd)::numeric, 4) FROM analysis_usage_events) AS total_cost;
"
```

5. Дождаться завершения: watermark ≈ max(messages.id), extraction count ≈ processed count.

**Ожидаемое время:** зависит от размера архива. ~1000 сообщений/минуту на импорт, ~100-200/минуту на extraction.

---

## S5-5. Quality report после полного архива

**Зависит от:** S5-4

**Задача:** Spawn analyst agent для полного quality report.

**Промпт для analyst:**
```
Run full quality report on the completed archive extraction.
Database: docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "SQL"

Analyze:
1. Overall metrics: total messages, extracted, empty rate, claims/facts/observations/relationships counts
2. Entity list with fact counts — who has most data
3. Category distribution of facts — what categories dominate
4. Decay class distribution
5. Language check: sample 20 facts, count how many values are in Russian vs English
6. Remaining system_status/technical facts that should be filtered
7. Duplicate/near-duplicate facts (same entity + category + similar key)
8. Relationship quality: list all relationships, flag any that connect to non-person entities
9. Cost summary: total tokens, total cost, cost per useful fact
10. Top 10 most information-rich messages (most claims extracted)

Output: structured report with metrics table, top 5 issues, and specific recommendations.
```

---

## S5-6. Точечные правки по результатам analyst

**Зависит от:** S5-5

**Задача:** По рекомендациям analyst агента — точечные правки промпта и post-processing.

Типичные правки:
- Расширить blocklist категорий если system_status всё ещё просачивается
- Добавить примеры в промпт для missed signal patterns
- Настроить confidence thresholds если много false positives/negatives

**Commit message:** по результатам конкретных правок

---

## S5-7. Догрузить сообщения после 7 марта

**Зависит от:** S5-4 (архив обработан)

**Проблема:** Архив экспортирован 7 марта 2026. Realtime listener работает с момента запуска
на VPS, но между 7 марта и текущим моментом есть гэп. Сообщения за этот период не в архиве
и не пойманы listener'ом (если он не работал непрерывно).

**Решение:** Использовать WTelegramClient API для получения истории чата.

**Задача:** Создать одноразовый скрипт/команду для backfill:

1. Создать `src/TgAssistant.Processing/Archive/HistoryBackfillService.cs`:
   - IHostedService, запускается один раз
   - Использует WTelegramClient для вызова `Messages_GetHistory` по каждому monitored chat
   - Параметры:
     - `Backfill__Enabled` (default false)
     - `Backfill__SinceDate` — начальная дата (default: "2026-03-07")
     - `Backfill__ChatIds` — список чатов (default: из TG_MONITORED_CHATS)
   - Скачивает сообщения порциями (limit=100, offset по дате)
   - Фильтрует дубликаты: проверяет (source=Realtime, chat_id, telegram_message_id) — не импортировать если уже есть
   - Сохраняет как source=Realtime (не Archive) чтобы не смешивать с архивным импортом
   - Скачивает медиа в /data/media/{chat_id}/{date}/
   - После завершения ставит Backfill__Enabled=false в логах и останавливается

2. Зарегистрировать в Program.cs:
   ```csharp
   services.AddHostedService<HistoryBackfillService>();
   ```

3. Добавить конфигурацию:
   - `BackfillSettings` в Settings.cs
   - Секция в appsettings.json, docker-compose.yml, .env.example

**Верификация:**
```bash
# После backfill — проверить что гэп закрыт
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "
SELECT
  MIN(timestamp) AS earliest,
  MAX(timestamp) AS latest,
  COUNT(*) AS total,
  COUNT(*) FILTER (WHERE source = 0) AS realtime,
  COUNT(*) FILTER (WHERE source = 1) AS archive
FROM messages WHERE chat_id = 885574984;
"
```

**Commit message:** `Add history backfill service to close gap between archive export and realtime listener`

---

## S5-8. Включить merge + embedding после полного прогона

**Зависит от:** S5-4, S5-7

**Задача:** После того как все сообщения обработаны — включить entity merge и embedding
для дедупликации entities и semantic search.

```env
MERGE_ENABLED=true
EMBEDDING_ENABLED=true
EMBEDDING_MODEL=text-embedding-3-small
```

Merge автоматически найдёт дубликаты entities (например "Катя" vs "Катя Иванова")
и объединит их. Embedding позволит semantic fact lookup для expensive pass (когда включим
его для realtime).

---

## Порядок выполнения

```
S5-1 (parser fix)          ← блокирует всё остальное
  ↓
S5-2 (проверка медиа)      ← параллельно с S5-3
S5-3 (отключить expensive)  ← конфиг
  ↓
S5-4 (полный архив)         ← основной прогон
  ↓
S5-5 (quality report)       ← analyst agent
  ↓
S5-6 (точечные правки)      ← по результатам
  ↓
S5-7 (backfill 7 марта → сейчас)  ← закрыть гэп
  ↓
S5-8 (merge + embedding)    ← финализация
```

Ожидаемый результат: полное досье по всем monitored чатам, от начала истории до текущего момента,
с фактами, связями и decay policy. Готовая база для Stage 6 (Telegram Bot) и Stage 8 (Web UI).
