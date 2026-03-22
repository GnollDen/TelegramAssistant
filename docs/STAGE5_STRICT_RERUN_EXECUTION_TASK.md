# Stage 5 Strict Rerun Execution

Режим: strict clean.

Причина: прошлые обработки были сделаны старым обработчиком, поэтому нужен максимально чистый повторный Stage 5 baseline-run.

Это operational execution pass.

## Цель

Сделать:

1. dry-run отчёт по очищаемым данным;
2. полную очистку Stage 5 и связанного semantic residue;
3. выставить safe first-run Stage 5 config;
4. запустить чистый Stage 5 rerun;
5. подтвердить, что rerun реально стартовал и не упёрся сразу в budget/config blockers.

## Ограничения

- не трогать Telegram auth/session файлы;
- не удалять raw `messages`, если отдельным шагом не доказано, что именно они тоже старые и должны быть переимпортированы;
- не делать Stage 6+ product evaluation;
- не делать broad refactor;
- destructive cleanup делать только после явного dry-run summary в отчёте.

## Strict clean scope

### MUST purge

- `message_extractions`
- `intelligence_observations`
- `intelligence_claims`
- `facts`
- `relationships`
- `communication_events`
- `chat_sessions`
- `chat_dialog_summaries`
- `daily_summaries`
- `text_embeddings`
- `analysis_state` keys like `stage5:%`
- `ops_budget_operational_states` keys tied to Stage5/launch-smoke budget state

### ALSO purge in strict mode

- `entities`
- `entity_aliases`
- `entity_merge_candidates`
- `entity_merge_commands`
- `merge_decisions`
- `fact_review_commands`
- any Stage5 semantic residue queues/caches that preserve prior interpretation state

### KEEP

- `messages`
- `archive_import_runs`
- external archive ingestion tables
- Telegram session/auth files
- Docker volumes unless explicitly required

### OPTIONAL / conditional

- Redis stream/pending state only if backlog/pending is non-zero
- archive media cached files only if they are proven to interfere with rerun

## First-run safe config

Для первого rerun выставить safe profile:

- `Analysis__Enabled=true`
- `Analysis__ArchiveOnlyMode=true`
- `Analysis__ExpensivePassEnabled=false`
- `Analysis__MaxExpensivePerBatch=0`
- `Analysis__SummaryEnabled=false`
- `Analysis__SummaryWorkerEnabled=false`
- `Analysis__EditDiffEnabled=false`
- `Embedding__Enabled=false`
- `VoiceParalinguistics__Enabled=false`

Если raw archive import уже сделан и `messages` сохраняются:

- `ArchiveImport__Enabled=false`

Если raw archive import еще нужен:

- не запускать destructive rerun analysis до отдельного подтверждения import path

## Budget guardrails

Перед rerun поднять лимиты так, чтобы Stage5 text-only baseline не ушёл в `hard_paused`.

Минимум пересмотреть:

- `BudgetGuardrails__DailyBudgetUsd`
- `BudgetGuardrails__StageTextAnalysisBudgetUsd`

Если будут включаться embeddings/summaries later:

- `BudgetGuardrails__StageEmbeddingsBudgetUsd`

## Проверки

### До cleanup

- build
- compose config
- healthcheck
- `--runtime-wiring-check`
- `--stage5-smoke`
- row counts for every table/key group in cleanup scope

### После cleanup

- повторная проверка row counts
- `--runtime-wiring-check`
- `--stage5-smoke`

### После старта rerun

Подтвердить:

- Stage5 worker реально стартовал
- archive-only mode реально активен
- no immediate hard pause from budget guardrails
- no immediate DB/runtime crash

## Финальный отчет строго

1. Что было в dry-run scope
2. Что реально очищено
3. Какие файлы изменены
4. Какой config применен для first rerun
5. Что проверено после cleanup
6. Стартовал ли Stage 5 rerun
7. Какие риски остались
8. Verdict: rerun started / blocked

