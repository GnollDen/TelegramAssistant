# Telegram Personal Assistant

## Pipeline
```text
[Telegram MTProto push] -> [Redis Stream] -> [Batch Worker] -> [PostgreSQL]
                                                 |
                                          [OpenRouter Media]
```

## Build & Run locally
```bash
docker compose build app
docker compose up -d
```

## Archive import (Stage 4)
1. Put Telegram Desktop export JSON in `ArchiveImport:SourcePath`.
2. Set `ArchiveImport:Enabled=true`.
3. Configure `ArchiveImport:MediaBasePath` to exported media root.
4. Restart host service.

Importer is resumable via `archive_import_runs.last_message_index`.
Archive media processing runs separately with rate limiting, file size caps, and image resize/compression before LLM calls.

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
- [~] 4: Archive import (in progress)
- [ ] 5: Claude dossier building
- [ ] 6: Telegram Bot chat mode
- [ ] 7: Cron notifications
- [ ] 8: Web UI
- [ ] 9: Inline mode
