# TelegramAssistant — Codex Backlog

## Project context

Repository: `GnollDen/TelegramAssistant` (GitHub, branch `master`).
Stack: C# .NET 8, PostgreSQL 16, Redis 7, Docker Compose, OpenRouter API.
Solution: `TelegramAssistant.sln` with projects in `src/`.

The application is a personal Telegram assistant that listens to chats via MTProto,
processes media through OpenRouter LLM APIs, and builds intelligence dossiers
(entities, facts, relationships, observations, claims) in PostgreSQL.

Stage 5 (intelligence/dossier building) is implemented and running.
This backlog covers two tracks: **fixing known issues** in Stage 5 and
**building an MCP server** for universal LLM access to the data.

> Status note (2026-03-16): часть пунктов Track A уже реализована в текущем коде (включая декомпозицию Stage5-сервисов и v10-эволюцию summary/extraction). Перед выполнением задач ниже обязательно сверять фактическое состояние классов в `src/TgAssistant.Intelligence/Stage5/`.

---

## Track A — Stage 5 fixes (priority order)

### A1. Split AnalysisWorkerService (God Class ~2200 lines)

**File:** `src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs`

**Problem:** Single file contains extraction orchestration, entity resolution, fact conflict
handling, intelligence persistence, regex heuristics, expensive model fallback state,
filler detection, validation — everything. Impossible to test or reason about in isolation.

**Task:** Extract into separate classes. All new classes go in `src/TgAssistant.Intelligence/Stage5/`.

1. **`ExtractionApplier.cs`** — move `ApplyExtractionAsync`, `PersistIntelligenceAsync`,
   `GetOrCreateCachedEntityAsync`, `GetCurrentFactsCachedAsync`, `UpsertEntityWithActorContextAsync`,
   `ObserveAliasAsync`, `QueueFactReviewIfNeededAsync`, fact conflict strategy methods
   (`GetFactConflictStrategy`, `ResolveFactStatus`, `IsSensitiveCategory`), and the
   `FactConflictStrategy` enum. Constructor receives repository interfaces.

2. **`ExtractionRefiner.cs`** — move `RefineExtractionForMessage`, `NormalizeExtractionForMessage`,
   `SanitizeExtraction`, `FinalizeResolvedExtraction`, `PruneLowValueSignals`,
   all `Promote*Fallback` methods, `TrimUnreferencedEntities`, `Add*IfMissing` helpers,
   `HasLocationSignal`, `HasContactSignal`, all content heuristic methods
   (`HasStrongDossierSignal`, `HasConcreteActionableAnchor`, `ContainsAny`,
   `IsLikelyFillerMessage`, `HasSemanticSignal`, etc.), and all compiled `Regex` fields.
   This class should be static or have no dependencies — pure functions.

3. **`ExtractionValidator.cs`** — move `ValidateExtractionForMessage`,
   `ValidateExtractionRecord`, `IsReasonableText`. Static class, pure functions.

4. **`ExpensivePassResolver.cs`** — move `ProcessExpensiveBacklogAsync`,
   `ResolveWithFallbackAsync`, `CanRunExpensivePassAsync`, model backoff state
   (`_expensiveBlockedUntilByModel`, `_expensiveFailureStreakByModel`),
   `RegisterModelFailure`, `RegisterModelSuccess`, `ComputeExpensiveBackoff`,
   `GetDistinctExpensiveModels`, `AreAllExpensiveModelsBlocked`, `ShouldFallback`,
   `IsProviderDenied`, `BuildExpensiveErrorPayload`. Constructor receives
   `AnalysisSettings`, `OpenRouterAnalysisService`, `IMessageExtractionRepository`,
   `IExtractionErrorRepository`, `IAnalysisUsageRepository`, `IMessageRepository`.

5. **`MessageContentBuilder.cs`** — move `BuildMessageText`, `BuildSemanticContent`,
   `TruncateForContext`, `LoadReplyContextAsync`, `LoadReplyMessageAsync`,
   `BuildCheapBatchPrompt` (move from `OpenRouterAnalysisService` if needed),
   `SplitMeaningfulLines`, `CollapseWhitespace`. Constructor receives `IMessageRepository`.

6. **`AnalysisWorkerService.cs`** — remains as thin orchestrator. `ExecuteAsync` loop,
   `ProcessCheapBatchAsync` (simplified — delegates to above classes),
   `EnsureDefaultPromptsAsync`. ~300 lines max.

**Acceptance criteria:**
- All new classes compile and are wired via DI in `Program.cs`.
- `AnalysisWorkerService` is under 400 lines.
- No behavioral changes — same extraction results, same DB writes.
- Default prompts (`DefaultCheapPrompt`, `DefaultExpensivePrompt`) stay as const strings
  in `AnalysisWorkerService` or move to a dedicated `DefaultPrompts.cs` static class.

---

### A2. Add transaction to ApplyExtractionAsync

**File:** the new `ExtractionApplier.cs` (after A1) or `AnalysisWorkerService.cs` if A1 is skipped.

**Problem:** `ApplyExtractionAsync` makes ~10+ independent DB calls (entity upserts,
fact upserts, relationship upserts, communication events, intelligence replace).
If it fails mid-way, data is left inconsistent.

**Task:**
- Inject `IDbContextFactory<TgAssistantDbContext>` into the class.
- Wrap the entire `ApplyExtractionAsync` body in:
  ```csharp
  await using var db = await _dbFactory.CreateDbContextAsync(ct);
  await using var tx = await db.Database.BeginTransactionAsync(ct);
  // ... all operations ...
  await tx.CommitAsync(ct);
  ```
- Repository calls inside must use the same `DbContext` or the transaction must span
  the connection. Simplest approach: pass a `NpgsqlConnection` + `NpgsqlTransaction`
  through, or use `TransactionScope`. Choose whichever is less invasive.

**Acceptance criteria:**
- If any sub-operation fails, nothing is committed for that message.
- Existing tests (if any) still pass.
- Add a comment explaining the transactional boundary.

