# CI/CD Pipeline Setup

## Architecture

```
[Push to main] → [GitHub Actions] → [Build Docker image] → [Push to GHCR]
                                                                    ↓
                              [VPS] ← [SSH: docker pull + restart] ←┘
```

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
5. VPS pulls immutable image for this run and recreates only `app`
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

## Rollback

```bash
# On VPS: pull specific version by commit SHA
docker pull ghcr.io/OWNER/tgassistant:<commit-sha>
IMAGE_TAG=ghcr.io/OWNER/tgassistant:<commit-sha> docker compose up -d app
```
