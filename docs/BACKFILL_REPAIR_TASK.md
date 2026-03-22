# Backfill Repair Task

Это repair pass после некорректного Phase A backfill.

## Контекст

Phase A technical rollout прошел, но продуктово выбран неверный `SinceDate`.

Проблемы:

- для 2 дополнительных чатов использован `SinceDate=2026-03-07`, а нужно собирать историю от начала чатов;
- в системе уже есть часть сообщений по этим чатам, поэтому перед полным historical backfill надо проверить, не будет ли грязного смешения истории;
- после нового backfill нужно отдельно проверить, что historical session slicing получился корректным.

## Главная цель

Исправить backfill для 2 дополнительных чатов:

1. понять текущее состояние уже загруженной истории;
2. решить cleanup scope именно для этих 2 чатов;
3. выполнить полный historical backfill от начала чатов;
4. проверить корректность последующей нарезки на сессии.

## Что нужно сделать

### 1. Diagnose existing state for the 2 chats

Для чатов:

- `5276431471`
- `3719942125`

Нужно показать:

- сколько `messages` уже есть по каждому чату;
- какие `source` у этих сообщений;
- какие временные диапазоны уже покрыты;
- есть ли уже `chat_sessions`, `message_extractions`, summaries, facts/claims/observations по этим чатам.

### 2. Decide repair scope

Нужно operator-useful решение:

- можно ли безопасно достроить историю поверх существующих данных;
- или нужно selective cleanup именно для этих 2 чатов перед полным backfill.

Если нужен selective cleanup:

- чистить только данные этих 2 чатов
- не трогать главный чат
- не делать полный reset всей базы

### 3. Run full historical backfill for the 2 chats

Нужно:

- задать backfill так, чтобы история шла от начала чатов, а не с 2026-03-07;
- не смешивать это с Phase B главного чата;
- сохранить safe runtime profile:
  - `Analysis__Enabled=true`
  - `Analysis__ArchiveOnlyMode=false`
  - expensive off
  - edit-diff off
  - embeddings off
  - listener conflict must be handled

### 4. Validate historical session slicing

После backfill нужно проверить:

- строятся ли `chat_sessions` по этим чатам;
- выглядит ли session distribution разумно;
- нет ли явной патологической фрагментации;
- progression по session_index последовательный или нет.

### 5. Do not start Phase B

Хвост главного чата пока не запускать.

Сначала нужно закрыть repair по 2 дополнительным чатам.

## Ограничения

- не делать полный reset всей базы
- не трогать главный чат без необходимости
- не запускать silently backfill Phase B
- если нужен selective cleanup, явно описать scope

## Финальный отчет строго

1. Что найдено по текущему состоянию 2 чатов
2. Какие файлы изменены
3. Нужен ли был selective cleanup и что именно очищено
4. Как выполнен полный historical backfill
5. Как выглядит session slicing после repair
6. Какие риски/аномалии остались
7. Verdict: repair accepted / needs another pass

