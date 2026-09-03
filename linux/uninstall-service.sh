#!/usr/bin/env bash
set -euo pipefail
systemctl --user disable --now doorpulse-recorder.service 2>/dev/null || true
rm -f "$HOME/.config/systemd/user/doorpulse-recorder.service"
systemctl --user daemon-reload
echo "DoorPulse user service removed. Your config/token/recordings were not deleted."
