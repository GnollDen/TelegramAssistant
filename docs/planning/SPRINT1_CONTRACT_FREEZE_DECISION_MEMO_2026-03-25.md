# Sprint 1 Contract Freeze Decision Memo

## Date

2026-03-25

## Scope Analyzed

- Authority docs only:
  - `docs/planning/README.md`
  - `docs/planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md`
  - `docs/planning/PRE_SPRINT0_GATE_NOTE_2026-03-25.md`
  - `docs/planning/PRE_SPRINT0_RUNTIME_ROLE_HEALTH_CONTRACT_2026-03-25.md`
  - `docs/planning/RUNTIME_TOPOLOGY_NOTE_2026-03-25.md`
- Current host/runtime files only:
  - `src/TgAssistant.Host/Program.cs`
  - `src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs`
  - `src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs`
  - `src/TgAssistant.Host/Startup/InfrastructureRegistrationExtensions.cs`
  - `src/TgAssistant.Host/Startup/SettingsRegistrationExtensions.cs`
  - `src/TgAssistant.Host/appsettings.json`
  - `docker-compose.yml`
  - `deploy/docker-compose.m1.preview.yml`

## Confirmed Current-State Evidence

- Runtime roles are currently fail-open:
  - missing role config defaults to `all`
  - invalid tokens fall back to `all`
- Current `--healthcheck` is one command that probes DB and Redis only.
- Current default deploy path is one `app` service with no pinned runtime role.
- Current host wiring can run `Ingest`, `Stage5`, `Ops`, and `Maintenance`; `Stage6` and `Web` are presently command/read-surface roles with no hosted-service startup work in this host.
- `Mcp` is not a host workload here; current code comments and preview compose treat it as a separate process.

## Sprint 1 Decisions

### 1. Fail-Closed Runtime Role Policy

- `RuntimeRoleSelection` must reject missing, empty, unknown, duplicated-to-empty, or `all` role input.
- No code path may widen to `All`.
- Allowed host role tokens in Sprint 1:
  - `ingest`
  - `stage5`
  - `stage6`
  - `web`
  - `ops`
  - `maintenance`
- `mcp` is not accepted by the .NET host role parser in Sprint 1.

### 2. Allowed Role Combinations Now

Primary rule:
- one pinned functional role per instance
- `ops` may be co-located only where explicitly listed below
- `maintenance` is a Stage5-only adjunct

Allowed now:
- `ingest`
- `ingest,ops`
- `stage5`
- `stage5,maintenance`
- `stage6`
- `web`
- `web,ops`

Temporary compatibility carve-out for the current single-app bot runtime:
- `ingest,stage5,maintenance,ops`
- This is Sprint-1-only bridge behavior because the current default `docker-compose.yml` still runs one host instance.
- This carve-out must be explicit, never implicit, and must not expand beyond this exact set.

Not allowed now:
- empty role config
- unknown tokens
- `all`
- `ops` alone
- `maintenance` alone
- any other multi-functional combination

### 3. Readiness vs Liveness Command Semantics

- `--liveness-check`
  - process-oriented only
  - must not require DB, Redis, Telegram, or model-provider reachability
  - success means the process can start, parse config, and build the host

- `--readiness-check`
  - role-aware admission check
  - must fail when pinned role config is invalid
  - must fail when DB or Redis is unavailable
  - for the temporary compatibility combo, readiness is still one result for the full pinned set
  - must not silently pass if startup had to downgrade or widen roles

- `--healthcheck`
  - retained in Sprint 1 as a compatibility alias to `--readiness-check`
  - reason: current compose already uses `--healthcheck`; changing that meaning to liveness would hide real not-ready states in the live deploy path

### 4. Startup Unsafe-Config Guards

Hard fail at startup when:
- runtime role is missing, empty, unknown, or disallowed
- selected role set is not in the allowed-combination list above
- DB connection string is missing or still uses placeholder password material
- Redis connection string is missing
- selected role includes `ingest` and Telegram API credentials are incomplete
- selected role includes `ops` and bot token is missing
- selected role includes `ops` and effective owner id is missing after `BotChat.OwnerId -> Telegram.OwnerUserId` fallback

