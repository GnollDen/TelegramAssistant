# Sprint 19 Task Pack Draft

## Name

Backfill / Realtime Coordination Stabilization

## Goal

Ввести надежную orchestration-модель `backfill first, realtime second`, которая исключает конфликт за `telegram.session` и гарантирует, что каждый monitored chat проходит historical catch-up до realtime-режима.

## Why This Sprint

Baseline/repair/follow-through подтвердили:

- pipeline может догонять historical backlog;
- главный operational риск сейчас в координации режимов, а не в отсутствии extraction-функциональности;
- без явной activation policy новый chat и recovery после downtime остаются хрупкими.

## Read First

1. `docs/BACKFILL_PHASE_PLAN.md`
2. `docs/BACKFILL_REPAIR_TASK.md`
3. `docs/BACKFILL_FOLLOWTHROUGH_CHECK.md`
4. `docs/BACKFILL_REALTIME_COORDINATION_INTEGRATION_DESIGN.md`
5. `docs/LAUNCH_READINESS.md`
6. `docs/BACKLOG_STATUS.md`

## Scope

In scope:

- startup detection of historical catch-up need (per chat)
- explicit coordination states for backfill/handover/realtime
- listener gating: disable realtime for not-ready chats
- activation policy + persistent completion markers
- interruption/partial-completion resume behavior
- observability for coordination decisions

Out of scope:

- broad Stage5 prompt/model redesign
- full reset/reimport all chats
- unrelated runtime wiring refactor
- product UX redesign

## Required Deliverables

### 1. Coordination State Model

Реализовать state model минимум с состояниями:

- `historical_required`
- `backfill_active`
- `handover_pending`
- `realtime_active`
- `degraded_backfill`

### 2. Startup Catch-Up Detection

На startup вычислять per-chat readiness и запускать backfill для pending chats до включения realtime.

### 3. Realtime Listener Gating

Listener не должен стартовать для chats без `ready_for_realtime=true`.

### 4. Handover and Activation Policy

Внедрить completion checks и переключение в realtime только при выполнении policy сигналов.

### 5. Partial/Interrupted Recovery

При interruption:

- сохранять причину и checkpoint;
- продолжать с checkpoint на следующем startup;
- не включать realtime преждевременно.

### 6. Scenario Coverage (Must Pass)

Обязательно покрыть:

- `Scenario A`: recovery catch-up after downtime
- `Scenario B`: new monitored chat onboarding

### 7. Operator Observability

Добавить operator-visible markers/counters/logs для answer на вопросы:

- какие chats не готовы к realtime;
- что блокирует handover;
- идет ли backfill progress.

## Implementation Notes

- Изменения должны быть additive и reviewable.
- Не трогать `Program.cs`/shared runtime wiring без необходимости.
- Если нужен schema change для markers: отдельная новая migration в `Infrastructure/Database/Migrations/`.
- Не менять существующие baseline/repair данные широким destructive cleanup.

## Verification Required

1. `dotnet build TelegramAssistant.sln`
2. startup check: deterministic mode choice (`backfill_active` vs `realtime_active`)
3. smoke on Scenario A (downtime catch-up)
4. smoke on Scenario B (new chat onboarding)
5. evidence, что listener gating работает и не конфликтует с backfill session

## Definition of Done

Sprint 19 завершен только если:

1. система детектирует historical catch-up need автоматически
2. realtime listener отключен для not-ready chats
3. handover в realtime происходит только после completion policy
4. interruption не приводит к silent realtime activation
5. оба обязательных сценария воспроизводимы и проходят

## Final Report Required

1. что изменено
2. какие файлы изменены
3. как работает coordination model после изменений
4. какие сценарии проверены и каков результат
5. оставшиеся риски и следующий hardening step
