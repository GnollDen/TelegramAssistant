# CI/CD Pipeline Setup

## Architecture

```
[Push to main] → [GitHub Actions] → [Build Docker image] → [Push to GHCR]
                                                                    ↓
                              [VPS] ← [SSH: docker pull + restart] ←┘
```

Default deploy path: preserved substrate/runtime shell only (`app`, `mcp`, `postgres`, `redis`, monitoring).
Legacy web/bot/Stage6 diagnostic surfaces are not part of the accepted baseline and are not deployed by default.

Default `docker compose up -d` service set:
- `postgres`
- `redis`
- `app`
- `mcp`
- `postgres-exporter`
- `redis-exporter`
- `prometheus`
- `grafana`

Startup-critical env for the default stack:
- `POSTGRES_PASSWORD`
- `GRAFANA_ADMIN_PASSWORD`
- `MCP_SSE_AUTH_TOKEN`
- `TG_API_ID`
- `TG_API_HASH`
- `TG_PHONE`
- `TG_OWNER_ID`
- `GEMINI_API_KEY`
- `CLAUDE_API_KEY`

Optional Grafana exposure env:
- `GRAFANA_BIND_ADDRESS` (default `127.0.0.1`)
- `GRAFANA_SERVER_DOMAIN` (default `localhost`)
- `GRAFANA_SERVER_ROOT_URL` (default Grafana internal protocol/domain/port pattern)
- `GRAFANA_SERVER_SERVE_FROM_SUB_PATH` (default `false`)

Operational note:
- `--seed-bootstrap-scope` can run under `Runtime__Role=ops`. That bounded path still needs the DB/Redis baseline, but it does not require Telegram ingest env unless you intentionally use an ingest-bearing runtime role.

## GitHub Repository Secrets

Go to: Repository → Settings → Secrets and variables → Actions

Add these secrets:

| Secret | Value | How to get |
|--------|-------|------------|
| `VPS_HOST` | IP address of your VPS | From hosting provider |
| `VPS_USER` | `tgassistant` | Created by setup-vps.sh |
| `VPS_SSH_KEY` | Private SSH key | `ssh-keygen -t ed25519` |
| `VPS_SSH_PORT` | `22` (or custom) | Your SSH port |
| `GHCR_TOKEN` | GitHub PAT | github.com/settings/tokens |

### Generate SSH Key Pair

```bash
# On your local machine
ssh-keygen -t ed25519 -C "github-deploy" -f ~/.ssh/tgassistant_deploy

# Copy public key to VPS
ssh-copy-id -i ~/.ssh/tgassistant_deploy.pub tgassistant@<VPS_IP>

# The PRIVATE key goes to GitHub secret VPS_SSH_KEY
cat ~/.ssh/tgassistant_deploy
```

### GitHub PAT (Personal Access Token)

1. Go to https://github.com/settings/tokens/new
2. Select scopes: `read:packages`, `write:packages`
3. Generate and save — this is `GHCR_TOKEN`

## Deploy Flow

1. Push/merge to `master` branch
2. GitHub Actions builds Docker image and pushes:
   - immutable tag: `<commit-sha>`
   - moving tag: `latest`
3. Deploy job SSHs to VPS
4. VPS updates repo with `git pull --ff-only origin master`
5. VPS pulls immutable image for this run and recreates only the clean-slate runtime shell services that changed
6. Liveness check verifies app is running and no immediate fatal startup
7. Postgres and Redis are NOT restarted

## Manual Operations

```bash
# SSH into VPS
ssh tgassistant@<VPS_IP>
cd /opt/tgassistant

# View logs
docker compose logs -f app
docker compose logs -f postgres

# Restart app
docker compose restart app

# Restart everything
docker compose down && docker compose up -d

# View DB
docker compose exec postgres psql -U tgassistant

# View Redis
docker compose exec redis redis-cli
```

`docker compose up -d` in the default stack does not start any legacy web, bot, or Stage6 operator service.

Bounded seed run example:

```bash
docker compose run --rm \
  -e Runtime__Role=ops \
  app \
  --seed-bootstrap-scope \
  --seed-dry-run \
  --seed-scope-key=chat:<chat-id> \
  --seed-operator-full-name="Operator Name" \
  --seed-tracked-full-name="Tracked Name"
```

## Rollback

```bash
# On VPS: pull specific version by commit SHA
docker pull ghcr.io/OWNER/tgassistant:<commit-sha>
IMAGE_TAG=ghcr.io/OWNER/tgassistant:<commit-sha> docker compose up -d app
```