---

### A3. Batch entity resolution to fix N+1

**File:** the new `ExtractionApplier.cs` or `AnalysisWorkerService.cs`.

**Problem:** For a batch of 50 messages with ~3 entities each, `GetOrCreateCachedEntityAsync`
makes 150 individual DB lookups with advisory locks.

**Task:**
- In `ProcessCheapBatchAsync`, before calling `ApplyExtractionAsync` per message:
  1. Collect all unique entity names from all extraction items in the batch.
  2. Do one bulk query: `SELECT * FROM entities WHERE name = ANY($1)` +
     `SELECT * FROM entity_aliases WHERE alias_norm = ANY($1)`.
  3. Build a shared `Dictionary<string, Entity>` (case-insensitive).
  4. Pass this pre-warmed cache into `ApplyExtractionAsync`.
- `GetOrCreateCachedEntityAsync` checks the shared cache first; only hits DB for truly new entities.

**Acceptance criteria:**
- For a batch of N messages with M unique entity names, DB makes at most M + 2 entity queries
  (bulk lookup + individual upserts for genuinely new entities), not N × entities_per_message.

---

### A4. Remove fragile regex heuristics

**Files:** `ExtractionRefiner.cs` (after A1) or `AnalysisWorkerService.cs`.

**Problem:** `PromoteLocationFallback`, `PromoteContactFallback`, `PromoteWorkAssessmentFallback`
use ~300 lines of hardcoded Russian-language regex. They produce false positives
(e.g. "обработка" matches "работ") and miss valid patterns.

**Task:**
- **Keep only high-precision fallbacks:**
  - Map link detection (yandex.ru/maps, google.com/maps, 2gis.ru) → location signal.
  - `@handle` pattern → contact signal.
- **Remove entirely:**
  - `PromoteWorkAssessmentFallback` and `LooksLikeWorkAssessmentMessage` — too domain-specific.
  - `HouseNumberTailRegex` — too fragile.
  - `HasStrongDossierSignal` keyword list — move these signals into the LLM prompt instead.
  - `HasConcreteActionableAnchor` keyword list — same, belongs in prompt.
- **Simplify `PruneLowValueSignals`:**
  - Instead of keyword-based gating, use a simpler rule: if extraction has any fact/claim/relationship
    with `confidence >= MinFactConfidence`, keep it. Otherwise discard.
  - If extraction has `category in (work, health, finance, relationship, location, contact)`, always keep.

**Acceptance criteria:**
- `ExtractionRefiner` (or equivalent) is under 200 lines.
- `ContainsAny` is only used for map link / @handle detection.
- All removed heuristic keywords are documented in a comment pointing to the prompt as the
  source of truth for signal detection.

---

### A5. Fix PruneLowValueSignals data loss

**File:** `ExtractionRefiner.cs` or `AnalysisWorkerService.cs`.

**Problem:** `PruneLowValueSignals` clears all observations/claims/facts when there is
no "high value structured signal" AND no "concrete actionable anchor" in the text.
Messages like "Катя сменила работу" get their correct extractions wiped because the text
has no digits/times/addresses.

**Task:** (may be subsumed by A4 if heuristics are simplified)
- Add category whitelist before pruning:
  ```csharp
  var hasHighValueCategory = item.Facts.Any(f => IsHighValueCategory(f.Category))
      || item.Claims.Any(c => IsHighValueCategory(c.Category));
  if (hasHighValueCategory) return; // skip pruning entirely
  ```
- Add confidence floor check:
  ```csharp
  var hasConfidentSignal = item.Facts.Any(f => f.Confidence >= _settings.MinFactConfidence)
      || item.Claims.Any(c => c.Confidence >= _settings.MinFactConfidence);
  if (hasConfidentSignal) return;
  ```

**Acceptance criteria:**
- A fact with `category=work` and `confidence=0.85` is never pruned regardless of message text.

---

### A6. Temporal fact supersede for availability/schedule

**File:** `ExtractionApplier.cs` or `AnalysisWorkerService.cs`, method near `GetFactConflictStrategy`.

**Problem:** `GetFactConflictStrategy("schedule")` returns `Parallel`. Every
"буду свободен в 15:00" creates a new fact instead of superseding the old one.
After a week, hundreds of `availability:free_time` facts accumulate per entity.

**Task:**
- For categories `availability` and `schedule`, check the age of the existing fact:
  ```csharp
  case "availability":
  case "schedule":
      if (sameKey != null && (DateTime.UtcNow - sameKey.UpdatedAt).TotalHours > 12)
          return FactConflictStrategy.Supersede;
      return FactConflictStrategy.Parallel;
  ```
- Make the TTL configurable: add `AnalysisSettings.TemporalFactSupersedeTtlHours` (default 12).
- Add to `docker-compose.yml` env: `Analysis__TemporalFactSupersedeTtlHours`.
- Add to `.env.example`: `ANALYSIS_TEMPORAL_FACT_SUPERSEDE_TTL_HOURS=12`.

**Acceptance criteria:**
- An `availability:free_time` fact older than 12h is superseded, not duplicated.
- A fact younger than 12h gets `Parallel` treatment (preserved for review).

---

### A7. Persist expensive model backoff state

**File:** `ExpensivePassResolver.cs` (after A1) or `AnalysisWorkerService.cs`.

**Problem:** `_expensiveBlockedUntilByModel` and `_expensiveFailureStreakByModel` are
in-memory dictionaries. Container restart loses all state, causing immediate retries
against a blocked provider.

**Task:**
- On `RegisterModelFailure`: write to `analysis_state` table:
  - key: `expensive:blocked_until:{model}`, value: Unix timestamp ms.
  - key: `expensive:streak:{model}`, value: streak count.
