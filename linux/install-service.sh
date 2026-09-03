#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NODE_BIN="$(command -v node || true)"
[ -n "$NODE_BIN" ] || { echo "Node.js was not found."; exit 2; }

DP_HOME="${XDG_DATA_HOME:-$HOME/.local/share}/DoorPulse"
UNIT_DIR="$HOME/.config/systemd/user"
UNIT="$UNIT_DIR/doorpulse-recorder.service"
mkdir -p "$UNIT_DIR"

cat > "$UNIT" <<UNIT
[Unit]
Description=DoorPulse Ring Camera Recorder
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=$APP_DIR
Environment=DOORPULSE_HOME=$DP_HOME
Environment=DOORPULSE_CONFIG=$DP_HOME/config.json
Environment=DOORPULSE_TOKEN_FILE=$DP_HOME/refresh-token.txt
Environment=DOORPULSE_FTP_PASSWORD_FILE=$DP_HOME/ftp-password.txt
ExecStart=$NODE_BIN $APP_DIR/Engine/recorder.mjs
Restart=always
RestartSec=10

[Install]
WantedBy=default.target
UNIT

systemctl --user daemon-reload
systemctl --user enable --now doorpulse-recorder.service

echo
echo "DoorPulse service installed and started."
echo "Status: systemctl --user status doorpulse-recorder"
echo "Live log: journalctl --user -u doorpulse-recorder -f"
echo
echo "For 24/7 operation after logout, you can enable user lingering:"
echo "  sudo loginctl enable-linger $USER"
