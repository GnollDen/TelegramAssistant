# Full Baseline Reimport

Режим: full baseline reimport.

Причина: raw import быстрый, а прошлые обработки и media path менялись. Нужен максимально чистый baseline без наследования старых raw/media/status/semantic хвостов.

Это operational execution pass.

## Цель

Сделать:

1. полный dry-run scope;
2. полную очистку baseline-данных;
3. заново выполнить archive import;
4. заново выполнить media path на новом baseline;
5. запустить safe Stage 5 analysis rerun;
6. подтвердить, что baseline действительно чистый и новый.

## Ограничения

- не трогать Telegram auth/session файлы;
- не удалять external archive ingestion tables без отдельной причины;
- не делать Stage 6+ product evaluation;
- не делать broad refactor;
- destructive cleanup выполнять только после явного dry-run summary.

## Cleanup scope

### MUST purge

Raw/import layer:

- `messages`
- `archive_import_runs`

Stage 5 / semantic derived layer:

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
- `analysis_usage_events`
- `extraction_errors`
- `analysis_state` keys like `stage5:%`
- `ops_budget_operational_states` keys tied to stage5/import/launch-smoke budget state

Semantic residue:

- `entities`
- `entity_aliases`
- `entity_merge_candidates`
- `entity_merge_commands`
- `merge_decisions`
- `fact_review_commands`

Also clear any FK-dependent domain/legacy tables that prevent a true clean baseline.

### KEEP

- Telegram session/auth files
- external archive ingestion tables unless explicitly requested otherwise
- app/system configs
- docker volumes unless explicitly required

### OPTIONAL

- Redis stream/pending state only if backlog/pending > 0
- media files on disk only if they are stale/broken and will be replaced

## Runtime profile for baseline

### Phase A: raw import + archive media

Use:

- `ArchiveImport__Enabled=true`
- `ArchiveImport__RequireCostConfirmation=false` if operator already approved full rerun
- `ArchiveImport__ConfirmProcessing=true`
- `ArchiveImport__MediaProcessingEnabled=true`

For first baseline keep heavy extras off:

- `Analysis__Enabled=false` during pure import if that makes sequencing cleaner
  or clearly justify if running import + analysis together
- `VoiceParalinguistics__Enabled=false`
- `Embedding__Enabled=false`
- `Analysis__SummaryEnabled=false`
- `Analysis__SummaryWorkerEnabled=false`
- `Analysis__EditDiffEnabled=false`
- `Analysis__ExpensivePassEnabled=false`

### Phase B: Stage 5 text baseline

After import is complete:

- `ArchiveImport__Enabled=false`
- `Analysis__Enabled=true`
- `Analysis__ArchiveOnlyMode=true`
- `Analysis__ExpensivePassEnabled=false`
- `Analysis__MaxExpensivePerBatch=0`
- `Analysis__SummaryEnabled=false`
- `Analysis__SummaryWorkerEnabled=false`
- `Analysis__SummaryHistoricalHintsEnabled=false`
- `Analysis__EditDiffEnabled=false`
- `Embedding__Enabled=false`
- `VoiceParalinguistics__Enabled=false`

## Budget guardrails

Before rerun, raise guardrails enough for:

- archive import
- archive media processing
- Stage 5 text baseline

At minimum review:

- `BudgetGuardrails__DailyBudgetUsd`
- `BudgetGuardrails__ImportBudgetUsd`
- `BudgetGuardrails__StageTextAnalysisBudgetUsd`
- `BudgetGuardrails__StageVisionBudgetUsd`
- `BudgetGuardrails__StageAudioBudgetUsd`

## Execution order

1. Dry-run counts and scope summary.
2. Stop writer/app if needed.
3. Execute full cleanup.
4. Recheck zero/clean state.
5. Apply Phase A config.
6. Start app and complete archive import.
7. Confirm media path behavior on fresh imported messages.
8. Switch to Phase B config.
9. Start Stage 5 baseline analysis.
10. Confirm real progress and no immediate hard pause/crash.

## Checks

### Before cleanup

- build
- compose config
- healthcheck
- row counts for all cleanup targets

### After cleanup

- row counts confirm clean baseline
- runtime wiring
- stage5 smoke consistency

### After import

- new `archive_import_runs`
- new `messages`
- pending/processed media behavior visible

### After Stage 5 start

- analysis worker started
- archive-only mode active
- real extraction progress visible
- no immediate budget block

## Финальный отчет строго

1. Что было в dry-run scope
2. Что реально очищено
3. Какие файлы изменены
4. Какой config применен для Phase A и Phase B
5. Что проверено после cleanup
6. Завершился ли fresh import
7. Стартовал ли новый Stage 5 baseline
8. Какие риски остались
9. Verdict: full baseline rebuilt / blocked