- On `RegisterModelSuccess`: delete both keys.
- On `TryGetModelBlockedUntil`: read from `analysis_state` first, then check in-memory cache.
- Keep in-memory cache as hot path; DB is source of truth on startup.
- On worker startup (`ExecuteAsync`), load all `expensive:*` keys from `analysis_state` into memory.

**Acceptance criteria:**
- After container restart, expensive model that was blocked still has its backoff respected.
- Successful call clears both DB and memory state.

---

### A8. Remove dead code

**Files:** `AnalysisWorkerService.cs`.

**Task:** Delete `ResolveIntelligenceEntityAsync` — it is never called (all paths use
`GetOrCreateCachedEntityAsync` instead).

**Acceptance criteria:** Method removed, project compiles.

---

### A9. Deduplicate OpenRouter DTOs

**Files:**
- `src/TgAssistant.Processing/Media/OpenRouterMediaProcessor.cs` (defines `OpenRouterRequest`, `OpenRouterMessage`, `OpenRouterResponse`, `OpenRouterChoice`, `OpenRouterResponseMessage` as `internal`).
- `src/TgAssistant.Intelligence/Stage5/OpenRouterAnalysisService.cs` (defines same-named but different DTOs as `internal`).

**Problem:** Two independent sets of OpenRouter API DTOs with different shapes
(`Content` is `object` in Media, `string` in Analysis). Changes to OpenRouter API
require updating both.

**Task:**
- Create `src/TgAssistant.Infrastructure/OpenRouter/OpenRouterDtos.cs`:
  ```csharp
  namespace TgAssistant.Infrastructure.OpenRouter;

  public class OpenRouterRequest
  {
      public string Model { get; set; } = "";
      public List<OpenRouterMessage> Messages { get; set; } = new();
      public OpenRouterResponseFormat? ResponseFormat { get; set; }
      public int? MaxTokens { get; set; }
      public float? Temperature { get; set; }
  }

  public class OpenRouterMessage
  {
      public string Role { get; set; } = "";
      public object Content { get; set; } = ""; // string or object[] for multimodal
  }

  // ... Response, Choice, ResponseMessage, Usage, ResponseFormat
  ```
- Update both `OpenRouterMediaProcessor` and `OpenRouterAnalysisService` to use shared DTOs.
- Remove the `internal` DTO definitions from both files.

**Acceptance criteria:**
- Single source of truth for OpenRouter DTOs.
- Both services compile and work with the shared types.

---

### A10. Fix HttpClient header race condition

**Files:** `OpenRouterAnalysisService.cs`, `OpenRouterEmbeddingService.cs`.

**Problem:** Both services mutate `HttpClient.DefaultRequestHeaders` in their constructors:
```csharp
_http.DefaultRequestHeaders.Remove("Authorization");
_http.DefaultRequestHeaders.Add("Authorization", ...);
```
If the DI container provides the same `HttpClient` instance (possible with `AddHttpClient`
misconfiguration), this is a race condition.

**Task:**
- In `Program.cs`, use named HttpClients:
  ```csharp
  services.AddHttpClient<OpenRouterAnalysisService>("analysis", client => {
      client.BaseAddress = new Uri(claudeBaseUrl);
      client.DefaultRequestHeaders.Add("Authorization", $"Bearer {claudeApiKey}");
      client.Timeout = TimeSpan.FromSeconds(analysisTimeout);
  });
  services.AddHttpClient<OpenRouterEmbeddingService>("embedding", client => {
      client.BaseAddress = new Uri(claudeBaseUrl);
      client.DefaultRequestHeaders.Add("Authorization", $"Bearer {claudeApiKey}");
      client.Timeout = TimeSpan.FromSeconds(60);
  });
  ```
- Remove header manipulation from service constructors.

**Acceptance criteria:**
- No `DefaultRequestHeaders.Remove`/`Add` calls in any service constructor.
- Each service gets its own `HttpClient` instance via `IHttpClientFactory`.

---

### A11. Neo4jSyncWorkerService layer violation

**File:** `src/TgAssistant.Intelligence/Stage5/Neo4jSyncWorkerService.cs`

**Problem:** Directly uses `IDbContextFactory<TgAssistantDbContext>` and queries EF DbSets,
bypassing repository interfaces. Intelligence layer should not depend on Infrastructure EF internals.

**Task:**
- Create repository interfaces in Core for the data Neo4j needs:
  ```csharp
  public interface INeo4jSyncRepository
  {
      Task<List<EntitySyncRow>> GetEntitiesUpdatedSinceAsync(DateTime since, int limit, CancellationToken ct);
      Task<List<RelationshipSyncRow>> GetRelationshipsUpdatedSinceAsync(DateTime since, int limit, CancellationToken ct);
      Task<List<FactSyncRow>> GetFactsUpdatedSinceAsync(DateTime since, int limit, CancellationToken ct);
  }
  ```
- Implement in Infrastructure using EF.
- Update `Neo4jSyncWorkerService` to use the new interface instead of `IDbContextFactory`.
- Remove the `using TgAssistant.Infrastructure.Database.Ef;` import from Neo4j worker.

**Acceptance criteria:**
- `TgAssistant.Intelligence` project has no reference to `TgAssistant.Infrastructure.Database.Ef` namespace.
- Neo4j sync still works identically.

---

## Track B — MCP Server

### B1. Project scaffold

**Location:** `mcp/` directory in repository root (sibling of `src/`, `deploy/`, `scripts/`).

**Task:** Create a TypeScript MCP server project:

```
mcp/
  package.json
  tsconfig.json
  Dockerfile
  src/
    index.ts          — entry point, creates MCP server, registers tools, starts SSE transport
    db.ts             — pg Pool wrapper (connection to PostgreSQL)
    tools/
      read-dossier.ts — search_entities, get_entity_dossier, get_facts, get_relationships
      read-messages.ts — get_recent_messages, get_message_extraction
      read-ops.ts     — get_pipeline_status, get_extraction_errors, get_merge_candidates
      write-mgmt.ts   — approve_fact, reject_fact, approve_merge, reject_merge
      write-ops.ts    — requeue_extractions, update_prompt
```

