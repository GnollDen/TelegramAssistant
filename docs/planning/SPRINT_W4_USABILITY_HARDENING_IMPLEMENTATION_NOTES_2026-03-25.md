# Sprint W4 Implementation Notes

## Date

2026-03-25

## Scope

Implementation pass for `Sprint W4: Usability and Hardening` only.

No broad redesign.
No feature expansion outside W4.
Preserved W1-W3 functional flow contracts.

## Implemented

1. Filters and sorting cleanup
- queue read contract now supports explicit sorting (`sortBy`, `sortDirection`);
- queue controls now expose practical triage filters with defaults and reset path;
- queue header now shows active sort state for operator clarity;
- queue links preserve actionable scope and stable return paths.

2. Empty/loading/error states
- explicit no-results and empty-queue states added in queue;
- explicit `Case Not Found` state on invalid case detail lookup;
- explicit `Artifact Unavailable` state when artifact is missing;
- hosted web route rendering now returns clearer route-not-found and render-error surfaces instead of raw failure feel.

3. Action confirmations
- confirmation gate added for costly case actions (`resolve`, `reject`);
- confirmation gate added for risky review actions (`confirm`, `reject`);
- action pages now render confirmation callouts with explicit confirm/cancel links.

4. Verification tightening
- web read smoke now asserts queue sort marker and queue no-results state;
- ops web smoke now asserts:
  - confirmation requirement for case resolve/reject,
  - explicit invalid-case state,
  - explicit missing-artifact state,
  - repeated navigation stability across queue/detail/evidence/artifact paths;
- web review smoke now asserts confirmation requirement for review confirm action route.

5. Basic stability/performance cleanup
- queue ordering path made explicit and deterministic by selected sort mode;
- route rendering now has guarded exception handling in hosted web runtime;
- `asOfUtc` parsing in hosted web request is now invariant/UTC-normalized.

6. Calm internal-tool presentation
- shell styling in hosted web and route renderer was tightened for calmer internal-tool readability (neutral background, structured cards, clearer nav).

## Files Changed

- `src/TgAssistant.Host/Web/WebRuntimeHostedService.cs`
- `src/TgAssistant.Web/Read/IWebReadServices.cs`
- `src/TgAssistant.Web/Read/WebOpsModels.cs`
- `src/TgAssistant.Web/Read/WebOpsService.cs`
- `src/TgAssistant.Web/Read/WebRouteRenderer.cs`
- `src/TgAssistant.Web/Read/WebReadVerificationService.cs`
- `src/TgAssistant.Web/Read/WebOpsVerificationService.cs`
- `src/TgAssistant.Web/Read/WebReviewModels.cs`
- `src/TgAssistant.Web/Read/WebReviewVerificationService.cs`

## Verification Evidence

### Build
- `dotnet build TelegramAssistant.sln` ✅ pass

### Runtime probes and web smoke
Attempted commands:
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --liveness-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --web-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --web-review-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web,ops --ops-web-smoke`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --search-smoke`

Observed result:
- all runtime commands blocked before startup by `RuntimeStartupGuard` with:
  - `Database:ConnectionString contains placeholder or unsafe secret material. Provide a real credential.`

Interpretation:
- verification code paths compile and are wired;
- full runtime smoke execution requires valid local DB/Redis secrets in host config.

## Outcome

W4 implementation scope completed in code.
Runtime smoke evidence is partially blocked by environment secrets guard and requires follow-up execution in a configured runtime.
