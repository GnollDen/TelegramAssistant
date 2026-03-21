---
name: tg-project-executor
description: Use for this TelegramAssistant project when implementing sprint tasks, reviewing architecture decisions, or modifying the communication-intelligence platform. Read the project decision docs first, preserve truth layers and reviewability, and execute work in bounded sprint/task-pack form with verification.
---

# TG Project Executor

Use this skill when working on the TelegramAssistant project.

## Read Order

Read these first:

1. `docs/PRODUCT_DECISIONS.md`
2. `docs/CODEX_TASK_PACKS.md`
3. current sprint task pack, for example `docs/SPRINT_01_TASK_PACK.md`
4. current sprint acceptance file, for example `docs/SPRINT_01_ACCEPTANCE.md`

Then inspect relevant code before editing.

## Project Identity

This project is:

- a universal communication intelligence core
- with a relationship-first domain module
- with strong provenance, review, and history requirements

Do not collapse it into:

- a simple chat bot
- a generic dossier extractor
- a pure romance advice tool

## Non-Negotiable Rules

- Keep facts, hypotheses, overrides, and review history separate.
- Preserve provenance and truth layers.
- Preserve reviewability and auditability.
- Keep analysis separate from action recommendations.
- Keep user style adaptation separate from state interpretation.
- Do not silently auto-apply high-risk interpretations.
- Prefer typed tables for core entities.
- Use JSON only for small auxiliary payloads.

## Architecture Rules

Respect these boundaries:

- platform layer: ingestion, archive import, storage, extraction foundations
- domain layer: periods, clarifications, profiles, state, strategy, drafts
- operator layer: bot, web, review console

When extending the system:

- prefer additive changes
- avoid rewriting stable core ingestion unless required
- avoid mixing UI concerns into persistence/domain code

## Sprint Execution Rules

For each sprint:

- inspect current code first
- make only scoped changes required by the task pack
- do not preemptively build future sprints unless needed for a clean extension point
- wire the minimum useful foundation for the next sprint

## Verification Rules

Always verify:

- build
- startup or initialization path if touched
- core create/read or behavior path for what was added

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
- avoid cleverness that hides system behavior from later review
