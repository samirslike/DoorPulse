#!/usr/bin/env bash
set -euo pipefail

APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
DP_HOME="$DATA_HOME/DoorPulse"

need() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1"
    return 1
  fi
}

need node || true
need npm || true
need ffmpeg || true
need curl || true

if ! command -v node >/dev/null 2>&1; then
  echo
  echo "Node.js 20 or newer is required before continuing."
  exit 2
fi

NODE_MAJOR="$(node -p "process.versions.node.split('.')[0]")"
if [ "$NODE_MAJOR" -lt 20 ]; then
  echo "Node.js $(node -v) found. DoorPulse Linux needs Node.js 20 or newer."
  exit 2
fi

if ! command -v npm >/dev/null 2>&1 || ! command -v ffmpeg >/dev/null 2>&1 || ! command -v curl >/dev/null 2>&1; then
  echo
  echo "Install the missing packages, then run ./setup.sh again."
  echo "Ubuntu example: sudo apt update && sudo apt install -y npm ffmpeg curl"
  exit 2
fi

mkdir -p "$DP_HOME/logs" "$HOME/Videos/DoorPulse"
chmod 700 "$DP_HOME"

if [ ! -f "$DP_HOME/config.json" ]; then
  sed "s#__HOME__#$HOME#g" "$APP_DIR/config.example.json" > "$DP_HOME/config.json"
  chmod 600 "$DP_HOME/config.json"
fi

cd "$APP_DIR"
echo "Installing the tested DoorPulse Ring runtime..."
npm install

echo
echo "DoorPulse Linux Phase 1 is prepared."
echo "Data folder: $DP_HOME"
echo "Recordings: $HOME/Videos/DoorPulse"
echo
echo "Next: ./auth.sh"
