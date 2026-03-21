# Sprint 12 Task Pack

## Name

Inbox / History / Activity Layer

## Goal

Add the first usable operational control layer so the product can surface:

- prioritized inbox items
- review backlog
- recent changes
- activity history
- cross-links between objects and their change trail

This sprint should make the system operationally steerable, not just analytically readable.

## Why This Sprint

The product now has:

- read-first web views
- web review/edit flows
- persisted review events
- a growing set of analytical artifacts

The next critical step is operational visibility.

Without it:

- review remains scattered
- change history is hard to inspect
- backlog cannot be triaged efficiently

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_12_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_12_TASK_PACK.md)
4. [SPRINT_12_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_12_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- inbox item models
- review event models
- current web read layer
- current web review layer

## Scope

In scope:

- inbox page
- history/activity page
- recent changes section
- object-to-history cross-links
- basic filtering/grouping for operational use
- verification paths

Out of scope:

- advanced search tuning
- saved views polish
- heavy analytics dashboards
- graph activity visualization
- GraphRAG

## Product Rules To Respect

The operational layer should make it easy to see:

- what needs attention now
- what changed recently
- why a decision happened

Inbox should prefer grouping like:

- Blocking
- High Impact
- Everything Else

History should:

- show chronological change feed
- support filters by object type and event type
- allow jumping from event to object and from object to event trail

This sprint should stay practical and quiet.

## Required Deliverables

### 1. Inbox Surface

Add a usable inbox page or route that shows:

- inbox items
- priority
- blocking flag
- summary
- linked object
- status

Support at least lightweight grouping or filtering for:

- blocking
- high impact
- everything else

### 2. History / Activity Surface

Add a usable history page or route that shows:

- domain review events
- key recent actions
- object type
- action/event type
- timestamp
- summary

### 3. Cross-Linking

Support:

- open object from history
- open history trail from object where practical

At minimum, implement this for a meaningful subset of high-impact objects.

### 4. Recent Changes Visibility

Add a recent changes summary usable from dashboard or operational web flow.

### 5. Basic Filtering

Support simple operational filters for:

- object type
- status
- priority or blocking

No advanced search is required in this sprint.

### 6. Verification Path

Add a verification path such as:

- `--ops-web-smoke`

That proves:

- inbox route resolves
- history route resolves
- seeded inbox items render
- seeded history events render
- object/history cross-linking works at least for one important object type

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. ops web smoke success
4. inbox page renders
5. history page renders
6. seeded items appear
7. cross-link path works

## Definition of Done

Sprint 12 is complete only if:

1. the product now has a usable operational inbox/history layer
2. backlog and recent changes are inspectable in web
3. the system is ready for later search, dossier, and saved-view refinement

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how inbox/history/activity now works
4. what verification was run
5. remaining limitations before Sprint 13
