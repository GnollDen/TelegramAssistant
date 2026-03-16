# Stage5/v10 Architecture Review (TelegramAssistant)

Date: 2026-03-16  
Scope: Stage5/Stage6 processing pipeline, worker topology, legacy/zombie code, documentation drift.

## Executive Summary

1. Stage5 orchestration уже разделена на отдельные компоненты (`ExtractionApplier`, `ExtractionRefiner`, `ExtractionValidator`, `ExpensivePassResolver`, `MessageContentBuilder`), что соответствует целевой архитектуре из backlog.
2. Основной источник лишней LLM-активности исторически был в summary-контуре: повторная генерация сессий и слабая фильтрация техшумов.
3. `ChatSessionSlicerWorkerService` дублирует ответственность `DialogSummaryWorkerService` по сессиям и выглядит как legacy-ветка.
4. `ContinuousRefinementWorkerService` использует устаревший prompt-id (`stage5_cheap_extract_v7`) и может «перетирать» актуальные v10-экстракции при включении.
5. В `Program.cs` зарегистрированы оба контура сессий (`ChatSessionSlicerWorkerService` + `DialogSummaryWorkerService`), что повышает риск двойной работы.
6. Фильтрация по `processing_status=Processed` в репозиториях присутствует, но это не защищает от логического дубляжа между воркерами.
7. Stage6 refinement выключен по умолчанию (`ContinuousRefinement.Enabled=false`), но код/документация не фиксируют чёткую стратегию его использования после v10.
8. Документация существенно отстаёт от текущего пайплайна: README и stage5 algorithm описывают устаревший state/потоки.

## Component Review

| Component | Status | Why | Recommendation |
|---|---|---|---|
| `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs` | OK | Тонкий оркестратор, фильтрация и делегирование выделены в отдельные сервисы. | Сохранить; дальше декомпозировать только при появлении новых фич. |
| `src/TgAssistant.Intelligence/Stage5/DialogSummaryWorkerService.cs` | Risky | Высокая стоимость при массовом backfill; отдельный watermark-контур и embedding-апдейты чувствительны к reset watermark. | Оставить основным контуром summary; поддерживать строгую фильтрацию и инкрементальную регенерацию. |
| `src/TgAssistant.Intelligence/Stage5/ChatSessionSlicerWorkerService.cs` | Legacy/Zombie | Отдельно режет сессии, но summary-воркер уже строит episodic sessions; дублирует ответственность и state (`stage5:slicer_watermark`). | Кандидат на удаление после подтверждения, что внешних потребителей нет. |
| `src/TgAssistant.Intelligence/Stage6/ContinuousRefinementWorkerService.cs` | Risky/Legacy | Использует `CheapPromptId = stage5_cheap_extract_v7`; может конфликтовать с v10-политикой и создавать лишние cheap-вызовы. | Оставить выключенным; перевести на v10 prompt-id или явно депрекейтнуть. |
| `src/TgAssistant.Host/Program.cs` hosted services block | Risky | Регистрирует оба session-контура и много фоновых воркеров без явной матрицы владения этапами. | Упростить registration: удалить/отключить slicer; задокументировать ownership каждого воркера. |
| `src/TgAssistant.Infrastructure/Database/MessageRepository.cs` | OK | Запросы для анализа/summary используют `processing_status == Processed` и media readiness. | Оставить; критерии фильтрации задокументировать в техдоках. |
| `README.md` | Legacy | Статусы этапов не соответствуют факту (Stage5/6 отмечены как незавершённые). | Обновить как source-of-truth для runtime-пайплайна. |
| `docs/stage5-extraction-algorithm.txt` | Legacy | Описание summary/refinement и prompt версии не соответствует текущему коду v10. | Переписать snapshot алгоритма под текущую реализацию. |
| `CODEX_STAGE5_FINAL.md` | Legacy | Исторический артефакт спринта; может вводить в заблуждение как “финальное” состояние. | Пометить как архивный документ или перенести в `docs/archive/`. |
| `WORKSPACE_HANDOFF_2026-03-16.md`, `docs/session-notes.md` | Risky | Оперативные заметки смешиваются с постоянной документацией. | Явно разделить “операционные логи” и “постоянные архитектурные доки”. |

