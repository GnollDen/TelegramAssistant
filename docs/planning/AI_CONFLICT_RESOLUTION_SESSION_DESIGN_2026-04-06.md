# AI Conflict Resolution Session Design

Date: `2026-04-06`  
Status: proposed bounded design  
Scope: operator resolution flow evolution, no broad rewrite

## 1. Problem Statement

Current conflict/review handling is at risk of state explosion: semantic interpretation gradually moves into handcrafted branching and projection heuristics.  
Conflict resolution and operator follow-up are inherently semantic and evidence-driven; scaling this with rule-heavy branching will degrade context quality and operator trust.

Therefore, conflict/review items should move into an AI-centric bounded session model, while deterministic control remains the only authority for durable writes and truth-layer changes.

## 2. Design Principle

### AI session owns

- conflict interpretation and hypothesis framing
- bounded operator dialogue (targeted follow-up questions)
- bounded retrieval requests within admitted scope/tool contracts
- evidence selection and claim shaping (`fact|inference|hypothesis`)
- structured final verdict proposal
- explicit uncertainty reporting

### Deterministic layer owns

- auth/session/scope admission and deny-safe enforcement
- tool whitelist, turn limits, retrieval limits, runtime budgets
- full audit trail and replay metadata
- schema validation + normalization + policy checks
- all durable writes and recompute triggers
- idempotency/retry safety and failure fallback

Non-negotiable rule: model output never writes durable truth directly.

## 3. Session Model

Lifecycle (`ConflictResolutionSessionV1`):

1. conflict/review item enters resolution session (`start`)
2. system builds initial bounded context from existing resolution detail path
3. AI may do bounded steps:
- ask one bounded operator question (`awaiting_operator_answer`)
- request bounded extra context through admitted tool contract
- revise interpretation
4. AI emits structured final verdict (`ready_for_commit` or `needs_web_review` or `fallback`)
5. deterministic normalizer/validator gates verdict
6. only validated proposal can be applied through existing deterministic action path

Bounded defaults (v1):

- `max_model_calls = 2`
- `max_retrieval_rounds = 1`
- `max_operator_turns = 2` (one follow-up question + one answer)
- `session_ttl = 30m`
- scope-local only (`tracked_person_id + scope_key + scope_item_key`)

## 4. Tool / Retrieval Boundaries

Admitted toolset (session-local, audited):

- `get_neighbor_messages`
- `get_evidence_refs`
- `get_durable_context`
- `ask_operator_question`

Hard constraints:

- scope-local only, no cross-scope retrieval
- no DB/raw query tool access for model
- explicit max rounds/max items per request
- each request logged with request/response manifest
- deterministic deny on non-whitelisted tool/params

## 5. Final Output Contract

Required structured verdict payload:

- `resolution_verdict`
- `resolved_claims`
- `rejected_claims`
- `evidence_refs_used`
- `operator_inputs_used`
- `remaining_uncertainties`
- `normalization_proposal`
- `confidence_calibration`

Contract rules:

- every retained claim must be evidence-backed or moved to uncertainty
- `normalization_proposal` is advisory until deterministic validation passes
- no direct model-to-durable write path

## 6. Insertion Point

Bounded insertion point: add a dedicated conflict-session application flow on top of existing resolution read/action path.

- read bootstrap source: current resolution detail projection (`ResolutionReadProjectionService.GetDetailAsync`)
- session orchestration boundary: `OperatorResolutionApplicationService` + new `/api/operator/resolution/conflict-session/*` endpoints
- durable apply boundary remains unchanged: existing `ResolutionActionCommandService`

Rationale:

- reuses current bounded context assembly and LoopV1 interpretation
- avoids rewriting Stage8/recompute/control plane
- keeps single deterministic mutating choke point for durable writes

## 7. First Implementation Slice

One bounded first slice:

- scope: canonical pilot scope only (`chat:885574984`)
- family: one conflict/review family only (`contradiction`/review subset)
- surface: web resolution detail only
- operator dialogue: max 1 follow-up question + 1 operator answer
- output: AI verdict -> deterministic normalization proposal only -> existing `/resolution/actions` apply path

Minimal scaffold to start:

- new service contract: `IResolutionConflictSessionService`
- session persistence tables:
- `operator_resolution_conflict_sessions`
- `operator_resolution_conflict_session_entries`
- endpoints:
- `POST /api/operator/resolution/conflict-session/start`
- `POST /api/operator/resolution/conflict-session/respond`
- optional `POST /api/operator/resolution/conflict-session/query`
- extend existing action request with optional linkage:
- `conflict_session_id`
- `conflict_verdict_revision`

## 8. Migration Principle

- current heuristic conflict semantics are temporary scaffolding
- new semantic conflict interpretation should move into bounded AI sessions
- handcrafted branching should stop growing where bounded AI session can own interpretation
- rollout is additive, feature-gated, deny-safe, reversible

## Acceptance Checks (v1)

- bounded limits enforced (turns/retrieval/model calls/ttl)
- zero direct model-to-durable writes
- deterministic normalizer/validator always runs before apply
- durable writes remain only through existing action command path
- full session audit trail present (context, tool calls, operator turns, verdict, validation outcome)
- fallback path works on schema/budget/scope violations
