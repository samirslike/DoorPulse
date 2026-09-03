#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Installing .NET 10 SDK..."
  sudo apt-get update
  sudo apt-get install -y dotnet-sdk-10.0
fi

echo "Using $(dotnet --version)"
echo "Installing Linux GUI dependencies..."
sudo apt-get update
sudo apt-get install -y libice6 libsm6 libfontconfig1 libgdiplus libx11-6 libxext6 libxrender1 libxrandr2 libxi6 libxcursor1 libxfixes3 libxinerama1

echo "Restoring Avalonia 12.1.2 packages..."
dotnet restore "$APP_DIR/GUI/DoorPulse.Linux.Gui.csproj"

echo "Building DoorPulse Linux GUI..."
dotnet build "$APP_DIR/GUI/DoorPulse.Linux.Gui.csproj" -c Release --no-restore

echo
echo "DoorPulse Linux GUI is ready."
echo "Run: ./gui-run.sh"