**Dependencies:**
```json
{
  "@modelcontextprotocol/sdk": "^1.12.1",
  "pg": "^8.13.1",
  "zod": "^3.24.2"
}
```

**Transport:** SSE over HTTP on port 3001 (configurable via `MCP_PORT` env var).

**Environment variables:**
- `DATABASE_URL` — PostgreSQL connection string (default: `postgresql://tgassistant:changeme@postgres:5432/tgassistant`)
- `MCP_PORT` — HTTP port (default: `3001`)
- `MCP_API_KEY` — optional bearer token for authentication

**Acceptance criteria:**
- `npm run build` succeeds.
- `npm start` starts SSE server on configured port.
- Server responds to MCP `initialize` handshake.
- Server lists all tools via `tools/list`.

---

### B2. Read tools — Dossier

**File:** `mcp/src/tools/read-dossier.ts`

**Tools:**

#### `search_entities`
- Input: `{ query: string }`
- SQL: match on `entities.name ILIKE`, `unnest(entities.aliases) ILIKE`, `entity_aliases.alias_norm ILIKE`
- Output: array of `{ id, type, name, aliases, actor_key, telegram_user_id, updated_at }`
- Limit: 20 results.

#### `get_entity_dossier`
- Input: `{ entity_name: string, facts_limit?: number (default 50), observations_limit?: number (default 30) }`
- Logic: find entity by name/alias (case-insensitive), then fetch:
  - Entity profile (id, type, name, aliases, actor_key, telegram_user_id)
  - Entity aliases from `entity_aliases`
  - Current facts from `facts WHERE entity_id = $1 AND is_current = TRUE`
  - Relationships with joined entity names
  - Recent observations from `intelligence_observations`
  - Recent claims from `intelligence_claims`
- Output: JSON object with all sections.

#### `get_facts`
- Input: `{ entity_name: string, category?: string, include_historical?: boolean (default false) }`
- Logic: find entity, then `SELECT FROM facts WHERE entity_id = $1` with optional category filter.
  If `include_historical` is false, add `AND is_current = TRUE`.
- Output: array of facts.

#### `get_relationships`
- Input: `{ entity_name: string }`
- Logic: find entity, join relationships with entity names on both sides.
- Output: array of `{ id, type, from_name, to_name, confidence, status, context_text }`.

**Acceptance criteria:**
- Each tool returns well-formatted JSON.
- Entity lookup is case-insensitive and searches aliases.
- "Entity not found" returns a clear error message, not an exception.

---

### B3. Read tools — Messages

**File:** `mcp/src/tools/read-messages.ts`

**Tools:**

#### `get_recent_messages`
- Input: `{ chat_id?: number, limit?: number (default 20, max 100), sender_name?: string }`
- SQL: `SELECT id, telegram_message_id, chat_id, sender_id, sender_name, timestamp, text, media_type, media_description, media_transcription FROM messages WHERE processing_status = 1` with optional filters.
- Order: `timestamp DESC`.
- Output: array of messages (truncate `text` to 500 chars per message).

#### `get_message_extraction`
- Input: `{ message_id: number }`
- SQL: join `messages` with `message_extractions` on `message_id`.
- Output: message metadata + `cheap_json` (parsed) + `expensive_json` (parsed if exists) + `needs_expensive`.

**Acceptance criteria:**
- Messages are returned newest-first.
- Media fields included only when non-null.

---

### B4. Read tools — Ops

**File:** `mcp/src/tools/read-ops.ts`

**Tools:**

#### `get_pipeline_status`
- Input: none
- Queries:
  - `analysis_state` WHERE key = 'stage5:watermark'
  - `COUNT(*) FROM messages WHERE processing_status = 1` (processed)
  - `COUNT(*) FROM message_extractions` (extracted)
  - `COUNT(*) FROM message_extractions WHERE needs_expensive` (expensive backlog)
  - `COUNT(*) FROM entity_merge_candidates WHERE status = 0` (merge pending)
  - `COUNT(*) FROM fact_review_commands WHERE status = 0` (review pending)
  - `COUNT(*) FROM extraction_errors WHERE created_at >= NOW() - INTERVAL '1 hour'` (errors 1h)
  - `SUM(cost_usd) FROM analysis_usage_events WHERE created_at >= NOW() - INTERVAL '24 hours'` (cost 24h)
  - `COUNT(*) FROM entities` (total entities)
  - `COUNT(*) FROM facts WHERE is_current = TRUE` (total current facts)
- Output: single JSON object with all metrics.

#### `get_extraction_errors`
- Input: `{ limit?: number (default 20), stage?: string }`
- SQL: `SELECT id, stage, message_id, reason, payload, created_at FROM extraction_errors` with optional stage filter.
- Order: `created_at DESC`.

#### `get_merge_candidates`
- Input: `{ limit?: number (default 20) }`
- SQL: join `entity_merge_candidates` (status=0) with `entities` for names.
- Output: array of `{ candidate_id, entity_low_name, entity_high_name, alias_norm, evidence_count, score, review_priority }`.

**Acceptance criteria:**
- `get_pipeline_status` executes in a single DB round-trip (one query with subqueries or CTEs).

---

### B5. Write tools — Management

**File:** `mcp/src/tools/write-mgmt.ts`

**Tools:**

#### `approve_fact`
- Input: `{ fact_id: string (UUID) }`
- SQL: `INSERT INTO fact_review_commands (fact_id, command, reason, status, created_at) VALUES ($1, 'approve', 'MCP manual approve', 0, NOW())`
- Output: confirmation message.

#### `reject_fact`
- Input: `{ fact_id: string (UUID) }`
- SQL: same as above with `command = 'reject'`.

#### `approve_merge`
- Input: `{ candidate_id: number }`
- SQL: `INSERT INTO entity_merge_commands (candidate_id, command, reason, status, created_at) VALUES ($1, 'approve', 'MCP manual approve', 0, NOW())`
- Check: verify candidate exists and status = 0 before inserting.
- Output: confirmation or error.

