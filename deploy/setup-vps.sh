#!/bin/bash
# ============================================================
# TelegramAssistant VPS Setup Script
# OS: Ubuntu 24.04 LTS
# Run as root on fresh VPS
# ============================================================

set -e

echo "=== Step 1: System Update ==="
apt update && apt upgrade -y

echo "=== Step 2: Essential packages ==="
apt install -y \
    curl \
    wget \
    git \
    htop \
    nano \
    unzip \
    ufw \
    fail2ban \
    wireguard-tools \
    ffmpeg \
    ca-certificates \
    gnupg \
    lsb-release

echo "=== Step 3: Create app user ==="
useradd -m -s /bin/bash tgassistant
usermod -aG sudo tgassistant
# Set password interactively:
# passwd tgassistant

echo "=== Step 4: SSH Hardening ==="
# Disable root login, password auth (after setting up SSH keys!)
# Uncomment these AFTER you've added your SSH key:
# sed -i 's/^PermitRootLogin yes/PermitRootLogin no/' /etc/ssh/sshd_config
# sed -i 's/^#PasswordAuthentication yes/PasswordAuthentication no/' /etc/ssh/sshd_config
# systemctl restart sshd

echo "=== Step 5: Firewall ==="
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp      # SSH
ufw allow 51820/udp   # WireGuard (if needed)
# ufw allow 8080/tcp  # Web UI (add later when needed)
ufw --force enable
ufw status

echo "=== Step 6: Fail2ban ==="
systemctl enable fail2ban
systemctl start fail2ban

echo "=== Step 7: Install Docker ==="
# Docker official repo
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc

echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  tee /etc/apt/sources.list.d/docker.list > /dev/null

apt update
apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Add app user to docker group
usermod -aG docker tgassistant

# Verify
docker --version
docker compose version

echo "=== Step 8: WireGuard Client ==="
# Create config directory
mkdir -p /etc/wireguard

cat << 'WGEOF'
# -------------------------------------------------------
# MANUAL STEP: Create /etc/wireguard/wg0.conf
# Copy your WireGuard client config here. Example:
#
# [Interface]
# PrivateKey = <your_private_key>
# Address = 10.0.0.2/24
# DNS = 1.1.1.1
#
# [Peer]
# PublicKey = <server_public_key>
# Endpoint = <vpn_server_ip>:51820
# AllowedIPs = 0.0.0.0/0
# PersistentKeepalive = 25
# -------------------------------------------------------
WGEOF

echo "After creating /etc/wireguard/wg0.conf, run:"
echo "  wg-quick up wg0"
echo "  systemctl enable wg-quick@wg0"

echo "=== Step 9: Create directory structure ==="
mkdir -p /opt/tgassistant
mkdir -p /opt/tgassistant/data/media
mkdir -p /opt/tgassistant/data/postgres
mkdir -p /opt/tgassistant/data/redis
mkdir -p /opt/tgassistant/data/telegram-session
mkdir -p /opt/tgassistant/logs
chown -R tgassistant:tgassistant /opt/tgassistant

echo "=== Step 10: Swap (insurance for 4GB RAM) ==="
if [ ! -f /swapfile ]; then
    fallocate -l 2G /swapfile
    chmod 600 /swapfile
    mkswap /swapfile
    swapon /swapfile
    echo '/swapfile none swap sw 0 0' >> /etc/fstab
    echo "Swap created: 2GB"
fi

echo ""
echo "============================================"
echo " Setup complete!"
echo " Next steps:"
echo " 1. Set password: passwd tgassistant"
echo " 2. Add SSH key to /home/tgassistant/.ssh/authorized_keys"
echo " 3. Configure WireGuard: nano /etc/wireguard/wg0.conf"
echo " 4. Start WireGuard: wg-quick up wg0"
echo " 5. Verify VPN: curl https://ifconfig.me"
echo " 6. Deploy app: see docker-compose.yml"
echo "============================================"
