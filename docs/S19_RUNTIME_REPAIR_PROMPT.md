Используй project skills из репозитория:

- skills/tg-project-executor/SKILL.md
- skills/tg-review-gate/SKILL.md

Сначала прочитай:
- skills/tg-project-executor/SKILL.md
- skills/tg-review-gate/SKILL.md
- docs/S19_RUNTIME_REPAIR.md
- docs/S19_SPRINT.md
- docs/S19_REPAIR.md
- docs/S19_CHECK.md

Это narrow runtime repair pass для Sprint 19.

Контекст:
- Sprint 19 wiring уже задеплоен
- reality check нашел 2 production defect:
  - duplicate key / race в EnsureStatesAsync
  - false degrade active auto-recovery backfill -> degraded_backfill

Работай строго по:
- docs/S19_RUNTIME_REPAIR.md

После фикса:
- задеплой patch
- проверь runtime behavior

Финальный отчет строго:
1. What changed
2. Which files changed
3. How the state-init race was fixed
4. How false-degrade was fixed
5. What was verified
6. Whether Sprint 19 runtime hold is cleared
