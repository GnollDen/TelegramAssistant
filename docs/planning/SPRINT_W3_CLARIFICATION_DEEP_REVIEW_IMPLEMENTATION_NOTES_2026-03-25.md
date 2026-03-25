# Sprint W3 Implementation Notes

## Date

2026-03-25

## Scope

Implementation pass for `Sprint W3: Clarification and Deep Review` only.

No broad redesign.
No W4 usability/hardening wave.
No bot clarification flow rewrite.

## Implemented

1. Expanded clarification review in web case detail:
- added deep-review snapshot with lifecycle, evidence window, participants, subject refs, reopen triggers;
- clarified evidence-first section with source class + timestamp + trail links;
- preserved existing quick clarification answer flow while adding richer answer inputs.

2. Long-form user context and correction flow:
- added dedicated long-form context/correction form in case detail;
- added source-kind/mode/correction metadata to case action contract;
- persisted correction metadata with source separation and structured payload;
- correction path marks linked artifacts stale (without forced broad reopen).

3. Evidence drill-down:
- added `case-evidence` route and page for focused evidence review;
- surfaced message-window drill-down with participant/date context and message-level links.

4. Timeline/date/people context:
- surfaced evidence time window and participant summary directly in case detail;
- added direct links from case detail to timeline/network/history for ambiguity review.

5. Linked navigation and history/outcome visibility:
- case detail now shows linked cases (heuristic overlap) and explicit linked objects from `stage6_case_links`;
- lifecycle timestamps + reopen triggers are now visible in deep-review surface;
- existing outcomes/history blocks remain first-class in case detail.

## Files Changed

- `src/TgAssistant.Core/Models/UserContextModels.cs`
- `src/TgAssistant.Web/Read/WebOpsModels.cs`
- `src/TgAssistant.Web/Read/WebOpsService.cs`
- `src/TgAssistant.Web/Read/WebRouteRenderer.cs`
- `src/TgAssistant.Web/Read/WebOpsVerificationService.cs`

## Verification Plan

Required for this pass:
- `dotnet build TelegramAssistant.sln`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --ops-web-smoke`

Optional follow-up when runtime env is fully provisioned:
- `--web-smoke`
- `--web-review-smoke`
- `--search-smoke`
- browser walkthrough for deep clarification review and long-form correction flow.
