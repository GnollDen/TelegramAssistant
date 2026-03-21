# Sprint 07 Task Pack

## Name

Draft Engine

## Goal

Implement the first usable draft engine so the system can turn strategy into:

- a main draft
- short alternative drafts
- style-aware refinement
- review-ready draft artifacts

This sprint should create a real draft layer without collapsing into generic message generation.

## Why This Sprint

The product now has:

- clarification orchestration
- periodization
- current state
- profiles
- strategy

The next critical layer is draft generation.

Without it:

- `/draft` remains unavailable
- strategy cannot become executable communication help
- style adaptation cannot yet influence output meaningfully

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_07_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_07_TASK_PACK.md)
4. [SPRINT_07_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_07_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- strategy outputs
- profile outputs
- current state outputs
- existing draft schema from Sprint 1 foundation

## Scope

In scope:

- draft synthesis services
- main + alternative draft generation
- style-aware draft shaping
- strategy-aligned draft generation
- notes/conflict handling for user-provided intent
- draft persistence
- verification paths

Out of scope:

- final bot `/draft` UX
- full draft review engine polish
- outcome learning optimization
- sent-message matching automation
- web draft UI
- GraphRAG

## Product Rules To Respect

Draft mode is a hybrid compose assistant.

It must support:

- generate from strategy/context
- refine with user notes
- review whether a draft fits the strategy

Drafts must optimize for:

- naturalness
- strategic fit
- sendability

Draft outputs should default to:

- `1` main draft
- `2` short alternatives

Optional user input may include:

- desired goal
- extra context
- speculative notes

If user notes conflict with strong evidence or strategy:

- show the conflict
- keep draft safer rather than silently following risky intent

Style should matter:

- natural user style
- but not at the cost of safety or strategic fit

## Required Deliverables

### 1. Draft Engine Service Layer

Implement dedicated services for:

- draft generation
- style shaping
- strategy fit checking
- draft packaging/persistence

You may structure this into services such as:

- `DraftEngine`
- `DraftGenerator`
- `DraftStyleAdapter`
- `DraftStrategyChecker`
- `DraftPackagingService`

### 2. Draft Generation

Generate drafts from:

- strategy record / primary option
- current state
- current period
- profiles
- recent message context
- optional user notes

Produce:

- one main draft
- two alternatives

### 3. Style Shaping

Use known style/profile signals for:

- message length
- directness
- warmth
- emotional density
- pacing

Do not overfit style if it conflicts with safety or strategy.

### 4. Conflict Handling

If user notes or requested tone conflict with strategy/safety:

- record the conflict
- keep the main draft safer
- optionally reflect the stronger/riskier variant only as an alternative if appropriate

### 5. Draft Packaging

Persist draft records with:

- main draft
- alt draft 1
- alt draft 2
- style notes
- confidence
- strategy link

### 6. Verification Path

Add a verification path such as:

- `--draft-smoke`

That proves:

- draft record created
- main + alternatives created
- strategy-linked draft generation works
- style shaping affects output
- conflict handling path exists

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. draft smoke success
4. draft record persisted
5. main and alternatives exist
6. style notes exist
7. conflict-handling path demonstrated

## Definition of Done

Sprint 7 is complete only if:

1. the system can turn strategy into sendable draft artifacts
2. drafts are not generic boilerplate
3. style is reflected without overriding safety
4. outputs are ready for later bot `/draft` and review integration

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how draft generation now works
4. what verification was run
5. remaining limitations before Sprint 8
