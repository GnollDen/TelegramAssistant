#!/bin/bash
# ============================================================
# One-time setup for GitHub-based deploy pipeline
# Run on VPS as tgassistant user
# ============================================================

set -e

echo "=== Setting up deploy pipeline ==="

# 1. Create project directory
sudo mkdir -p /opt/tgassistant
sudo chown tgassistant:tgassistant /opt/tgassistant
cd /opt/tgassistant

# 2. Clone repo (first time only)
if [ ! -d ".git" ]; then
    echo "Enter your GitHub repo URL (e.g. https://github.com/user/tgassistant.git):"
    read REPO_URL
    git clone "$REPO_URL" .
fi

# 3. Create .env from template
if [ ! -f ".env" ]; then
    cp .env.example .env
    echo ""
    echo "IMPORTANT: Edit .env with your secrets:"
    echo "  nano /opt/tgassistant/.env"
    echo ""
fi

# 4. Login to GitHub Container Registry
echo "Logging into GitHub Container Registry..."
echo "You'll need a Personal Access Token (PAT) with 'read:packages' scope"
echo "Create one at: https://github.com/settings/tokens/new"
echo ""
echo "Enter your GitHub username:"
read GH_USER
echo "Enter your PAT:"
read -s GH_TOKEN
echo "$GH_TOKEN" | docker login ghcr.io -u "$GH_USER" --password-stdin

# Save token for future pulls (used by deploy workflow)
echo "$GH_TOKEN" > ~/.ghcr-token
chmod 600 ~/.ghcr-token

# 5. Create data directories
mkdir -p data/media data/postgres data/redis data/telegram-session logs

# 6. Start infrastructure (postgres + redis)
docker compose up -d postgres redis

echo ""
echo "=== Setup complete! ==="
echo ""
echo "Next steps:"
echo "1. Edit .env:  nano /opt/tgassistant/.env"
echo "2. In .env set IMAGE_TAG (and optionally MCP_IMAGE_TAG) plus startup-critical vars:"
echo "   POSTGRES_PASSWORD, GRAFANA_ADMIN_PASSWORD, MCP_SSE_AUTH_TOKEN,"
echo "   TG_API_ID, TG_API_HASH, TG_PHONE, TG_OWNER_ID,"
echo "   GEMINI_API_KEY, CLAUDE_API_KEY"
echo "3. Push to main branch on GitHub — deploy will happen automatically"
echo ""
echo "Default compose path starts only the clean-slate runtime shell."
echo "Legacy web/bot/Stage6 diagnostics are not part of the baseline deployment."
echo ""
echo "Manual deploy:  docker compose up -d app"
echo "View logs:      docker compose logs -f app"
