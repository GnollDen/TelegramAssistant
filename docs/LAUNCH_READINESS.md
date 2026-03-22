# Launch Readiness

## Purpose

Summarize what is already strong enough for first serious use and what still needs polish before broader real-world operation.

## What Is Already Ready In Principle

The system now has a full core loop:

- primary ingestion
- clarification
- periodization
- current state
- profiles
- strategy
- draft generation
- draft review
- bot command layer
- web read/review/ops/search surfaces
- graph/network layer
- outcome/learning layer
- budget guardrails
- eval harness
- external archive ingestion
- competing-context integration

This means the product is no longer missing its main reasoning architecture.

## What Makes It Not Fully Launch-Ready Yet

The main remaining risks are no longer architectural.
They are operational and polish-related:

- compile/runtime stability across the whole solution
- surface polish for budget/eval/outcome/operator flows
- wider edit/review coverage
- deployment/monitoring cleanliness
- documentation and operator clarity

## Minimum Conditions For First Serious Use

Before broader real use, the project should satisfy:

1. clean full-solution build
2. no blocking runtime-wiring regressions
3. all main smoke paths pass reliably
4. budget/eval visibility available to operator
5. outcome trail and review flows are inspectable enough for debugging
6. basic deployment/monitoring runbooks are coherent

## What Does Not Need To Wait

You do not need to wait for:

- perfect UX polish
- full GraphRAG
- deep auto-adaptation
- advanced experimentation dashboards
- complete operator UI

## Recommended Immediate Focus

The next phase should be:

1. stabilization
2. operational polish
3. launch-readiness verification

Not:

1. more major architecture

## Recommendation

Run one focused polish sprint aimed at:

- compile/runtime consistency
- operator visibility
- outcome and eval inspectability
- deployment and runbook hardening

Then reassess for first serious production-like use.

## Practical Verification Bundle

For operator-facing launch checks, use this minimal bundle:

1. `dotnet build TelegramAssistant.sln`
2. `dotnet run --project src/TgAssistant.Host -- --runtime-wiring-check`
3. `dotnet run --project src/TgAssistant.Host -- --launch-smoke`

`--launch-smoke` is intended as a consolidated pre-launch path and verifies:

- foundation/domain smoke
- web read/review/search surfaces
- ops web visibility (inbox/history/budget/eval)
- outcome trail chain checks
- budget guardrail visibility check
- eval smoke (including experiment-layer smoke)
