---
name: tg-project-executor
description: Use for the TelegramAssistant project when implementing sprint tasks, modifying architecture, or extending the communication-intelligence platform. Read product decision docs first, preserve truth layers and reviewability, work in bounded sprint/task-pack form, and always verify builds and changed behavior.
---

# TG Project Executor

Use this skill when implementing work in the TelegramAssistant project.

## Read Order

Always read these first:

1. `docs/PRODUCT_DECISIONS.md`
2. `docs/CODEX_TASK_PACKS.md`
3. current sprint task pack, for example `docs/SPRINT_01_TASK_PACK.md`
4. current sprint acceptance file, for example `docs/SPRINT_01_ACCEPTANCE.md`

Then inspect the relevant code before editing.

## Project Identity

This project is:

- a universal communication intelligence core
- with a relationship-first domain module
- with strong provenance, review, and history requirements

Do not reduce it to:

- a generic chatbot
- a pure dossier extractor
- a pure romance-advice system

## Non-Negotiable Rules

- Keep facts, hypotheses, overrides, and review history separate.
- Preserve provenance and truth layers.
- Preserve reviewability and auditability.
- Keep analysis separate from action recommendations.
- Keep style adaptation separate from state interpretation.
- Do not silently auto-apply high-risk interpretations.
- Prefer typed tables for core entities.
- Use JSON only for small auxiliary payloads.

## Architecture Rules

Respect these layers:

- platform: ingestion, archive import, storage, extraction foundations
- domain: periods, clarifications, profiles, state, strategy, drafts
- operator: bot, web, review console

When extending the system:

- prefer additive changes
- avoid rewriting stable ingestion unless required
- keep UI concerns out of persistence/domain code

## Sprint Execution Rules

For each sprint:

- inspect current code first
- make only scoped changes required by the task pack
- do not implement future sprint logic unless needed for a clean extension point
- wire the minimum useful foundation for the next sprint

## Verification Rules

Always verify:

- build
- startup or initialization path if touched
- create/read or behavior path for what was added

If migrations change:

- report migration scope clearly

If verification is partial:

- say exactly what was not verified

## Reporting Contract

Final report should always include:

1. what changed
2. files changed
3. what was verified
4. known risks or limitations
5. suggested next task

## When Unsure

If a choice is ambiguous:

- choose the path that preserves architecture, provenance, and reviewability
- prefer clean extension points over shortcuts
- avoid cleverness that hides behavior from later review
