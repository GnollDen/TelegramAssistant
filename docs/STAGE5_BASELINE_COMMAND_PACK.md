# Stage 5 Full Baseline Command Pack

> Status note (2026-03-30): historical/supporting command pack for prior Stage 5 baseline runs.
> Not an authority doc for Stage 6 remediation sequencing or Stage 6 rebuild planning.

Цель:

- full baseline rebuild
- fresh archive import
- archive media processing
- voice/paralinguistics processing
- Stage 5 cheap analysis
- inline summaries

Без:

- expensive pass
- edit-diff
- embedding worker

Это единый актуальный пакет команд с учетом всех последних решений.

## Runtime target profile

Должно быть включено:

- `ArchiveImport__Enabled=true` на Phase A
- `ArchiveImport__MediaProcessingEnabled=true`
- `Analysis__Enabled=true` на Phase B
- `Analysis__ArchiveOnlyMode=true`
- `Analysis__SummaryEnabled=true`
- `Analysis__SummaryWorkerEnabled=false`
- `VoiceParalinguistics__Enabled=true`

Должно быть выключено:

- `Analysis__ExpensivePassEnabled=false`
- `Analysis__MaxExpensivePerBatch=0`
- `Analysis__EditDiffEnabled=false`
- `Embedding__Enabled=false`

## 0. Перейти в репозиторий

```bash
cd /home/codex/projects/TelegramAssistant
```

## 1. Сохранить backup текущего `.env`

```bash
cp .env ".env.backup.$(date +%F-%H%M%S)"
```

## 2. Применить общий baseline config

```bash
python3 - <<'PY'
from pathlib import Path

path = Path(".env")
text = path.read_text(encoding="utf-8")

updates = {
    "ARCHIVE_IMPORT_REQUIRE_CONFIRMATION": "false",
    "ARCHIVE_IMPORT_CONFIRM_PROCESSING": "true",
    "ARCHIVE_MEDIA_PROCESSING_ENABLED": "true",
    "ANALYSIS_ARCHIVE_ONLY_MODE": "true",
    "ANALYSIS_EXPENSIVE_PASS_ENABLED": "false",
    "ANALYSIS_MAX_EXPENSIVE_PER_BATCH": "0",
    "ANALYSIS_SUMMARY_ENABLED": "true",
    "ANALYSIS_SUMMARY_WORKER_ENABLED": "false",
    "ANALYSIS_SUMMARY_HISTORICAL_HINTS_ENABLED": "false",
    "ANALYSIS_EDIT_DIFF_ENABLED": "false",
    "EMBEDDING_ENABLED": "false",
    "VOICE_PARALINGUISTICS_ENABLED": "true",
    "BUDGET_GUARDRAILS_ENABLED": "true",
    "BUDGET_GUARDRAILS_DAILY_BUDGET_USD": "60.0",
    "BUDGET_GUARDRAILS_IMPORT_BUDGET_USD": "20.0",
    "BUDGET_GUARDRAILS_STAGE_TEXT_BUDGET_USD": "35.0",
    "BUDGET_GUARDRAILS_STAGE_EMBEDDINGS_BUDGET_USD": "1.0",
    "BUDGET_GUARDRAILS_STAGE_VISION_BUDGET_USD": "12.0",
    "BUDGET_GUARDRAILS_STAGE_AUDIO_BUDGET_USD": "12.0",
}

lines = text.splitlines()
seen = set()
out = []
for line in lines:
    if not line or line.lstrip().startswith("#") or "=" not in line:
        out.append(line)
        continue
    key, value = line.split("=", 1)
    key = key.strip()
    if key in updates:
        out.append(f"{key}={updates[key]}")
        seen.add(key)
    else:
        out.append(line)

for key, value in updates.items():
    if key not in seen:
        out.append(f"{key}={value}")

path.write_text("\n".join(out) + "\n", encoding="utf-8")
print("Updated .env with full Stage 5 baseline defaults.")
PY
```

## 3. Dry-run counts before destructive cleanup

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as messages from messages;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as archive_import_runs from archive_import_runs;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as message_extractions from message_extractions;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as facts from facts;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as relationships from relationships;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as text_embeddings from text_embeddings;"
docker compose exec -T postgres redis-cli XPENDING tg-messages batch-workers
```

## 4. Остановить app перед cleanup

```bash
docker compose stop app
```

## 5. Full destructive cleanup

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant <<'SQL'
begin;

delete from draft_outcomes;
delete from strategy_options;
delete from profile_traits;
delete from clarification_answers;
delete from transitions;

delete from fact_review_commands;
delete from merge_decisions;
delete from entity_merge_commands;
delete from entity_merge_candidates;
delete from entity_aliases;
delete from entities;

delete from message_extractions;
delete from intelligence_observations;
delete from intelligence_claims;
delete from facts;
delete from relationships;
delete from communication_events;
delete from chat_sessions;
delete from chat_dialog_summaries;
delete from daily_summaries;
delete from text_embeddings;
delete from analysis_usage_events;
delete from extraction_errors;
delete from archive_import_runs;
delete from messages;

delete from analysis_state where key like 'stage5:%';
delete from ops_budget_operational_states where key like 'stage5%' or key like 'launch_smoke_budget_%' or key like 'archive_media_%' or key like 'voice_paralinguistics%';

commit;
SQL
```

## 6. Optional Redis cleanup only if backlog exists

```bash
docker compose exec -T redis redis-cli XPENDING tg-messages batch-workers
docker compose exec -T redis redis-cli DEL tg-messages
```

Использовать `DEL` только если действительно нужен чистый пустой stream после проверки.