#### `reject_merge`
- Input: `{ candidate_id: number }`
- SQL: same with `command = 'reject'`.

**Acceptance criteria:**
- Write tools enqueue commands (not execute directly) — existing workers process them.
- Duplicate command prevention: check if pending command already exists for same entity/fact.

---

### B6. Write tools — Ops

**File:** `mcp/src/tools/write-ops.ts`

**Tools:**

#### `requeue_extractions`
- Input: `{ mode: "all" | "window" | "empty", hours?: number (default 24) }`
- Modes:
  - `all`: `DELETE FROM message_extractions; UPDATE analysis_state SET value = 0 WHERE key = 'stage5:watermark'`
  - `window`: delete extractions for messages in last N hours, set watermark to min of deleted.
  - `empty`: delete extractions where Facts+Events+Relationships are all empty arrays, set watermark.
- Output: `{ watermark, extractions_remaining, deleted_count }`
- **CRITICAL:** wrap in transaction.

#### `update_prompt`
- Input: `{ prompt_id: string, system_prompt: string }`
- SQL: `UPDATE prompt_templates SET system_prompt = $2, updated_at = NOW() WHERE id = $1`
- Validate: prompt_id exists. Return error if not found.
- Output: confirmation with updated_at timestamp.

**Acceptance criteria:**
- `requeue_extractions` is transactional.
- `update_prompt` returns error for non-existent prompt_id.

---

### B7. Dockerfile for MCP server

**File:** `mcp/Dockerfile`

```dockerfile
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src/ src/
RUN npm run build

FROM node:22-alpine
WORKDIR /app
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY package.json ./
EXPOSE 3001
CMD ["node", "dist/index.js"]
```

**Acceptance criteria:**
- `docker build -t tga-mcp mcp/` succeeds.
- Container starts and binds to port 3001.

---

### B8. Docker Compose integration

**File:** `docker-compose.yml`

**Task:** Add `mcp` service:

```yaml
  mcp:
    build:
      context: ./mcp
      dockerfile: Dockerfile
    container_name: tga-mcp
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      DATABASE_URL: "postgresql://tgassistant:${POSTGRES_PASSWORD:-changeme}@postgres:5432/tgassistant"
      MCP_PORT: "3001"
      MCP_API_KEY: "${MCP_API_KEY:-}"
    ports:
      - "127.0.0.1:3001:3001"
```

**Also update:**
- `.env.example`: add `MCP_API_KEY=`.
- `README.md`: add MCP server section with usage instructions.

**Acceptance criteria:**
- `docker compose up -d mcp` starts the MCP server.
- MCP server connects to PostgreSQL and responds to tool calls.
- Port 3001 is bound to localhost only (not exposed publicly).

---

### B9. Authentication middleware

**File:** `mcp/src/index.ts`

**Task:** If `MCP_API_KEY` is set, validate `Authorization: Bearer <key>` header on
SSE connection. Return 401 if missing or invalid.

**Acceptance criteria:**
- With `MCP_API_KEY` set: unauthenticated requests get 401.
- With `MCP_API_KEY` empty/unset: all requests pass (development mode).

---

### B10. MCP resources (optional, low priority)

**File:** `mcp/src/index.ts` or `mcp/src/resources.ts`

**Task:** Expose MCP resources for prompt context:
- `tgassistant://prompts/cheap` — current cheap extraction prompt text.
- `tgassistant://prompts/expensive` — current expensive reasoning prompt text.
- `tgassistant://status` — pipeline status summary.

These are read via MCP `resources/read` protocol, not tools.

**Acceptance criteria:**
- Resources are listed in `resources/list`.
- Resource content is returned as text.

---

## Track C — OpenRouter Broadcast observability

### C1. Broadcast trace schema and correlation keys

**Task:** Define a single trace schema for all OpenRouter calls in runtime.

- Fields: `phase`, `chat_id`, `session_id`, `slice_id`, `chunk_index`, `message_id`, `worker`, `pipeline_version`.
- Document required vs optional fields and value constraints.
- Add JSON examples for `cheap`, `summary`, `edit_diff`, `expensive`.

**Acceptance criteria:**
- New doc: `docs/observability/openrouter-broadcast.md`.
- Examples are copy-paste ready and aligned with current Stage5 flow.

---

### C2. Centralized metadata injection in OpenRouter client path

**Task:** Ensure all OpenRouter requests pass through one shared metadata injection path.

- Add `user/session_id/trace` payload composition in the common request builder.
- Cover all phases: `cheap`, `summary`, `edit_diff`, `expensive`, media where applicable.
- Remove duplicated ad-hoc metadata construction.

**Acceptance criteria:**
- 100% OpenRouter calls include standardized metadata.
- No duplicate metadata builders remain in code.

---

### C3. Runtime correlation with Stage5 sessions and slices

**Task:** Map runtime identifiers so one slice/session can be reconstructed from traces.

- Use deterministic `session_id`/`slice_id` mapping from DB/session model.
- Include `chunk_index` for chunked cheap/summary flows.

**Acceptance criteria:**
- For any processed slice, trace stream shows cheap chunk calls and summary call in one correlated chain.

---

### C4. Privacy and redaction policy

**Task:** Add privacy guardrails for broadcast payloads.

- Do not send raw message text in trace metadata.
- Keep only IDs and operational labels.
- Document redaction rules and safe fields list.

**Acceptance criteria:**
- Trace metadata contains no sensitive plaintext content.
- Policy documented in `docs/observability/openrouter-broadcast.md`.

---

### C5. Sampling and feature flags

**Task:** Add config switches for Broadcast behavior.

- New settings:
  - `Analysis__BroadcastEnabled`
  - `Analysis__BroadcastSampleRate`
  - `Analysis__BroadcastForceForErrors`