Warn, but do not block, in Sprint 1 when:
- `stage6` or `web` are selected in this host and no hosted services are started for them
- `BotChat.DefaultCaseId` / `BotChat.DefaultChatId` are unset
- optional providers such as Neo4j are disabled

### 5. Insecure Default Removal Boundaries

Remove in Sprint 1:
- code-level fallback to role `all`
- code-level fallback DB password/connection defaults such as `changeme`
- deploy-time placeholder secrets in compose for:
  - `POSTGRES_PASSWORD`
  - `MCP_SSE_AUTH_TOKEN`
- deploy-time sentinel owner default `BOTCHAT_OWNER_ID:-0`

Keep in Sprint 1:
- local sample values in checked-in `appsettings.json` only if startup guards reject them for real runtime use
- localhost/127.0.0.1 hostnames that match the current single-host deployment topology
- non-secret feature defaults that do not widen runtime scope

Out of Sprint 1 boundary:
- unique Redis consumer naming per instance
- queue reclaim redesign
- broader multi-process runtime split beyond the one explicit compatibility carve-out

## Smallest Recommended Fix Set

- Make runtime-role parsing fail closed and combination-validated.
- Add explicit `--liveness-check` and `--readiness-check`; keep `--healthcheck` as readiness alias.
- Add startup validation before normal host run.
- Pin the default compose `app` service to the explicit temporary compatibility role set.
- Remove deploy-time placeholder secrets and code-level secret fallbacks.

Expected risk reduction:
- eliminates silent role widening
- separates process-up from safe-to-admit-work
- blocks known bad deploys before they become partial live failures
- preserves the current bot runtime without forcing Sprint 2 topology work

Tradeoff cost:
- stricter startup will reject some previously tolerated local/deploy configs
- default compose must become explicit about role and required secrets

## Acceptance Checklist

- [ ] Missing runtime role fails startup.
- [ ] Unknown role token fails startup.
- [ ] `all` fails startup.
- [ ] Disallowed combinations fail startup.
- [ ] Allowed combinations above pass startup.
- [ ] Current single-app compose path passes only with explicit `ingest,stage5,maintenance,ops`.
- [ ] `--liveness-check` succeeds when the process can build even if DB/Redis are intentionally unavailable.
- [ ] `--readiness-check` fails when DB is unavailable.
- [ ] `--readiness-check` fails when Redis is unavailable.
- [ ] `--healthcheck` matches `--readiness-check` result in Sprint 1.
- [ ] `ingest` startup fails when Telegram API credentials are incomplete.
- [ ] `ops` startup fails when bot token is missing.
- [ ] `ops` startup fails when effective owner id is missing.
- [ ] Placeholder DB secret material is rejected at startup.
- [ ] Compose no longer supplies placeholder secret defaults for production-facing services.

## File Targets

- `src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs`
  - fail-closed parsing
  - allowed-combination validation
  - remove `all`/invalid fallback behavior

- `src/TgAssistant.Host/Program.cs`
  - add `--liveness-check`
  - add `--readiness-check`
  - keep `--healthcheck` as readiness alias
  - run startup config validation before normal host execution

- `src/TgAssistant.Host/Startup/SettingsRegistrationExtensions.cs`
  - add role-aware config validation hooks

- `src/TgAssistant.Host/Startup/InfrastructureRegistrationExtensions.cs`
  - remove code-level DB/Redis unsafe fallbacks

- `docker-compose.yml`
  - pin the `app` role explicitly to the Sprint-1 compatibility combo
  - remove placeholder secret defaults and owner-id sentinel default
  - keep current single-app topology

- `src/TgAssistant.Host/appsettings.json`
  - keep as sample-only baseline
  - do not rely on it for deploy-safe secrets or role defaults

- `deploy/docker-compose.m1.preview.yml`
  - align token/combination wording with the Sprint 1 contract only if touched for consistency

## Runtime Verification Still Needed

- one normal path:
  - default compose app with explicit `ingest,stage5,maintenance,ops`
- one failure path:
  - invalid role token and missing bot token
- one integration edge:
  - readiness false with DB/Redis outage while liveness stays true

Residual risk after Sprint 1:
- temporary single-app compatibility combo still has soft isolation boundaries
- Stage6/Web host roles remain only partially deployable in this host
- Redis consumer collision risk remains until Sprint 2
