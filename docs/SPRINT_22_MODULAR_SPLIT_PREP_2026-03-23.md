# Sprint 22 Modular Split M1 - Prep (No Production Rollout)

## Scope

This prep package defines M1 workload boundaries and startup role mapping, but does not switch active production deployment while Stage 5 tail is running.

## Target workloads

- `tga-ingest`
- `tga-stage5`
- `tga-stage6`
- `tga-web`
- `tga-mcp`

## Runtime role mapping (prep)

Current role parser supports comma-separated role sets (`--runtime-role=...`).

Planned mapping:
- `tga-ingest` -> `ingest,ops`
- `tga-stage5` -> `stage5,maintenance`
- `tga-stage6` -> `stage6`
- `tga-web` -> `web,ops`
- `tga-mcp` -> MCP container/process (already isolated path)

Rationale:
- ingest keeps Telegram session ownership,
- stage5 isolation reduces coupling to stage6/web experiments,
- ops surfaces remain available without coupling to ingest loop.

## Existing prep artifacts

- Runtime-role startup decomposition:
  - `src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs`
  - `src/TgAssistant.Host/Startup/HostedServiceRegistrationExtensions.cs`
- Preview compose split (non-default path):
  - `deploy/docker-compose.m1.preview.yml`

## Ownership constraints

- Telegram connectivity/session remains owned by ingest role.
- Stage5 role must not run ingest listener/bot loop.
- Stage6/web roles must not claim ingest ownership implicitly.
- One repository, one database, one migration lineage remain unchanged.

## Rollout hold policy

Do not activate M1 split in production until:
1. active Stage 5 tail is completed,
2. Sprint 20 safety rails rollout window is executed first,
3. runtime wiring check passes for each M1 workload role,
4. no duplicate role ownership is detected at startup.

## Post-tail verification checklist

- `dotnet run --project src/TgAssistant.Host -- --runtime-role=ingest --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage5 --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=stage6 --runtime-wiring-check`
- `dotnet run --project src/TgAssistant.Host -- --runtime-role=web --runtime-wiring-check`
- preview compose up with `--profile m1-preview` in isolated non-prod environment only.