- Wire to `Settings.cs`, `appsettings.json`, `docker-compose.yml`, `.env.example`.

**Acceptance criteria:**
- Broadcast can be disabled without code changes.
- Sampling is respected at runtime.
- Error calls are always broadcast when force flag is enabled.

---

### C6. Sink integration (Webhook/Langfuse-compatible)

**Task:** Export broadcast events to one external sink for troubleshooting and analytics.

- Normalize events by `session_id`/`slice_id`/`phase`.
- Store minimal latency, status, model, token/cost info.

**Acceptance criteria:**
- At least one sink receives structured events continuously.
- Failed calls are visible with enough context to debug.

---

### C7. Alerts for summary gaps and failure spikes

**Task:** Add practical alerts tied to known incidents.

- "cheap observed but no summary within N minutes"
- "summary error rate spike"
- "phase-level failures above threshold"

**Acceptance criteria:**
- At least 3 active alerts with documented thresholds and owners.

---

### C8. Coverage report: cheap vs summary

**Task:** Provide periodic report showing pipeline coverage and lag.

- Metrics: cheap count, summary count, ratio, avg/max lag to summary.
- Group by chat/session/slice window.

**Acceptance criteria:**
- One command/script generates a readable report for the last 24h.

---

### C9. Rollout and rollback plan

**Task:** Ship broadcast incrementally with fast fallback.

- Stages: dev -> canary -> production.
- Define rollback procedure and owner.

**Acceptance criteria:**
- Runbook contains enable/disable and rollback steps.
- Rollback is tested at least once in non-prod.

---

### C10. Operations runbook update

**Task:** Add incident playbook for "summary missing after slices".

- How to trace by `slice_id/session_id`.
- How to detect dropped/filtered calls.
- How to confirm recovery.

**Acceptance criteria:**
- New section in ops docs with step-by-step flow.

---

## Track D — AnalysisWorkerService refactor

### D1. Baseline and safety harness

**Task:** Capture baseline metrics and define refactor safety checks before moving logic.

- Record current method map and line count for `AnalysisWorkerService`.
- Capture one reference run snapshot: processed sessions, chunk count, cheap/summary usage counts, errors.
- Document non-regression checklist for Stage5 runtime behavior.

**Acceptance criteria:**
- New doc `docs/reports/analysis-worker-refactor-baseline.md`.
- Baseline includes command list and reference output values.

---

### D2. Extract SessionSlicePlanner service

**Task:** Move session/slice planning and skip/quarantine mechanics out of `AnalysisWorkerService`.

- Extract methods related to slicing and session indexing:
  - `EnsureSessionSlicesForMessagesAsync`
  - `SplitByGap`
  - `ResolveTargetSessionIndex`
  - skip-counter helpers and quarantine logic.
- Keep persistence through existing repositories.

**Acceptance criteria:**
- `AnalysisWorkerService` no longer contains slice-building internals.
- Behavior of session creation and quarantine counters remains unchanged.

---

### D3. Extract CheapPassPipeline service

**Task:** Move cheap-pass orchestration and chunk execution to a dedicated service.

- Extract:
  - `ProcessCheapBatchesAsync`
  - `ProcessCheapBatchAsync`
  - `TryProcessChunkOneByOneAsync`
  - cheap chunk request/model map helpers.
- Keep current retries/fallback semantics.

**Acceptance criteria:**
- Cheap pass runs through one injected service.
- No change in extraction write path and failure handling.

---

### D4. Extract SessionSummary service

**Task:** Move session summary generation and post-slice summary gating into dedicated service.

- Extract:
  - `EnsureSessionSummaryAfterSliceAsync`
  - `GenerateSessionSummaryAsync`
  - summarizable message filtering and summary parsing helpers.
- Preserve current “summary after each slice pass” expectation.

**Acceptance criteria:**
- Summary path is isolated and testable.
- Existing summary model usage and storage semantics are preserved.

---

### D5. Extract OpenRouterRecoveryGate service

**Task:** Isolate provider recovery wait and probe logic.

- Extract:
  - `WaitForOpenRouterRecoveryAsync`
  - fallback/recovery gating helpers.
- Keep configurable timing from `AnalysisSettings`.

**Acceptance criteria:**
- Recovery loops are removed from orchestrator class.
- Runtime pause/resume behavior remains identical.

---

### D6. Shrink AnalysisWorkerService to orchestrator

**Task:** Keep only top-level loop and coordination in `AnalysisWorkerService`.

- Class should orchestrate: expensive backlog, reanalysis, session-first pass, seeding, delays.
- Push implementation details to extracted services.

**Acceptance criteria:**
- `AnalysisWorkerService.cs` target size: <= 600 lines.
- No direct business-heavy helpers left in orchestrator.

---

### D7. DI and wiring cleanup

**Task:** Register new services and remove obsolete dependencies.

- Update `Program.cs` DI registrations.
- Ensure constructor dependency graph is minimal and coherent.

**Acceptance criteria:**
- App starts without DI resolution errors.
- No dead dependencies in `AnalysisWorkerService` constructor.

---

### D8. Documentation update

**Task:** Update Stage5 architecture docs for new service boundaries.

- Add component map and responsibility table.
- Include execution path: session -> chunk -> cheap -> apply -> summary.

**Acceptance criteria:**
- Docs updated under `docs/` with current class ownership map.

---

### D9. Build and runtime verification

**Task:** Validate refactor with build + smoke run checks.

- Run `dotnet build TelegramAssistant.sln`.
- Run container/log smoke checks on Stage5 pass:
  - session processing progress
  - cheap calls present
  - summary calls present
  - no new fatal/error spikes.

**Acceptance criteria:**
- Build succeeds.
- Smoke run confirms parity on core Stage5 signals.

---

### D10. Commit strategy

**Task:** Deliver refactor in small, reviewable commits.

- Suggested commit slices:
  1. planner extraction
  2. cheap pipeline extraction
  3. summary extraction
  4. recovery gate extraction
  5. orchestrator cleanup + docs