## 7. Проверить clean baseline

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as messages from messages;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as archive_import_runs from archive_import_runs;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as message_extractions from message_extractions;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as facts from facts;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as relationships from relationships;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as text_embeddings from text_embeddings;"
```

## 8. Phase A config: import + media + voice, analysis off

```bash
python3 - <<'PY'
from pathlib import Path
path = Path(".env")
text = path.read_text(encoding="utf-8")
repls = {
    "ARCHIVE_IMPORT_ENABLED=false": "ARCHIVE_IMPORT_ENABLED=true",
    "ANALYSIS_ENABLED=true": "ANALYSIS_ENABLED=false",
    "VOICE_PARALINGUISTICS_ENABLED=false": "VOICE_PARALINGUISTICS_ENABLED=true",
}
for old, new in repls.items():
    text = text.replace(old, new)
if "ARCHIVE_IMPORT_ENABLED=" not in text:
    text += "\nARCHIVE_IMPORT_ENABLED=true\n"
if "ANALYSIS_ENABLED=" not in text:
    text += "\nANALYSIS_ENABLED=false\n"
if "VOICE_PARALINGUISTICS_ENABLED=" not in text:
    text += "\nVOICE_PARALINGUISTICS_ENABLED=true\n"
path.write_text(text, encoding="utf-8")
print("Phase A config applied.")
PY
```

## 9. Render config and start Phase A

```bash
docker compose config >/tmp/tga-compose.phaseA.yml
rg -n "ARCHIVE_IMPORT_|ANALYSIS_ENABLED|VOICE_PARALINGUISTICS_ENABLED|BUDGET_GUARDRAILS_" .env /tmp/tga-compose.phaseA.yml
docker compose up -d --build app
docker compose ps
docker compose exec -T app dotnet TgAssistant.Host.dll --healthcheck
docker compose exec -T app dotnet TgAssistant.Host.dll --runtime-wiring-check
```

## 10. Monitor fresh import + media path

```bash
docker logs --since 15m tga-app 2>&1 | rg -i "Archive import|Archive media processed|Voice paralinguistics|awaiting confirmation|media missing|Budget limited|processed message_id"
```

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as messages from messages;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as archive_import_runs from archive_import_runs;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select processing_status, count(*) from messages group by processing_status order by processing_status;\"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select media_type, count(*) from messages where media_type <> 0 group by media_type order by media_type;\"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select count(*) as pending_archive_media from messages where source = 1 and processing_status = 0 and media_type <> 0 and media_path is not null;\"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select count(*) as voice_pending from messages where media_type in (2,3,4) and media_path is not null and media_paralinguistics_json is null;\"
```

Ждать окончания import/media-pass до стабилизации числа `messages` и падения pending media backlog.

## 11. Phase B config: analysis on, import off

```bash
python3 - <<'PY'
from pathlib import Path
path = Path(".env")
text = path.read_text(encoding="utf-8")
repls = {
    "ARCHIVE_IMPORT_ENABLED=true": "ARCHIVE_IMPORT_ENABLED=false",
    "ANALYSIS_ENABLED=false": "ANALYSIS_ENABLED=true",
}
for old, new in repls.items():
    text = text.replace(old, new)
if "ARCHIVE_IMPORT_ENABLED=" not in text:
    text += "\nARCHIVE_IMPORT_ENABLED=false\n"
if "ANALYSIS_ENABLED=" not in text:
    text += "\nANALYSIS_ENABLED=true\n"
path.write_text(text, encoding="utf-8")
print("Phase B config applied.")
PY
```

## 12. Restart app for Stage 5 baseline

```bash
docker compose up -d app
docker compose exec -T app dotnet TgAssistant.Host.dll --healthcheck
docker compose exec -T app dotnet TgAssistant.Host.dll --runtime-wiring-check
docker compose exec -T app dotnet TgAssistant.Host.dll --stage5-smoke
```

## 13. Monitor Stage 5 baseline

```bash
docker logs --since 15m tga-app 2>&1 | rg -i "Stage5 analysis worker started|archive_only_mode|cheap batch|summary|session chunk|hard pause|quota|budget|Voice paralinguistics"
```

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as message_extractions from message_extractions;"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select count(*) as analyzed_sessions from chat_sessions where is_analyzed=true;\"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select phase, model, sum(total_tokens) as tokens, sum(cost_usd) as cost_usd from analysis_usage_events where created_at >= now() - interval '1 hour' group by phase, model order by cost_usd desc;\"
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c \"select key, state, reason, updated_at from ops_budget_operational_states order by updated_at desc limit 30;\"
```

## 14. Emergency pause

```bash
python3 - <<'PY'
from pathlib import Path
path = Path(".env")
text = path.read_text(encoding="utf-8")
text = text.replace("ANALYSIS_ENABLED=true", "ANALYSIS_ENABLED=false")
text = text.replace("ARCHIVE_IMPORT_ENABLED=true", "ARCHIVE_IMPORT_ENABLED=false")
path.write_text(text, encoding="utf-8")
print("ANALYSIS_ENABLED=false and ARCHIVE_IMPORT_ENABLED=false")
PY

docker compose up -d app
```

## 15. Expected behavior

### Phase A

Должны идти:

- fresh archive import
- archive media processing
- voice/paralinguistics processing

Не должны идти:

- Stage 5 cheap analysis
- expensive pass
- embedding worker
- edit-diff

### Phase B

Должны идти:

- Stage 5 cheap analysis
- inline summary-assisted chunk flow

Могут продолжать идти:

- voice/paralinguistics backlog, если не успел закончиться

Не должны идти:

- expensive pass
- edit-diff
- embedding worker
