# Timeline Revision Policy

## Purpose

Prevent timeline rebuilds from silently mutating historical interpretation.

## Current MVP State

Current periodization is additive-first and does not yet implement a full revision model.

This is acceptable for MVP, but future timeline rebuild/edit flows must follow explicit supersede rules.

## Policy

Timeline changes should prefer:

- propose
- review
- supersede

Not:

- silent overwrite

## Required Future Behavior

When a rebuild materially changes timeline interpretation, the system should support:

- new proposed periods/transitions
- explicit merge/split proposals
- retirement or supersede marking for old period objects
- preserved audit/review trail

## Review Triggers

Future revision flow should trigger review when:

- a period boundary changes materially
- a period label changes materially
- a transition changes from resolved to unresolved or vice versa
- major evidence or clarification changes historical interpretation

## Non-Goals For Now

Before Sprint 6, do not build:

- full versioned timeline UI
- full timeline branching model
- destructive timeline rewrites

## Practical Rule

Until revision support exists, downstream services should treat timeline outputs as current interpreted artifacts with audit trail, not immutable ground truth.
