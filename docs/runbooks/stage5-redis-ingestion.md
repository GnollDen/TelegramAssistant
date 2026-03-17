# Stage5 Redis Ingestion and PEL Reclaim Runbook

Last updated: 2026-03-17.

## Scope

Use this runbook when realtime ingestion stalls, Redis pending grows, or reclaimed entries are not returning to workers.

Defaults used by runtime:
- stream: `tg-messages`
- consumer group: `batch-workers`
- consumer: `worker-1`

## 1) Fast checks

Recent ingestion/reclaim logs:

```bash
docker logs --since 15m tga-app 2>&1 | rg -i "Redis stream pending status|Redis pending reclaim executed|Redis reclaimed messages delivered|Stage5|Batch worker" | tail -n 220
```

Check stream and group state:

```bash
docker exec -i tga-redis redis-cli XINFO STREAM tg-messages
docker exec -i tga-redis redis-cli XINFO GROUPS tg-messages
docker exec -i tga-redis redis-cli XPENDING tg-messages batch-workers
```

Check pending by consumer:

```bash
docker exec -i tga-redis redis-cli XPENDING tg-messages batch-workers - + 50 worker-1
```

## 2) Validate reclaim configuration in app

Confirm reclaim settings in the running app container:

```bash
docker exec -i tga-app /bin/sh -lc 'env | sort | rg "^Redis__"'
```

Key settings:
- `Redis__EnablePendingReclaim=true`
- `Redis__PendingReclaimIntervalSeconds` (default `30`)
- `Redis__PendingMinIdleSeconds` (default `60`)
- `Redis__PendingReclaimBatchSize` (default `100`)
- `Redis__PendingMetricsLogIntervalSeconds` (default `60`)

## 3) Interpret symptoms

- High `XPENDING` + no `Redis pending reclaim executed` logs:
  - reclaim disabled or interval too high.
- Reclaim logs appear but pending stays flat/grows:
  - consumer crashes before `XACK`, or batch worker is blocked.
- Pending mostly on one dead consumer:
  - reclaim should move entries after `PendingMinIdleSeconds`; if not, verify group/stream names and idle threshold.

## 4) Controlled recovery

1. Ensure app is healthy and connected to Redis.
2. Restart only app container to trigger startup reclaim:
   ```bash
   docker compose restart app
   ```
3. Re-check:
   - `XPENDING tg-messages batch-workers`
   - app logs for `Redis pending reclaim executed` and `Redis reclaimed messages delivered`.

## 5) Exit criteria

- Pending count is stable or decreasing.
- Reclaimed messages are delivered and then acknowledged.
- No prolonged ingestion stalls in app logs.
