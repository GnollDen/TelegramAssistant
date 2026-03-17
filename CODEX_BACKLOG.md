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

## Execution order recommendation

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

Each task is independent enough to be a single commit/PR.
A1 remains the largest refactor task.
