Используй project skills из репозитория:

- skills/tg-project-executor/SKILL.md
- skills/tg-review-gate/SKILL.md

Сначала прочитай:
- skills/tg-project-executor/SKILL.md
- skills/tg-review-gate/SKILL.md
- docs/MERGE_SEQUENCE_2026-03-23.md
- docs/MERGE_BATCH_1_G1_G2.md
- docs/LAUNCH_READINESS.md
- docs/S19_SPRINT.md
- docs/S19_REPAIR.md
- docs/S19_CHECK.md

Это merge-prep / review gate для Batch 1.

Контекст:
- Batch 1 = production-used runtime fixes + Stage5 repair tooling
- это самый рискованный и самый важный слой текущего рабочего дерева
- нужно review-ить и подготовить его как отдельный mergeable changeset
- не смешивать сюда Sprint 21 / Sprint 20 / Sprint 22 beyond unavoidable context

Главная цель:
подготовить чистое решение по Batch 1:
- что входит
- что не входит
- pass/hold
- что нужно поправить перед merge, если нужно

Нужно сделать:

1. Review Batch 1 scope
- только файлы из docs/MERGE_BATCH_1_G1_G2.md
- Program.cs смотреть только в части G1/G2

2. Дать findings-first gate
- реальные production-risk findings, если они есть
- если все ок, так и сказать

3. Подготовить merge recommendation
Нужно ответить:
- Batch 1 mergeable as-is
- mergeable with small follow-up
- hold

4. Если есть mixed hunks
- явно указать, какие hunks/файлы надо отделить перед merge

Финальный отчет строго:
1. Findings
2. What belongs to Batch 1
3. What must be excluded from Batch 1
4. Merge verdict: pass / pass with follow-up / hold
5. What should happen next