**Acceptance criteria:**
- Each commit builds independently.
- Commit messages are imperative and scoped.

---

## Architecture decisions baseline (2026-03-17)

These decisions are agreed and should be treated as source-of-truth for new work.

1. Tentative facts use **manual review gate only** (no auto-approve queueing).
2. MCP target is **dual transport**:
   - `stdio` for local/Codex usage
   - `SSE/HTTP` for remote clients (including Claude)
3. Session-first must not hard-block on missing previous summary:
   - use bootstrap prompt for dialog start marker
   - include optional pre-dialog context when available
4. Redis ingestion SLA is **at-least-once**, with mandatory PEL reclaim.
5. `TestModeMaxSessionsPerChat` is test-only and must not shape production behavior.
6. Summary policy:
   - summary is generated right after slice processing
   - summary can also be triggered by manual bot command
   - avoid duplicate background re-generation in steady state

---

## Track E — Stabilization backlog from architecture review

### E1. Remove auto-approve for tentative facts (P0)

**Files:** `ExtractionApplier.cs`, `FactReviewCommandWorkerService.cs`, MCP/write command path.

**Task:**
- Stop enqueueing `"approve"` automatically for tentative facts.
- Keep tentative status until explicit manual command.
- Ensure review worker applies only explicit operator commands.

**Acceptance criteria:**
- Tentative facts never become `Confirmed` without manual action.
- Existing approve/reject commands still work.

**Validation:**
- E2E flow: create low-confidence fact -> verify `Tentative` persists -> send manual approve -> verify `Confirmed`.

---

### E2. Add Redis PEL reclaim and pending observability (P0)

**Files:** `src/TgAssistant.Infrastructure/Redis/RedisMessageQueue.cs`, ingestion worker metrics/logs.

**Task:**
- Add reclaim cycle for consumer group pending entries (`XPENDING` + `XAUTOCLAIM`/`XCLAIM`).
- Run reclaim on startup and periodically.
- Add metrics/log fields for pending count and oldest pending age.

**Acceptance criteria:**
- Messages read but not ACKed before crash are reprocessed after restart.
- Pending queue is observable from app metrics/logs.

**Validation:**
- Integration scenario: read without ACK -> crash -> restart -> message processed exactly once semantically (idempotent DB path), ACKed in Redis.

---

### E3. Persist expensive backoff per model (P0)

**Files:** `ExpensivePassResolver.cs`, `analysis_state` key handling.

**Task:**
- Replace global expensive backoff key with per-model keys:
  - `stage5:expensive:blocked_until:{model}`
  - `stage5:expensive:streak:{model}`
- Load persisted state on startup.

**Acceptance criteria:**
- Blocking one model does not block all expensive models.
- Restart preserves model-level backoff state.

**Validation:**
- Simulate failures on primary model; confirm fallback model still runs and state keys are isolated.

---

### E4. Bootstrap previous-summary fallback for session-first (P0)

**Files:** `AnalysisWorkerService.cs`, summary prompt builders.

**Task:**
- Remove hard block when previous session summary is missing.
- For first valid slice without summary chain, inject bootstrap prompt:
  - explicit marker that dialog starts from this slice/time boundary
  - optional pre-dialog context block if available.
- Keep extraction continuity without dropping slice processing.

**Acceptance criteria:**
- Missing previous summary no longer deadlocks later sessions.
- Extraction still runs for current slice with clear bootstrap context.

**Validation:**
- Seed chat sessions with empty previous summary and verify next session reaches analyzed state and produces summary.

---

### E5. Enforce summary ownership policy (post-slice + manual trigger) (P1)

**Files:** `AnalysisWorkerService.cs`, `DialogSummaryWorkerService.cs`, bot command handlers.

**Task:**
- Keep mandatory summary generation after slice processing.
- Add/keep manual bot command to trigger re-summary.
- Prevent duplicate summary generation in steady state by separating:
  - normal post-slice path (default)
  - explicit repair/rebuild path (manual or flagged jobs).

**Acceptance criteria:**
- One normal summary generation per processed slice/session.
- Manual command can force re-summary safely.

**Validation:**
- Compare `analysis_usage_events` summary-phase count before/after and verify reduced duplicate calls.

---

### E6. MCP dual-transport implementation (stdio + SSE/HTTP) (P1)

**Files:** MCP server project (`mcp/` target structure or `src/TgAssistant.Mcp/` until migration), docker compose integration.

**Task:**
- Keep `stdio` transport for local toolchains (Codex).
- Add `SSE/HTTP` transport for remote MCP clients (Claude and others).
- Make transport selectable by env/config (`MCP_TRANSPORT=stdio|sse`).
- Preserve existing read tools and add missing write tools per Track B plan.

**Acceptance criteria:**
- Server starts in stdio mode and sse mode with identical tool registry.
- SSE endpoint works with auth and localhost bind policy.

**Validation:**
- `npm run build` (or equivalent MCP build), local stdio smoke call, SSE smoke call via HTTP client.

---

### E7. Separate test/prod session cap behavior (P1)

**Files:** analysis settings + workers using `TestModeMaxSessionsPerChat`.

**Task:**
- Ensure test-mode cap is never applied implicitly in production.
- Introduce explicit production-safe limit flag if needed, with clear default semantics.
- Add metrics for skipped/quarantined sessions due to caps.

**Acceptance criteria:**
- Production path is not constrained by test cap defaults.
- Any cap-induced skip is visible in telemetry.

**Validation:**
- Synthetic large-chat run confirms no unintended quarantine from test setting.

---

### E8. Docs and backlog alignment pass (P2)

**Files:** `README.md`, `AGENTS.md`, `CODEX_BACKLOG.md`, runtime key docs.

**Task:**
- Align docs with actual topology and selected architecture decisions.
- Document runtime state keys and summary ownership model.
- Reconcile MCP target-state vs implementation plan.

