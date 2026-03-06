# Telegram Personal Assistant

## Pipeline
```
[Telegram MTProto push] → [Redis Stream] → [Batch Worker] → [PostgreSQL]
                                                 ↓
                                          [Gemini Flash Lite]
```

## Build & Run locally
```bash
docker compose build app
docker compose up -d
```

## Stages
- [x] 0: Infrastructure + VPS
- [x] 1: Telegram Listener → Redis  
- [x] 2: Batch Worker → PostgreSQL
- [ ] 3: Gemini media processing
- [ ] 4: Archive import
- [ ] 5: Claude dossier building
- [ ] 6: Telegram Bot chat mode
- [ ] 7: Cron notifications
- [ ] 8: Web UI
- [ ] 9: Inline mode
