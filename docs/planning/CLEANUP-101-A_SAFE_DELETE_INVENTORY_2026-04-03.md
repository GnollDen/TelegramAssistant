# CLEANUP-101-A Safe Delete Inventory (2026-04-03)

Scope: inventory-only confirmation for `CLEANUP-101-A` (no broad deletions applied in this slice).

## Candidate Set (bounded)

1. `src/TgAssistant.Web/` (directory residue)
2. Tracked local SDK cache/sentinel files under `.dotnet/.dotnet/`:
   - `.dotnet/.dotnet/.workloadAdvertisingManifestSentinel9.0.300`
   - `.dotnet/.dotnet/.workloadAdvertisingUpdates9.0.300`
   - `.dotnet/.dotnet/9.0.311.aspNetCertificateSentinel`
   - `.dotnet/.dotnet/9.0.311.dotnetFirstUseSentinel`
   - `.dotnet/.dotnet/9.0.311.toolpath.sentinel`
   - `.dotnet/.dotnet/9.0.311_IsDockerContainer.dotnetUserLevelCache`
   - `.dotnet/.dotnet/9.0.311_MachineId.dotnetUserLevelCache`
   - `.dotnet/.dotnet/MachineId.v1.dotnetUserLevelCache`
   - `.dotnet/.dotnet/TelemetryStorageService/20260307154602_f312ded81dea4cddb0ad26df0c7721aa.trn`
   - `.dotnet/.dotnet/TelemetryStorageService/20260307154609_5e3ed45573ee44069e4acddbf9103cc6.trn`
   - `.dotnet/.dotnet/TelemetryStorageService/20260307154609_f68ece69d6f74a4db704f1a893dd0876.trn`
   - `.dotnet/.dotnet/TelemetryStorageService/20260307154609_f8bf1a89cdc1492cab32e85d2f67b99a.trn`
   - `.dotnet/.dotnet/TelemetryStorageService/20260307154610_296cb59d4fa24f9b9c795a3d0f8abda7.trn`
   - `.dotnet/.dotnet/TelemetryStorageService/20260307154612_78ab627da63642d1aafbc6d0bab351eb.trn`

## Evidence: no active callers / references

### `src/TgAssistant.Web/`

- Filesystem check:
  - `find src/TgAssistant.Web -maxdepth 3 -type f | sort`
  - Result: only `obj/*` intermediate files, no source or project files.
- Tracked-file check:
  - `git ls-files | rg '^src/TgAssistant\\.Web/'`
  - Result: no tracked files.
- Solution/runtime wiring checks:
  - `cat TelegramAssistant.sln`
  - Result: no `TgAssistant.Web` project entry.
  - `rg -n "TgAssistant\\.Web" --glob '!tasks.json' --glob '!task_slices.json' --glob '!docs/**'`
  - Result: no matches.
  - `rg -n "TgAssistant\\.Web|\\.dotnet" TelegramAssistant.sln docker-compose.yml README.md src .github deploy --glob '*.csproj' --glob '*.yml' --glob '*.yaml' --glob '*.md' --glob '*.sln'`
  - Result: no matches.

Conclusion: orphan residue, safe deletion candidate for CLEANUP-101-B.

### `.dotnet/.dotnet/*` tracked cache/sentinel files

- Enumeration:
  - `git ls-files .dotnet/.dotnet | sort`
  - Result: only SDK first-run/cache/sentinel and telemetry transaction artifacts.
- Caller/reference checks:
  - `rg -n "\\.dotnet/.dotnet|TelemetryStorageService|workloadAdvertising|dotnetFirstUseSentinel|aspNetCertificateSentinel|toolpath\\.sentinel"`
  - Result: no matches.
  - `rg -n "TgAssistant\\.Web|\\.dotnet" TelegramAssistant.sln docker-compose.yml README.md src .github deploy --glob '*.csproj' --glob '*.yml' --glob '*.yaml' --glob '*.md' --glob '*.sln'`
  - Result: no matches.
- Ignore policy check:
  - `.gitignore` contains `.dotnet/`, indicating this surface is non-source local state.

Conclusion: stale tracked local-environment artifacts with no active callers, safe deletion candidates for CLEANUP-101-B.

## Deletion Execution Boundary

- This slice intentionally does not execute broad deletions.
- Proposed execution slice: `CLEANUP-101-B` deletes exactly the paths listed above, then re-runs solution/search validation.
