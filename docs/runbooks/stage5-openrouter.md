# Stage5 OpenRouter Runbook

Last updated: 2026-03-17.

## Scope

Use this runbook when Stage5 throughput drops, OpenRouter starts failing, or LLM cost suddenly spikes.

## 1) Fast triage (first 5 minutes)

Check recent Stage5/OpenRouter failures, retries, and cooldowns:

```bash
docker logs --since 15m tga-app 2>&1 | rg -i "OpenRouter transient failure|OpenRouter request failed without retry|balance/quota issue|cheap phase blocked by cooldown|Stage5 session chunk failed|Stage5 summary loop failed" | tail -n 200
```

Check current expensive backlog:

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as expensive_backlog from message_extractions where needs_expensive=true;"
```

Check cost/tokens by phase+model for the last hour:

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select phase, model, sum(total_tokens) as tokens, sum(cost_usd) as cost_usd from analysis_usage_events where created_at >= now() - interval '1 hour' group by phase, model order by cost_usd desc;"
```

## 2) Stage5 progress and queue health

Session analysis queue and PEL-related state:

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select count(*) as pending_sessions from chat_sessions where is_analyzed=false;"
docker logs --since 15m tga-app 2>&1 | rg -i "pending|reclaim|xautoclaim|session-first pass done|session chunk pass done" | tail -n 200
```

Checkpoint keys (spot-check):

```bash
docker compose exec -T postgres psql -U tgassistant -d tgassistant -c "select key, value, updated_at from analysis_state where key like 'stage5:%' order by updated_at desc limit 50;"
```

## 3) Typical symptoms and likely causes

- `OpenRouter transient failure; retrying ...`
  - transient provider/network problem, auto-retries are active.
- `OpenRouter balance/quota issue ...`
  - credit/quota exhausted or billing problem; cheap phase may enter cooldown.
- `OpenRouter cheap phase blocked by cooldown ...`
  - expected short pause after repeated balance/quota failures.
- `Stage5 summary loop failed`
  - summary pipeline issue (prompt/model/provider/DB), check stack trace near this log.

## 4) Stabilization actions

1. If balance/quota is exhausted, restore provider budget first; cooldown clears automatically.
2. If failures are model-specific, switch to a healthy model in config and restart app.
3. If expensive backlog is growing too fast, temporarily reduce expensive load and prioritize cheap/session-first progress.
4. If summary failures persist, keep session-first running and investigate summary prompts/model separately.

## 5) Exit criteria

- No new balance/quota/cooldown warnings in the last 10-15 minutes.
- `session-first pass done` and/or `session chunk pass done` logs resume.
- Expensive backlog is stable or decreasing.
- Hourly cost growth is within budget expectations.
