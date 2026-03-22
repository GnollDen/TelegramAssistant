# Sprint 19 Acceptance Draft

## Purpose

Проверить, что runtime coordination между historical backfill и realtime listener стал предсказуемым, безопасным и операционно наблюдаемым.

## Acceptance Checklist

## Startup Mode Decision

- при startup система вычисляет per-chat historical readiness
- если есть not-ready chat, выбирается `backfill_active`
- если все chats ready, выбирается `realtime_active`

## Listener Gating

- listener не активируется для chat без `ready_for_realtime`
- во время `backfill_active` нет конкурирующего доступа к `telegram.session`
- при handover listener включается только после completion check

## Activation Policy Enforcement

- backfill boundary signals подтверждены
- processing completion signals подтверждены
- stability signals подтверждены
- только после этого chat получает `ready_for_realtime=true`

## Interruption Safety

- partial completion переводит chat в `degraded_backfill`
- фиксируется причина блокировки и checkpoint
- следующий startup возобновляет catch-up без полного reset

## Scenario A: Recovery After Downtime

- после downtime система уходит в catch-up mode
- historical gap закрывается
- handover в realtime выполняется только после completion markers

## Scenario B: New Monitored Chat Onboarding

- новый chat автоматически проходит historical catch-up
- realtime до catch-up completion не активируется
- после handover chat корректно входит в realtime поток

## Observability

- state transitions логируются
- доступен список chats, блокирующих realtime readiness
- counters/markers позволяют оператору отличить "идет догон" от "новый blocker"

## Hold Conditions

Hold Sprint 19, если выполняется хотя бы одно:

- realtime активируется до historical completion
- listener конфликтует с backfill по `telegram.session`
- нет явного persistent state для completion/interrupt
- один из обязательных сценариев не воспроизводим
- mode decision на startup недетерминирован

## Pass Condition

Sprint 19 passes if:

- backfill/realtime coordination управляется явной policy
- recovery и onboarding сценарии проходят без ручных workaround
- handover в realtime происходит только после проверяемого completion
