#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

SINCE="${1:-10m}"

log "Worker logs since ${SINCE}"
compose logs worker --since "${SINCE}"
