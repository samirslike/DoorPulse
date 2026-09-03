#!/usr/bin/env bash
set -euo pipefail
systemctl --user status doorpulse-recorder.service --no-pager || true
