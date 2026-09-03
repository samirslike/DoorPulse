#!/usr/bin/env bash
set -euo pipefail
journalctl --user -u doorpulse-recorder.service -n 100 --no-pager
