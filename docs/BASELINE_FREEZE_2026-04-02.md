# Baseline Freeze - 2026-04-02

## Purpose
This document freezes the **pre AI-first cleanup baseline** of `TelegramAssistant` as an archival reference point.

Freeze date: **2026-04-02 (UTC)**

## What Exactly Is Archived
The archived baseline includes the current repository state on `master` at the freeze commit (see "Safety Status") with all existing implementation and documentation, including:
- current Stage5 pipeline implementation,
- current Stage6/case-management implementation,
- current MCP server implementation,
- current Web placeholder/UI direction,
- accumulated historical sprint/task-pack/planning/documentation layers,
- current DB schema lineage represented by existing migrations.

## Product Direction Statement
As of this freeze point:
- the current Stage5/Stage6/Web/MCP implementation is **no longer the main product direction**,
- old sprint/task-pack/doc sprawl is **historical/reference only**,
- the active direction is **AI-first person intelligence architecture**.

## Safety Status

### Freeze References
- archive branch: `archive/pre-ai-first-baseline-2026-04-02`
- archive tag (annotated): `archive-baseline-2026-04-02`

### Current HEAD Snapshot (Before Freeze Commit)
- source `master` HEAD before adding this freeze note: `ff31068ebddc175acb376d7440386941bf3941ae`

### Known Dirty Worktree State (Before Cleanup Track)
Before this freeze track started, worktree had unrelated local modifications:
- modified: `.env.example`
- modified: `docker-compose.yml`
- untracked: `.env.backup.before-5050-20260330-164356`
- untracked: `backups/`

These were intentionally **not cleaned** in Sprint A.

## Expected Future Cleanup Surface (Post-Freeze)
The following areas are expected to be touched in cleanup sprints after this freeze:

### DB-focused surface
Likely cleanup targets include legacy/transition schema groups (exact DDL to be decided in Sprint B/C):
- stage/domain workflow tables (e.g. `domain_*`, `stage6_*`),
- operational coordination/guard tables (e.g. `ops_*`),
- Stage5 extraction/support tables (e.g. `message_extractions`, `chat_dialog_summaries`, related artifacts),
- migration lineage extensions via **new** migration files only (no edits to existing migration files).

### Repo/config/docs surface
Likely cleanup targets include:
- legacy planning/task-pack documents under `docs/` and `mcp/` historical references,
- obsolete/transition configuration fragments across host/deploy files,
- repository hygiene updates (indexing, archive placement, docs routing to AI-first architecture).

## Cleanup Safety Gate
Destructive cleanup actions (DB-level removals, mass repo deletions, broad config purges) must occur **only after** this frozen baseline point.

Sprint A explicitly does **not** perform:
- broad deletes,
- DB cleanup,
- repo mass cleanup.

## Handoff Order (Execution)
- Sprint B: DB Cleanup Phase 1
- Sprint C: DB Cleanup Phase 2
- Sprint D: Repo/Config Hygiene
- Sprint E: Verification Gate

## Status
This freeze note is the authoritative boundary between:
- "old world" (archived baseline), and
- AI-first cleanup/rebuild execution.
