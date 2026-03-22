# Backfill / Realtime Coordination Integration Design

Это design-док для следующего stabilization sprint.
Текущий документ не меняет runtime wiring и не запускает live integration.

## 1. Problem Statement

Из operational опыта baseline/repair:

- исторический догон и realtime listener конфликтуют за общий `telegram.session`;
- параллельный запуск backfill + listener увеличивает риск дубликатов/гонок и нестабильного прогресса;
- новый monitored chat должен сначала проходить historical catch-up, а не сразу включаться в realtime.

Целевой принцип: **backfill first, realtime second**.

## 2. Coordination Model

Система работает как state machine уровня orchestration:

- `historical_required` -> найден хотя бы один chat, которому нужен catch-up;
- `backfill_active` -> listener полностью disabled, выполняется historical догон;
- `handover_pending` -> backfill завершен, идет финальная проверка completion markers;
- `realtime_active` -> listener enabled только для ready chats;
- `degraded_backfill` -> backfill прерван/частично завершен, listener остается disabled для affected chats.

## 3. Startup Detection (Historical Catch-Up Need)

При старте host должен вычислять `historical_required` по каждому monitored chat:

1. Есть chat в monitoring scope, но нет подтвержденного historical completion marker.
2. Есть gap между последним подтвержденным historical checkpoint и текущим backfill target.
3. Chat добавлен недавно (onboarding) и ни разу не проходил catch-up.

Если хотя бы один chat не ready, orchestration переводит систему в `backfill_active`.

## 4. Mode Rules

## Backfill Mode

- `HistoryBackfillService` активен для pending chats.
- realtime listener (`TelegramClientService`/MTProto polling path) не запускается для этих chats.
- Stage5 processing остается включенным для backfill-потока.
- Режим сохраняется до подтверждения completion policy.

## Handover

- После достижения backfill boundary система делает completion check.
- Если check зеленый -> chat помечается `ready_for_realtime`.
- Если check частичный/красный -> chat остается в backfill/degraded state.

## Realtime Mode

- Listener разрешается только для chats со статусом `ready_for_realtime=true`.
- При появлении нового monitored chat выполняется локальный возврат этого chat в `historical_required` без глобального reset.

## 5. Activation Policy

Chat считается `ready for realtime`, только если одновременно выполнены 3 группы сигналов.

## A. Backfill Boundary Signals

- historical range для chat закрыт до заданного `backfill_target`;
- `messages` по chat имеют стабильный верхний watermark в двух подряд checks;
- не обнаружен активный historical fetch lag.

## B. Processing Completion Signals

- session slicing покрывает historical range без явных gaps;
- extraction backlog по chat не имеет устойчивого роста;
- first/last extracted timestamps консистентны с message range.

## C. Stability Signals

- нет повторяющегося retry-loop/blocker по этому chat;
- нет признаков конфликта за `telegram.session`;
- monitoring check помечает chat как handover-safe.

### Partial Completion / Interruption Policy

Если completion частичный или процесс прерван:

- chat переводится в `degraded_backfill`;
- realtime для этого chat остается disabled;
- фиксируется причина (`interrupted`, `backlog_not_drained`, `session_conflict`, `unknown`);
- следующий startup продолжает catch-up от последнего подтвержденного checkpoint, без полного reset.

## 6. Required State Markers (Design Contract)

Для следующего sprint нужно добавить/стандартизировать persistent markers (минимум):

- `chat_backfill_state.mode` (`historical_required|backfill_active|handover_pending|realtime_active|degraded_backfill`)
- `chat_backfill_state.last_confirmed_message_ts`
- `chat_backfill_state.backfill_target_ts`
- `chat_backfill_state.completion_checked_at`
- `chat_backfill_state.ready_for_realtime` (bool)
- `chat_backfill_state.block_reason` (nullable)

Дополнительно допускается агрегированный marker в `analysis_state` для fast startup decision.

## 7. Scenario Coverage

## Scenario A: Recovery Catch-Up After Downtime

- После простоя startup видит gap до актуального target.
- affected chats входят в `backfill_active`.
- listener остается disabled для affected chats до completion markers.
- после successful handover включается realtime.

## Scenario B: New Monitored Chat Onboarding

- Новый chat при добавлении автоматически получает `historical_required`.
- Сначала выполняется полный/пороговый historical catch-up.
- До `ready_for_realtime=true` listener на chat не активируется.
- После handover chat подключается к общему realtime циклу.

## 8. Observability/Operations Requirements

- Явный лог transition между coordination states.
- Отдельные counters: `backfill_pending_chats`, `ready_for_realtime_chats`, `degraded_chats`.
- Быстрый operator check: какие chats блокируют включение realtime.

## 9. Non-Goals (For This Prep)

- Нет live переключения продового runtime в рамках этого документа.
- Нет изменений `Program.cs`, `appsettings`, shared wiring прямо сейчас.
- Нет массового redesign Stage5/Stage6.
