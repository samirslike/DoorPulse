#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DP_HOME="${DOORPULSE_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/DoorPulse}"
TOKEN="$DP_HOME/refresh-token.txt"
[ -f "$TOKEN" ] || { echo "Ring token not found. Run ./auth.sh first."; exit 2; }
node "$APP_DIR/Engine/ring-cameras-helper.mjs" "$TOKEN"
