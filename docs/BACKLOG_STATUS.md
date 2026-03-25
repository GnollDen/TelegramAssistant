# Backlog Status

## Status Note

This document is historical status framing, not the authoritative sprint-start planning source.

For current implementation planning and sprint order, use:
- [planning/README.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\README.md)
- [planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md)

This file may overstate readiness relative to the active planning pack and should not be treated as the primary execution authority.

## Current Reality

The project is no longer just a Stage5/Stage6 bot pipeline.

The implemented product now includes:

- Stage5 ingestion and extraction
- clarification orchestration
- periodization
- current state
- profiles
- strategy
- draft generation
- draft review
- bot command layer
- web read layer
- web review/edit layer
- inbox/history/activity layer
- search/saved views/dossier layer
- graph/network layer
- outcome/learning layer
- budget guardrails and eval harness
- external archive ingestion
- competing relationship context

## Accepted Major Milestones

Accepted implementation path:

- Sprint 1 foundation
- Sprint 1.1 repair
- Sprint 2 clarification orchestration
- Sprint 3 periodization MVP plus revalidation
- Sprint 4 current state engine
- Sprint 5 profiles engine
- Sprint 6 strategy engine
- Sprint 7 draft engine
- Sprint 8 draft review engine
- Sprint 9 bot command layer
- Sprint 10 web read layer
- Sprint 11 web review/edit layer
- Sprint 12 inbox/history/activity layer
- Sprint 13 search/saved views/dossier polish
- Sprint 14 graph/network layer
- Sprint 15 outcome/learning layer
- Sprint 16 budget guardrails and eval harness
- Sprint 17 external archive ingestion
- Sprint 18 competing relationship context

## What Is Still Core Backlog

The main architecture loop is now largely complete.

Remaining backlog is mostly polish, hardening, and controlled productization:

- A/B experiment layer on top of eval harness
- production-facing budget/eval visibility polish
- outcome UI polish
- richer review/edit coverage beyond the current MVP edit paths
- custom reminders / scheduling
- production hardening and deployment cleanup
- docs cleanup and archive hygiene

## What Is No Longer The Main Backlog

These areas are no longer "missing core product":

- timeline/state/profile/strategy foundations
- draft/review foundations
- bot command integration
- basic web inspection and review
- graph/network baseline
- external archive ingestion baseline

They may still have polish backlog, but the core capabilities exist.

## Current Recommended Focus

If the goal is practical product readiness, the next focus should be:

1. polish and stabilize operational surfaces
2. strengthen eval/A-B workflow
3. harden deployment and monitoring
4. improve UX around already-built capabilities

## Notes On Older Docs

Two files are still useful, but no longer reflect the whole project by themselves:

- [README.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\README.md)
- [CODEX_BACKLOG.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\CODEX_BACKLOG.md)

They should be read as:

- deployment/runtime reference
- Stage5/media-specific backlog context

Not:

- the full current backlog
- the full current architecture map
