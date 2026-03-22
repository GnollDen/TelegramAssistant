# Backfill Phase Plan

Это operational rollout plan после успешного Stage 5 baseline по архивному чату.

## Контекст

Сейчас:

- основной архивный baseline Stage 5 практически завершен
- главный чат уже покрыт экспортом
- нужен исторический добор:
  - 2 дополнительных monitored chats
  - маленький хвост главного чата после даты экспорта

## Главная цель

Аккуратно добрать недостающую историю без полного нового baseline reset и без смешивания rollout в один неконтролируемый широкий прогон.

## Общие правила

### 1. Backfill идет вместе с обработкой

Backfill phase означает:

- HistoryBackfillService подтягивает historical messages
- они сохраняются как realtime/history path
- Stage 5 их обрабатывает

Значит:

- `Analysis__ArchiveOnlyMode=false`

иначе новые backfill messages не будут идти в Stage 5 analysis.

### 2. Делить на 2 фазы

Не делать один широкий одинаковый прогон на все чаты сразу.

Правильная схема:

- `Phase A`: 2 дополнительных чата
- `Phase B`: маленький хвост главного чата

### 3. Без нового полного cleanup

Backfill rollout делается поверх уже принятого baseline.

Не делать:

- full reset
- full reimport
- broad cleanup

## Phase A

### Scope

Два дополнительных monitored chats.

### Config intent

- `Backfill__Enabled=true`
- `Backfill__ChatIds=<2 дополнительных чата>`
- `Backfill__SinceDate=<осмысленная дата старта>`
- `Analysis__Enabled=true`
- `Analysis__ArchiveOnlyMode=false`

Сохранить safe defaults:

- `Analysis__ExpensivePassEnabled=false`
- `Analysis__EditDiffEnabled=false`
- `Embedding__Enabled=false`

Media/audio:

- можно оставить включенными только если budget/операционный шум позволяют
- иначе сначала text/history backfill, потом enrichments отдельно

### Acceptance for Phase A

- history backfill реально стартовал по 2 чатам
- сообщения приходят
- Stage 5 их реально подхватывает
- нет нового blocker-а
- budget остается контролируемым

## Phase B

### Scope

Только хвост главного чата после даты экспортного архива.

### Config intent

- `Backfill__Enabled=true`
- `Backfill__ChatIds=<главный чат>`
- `Backfill__SinceDate=<дата после export cutoff>`
- `Analysis__ArchiveOnlyMode=false`

### Goal

Добрать только недостающий пост-архивный хвост, не тянуть заново весь главный чат.

### Acceptance for Phase B

- хвост главного чата реально подтянут
- нет широкого дубля архивной истории
- Stage 5 обработал новые сообщения

## Operational watchpoints

- budget guardrails
- voice warning-noise
- provider instability on vision/audio
- duplicates between archive and realtime history layers

## Финальный rollout-отчет по каждой фазе

1. Что включено в фазу
2. Какие файлы изменены
3. Какой config применен
4. Что реально было добрано
5. Как Stage 5 это обработал
6. Какие риски/аномалии выявлены
7. Verdict: phase accepted / needs follow-up

