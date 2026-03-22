# Sprint 14 Task Pack

## Name

Graph / Network Layer

## Goal

Implement the first usable social graph and network reasoning layer so the product can represent:

- people
- groups
- places
- work contexts
- roles
- influence edges
- information flow
- links to periods and events

This sprint should make the surrounding social context operational, not just implied.

## Why This Sprint

The product now has:

- timeline
- current state
- profiles
- strategy
- drafts/review
- bot layer
- web read/review/retrieval layers

The next critical step is network reasoning.

Without it:

- third-party influence stays under-modeled
- surrounding social context remains fragmented
- later external archive ingestion has no strong target layer

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_14_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_14_TASK_PACK.md)
4. [SPRINT_14_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_14_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)
6. [EXTERNAL_ARCHIVE_INGESTION_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\EXTERNAL_ARCHIVE_INGESTION_POLICY.md)
7. [COMPETING_RELATIONSHIP_CONTEXT_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\COMPETING_RELATIONSHIP_CONTEXT_POLICY.md)

Also inspect:

- existing entity and relationship models
- merge logic
- web routes
- current graph/network placeholders if any

## Scope

In scope:

- graph/network service layer
- node role handling
- influence edge handling
- information flow handling
- period/event linkage
- network web surface
- verification paths

Out of scope:

- full graph visualization polish
- graph algorithms platform work
- GraphRAG retrieval
- external archive ingestion itself

## Product Rules To Respect

The graph should remain centered on the focal pair, but include surrounding context.

The graph should support nodes such as:

- person
- group
- place
- work_context

Roles should support the agreed base set, including:

- friend
- close_friend
- family
- ex_partner
- new_interest
- bridge
- conflict_source
- advisor
- group
- place
- work_context

Influence types should support at least:

- supportive
- complicating
- mediating
- informational
- stabilizing
- destabilizing

The graph must support:

- one primary role plus additional roles where needed
- influence hypotheses and low-confidence candidates
- information-flow direction where relevant
- period overrides or period linkage where relevant

The graph should enrich reasoning, not silently redefine the primary relationship model.

## Required Deliverables

### 1. Graph / Network Service Layer

Implement dedicated services for:

- node synthesis / network assembly
- edge and influence synthesis
- importance scoring
- period linkage

You may structure this into services such as:

- `NetworkGraphService`
- `NodeRoleResolver`
- `InfluenceEdgeBuilder`
- `InformationFlowBuilder`
- `NetworkScoringService`

### 2. Node Handling

Support nodes for at least:

- people
- groups
- places
- work contexts

Use existing canonical entities where possible.
Do not create unnecessary duplicate truth objects.

### 3. Role Handling

Support:

- primary role
- additional roles
- global role
- period-linked or period-overridden role context where practical

### 4. Influence and Information Flow

Support:

- influence type
- strength or confidence
- evidence linkage
- direction for information flow where relevant

Low-confidence influence should remain reviewable rather than silently authoritative.

### 5. Period / Event Linkage

Link network objects to:

- periods
- events
- clarifications

Where this materially helps interpretation.

### 6. Network Web Surface

Add a usable network page/route that shows:

- node list
- basic filters
- selected node detail
- linked periods/events where practical

Full graph visualization polish is not required, but the layer must be inspectable in web.

### 7. Verification Path

Add a verification path such as:

- `--network-smoke`

That proves:

- nodes can be assembled
- roles are visible
- influence edges are visible
- at least one information-flow edge exists where seeded
- network route/page renders

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. network smoke success
4. nodes render
5. roles render
6. influence edges render
7. network page renders

## Definition of Done

Sprint 14 is complete only if:

1. the product now has a usable social graph/network reasoning layer
2. surrounding social context is inspectable and linkable
3. the system is ready for later external archive ingestion and richer influence reasoning

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how graph/network now works
4. what verification was run
5. remaining limitations before Sprint 15
