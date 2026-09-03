#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOORPULSE_APP_DIR="$APP_DIR"
export DOORPULSE_HOME="${DOORPULSE_HOME:-${XDG_DATA_HOME:-$HOME/.local/share}/DoorPulse}"
exec dotnet run --project "$APP_DIR/GUI/DoorPulse.Linux.Gui.csproj" -c Release --no-build
