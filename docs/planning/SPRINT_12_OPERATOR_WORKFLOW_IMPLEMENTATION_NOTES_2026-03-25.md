# Sprint 12 — Operator Workflow in Bot and Web (implementation notes)

Date: 2026-03-25

## What changed

Sprint 12 implementation establishes a single Stage 6 operator workflow over canonical `stage6_cases`.

Implemented:
- unified operator queue via web `/queue` and `/inbox` backed by `stage6_cases` (no second queue model)
- explicit bot/operator queue commands:
  - `/cases`
  - `/case <stage6-case-id>`
  - `/resolve`, `/reject`, `/refresh`, `/annotate`
- web case and artifact detail surfaces:
  - `/case-detail?caseId=...`
  - `/artifact-detail?artifactType=...`
- web human actions:
  - `/case-action` (`resolve`, `reject`, `refresh`, `annotate`)
  - `/artifact-action` (`refresh`)
- clarification intake through web:
  - `/clarification-answer?caseId=...&answer=...`
- evidence-first rendering for clarification judgment in bot/web flows.

## Bot/Web split (enforced)

- bot: fast intake and quick decisions (`/gaps`, `/answer`, `/cases`, `/case`, lifecycle shortcuts)
- web: expanded queue filtering, evidence-first case/artifact review, detailed context/history, and longer-form operator actions

## Lifecycle behavior

- `resolve` / `reject` update `stage6_cases` status and write review events
- clarification-linked cases propagate status updates to clarification workflow when applicable
- `refresh` marks linked artifacts stale and reopens relevant cases/questions for fresh generation
- `annotate` writes source-separated operator context (`operator_annotation`) in `stage6_user_context_entries`

## Verification notes

- `dotnet build TelegramAssistant.sln` succeeds
- runtime smoke entrypoints (`--bot-smoke`, `--ops-web-smoke`) currently require non-placeholder runtime DB credentials in environment and were blocked in this run by `RuntimeStartupGuard`
