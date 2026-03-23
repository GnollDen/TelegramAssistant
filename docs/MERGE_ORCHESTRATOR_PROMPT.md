Используй project skills из репозитория:

- skills/tg-project-executor/SKILL.md
- skills/tg-review-gate/SKILL.md

Сначала прочитай:
- skills/tg-project-executor/SKILL.md
- skills/tg-review-gate/SKILL.md
- docs/MERGE_EXECUTION_PLAN_2026-03-23.md
- docs/MERGE_MULTI_AGENT_PLAN_2026-03-23.md
- docs/MERGE_BATCH_1_G1_G2.md
- docs/MERGE_BATCH_2_S21.md
- docs/MERGE_BATCH_3_S20.md
- docs/MERGE_BATCH_4_S22.md
- docs/MERGE_BATCH_4_FOLLOWUP_TASK.md
- docs/LAUNCH_READINESS.md

Ты merge orchestrator.

Главная цель:
подготовить рабочее дерево к чистому merge sequence по batch-ам без повторного смешивания scope.

Работай так:

1. Сначала составь краткий merge execution plan
Нужно явно подтвердить:
- какой batch идет первым
- какие worker tasks можно делать параллельно
- где есть file/hunk collision risk

2. Самостоятельно подними воркеров
Минимум:

- Worker A:
  Batch 1 merge execution prep
  Scope:
  - G1/G2 runtime-repair layer
  - clean path+hunk prep for Program.cs

- Worker B:
  Batch 2 merge execution prep
  Scope:
  - Sprint 21 clean changeset
  - Program.cs Sprint 21 hunks + Startup/*

- Worker C:
  Batch 4 follow-up cleanup
  Scope:
  - docs/prep-only fixes
  - preview compose ownership cleanup

3. Управляй collision risk
- Worker A and Worker B must not blindly edit the same Program.cs ranges
- Worker C must stay docs/preview-only
- do not start actual production rollout

4. Собери финальный orchestrator report
Нужно вернуть:
- что сделал каждый worker
- какие файлы изменены
- что уже merge-ready
- что still needs a follow-up before merge
- recommended exact merge order after this pass

Ограничения:
- не делать broad destructive ops
- не смешивать batch-ы обратно
- это merge-execution prep, не prod rollout

Финальный отчет строго:
1. Execution plan
2. What Worker A did
3. What Worker B did
4. What Worker C did
5. What is merge-ready now
6. What still needs follow-up
7. Recommended merge order
