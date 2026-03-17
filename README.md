# Telegram Personal Assistant

## Pipeline
```text
[Telegram MTProto push] -> [Redis Stream] -> [Batch Worker] -> [PostgreSQL]
                                                 |
                                          [OpenRouter Media]
                                                 |
                                         [Stage5 Analysis v10]
                                        /         |          \
                                [Cheap/Expensive] |      [Summaries]
                                                   |
                                            [Stage6 Bot/RAG]
```

## Current worker topology (Stage5/Stage6)
- Core ingestion: `TelegramListenerService` → `BatchWorkerService` → `ArchiveImportWorkerService`
- Media enrichment: `ArchiveMediaProcessorService`, `VoiceParalinguisticsWorkerService`
- Stage5 extraction (session-first): `AnalysisWorkerService` + `ExpensivePassResolver`
- Stage5 summaries (slice-based): `DialogSummaryWorkerService` (+ historical retrieval embeddings)
- Stage6 refinement: `ContinuousRefinementWorkerService` (disabled by default)
- Stage6 chat/dossier runtime: `TelegramBotHostedService` + retrieval/embedding workers

### Stage5 cursors (current)
- Session analysis queue: `chat_sessions.is_analyzed=false` with idle gate `Analysis.SessionAnalysisMinIdleMinutes`.
- Session chunk checkpoints: `analysis_state` keys `stage5:session_chunk_checkpoint:{chatId}:{sessionIndex}`.
- Slice summary checkpoints: `analysis_state` keys `stage5:summary:session:{chatId}:{sessionIndex}`.
- Legacy summary message-watermarks (`stage5:summary_watermark`, `stage5:summary_extraction_watermark`) are no longer used by runtime.

## Documentation map
- Runtime source-of-truth: `README.md` and `docs/stage5-extraction-algorithm.txt`.
- Product/roadmap context: `CODEX_BACKLOG.md`, `docs/stage5_product_backlog.md`, `docs/backlog/stage5_hybrid_evolution.md`.
- Historical artifacts: `docs/archive/`.

## Build & Run locally
```bash
docker compose build app
docker compose up -d
```

## Faster builds on VPN
- `deploy/Dockerfile` is optimized for NuGet cache reuse (project files are copied before source code).
- BuildKit cache stores downloaded packages between builds (`/root/.nuget/packages` cache mount).
- `docker-compose.yml` forwards `HTTP_PROXY`/`HTTPS_PROXY`/`NO_PROXY` build args; keep `api.nuget.org,nuget.org,globalcdn.nuget.org` in `NO_PROXY`.
- If build is still slow, keep Docker build cache intact and avoid `docker builder prune -a`.

## Archive import (Stage 4)
1. Put Telegram Desktop export JSON in `ArchiveImport:SourcePath`.
2. Set `ArchiveImport:Enabled=true`.
3. Configure `ArchiveImport:MediaBasePath` to exported media root.
4. Set `ArchiveImport:ConfirmProcessing=false` and start app once to get cost estimate.
5. After checking estimate, set `ArchiveImport:ConfirmProcessing=true` to start actual import.

Importer is resumable via `archive_import_runs.last_message_index`.
Archive media processing can run independently (`ArchiveImport:MediaProcessingEnabled`).

## Burst photo guard
To control token spend for chat "file dumps":
- `Media:EnablePhotoBurstGuard`
- `Media:PhotoBurstThreshold`
- `Media:PhotoBurstKeepCount`
- `Media:PhotoBurstWindowSeconds`

When threshold is exceeded, only the first `PhotoBurstKeepCount` photos are sent to LLM, others are marked as skipped by policy.

## Stages
- [x] 0: Infrastructure + VPS
- [x] 1: Telegram Listener -> Redis
- [x] 2: Batch Worker -> PostgreSQL
- [x] 3: Media processing
- [x] 4: Archive import
- [x] 5: Dossier/Intelligence pipeline (Stage5 v10)
- [x] 6: Telegram Bot chat mode + RAG
- [ ] 7: Cron notifications
- [ ] 8: Web UI
- [ ] 9: Inline mode


## Archive Recovery Script
Run deploy/archive-repair.sh from repo root on server to:
- remove duplicate messages,
- normalize export placeholders,
- show archive media status and reasons,
- optionally re-queue not-found media with --retry-not-found.

# Monitoring (Grafana + Prometheus)

Bring up monitoring stack together with app:

```bash
docker compose up -d prometheus grafana postgres-exporter redis-exporter app
```

Default local endpoints:
- Grafana: http://127.0.0.1:3000
- Prometheus: http://127.0.0.1:9090

Configure budgets in `.env`:
- `LLM_BUDGET_HOURLY_USD`
- `LLM_BUDGET_DAILY_USD`

Provisioned dashboards:
- `Stage5 Ops` (drilldown link to `LLM Cost`)
- `LLM Cost`

Provisioned alert rules (UI-only):
- `LLMCostHourlyExceeded`
- `LLMCostDailyExceeded`

Sanity-check SQL:

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -f scripts/llm-cost-sanity.sql
```
