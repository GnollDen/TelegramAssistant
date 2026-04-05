# OPINT-004-E: Telegram CTA Hierarchy Next Slice (2026-04-05)

## Current Baseline (Integrated)
Telegram operator baseline in `master` now includes:
- decision-oriented resolution cards,
- improved evidence preview rendering,
- cleaner Telegram -> Operator Web handoff,
- clearer post-action feedback after resolution actions.

## Deferred Gap (Explicit)
Current Telegram card CTA rows are still static.
The recommended next action is not yet prioritized visually or by ordering.

## Next Narrow Slice Scope
Bounded follow-up `OPINT-004-E` will implement:
- explicit recommended-next-action detection per card,
- CTA ordering that places recommended action first,
- minimal visual emphasis for recommended action button in Telegram constraints.

## Out Of Scope For This Pass
- broad Telegram UX redesign,
- new action taxonomy,
- multi-surface interaction redesign.
