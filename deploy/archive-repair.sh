#!/usr/bin/env bash
set -euo pipefail

# Safe recovery helper for archive import runs.
# Usage:
#   ./deploy/archive-repair.sh
#   ./deploy/archive-repair.sh --retry-not-found
#   ./deploy/archive-repair.sh --source-path "/opt/tgassistant/TelegramAssistant/archive/ChatExport_2026-03-07/result.json" --retry-not-found

SOURCE_PATH=""
RETRY_NOT_FOUND="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source-path)
      SOURCE_PATH="$2"
      shift 2
      ;;
    --retry-not-found)
      RETRY_NOT_FOUND="true"
      shift
      ;;
    *)
      echo "Unknown arg: $1" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "docker-compose.yml" ]]; then
  echo "Run this script from project root (docker-compose.yml not found)." >&2
  exit 1
fi

PSQL=(docker compose exec -T postgres psql -U tgassistant -d tgassistant -v ON_ERROR_STOP=1)

echo "== Archive runs (latest 5) =="
if [[ -n "$SOURCE_PATH" ]]; then
  "${PSQL[@]}" -c "
    SELECT id, status, source_path, imported_messages, queued_media, total_messages, total_media, estimated_cost_usd, updated_at
    FROM archive_import_runs
    WHERE source_path = '$SOURCE_PATH'
    ORDER BY created_at DESC
    LIMIT 5;"
else
  "${PSQL[@]}" -c "
    SELECT id, status, source_path, imported_messages, queued_media, total_messages, total_media, estimated_cost_usd, updated_at
    FROM archive_import_runs
    ORDER BY created_at DESC
    LIMIT 5;"
fi

echo

echo "== Deduplicate messages by (source, chat_id, telegram_message_id) =="
"${PSQL[@]}" -c "
WITH d AS (
  SELECT id,
         ROW_NUMBER() OVER (PARTITION BY source, chat_id, telegram_message_id ORDER BY id) AS rn
  FROM messages
)
DELETE FROM messages m
USING d
WHERE m.id = d.id
  AND d.rn > 1;"

echo

echo "== Normalize Telegram export placeholders =="
"${PSQL[@]}" -c "
UPDATE messages
SET processing_status = 3,
    media_description = 'Skipped: Telegram export placeholder (file not downloaded)'
WHERE source = 1
  AND media_type <> 0
  AND media_path ILIKE '%(File exceeds maximum size.%';"

echo

echo "== Media status (archive only) =="
"${PSQL[@]}" -c "
SELECT processing_status, COUNT(*)
FROM messages
WHERE source = 1 AND media_type <> 0
GROUP BY processing_status
ORDER BY processing_status;"

echo

echo "== PendingReview reasons (top 20) =="
"${PSQL[@]}" -c "
SELECT COALESCE(media_description, 'NULL') AS reason, COUNT(*)
FROM messages
WHERE source = 1 AND media_type <> 0 AND processing_status = 3
GROUP BY 1
ORDER BY 2 DESC
LIMIT 20;"

if [[ "$RETRY_NOT_FOUND" == "true" ]]; then
  echo
  echo "== Re-queue not_found media =="
  "${PSQL[@]}" -c "
  UPDATE messages
  SET processing_status = 0,
      processed_at = NULL,
      media_description = NULL
  WHERE source = 1
    AND media_type <> 0
    AND processing_status = 3
    AND media_description = 'Unrecognized: Media file not found';"

  echo
  echo "== Media status after re-queue =="
  "${PSQL[@]}" -c "
  SELECT processing_status, COUNT(*)
  FROM messages
  WHERE source = 1 AND media_type <> 0
  GROUP BY processing_status
  ORDER BY processing_status;"
fi

echo

echo "Done."
