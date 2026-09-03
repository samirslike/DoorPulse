#!/usr/bin/env bash
set -euo pipefail
APP_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
read -r -p "Ring email: " EMAIL
read -r -s -p "Ring password: " PASSWORD
echo
export DOORPULSE_RING_EMAIL="$EMAIL"
export DOORPULSE_RING_PASSWORD="$PASSWORD"
node "$APP_DIR/Engine/linux-auth.mjs"
unset DOORPULSE_RING_EMAIL DOORPULSE_RING_PASSWORD EMAIL PASSWORD