**Acceptance criteria:**
- No contradictions between docs for Stage5 summary flow, MCP transport, and runtime keys.

**Validation:**
- Consistency checklist + grep-based verification script.

---

## Execution order recommendation

**Phase 0 (stabilization baseline from 2026-03-17 decisions):**
E1 → E2 → E3 → E4 → E5 → E6 → E7 → E8

**Phase 1 (data integrity + quality):**
A2 → A5 → A6 → A8

**Phase 2 (architecture cleanup):**
A1 → A3 → A4 → A9 → A10

**Phase 3 (MCP server):**
B1 → B2 → B3 → B4 → B5 → B6 → B7 → B8 → B9

**Phase 4 (layer cleanup, low priority):**
A7 → A11 → B10

**Phase 5 (broadcast observability):**
C1 → C2 → C3 → C5 → C4 → C6 → C7 → C8 → C9 → C10

**Phase 6 (analysis worker refactor):**
D1 → D2 → D3 → D4 → D5 → D6 → D7 → D8 → D9 → D10

Each task is independent enough to be a single commit/PR.
A1 remains the largest refactor task.

---

## Sprint plan (non-blocking + multi-agent orchestration)

Goal: keep product development moving while stabilization/refactor tasks run in parallel with minimal merge conflicts.

### Sprint S0 (1-2 days) — Baseline and branch strategy

**Scope:**
- Freeze architecture decisions from 2026-03-17 baseline.
- Define ownership map for agents (by files/modules).
- Prepare integration checklist for fast parallel merges.

**Deliverables:**
- `main` branch protection + small PR policy.
- File ownership matrix for parallel agents.
- Smoke commands pinned for each PR (`dotnet build`, MCP build when touched).

---

### Sprint S1 (P0 hardening, parallel) — E1 + E2 + E3 + E4

Run as 4 parallel lanes.

**Lane A (Core facts):** E1  
**Files:** `ExtractionApplier.cs`, fact-review command flow.

**Lane B (Ingestion reliability):** E2  
**Files:** `RedisMessageQueue.cs`, ingestion telemetry.

**Lane C (Expensive isolation):** E3  
**Files:** `ExpensivePassResolver.cs`, `analysis_state` keys.

**Lane D (Session continuity):** E4  
**Files:** `AnalysisWorkerService.cs`, summary context/prompt builder.

**Merge order:** B -> C -> A -> D  
Reason: reliability primitives first, then state model, then behavior changes, then session-path fallback.

**Exit criteria:**
- No queue deadlocks after crash/restart scenario.
- Tentative facts require explicit manual approval.
- Per-model expensive backoff survives restart.
- Missing previous summary no longer blocks downstream session processing.

---

### Sprint S2 (P1 behavior + MCP foundation, parallel) — E5 + E6(part1) + E7

Run as 3 parallel lanes.

**Lane A (Summary ownership):** E5  
**Files:** `AnalysisWorkerService.cs`, `DialogSummaryWorkerService.cs`, bot command entrypoints.

**Lane B (MCP transport foundation):** E6 part 1 + B1-B4  
**Files:** MCP project structure, transport selection (`stdio|sse`), read-tools parity.

**Lane C (Runtime limits):** E7  
**Files:** analysis settings + workers using `TestModeMaxSessionsPerChat`.

**Merge order:** C -> A -> B  
Reason: reduce runtime surprises first, then summary flow policy, then external MCP interface layer.

**Exit criteria:**
- One default summary pass per slice/session in steady-state.
- Manual re-summary command works.
- Production path independent from test cap defaults.
- MCP starts in both `stdio` and `sse` modes with same read-tool registry.

---

### Sprint S3 (MCP completion + ops integration) — E6(part2) + B5-B9

**Scope:**
- Write tools (management/ops commands).
- Docker integration and localhost bind policy.
- Auth middleware for SSE endpoint.

**Parallel lanes:**
- Lane A: write-tools (`B5`, `B6`)
- Lane B: container/runtime (`B7`, `B8`)
- Lane C: auth and hardening (`B9`)

**Exit criteria:**
- End-to-end MCP workflow works for Codex and Claude clients.
- SSE endpoint authenticated and deployable via docker-compose.

---

### Sprint S4 (Quality and observability without blocking feature work) — A2/A5/A6/A8 + C1-C5

**Scope:**
- Data-integrity improvements in Stage5 path.
- Broadcast observability foundations and sampling controls.

**Parallelization rule:**
- One agent owns Stage5 data-path changes (A-tasks).
- One agent owns telemetry schema/injection (C1-C3).
- One agent owns privacy/sampling policy (C4-C5).

**Exit criteria:**
- Transactional integrity and quality safeguards in extraction apply path.
- Trace coverage available with privacy-safe defaults.

---

### Sprint S5 (Refactor track, low coupling to delivery) — D1-D10 + remaining A/B/C tails

**Scope:**
- Controlled decomposition of `AnalysisWorkerService`.
- Finish optional/low-priority items (`A7`, `A11`, `B10`, `C6-C10`).

**Rule:**
- Refactor PRs are small, behavior-preserving, and continuously rebased on stabilized code from S1-S4.

**Exit criteria:**
- Orchestrator shrunk to target size.
- Runtime parity verified by smoke metrics/logs.

---

## Multi-agent orchestration protocol

1. Create one orchestrator branch per sprint and one feature branch per lane (`s1-lane-a-*`, `s1-lane-b-*`, ...).
2. Assign strict file ownership per lane to avoid overlap in the same sprint.
3. Require lane-level smoke checks before merge:
   - C# touched: `dotnet build TelegramAssistant.sln`
   - MCP touched: MCP build command for the active MCP project
4. Merge lanes in planned order; rebase remaining lanes after each merge.
5. Keep a daily integration window for conflict resolution and end-to-end smoke run.
6. Defer cross-cutting refactors to S5 unless they are required to unblock a P0/P1 fix.
