# Stage 5 Selective Chat Recompute

Это targeted recompute pass только для двух backfill-чатов.

## Контекст

Полный reset всей базы не нужен.
Главный архивный чат трогать нельзя.

Нужно selectively пересчитать Stage 5 output только для двух чатов:

- `3719942125`
- `5276431471`

на уже исправленном коде и с сохранением raw substrate.

## Главная цель

Сохранить:

- raw `messages`
- media files / media paths
- caches where practical
- главный архивный чат и его derived state

И заново построить Stage 5 derived layer только для двух backfill-чатов.

## Что нужно сделать

### 1. Dry-run scope

Показать, что именно будет очищено для этих двух chat_id:

- `chat_sessions`
- `message_extractions`
- `intelligence_observations`
- `intelligence_claims`
- `facts`
- `relationships`
- `communication_events`
- `chat_dialog_summaries`
- `daily_summaries`
- related `analysis_state` keys / runtime markers
- any tightly coupled per-chat Stage 5 residue

Отдельно показать:

- что главный чат не затрагивается
- что raw messages не удаляются

### 2. Selective cleanup

Выполнить cleanup строго только для:

- `3719942125`
- `5276431471`

Не трогать:

- главный чат
- global baseline elsewhere
- external archive ingestion tables
- Telegram session/auth

### 3. Recompute run

Запустить Stage 5 follow-through так, чтобы:

- эти два чата были пересчитаны на текущем исправленном коде
- `Analysis__ArchiveOnlyMode=false`
- expensive off
- edit-diff off
- embeddings off

Если нужен временный operational профиль для recompute — описать его явно.

### 4. Verification

Нужно подтвердить:

- session coverage строится заново
- extraction идет по full historical range этих чатов
- ordering/seed path больше не starves
- нет нового blocker-а

### 5. Acceptance question

Нужно ответить:

- recompute завершен корректно
- качество/coverage стали чище
- можно идти дальше, не трогая главный чат

## Ограничения

- не делать full DB reset
- не чистить главный архивный чат
- не запускать Phase B главного чата
- не делать broad redesign

## Финальный отчет строго

1. Что было в dry-run scope
2. Что реально очищено
3. Какие файлы изменены
4. Какой runtime profile применен
5. Что показала проверка после recompute
6. Какие риски остались
7. Verdict: recompute accepted / needs another pass