## Worker Topology and Duplicate Paths

- Registration:
  - `AnalysisWorkerService`: `src/TgAssistant.Host/Program.cs:181`
  - `ContinuousRefinementWorkerService` (conditional): `src/TgAssistant.Host/Program.cs:182-185`
  - `ChatSessionSlicerWorkerService`: `src/TgAssistant.Host/Program.cs:192`
  - `DialogSummaryWorkerService`: `src/TgAssistant.Host/Program.cs:193`
- Duplicate risk:
  - Session boundaries can be managed by both slicer and summary worker.
  - `ChatSessionSlicerWorkerService` writes empty summary for new sessions (`Summary = string.Empty`) and maintains separate watermark (`stage5:slicer_watermark`).
- Refinement risk:
  - Stage6 candidate loop may re-touch old extractions and apply intelligence writes again.
  - Prompt version mismatch (`v7` vs active v10 pipeline) is explicit in code.

## Candidates for Removal / Deprecation / Config-Off

### Remove (after smoke check in staging)
1. `src/TgAssistant.Intelligence/Stage5/ChatSessionSlicerWorkerService.cs`
2. DI registration line for slicer in `src/TgAssistant.Host/Program.cs`
3. Related config keys:
   - `Analysis.SessionSlicerEnabled`
   - `Analysis.SessionSlicerPollIntervalSeconds`
   - `Analysis.SessionSlicerBatchSize`
4. Watermark key usage/doc mentions: `stage5:slicer_watermark`

### Keep Config-Off (default false)
1. `src/TgAssistant.Intelligence/Stage6/ContinuousRefinementWorkerService.cs`
2. `ContinuousRefinement` section in config with explicit warning “off for v10 unless targeted campaign”.

### Mark Deprecated (docs + code comments)
1. `CODEX_STAGE5_FINAL.md` (historical sprint artifact)
2. Any references to prompt ids `< v10` in operational docs.

## Documentation Drift and Required Updates

1. `README.md`
   - Обновить статус стадий (Stage5/Stage6 are live).
   - Добавить блок “Current Worker Topology”.
   - Добавить “Noise Control” (service/empty/command filtering, summary constraints).
2. `docs/stage5-extraction-algorithm.txt`
   - Обновить prompt/version references (v10).
   - Переписать раздел summary-watermarks (`stage5:summary_extraction_watermark` + `stage5:summary_watermark`).
   - Добавить роль cheap_json в summary input.
3. `CODEX_BACKLOG.md`
   - Явно отметить статус выполненных A-задач (особенно A1/A8 и связанные с v10 schema/summarization).
   - Вынести “open issues” отдельным блоком вместо старых формулировок.
4. `docs/stage5_product_backlog.md`
   - Снять формулировку “backlog closed” и заменить на “stabilization backlog”.
5. `docs/session-notes.md` и `WORKSPACE_HANDOFF_2026-03-16.md`
   - Добавить дисклеймер “operational, not source-of-truth architecture”.

## Practical Sync Plan

1. Обновить `README.md` как главный runtime overview.
2. Синхронизировать `docs/stage5-extraction-algorithm.txt` с текущим кодом Stage5/Stage6.
3. Обновить `CODEX_BACKLOG.md` в формате “done / in-progress / deprecated”.
4. Пометить исторические артефакты (`CODEX_STAGE5_FINAL.md`) как archive-only.
5. После doc-sync выполнить короткий consistency-check:
   - список воркеров в `Program.cs`
   - список watermark keys
   - активные prompt ids (cheap/expensive/summary/refinement).
