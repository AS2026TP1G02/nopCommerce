#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

require_cmd curl

MODE="${1:-}"

if [[ -z "${MODE}" ]]; then
  printf 'Usage: %s <normal|slow|unavailable|contradictory>\n' "$0" >&2
  exit 1
fi

log "Setting WMS mode to ${MODE}"
curl_json -X POST "${WMS_BASE_URL}/mode/${MODE}"
