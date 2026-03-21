# Testing Expansion Note

## Purpose

Capture the next testing step beyond smoke checks.

## Current State

The project already has useful smoke paths for:

- foundation
- clarification
- periodization
- current state
- profiles

This is enough for active sprinting, but not enough for long-term regression control.

## Next Testing Layer

After Sprint 6, add a broader verification layer with:

- targeted integration tests
- small scenario-based tests
- focused rule tests for mapping/orchestration logic

## Priority Areas

Test expansion should first cover:

- clarification dependency resolution
- contradiction to conflict creation
- period boundary and unresolved transition behavior
- state label/confidence mapping
- profile trait stability and sensitive trait suppression
- strategy option/risk generation once Sprint 6 lands

## Test Philosophy

Prefer:

- compact deterministic scenario tests
- high-signal domain assertions
- stable seeded fixtures

Avoid:

- giant brittle end-to-end test suites too early

## Practical Goal

Keep smoke paths for deployment sanity.
Add targeted integration tests for domain logic that is now too important to verify only by manual sprint runs.
