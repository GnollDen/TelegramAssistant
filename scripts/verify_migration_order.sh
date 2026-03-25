#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATIONS_DIR="${1:-$ROOT_DIR/src/TgAssistant.Infrastructure/Database/Migrations}"
ALLOWLIST_FILE="${2:-$ROOT_DIR/scripts/migration_prefix_collisions.allowlist}"

if [[ ! -d "$MIGRATIONS_DIR" ]]; then
  echo "ERROR: migrations directory not found: $MIGRATIONS_DIR" >&2
  exit 1
fi

mapfile -t files < <(find "$MIGRATIONS_DIR" -maxdepth 1 -type f -name '*.sql' -printf '%f\n' | LC_ALL=C sort)
if [[ ${#files[@]} -eq 0 ]]; then
  echo "ERROR: no migration files found in $MIGRATIONS_DIR" >&2
  exit 1
fi

echo "Migration order check: $MIGRATIONS_DIR"
echo "Authoritative runtime order: lexicographic filename order (StringComparer.Ordinal)"
for i in "${!files[@]}"; do
  printf 'ORDER %03d %s\n' "$((i + 1))" "${files[i]}"
done

name_regex='^[0-9]{4}_[a-z0-9][a-z0-9_]*\.sql$'
declare -A by_prefix
violations=0

for f in "${files[@]}"; do
  if [[ ! "$f" =~ $name_regex ]]; then
    echo "ERROR: migration filename does not match required format 'NNNN_snake_case.sql': $f" >&2
    violations=1
    continue
  fi
  prefix="${f%%_*}"
  by_prefix["$prefix"]+="${by_prefix[$prefix]:+,}$f"
done

if [[ $violations -ne 0 ]]; then
  echo "RESULT FAIL" >&2
  exit 1
fi

declare -A allowed
if [[ -f "$ALLOWLIST_FILE" ]]; then
  while IFS= read -r line; do
    line="${line%%#*}"
    line="$(echo "$line" | xargs)"
    [[ -z "$line" ]] && continue
    key="${line%%:*}"
    value="${line#*:}"
    allowed["$key"]="$value"
  done < "$ALLOWLIST_FILE"
fi

for prefix in "${!by_prefix[@]}"; do
  IFS=',' read -r -a bucket <<< "${by_prefix[$prefix]}"
  if [[ ${#bucket[@]} -le 1 ]]; then
    continue
  fi

  actual="$(printf '%s\n' "${bucket[@]}" | LC_ALL=C sort | paste -sd, -)"
  allowed_bucket="${allowed[$prefix]:-}"
  if [[ -z "$allowed_bucket" ]]; then
    echo "ERROR: ambiguous migration prefix '$prefix' has no allowlist entry: $actual" >&2
    violations=1
    continue
  fi

  normalized_allowed="$(printf '%s\n' ${allowed_bucket//,/ } | LC_ALL=C sort | paste -sd, -)"
  if [[ "$actual" != "$normalized_allowed" ]]; then
    echo "ERROR: migration prefix '$prefix' collision set changed." >&2
    echo "  allowed: $normalized_allowed" >&2
    echo "  actual : $actual" >&2
    violations=1
  fi
done

for prefix in "${!allowed[@]}"; do
  if [[ -z "${by_prefix[$prefix]:-}" ]]; then
    echo "ERROR: stale allowlist entry for prefix '$prefix'" >&2
    violations=1
  fi
done

if [[ $violations -ne 0 ]]; then
  echo "RESULT FAIL" >&2
  exit 1
fi

echo "RESULT PASS total_migrations=${#files[@]}"
