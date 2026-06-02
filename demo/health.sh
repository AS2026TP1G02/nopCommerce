#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

require_cmd curl

log "Compose service status"
compose ps

log "WMS /health"
curl_json "${WMS_BASE_URL}/health"

log "WMS /mode"
curl_json "${WMS_BASE_URL}/mode"

log "POS /health"
curl_json "${POS_BASE_URL}/health"

log "POS /mode"
curl_json "${POS_BASE_URL}/mode"

log "RabbitMQ queues"
rabbitmq_queues

log "RabbitMQ UI: ${RABBITMQ_UI_URL}"
