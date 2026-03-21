# Sprint 01 Acceptance

## Purpose

Use this checklist after Codex completes Sprint 1.

The sprint passes only if the foundation is actually usable for the next sprint.

## Acceptance Checklist

## Build and Runtime

- solution builds successfully
- application starts successfully
- new migration(s) apply successfully
- existing archive import path still starts
- existing realtime path is not obviously broken by startup wiring

## Schema Coverage

Confirm these object families exist in code and storage:

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

## Repository Coverage

Confirm there are usable repository methods for:

- create
- read by id
- basic query by relevant grouping

At minimum for:

- periods
- clarification questions
- state snapshots
- offline events

## Persistence Smoke

Verify that it is possible to:

1. create a period
2. create a clarification question linked to a period
3. create a state snapshot
4. create an offline event
5. read all four back successfully

## Architectural Checks

- new objects are not shoved into generic JSON blobs when they should be first-class tables
- hypotheses remain separate from facts
- review/history concerns are not discarded
- provenance fields exist where expected
- host wiring is explicit, not hidden in hacks

## Regression Checks

- no obvious breakage of current project startup
- no removal or accidental modification of existing ingestion services
- no accidental destructive migration behavior

## Human Review Questions

Ask these after reading Codex’s report:

1. Did Sprint 1 create a clean domain foundation, or just add tables?
2. Can Sprint 2 start clarification logic without redoing schema?
3. Can Sprint 3 start period/state logic without redesigning persistence?
4. Did Codex preserve the existing project shape?
5. Are there any architectural shortcuts that will cause pain next sprint?

## Ship Decision

Sprint 1 is accepted if:

- all acceptance checklist items pass
- no critical regression is visible
- no major schema redesign is immediately required

If not, hold Sprint 2 and fix Sprint 1 first.
