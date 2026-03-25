#!/usr/bin/env bash
set -euo pipefail

# Safe push wrapper:
# - blocks pushes that include .github/workflows/* by default
# - allows explicit override with ALLOW_WORKFLOW_PUSH=1

REMOTE="${1:-origin}"
BRANCH="${2:-$(git branch --show-current)}"

if [[ -z "${BRANCH}" ]]; then
  echo "Cannot detect current branch. Pass branch explicitly: scripts/safe_push.sh origin <branch>" >&2
  exit 1
fi

if ! git rev-parse --verify "${REMOTE}/${BRANCH}" >/dev/null 2>&1; then
  echo "Remote branch ${REMOTE}/${BRANCH} not found. Pushing as new branch." >&2
  RANGE="HEAD"
else
  RANGE="${REMOTE}/${BRANCH}..HEAD"
fi

CHANGED_FILES="$(git diff --name-only ${RANGE})"

if [[ -n "${CHANGED_FILES}" ]] && echo "${CHANGED_FILES}" | rg -q '^\.github/workflows/'; then
  if [[ "${ALLOW_WORKFLOW_PUSH:-0}" != "1" ]]; then
    echo "Push blocked: outgoing commits include .github/workflows/*" >&2
    echo "This commonly fails when PAT has no 'workflow' scope." >&2
    echo "Fix options:" >&2
    echo "1) split workflow changes into a separate push with proper token scope" >&2
    echo "2) push non-workflow commit batch only" >&2
    echo "3) if intentional, rerun with ALLOW_WORKFLOW_PUSH=1" >&2
    exit 2
  fi
fi

echo "Pushing ${BRANCH} -> ${REMOTE}/${BRANCH}"
if [[ "${DRY_RUN:-0}" == "1" ]]; then
  echo "DRY_RUN=1: skip actual push"
  exit 0
fi

exec git push "${REMOTE}" "${BRANCH}"
