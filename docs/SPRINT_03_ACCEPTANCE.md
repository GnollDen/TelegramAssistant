# Sprint 03 Acceptance

## Purpose

Validate that Sprint 3 produced a real periodization MVP rather than only period-shaped records.

## Acceptance Checklist

## Boundary Detection

- candidate boundaries are produced from actual history
- boundaries are not based on pauses only
- dynamic/event-based boundaries are present where appropriate

## Period Assembly

- multiple periods can be created from one meaningful history
- periods contain usable summaries
- periods contain key fields, not just timestamps and labels

## Transition Logic

- transitions are created between periods
- unresolved transitions are represented when cause is unclear
- unresolved transitions do not silently invent causes

## Evidence Packs

- evidence packs contain real linked evidence
- evidence is drawn from canonical sources
- evidence is not empty filler

## Merge/Split Proposals

- likely merge/split situations can be surfaced
- proposals are reviewable
- proposals are not auto-applied

## Review Priority

- period review priority is computed
- low-confidence/high-impact/conflict cases rise appropriately

## Verification

- build passes
- startup passes
- at least one real or seeded history produces a non-trivial timeline
- at least one unresolved transition is demonstrated
- at least one proposal path is demonstrated

## Hold Conditions

Hold Sprint 3 if any of these are true:

- periods are basically just sessions renamed
- transitions are not explicit
- unresolved transitions are missing
- evidence packs are hollow
- merge/split logic does not exist

## Pass Condition

Sprint 3 passes if:

- timeline becomes a real usable narrative layer
- historical interpretation can support later state logic
