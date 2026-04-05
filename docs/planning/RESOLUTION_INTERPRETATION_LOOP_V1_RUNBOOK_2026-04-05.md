# ResolutionInterpretationLoopV1 Runbook (Bounded Slice)

## Scope
- Canonical scope only: `chat:885574984`.
- Insertion point: resolution detail projection before review surfacing (`ResolutionReadProjectionService.GetDetailAsync`).
- Deterministic control plane remains authoritative for auth/session/scope, budgets, persistence, promotion/review states, and durable writes.

## Loop Shape
1. Build small initial context from current resolution item, bounded evidence, notes, and scoped durable summaries.
2. Model decides if context is sufficient.
3. Optional single bounded retrieval round.
4. Model returns structured interpretation used only for review surfacing.

## Hard Limits
- Max additional retrieval rounds: `1`.
- Scope lock: only `chat:885574984`.
- Allowed retrieval types: `additional_evidence`, `durable_context`.
- No model-direct durable writes.
- Full per-run audit trail required (context, model rounds, retrieval round, final output/fallback).

## Output Contract
- `context_sufficient`
- `requested_context_type`
- `interpretation_summary`
- `key_claims`
- `explicit_uncertainties`
- `review_recommendation`
- `evidence_refs_used`

## Validation
Command:
```bash
dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --resolution-interpretation-loop-v1-validate
```

Default artifact:
- `/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/artifacts/resolution-interpretation-loop/resolution-interpretation-loop-v1-validation-report.json`

Validation expectations:
- Initial bounded context is recorded.
- Zero or one additional retrieval round.
- Final structured interpretation is produced.
- Audit trail is present.
- No cross-scope retrieval.
- No model-to-durable writes.
