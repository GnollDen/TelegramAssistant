# Stage5 Product Backlog (Post-MVP)

## Status
- Stage 5 backlog moved from implementation to **stabilization mode** (v10).
- Core P0/P1/P2 implementation is in production, but runtime tuning (cost/noise/language quality) is still active.

## Done (P0/P1/P2)
- Identity correctness:
  - deterministic actor identity (`chat_id:sender_id`), alias storage, merge candidates, merge decisions/commands.
  - conservative auto-merge and configurable weak-candidate auto-reject.
- Extraction quality:
  - hardened prompts with few-shot rules,
  - JSON schema validation before deserialize/apply,
  - context enrichment (`meta`, bounded `reply_context`),
  - category-aware conflict policy (`supersede/parallel/tentative`),
  - configurable confidence thresholds and high-confidence auto-confirm.
- Expensive pipeline reliability:
  - primary/fallback model chain,
  - per-model exponential backoff buckets,
  - dedicated expensive retry queue (`expensive_retry_count`, `expensive_next_retry_at`, `expensive_last_error`) with max retries and cheap-finalize on exhaustion.
- Manual review pipeline:
  - `fact_review_commands` queue + worker,
  - idempotent command apply,
  - enqueue dedup + configurable re-enqueue window,
  - stale pending timeout + retention cleanup.
- Observability and ops:
  - `extraction_errors` persistence,
  - `analysis_usage_events` (phase/model/tokens/cost),
  - `stage5_metrics_snapshots` now include usage (`analysis_requests_1h`, `analysis_tokens_1h`, `analysis_cost_usd_1h`) and queue health,
  - maintenance worker cleanup policy for telemetry/decisions/review queues,
  - operational SQL script: `scripts/llm-cost-sanity.sql`

## Runtime Control Surface (Config)
- `Analysis.*`:
  - `BatchSize`, `PollIntervalSeconds`, `CheapModel`, `ExpensiveModel`, `ExpensiveFallbackModel`
  - cheap A/B controls: `CheapModelAbEnabled`, `CheapBaselineModel`, `CheapCandidateModel`, `CheapAbCandidatePercent`
  - `CheapMaxTokens`, `ExpensiveMaxTokens`
  - `MaxExpensivePerBatch`, `MaxExpensiveRetryCount`, `ExpensiveRetryBaseSeconds`
  - `ExpensiveCooldownMinutes`, `ExpensiveFailureBackoffBaseSeconds`, `ExpensiveFailureBackoffMaxMinutes`
  - `CheapConfidenceThreshold`, `MinFactConfidence`, `MinSensitiveFactConfidence`, `MinRelationshipConfidence`, `AutoConfirmFactConfidence`
  - `FactReviewBatchSize`, `FactReviewReenqueueHours`
- `Merge.*`:
  - `MaxCandidatesPerRun`, `CommandBatchSize`
  - `AutoRejectScoreThreshold`, `AutoRejectAliasLengthMax`
- `Maintenance.*`:
  - retention for errors/metrics/merge decisions/fact review commands
  - `FactReviewPendingTimeoutDays`

## Post-Closure Product Questions (Not Engineering Blockers)
1. Which dossier categories are mandatory for personal use (career/finance/health/family/habits)?
2. Final policy for sensitive facts: always manual confirm or allow auto-confirm above stricter threshold?
3. Cross-chat identity strategy: global merge by default or per-chat isolation with manual linking?
4. Target precision/recall balance for noisy chats.
5. Default expensive model policy (quality-first vs cost-first) for long archive runs.
