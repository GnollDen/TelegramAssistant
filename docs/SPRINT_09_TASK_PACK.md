# Sprint 09 Task Pack

## Name

Bot Command Layer

## Goal

Integrate the existing engines into a usable Telegram bot command surface so the system can serve:

- `/state`
- `/next`
- `/draft`
- `/review`
- `/gaps`
- `/answer`
- `/timeline`
- `/offline`

This sprint should expose real product capabilities through the bot without expanding into web UI work.

## Why This Sprint

The product now has:

- clarification orchestration
- periodization
- current state
- profiles
- strategy
- draft generation
- draft review

The next critical step is command-level integration.

Without it:

- the engines exist, but the product is still not operable through the main user interface
- bot workflows remain theoretical

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_09_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_09_TASK_PACK.md)
4. [SPRINT_09_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_09_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- existing bot command handlers
- current engine service interfaces
- current runtime wiring

## Scope

In scope:

- bot command integration
- command-to-engine wiring
- compact response formatting
- guided clarification answer flow
- offline command integration to existing pipeline
- basic help/menu discoverability if needed
- verification paths

Out of scope:

- web UI
- major bot visual polish
- reminder scheduler expansion
- full conversational free-form agent mode
- GraphRAG

## Product Rules To Respect

Bot interaction should remain:

- concise
- operational
- guided where helpful

The command surface should support at minimum:

- `/state`
- `/next`
- `/draft`
- `/review`
- `/gaps`
- `/answer`
- `/timeline`
- `/offline`

Formatting expectations:

- `/state`: state, status, signals, risk, next, do not
- `/draft`: main, alt 1, alt 2, short why
- `/review`: assessment, risks, safer, more natural
- `/gaps`: one question at a time, why it matters, options, answer path
- `/timeline`: current period plus a few prior periods

When uncertainty is high:

- commands should reflect it briefly
- not overstate confidence

## Required Deliverables

### 1. Command Wiring

Wire the existing engines into bot commands for:

- state
- next
- draft
- review
- gaps
- answer
- timeline
- offline

### 2. Response Formatting

Implement compact response builders so outputs are usable in Telegram and follow the product formatting rules.

### 3. Clarification Flow

Support:

- `/gaps` for guided question delivery
- `/answer` for answering/continuing the flow

The system should handle:

- current top question
- why it matters
- basic options or free input path

### 4. Draft and Review Flows

Support:

- `/draft` using strategy/context
- `/review` for free-form text or latest relevant draft

### 5. Timeline and State Flows

Support:

- `/state` from current state engine
- `/next` from strategy engine
- `/timeline` from periodization outputs

### 6. Offline Command Integration

Support `/offline` as an entry into the existing offline pipeline.

It is acceptable in this sprint to keep offline command behavior operational but simple.

### 7. Verification Path

Add a verification path such as:

- `--bot-smoke`

That proves:

- command handlers resolve
- each main command can run end-to-end at least in seeded/test mode
- outputs are non-empty and formatted
- clarification answer flow works

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. bot smoke success
4. `/state` path works
5. `/next` path works
6. `/draft` path works
7. `/review` path works
8. `/gaps` and `/answer` path works
9. `/timeline` path works

## Definition of Done

Sprint 9 is complete only if:

1. the main bot command surface is operational
2. commands are backed by real engines, not placeholders
3. outputs are concise and usable in Telegram
4. the product is materially more usable through the bot

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how bot command integration now works
4. what verification was run
5. remaining limitations before Sprint 10
