# Sprint 01 Task Pack

## Name

Foundation Domain Layer

## Goal

Add the first full domain foundation for the new product layer:

- schema
- repositories
- host wiring
- smoke verification paths

This sprint should create the new data backbone without yet implementing the full product logic for:

- periodization
- clarification generation
- strategy engine
- web UI

## Outcome

After this sprint, the codebase should be ready for Sprint 2 and Sprint 3 work on:

- clarification orchestration
- timeline/period logic
- state engine

## Read First

Before editing code, read these files:

- [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
- [IMPLEMENTATION_BACKLOG.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\IMPLEMENTATION_BACKLOG.md)
- [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
- [AGENT_ROLES.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\AGENT_ROLES.md)

Also inspect current code in:

- [Program.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Host\Program.cs)
- [IRepositories.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Core\Interfaces\IRepositories.cs)
- [TgAssistantDbContext.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Infrastructure\Database\Ef\TgAssistantDbContext.cs)
- [DbRows.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Infrastructure\Database\Ef\DbRows.cs)
- existing migrations in [Migrations](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Infrastructure\Database\Migrations)

## Scope

In scope:

- new domain models
- DB rows
- migrations
- repository interfaces
- repository implementations
- DI registration
- minimal smoke-path verification support

Out of scope:

- period boundary detection logic
- clarification generation logic
- score-to-label mapping implementation
- bot UX expansion
- web UI implementation
- graph reasoning logic
- deep audio processing logic

## Mandatory Rules

- Do not break existing archive import, realtime ingestion, or current bot runtime.
- Do not collapse facts, hypotheses, overrides, and review history into one model.
- Preserve provenance and reviewability.
- Prefer typed tables for core domain objects; use JSON only for small auxiliary payloads.
- Keep new logic behind clean services and repositories, not ad hoc SQL spread across the codebase.
- If unsure between “quick hack” and “clean extension point”, choose the extension point.

## New Domain Objects To Add

Implement storage and model support for:

- periods
- transitions
- hypotheses
- clarification questions
- clarification answers
- offline events
- audio assets
- audio segments
- audio snippets
- state snapshots
- profile snapshots
- profile traits
- strategy records
- strategy options
- draft records
- draft outcomes
- inbox items
- conflict records
- dependency links

## Data Model Expectations

Use `PRODUCT_DECISIONS.md` as source of truth for required fields.

At minimum, the first implementation must support:

- typed identity
- timestamps
- provenance
- confidence/trust where applicable
- review-related fields where applicable
- links between objects

## Required Code Changes

### 1. Core Models

Add or extend domain models in appropriate `TgAssistant.Core.Models` namespaces/files for the new entities.

### 2. EF Rows / DbContext

Add EF row types and map them in:

- [DbRows.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Infrastructure\Database\Ef\DbRows.cs)
- [TgAssistantDbContext.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Infrastructure\Database\Ef\TgAssistantDbContext.cs)

### 3. Repository Interfaces

Add repository interfaces and method surfaces in:

- [IRepositories.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Core\Interfaces\IRepositories.cs)

Only add methods needed for:

- create
- update
- get by id
- basic query by case/period/status

Do not over-design full query API yet.

### 4. Repository Implementations

Add repository implementations in:

- `src/TgAssistant.Infrastructure/Database/*`

Implement minimal usable methods for each object family.

### 5. Migrations

Add new SQL migration file(s) under:

- [Migrations](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Infrastructure\Database\Migrations)

Migration style should match the current project style.

### 6. Host Wiring

Register new repositories/services in:

- [Program.cs](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\src\TgAssistant.Host\Program.cs)

### 7. Minimal Verification Helpers

Add the smallest reasonable verification path so it is possible to:

- insert a period
- insert a clarification question
- insert a state snapshot
- insert an offline event
- read them back

This can be:

- repository tests if test infra exists
- or a very small internal smoke path

Do not build temporary UI for this.

## Implementation Notes

Recommended structuring:

- keep one file per major model when practical
- avoid giant “misc” files for all new models
- keep repository code grouped by object family

For JSON usage:

- acceptable for small arrays like evidence refs or answer options
- not acceptable for replacing first-class tables like profile traits or strategy options

## Deliverables

By the end of Sprint 1, the repository should contain:

- new typed models
- new DB rows
- new migrations
- new repository interfaces
- new repository implementations
- host registration for the new layer
- verification evidence that the new persistence layer works

## Definition of Done

Sprint 1 is complete only if:

1. the solution builds successfully
2. migrations apply successfully
3. app startup still works
4. existing ingestion paths are not broken
5. new domain objects can be created and read
6. the code structure clearly supports Sprint 2 and Sprint 3

## Verification Required

Codex must verify and report:

- solution build result
- migration/app initialization result
- at least one create/read path for:
  - period
  - clarification question
  - state snapshot
  - offline event

## Expected Final Response From Codex

The final report for this sprint must include:

1. summary of changes
2. list of files changed
3. verification performed
4. known limitations
5. recommended Sprint 2 starting point

## Do Not Start Yet

Do not implement in this sprint:

- question generation
- period detection
- strategy scoring
- bot command flows
- web screens
- graph visualization
- advanced audio analytics
