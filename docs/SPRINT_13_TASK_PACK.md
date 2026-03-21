# Sprint 13 Task Pack

## Name

Search + Saved Views + Dossier Polish

## Goal

Add the first usable retrieval and inspection layer so the product can support:

- search across key analytical artifacts
- saved operational views
- a clearer dossier surface for confirmed items, hypotheses, and conflicts

This sprint should improve inspection and retrieval, not redesign the whole web app.

## Why This Sprint

The product now has:

- web read pages
- web review/edit flows
- inbox/history/activity layer

The next critical step is retrieval.

Without it:

- the user must navigate manually through pages
- finding specific artifacts is slow
- dossier remains underpowered as an inspection surface

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_13_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_13_TASK_PACK.md)
4. [SPRINT_13_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_13_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- current web read layer
- current ops/inbox/history layer
- existing fact/hypothesis/conflict models

## Scope

In scope:

- global search page/route
- keyword and filter-based retrieval across key artifacts
- saved views
- clearer dossier page
- better grouping of confirmed / hypotheses / conflicts
- verification paths

Out of scope:

- advanced semantic search
- full-text indexing optimization
- graph search
- heavy analytics
- GraphRAG

## Product Rules To Respect

Search should be:

- practical
- filterable
- case-scoped

Saved views should support operational needs such as:

- blocking
- current period
- conflicts

Dossier should clearly separate:

- confirmed items
- hypotheses
- conflicts

This sprint is about usability and retrieval, not about building a search engine platform.

## Required Deliverables

### 1. Search Surface

Add a search page/route that can retrieve across at least:

- inbox items
- clarifications
- periods
- conflicts
- profiles or traits where practical
- strategy/draft artifacts where practical

Search should support:

- text query
- basic filters

### 2. Saved Views

Add saved operational views for at least:

- blocking
- current period
- conflicts

These can be implemented simply, but should be reusable and directly accessible in web.

### 3. Dossier Surface

Add or improve dossier route/page so it clearly shows:

- confirmed
- hypotheses
- conflicts

It should be usable as an inspection surface, even if editing remains basic.

### 4. Retrieval Model

Use a practical read model that supports:

- case-scoped retrieval
- stable filters
- linking from results to underlying object pages or trails

### 5. Verification Path

Add a verification path such as:

- `--search-smoke`

That proves:

- search route resolves
- seeded searchable artifacts appear
- filters affect results
- saved views resolve
- dossier route shows confirmed/hypotheses/conflicts sections

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. search smoke success
4. search route renders
5. results appear for seeded data
6. saved views work
7. dossier route renders with grouped sections

## Definition of Done

Sprint 13 is complete only if:

1. the user can retrieve key artifacts without manual page-by-page navigation
2. saved views support operational work
3. dossier is materially more usable as an inspection surface

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how search/saved views/dossier now work
4. what verification was run
5. remaining limitations before Sprint 14
