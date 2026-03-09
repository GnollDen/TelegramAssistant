#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

docker compose exec -T postgres psql -U tgassistant -d tgassistant -f /dev/stdin <<'SQL'
\pset pager off
\x off
\timing off
\i scripts/merge-review.sql
SQL
