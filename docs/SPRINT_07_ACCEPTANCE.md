# Sprint 07 Acceptance

## Purpose

Validate that Sprint 7 produced a real draft layer rather than generic text generation.

## Acceptance Checklist

## Draft Coverage

- draft record is generated
- one main draft exists
- two alternatives exist
- strategy linkage exists

## Draft Quality

- drafts are grounded in state/strategy/profile context
- drafts are not identical boilerplate variants
- style shaping is visible
- sendability is preserved

## Safety and Strategy Fit

- risky user intent does not silently override strategy
- conflict-handling path exists
- safer main draft behavior is visible where needed

## Persistence

- draft record persists correctly
- main and alternative drafts are stored
- style notes and confidence are stored

## Verification

- build passes
- startup passes
- draft smoke passes
- persistence works

## Hold Conditions

Hold Sprint 7 if any of these are true:

- drafts are generic and weakly tied to strategy
- only one draft exists
- style shaping is absent
- conflict handling is absent
- outputs are not meaningfully persistable

## Pass Condition

Sprint 7 passes if:

- the system now has a usable draft layer
- ready to support bot `/draft` and later draft review workflows
