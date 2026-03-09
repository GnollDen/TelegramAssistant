#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

echo "[1/4] Building application image..."
docker compose build app

echo "[2/4] Recreating app container..."
docker compose up -d --force-recreate app

echo "[3/4] Last startup logs..."
docker compose logs --tail=120 app

echo "[4/4] Stage5 quick status..."
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "
SELECT
  (SELECT COUNT(*) FROM messages WHERE processing_status=1) AS processed_messages,
  (SELECT COUNT(*) FROM message_extractions) AS extracted_messages,
  (SELECT COUNT(*) FROM message_extractions WHERE needs_expensive) AS expensive_backlog,
  (SELECT COUNT(*) FROM entity_merge_candidates WHERE status=0) AS merge_candidates_pending;"
