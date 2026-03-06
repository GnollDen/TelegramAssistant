#!/bin/bash
# ============================================================
# Deploy / Update TelegramAssistant
# Run from project root on VPS as tgassistant user
# ============================================================

set -e

echo "=== Pulling latest code ==="
git pull

echo "=== Building and restarting ==="
docker compose build app
docker compose up -d

echo "=== Status ==="
docker compose ps

echo "=== Recent logs ==="
docker compose logs --tail=20 app
