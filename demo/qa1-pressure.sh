#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

require_cmd curl
require_cmd k6

DURATION_SECONDS="${1:-30}"
ORDER_TARGET="${2:-50}"
ARTIFACT_DIR="${DEMO_DIR}/artifacts/$(date +%Y%m%d-%H%M%S)"

mkdir -p "${ARTIFACT_DIR}"

log "Artifacts: ${ARTIFACT_DIR}"

log "Resetting WMS to normal"
curl_json -X POST "${WMS_BASE_URL}/mode/normal" > "${ARTIFACT_DIR}/wms-normal-before.json"

log "Queue state before pressure"
rabbitmq_queues | tee "${ARTIFACT_DIR}/queues-before.txt"

START_TIME="$(timestamp)"
log "Starting unavailable window at ${START_TIME}"
curl_json -X POST "${WMS_BASE_URL}/mode/unavailable" > "${ARTIFACT_DIR}/wms-unavailable.json"

(
  sleep "${DURATION_SECONDS}"
  RECOVERY_TIME="$(timestamp)"
  log "Restoring WMS to normal at ${RECOVERY_TIME}"
  curl_json -X POST "${WMS_BASE_URL}/mode/normal" > "${ARTIFACT_DIR}/wms-normal-after.json"
  rabbitmq_queues | tee "${ARTIFACT_DIR}/queues-after-recovery.txt"
  printf '%s\n' "${RECOVERY_TIME}" > "${ARTIFACT_DIR}/recovery-time.txt"
) &
RESTORE_PID=$!

log "Running checkout load test ORDER_TARGET=${ORDER_TARGET}"
(
  cd "${REPO_ROOT}/load-test"
  ORDER_TARGET="${ORDER_TARGET}" BASE_URL="${NOP_BASE_URL}" ./run-load-test.sh automated
) | tee "${ARTIFACT_DIR}/load-test.log"

wait "${RESTORE_PID}"

log "Waiting 60 seconds for backlog drain measurement"
sleep 60
rabbitmq_queues | tee "${ARTIFACT_DIR}/queues-after-60s.txt"

log "Capturing worker logs"
compose logs worker --since 15m | tee "${ARTIFACT_DIR}/worker.log"

printf '%s\n' "${START_TIME}" > "${ARTIFACT_DIR}/unavailable-start-time.txt"

log "QA-1 capture complete"
log "Review: ${ARTIFACT_DIR}/load-test.log"
log "Review: ${ARTIFACT_DIR}/queues-after-recovery.txt"
log "Review: ${ARTIFACT_DIR}/queues-after-60s.txt"
log "Review: ${ARTIFACT_DIR}/worker.log"
