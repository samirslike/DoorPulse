#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DP_HOME="${DOORPULSE_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/DoorPulse}"
export DOORPULSE_HOME="$DP_HOME"
export DOORPULSE_CONFIG="${DOORPULSE_CONFIG:-$DP_HOME/config.json}"
export DOORPULSE_TOKEN_FILE="${DOORPULSE_TOKEN_FILE:-$DP_HOME/refresh-token.txt}"
export DOORPULSE_FTP_PASSWORD_FILE="${DOORPULSE_FTP_PASSWORD_FILE:-$DP_HOME/ftp-password.txt}"

[ -f "$DOORPULSE_CONFIG" ] || { echo "Config not found. Run ./setup.sh first."; exit 2; }
[ -f "$DOORPULSE_TOKEN_FILE" ] || { echo "Ring token not found. Run ./auth.sh first."; exit 2; }

cd "$APP_DIR"
exec node "$APP_DIR/Engine/recorder.mjs"
