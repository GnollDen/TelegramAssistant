# Telegram Personal Assistant

Personal assistant with psychological analysis capabilities, built around Telegram.

## Architecture

```
TgAssistant.Host          — Entry point, DI, hosted services
TgAssistant.Core          — Domain models, interfaces, configuration (no dependencies)
TgAssistant.Infrastructure — PostgreSQL, Redis implementations
TgAssistant.Telegram      — WTelegramClient listener + Telegram.Bot interaction
TgAssistant.Processing    — Batch worker, Gemini media processing, archive import
TgAssistant.Intelligence  — Claude API, dossier builder, prompt management
TgAssistant.Web           — REST API + UI for dossier viewer
```

## Pipeline

```
[Telegram MTProto] → [Redis Stream] → [Batch Worker] → [PostgreSQL]
                                           ↓
                                    [Gemini Flash Lite]
                                    (media → JSON)
```

## Prerequisites

- .NET 8 SDK
- PostgreSQL 16
- Redis 7
- ffmpeg (for audio conversion)

## Setup

1. Copy `appsettings.json` → `appsettings.Development.json`
2. Fill in Telegram API credentials (from https://my.telegram.org)
3. Fill in database connection string
4. Fill in OpenRouter API keys
5. Run: `dotnet run --project src/TgAssistant.Host`

## Development Stages

- [x] Stage 0: Solution skeleton + domain models
- [ ] Stage 1: Telegram Listener → Redis
- [ ] Stage 2: Batch Worker → PostgreSQL
- [ ] Stage 3: Gemini media processing
- [ ] Stage 4: Archive import + preprocessing
- [ ] Stage 5: Claude dossier building
- [ ] Stage 6: Telegram Bot chat mode
- [ ] Stage 7: Cron notifications
- [ ] Stage 8: Web UI
- [ ] Stage 9: Inline mode
