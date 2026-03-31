# Sprint N1 Known Limits and Follow-Up Note

## Date

2026-03-31

## Purpose

Record non-blocking follow-ups for current baseline so operators do not confuse backlog items with regressions.

## Non-Blocking Follow-Ups

- Additional product polish and feature breadth beyond accepted Stage 6 surfaces.
- Extended observability/reporting depth beyond required runtime and smoke checks.
- Backlog items unrelated to Stage 5/6 acceptance baseline (regular product backlog).

## Not a Regression / Not a Blocker

Do **not** classify as blocker by default:

- absence of new redesign/rebuild work in this readiness sprint;
- remaining product backlog that does not break current accepted bot/web/MCP Stage 6 flows;
- synthetic scope artifacts seen only in explicitly synthetic drills.

## Blocker Definition for This Baseline

Treat as blocker only if one of these occurs:

- baseline runtime checks fail (`liveness/readiness/runtime-wiring`);
- Stage 6 operator surfaces regress on real scopes;
- synthetic scope leaks into normal operator default flow;
- `sender_id=0` defect reappears in 1:1 ingest path.
