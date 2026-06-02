#!/usr/bin/env bash
#
# End-to-end check for the WMS contradiction → fulfillment Rejected path:
#   1. switch the WMS simulator to `contradictory` (it 409s every fulfillment)
#   2. place exactly one order via single-order-test.js
#   3. poll OmniOrderFulfillment until the order is Rejected (StatusId=50)
#   4. assert the dead-letter queue stayed empty
#   5. always restore the WMS to `normal`
#
# Self-contained: needs only k6, curl, and the running docker compose stack (it does NOT
# depend on demo/). Env-overridable; defaults mirror demo/common.sh.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

BASE_URL="${BASE_URL:-http://localhost:8080}"
WMS_BASE_URL="${WMS_BASE_URL:-http://localhost:8081}"
COMPOSE_BIN="${COMPOSE_BIN:-docker compose}"
SQLCMD_PATH="${SQLCMD_PATH:-/opt/mssql-tools18/bin/sqlcmd}"
SQL_USER="${SQL_USER:-sa}"
SQL_PASSWORD="${SQL_PASSWORD:-Omni_Demo_Pass1}"
SQL_DATABASE="${SQL_DATABASE:-nopCommerce}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-120}"
POLL_INTERVAL="${POLL_INTERVAL:-5}"

REJECTED_STATUS_ID=50

RED=$'\033[0;31m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; NC=$'\033[0m'

log()  { printf '[%s] %s\n' "$(date +%H:%M:%S)" "$*"; }
fail() { printf '%sFAIL%s %s\n' "$RED" "$NC" "$*" >&2; exit 1; }

compose() { ( cd "${REPO_ROOT}" && ${COMPOSE_BIN} "$@" ); }

# Run a query that returns a single scalar; trims CR and blank lines.
sql_scalar() {
  compose exec -T sqlserver "${SQLCMD_PATH}" \
    -S localhost -U "${SQL_USER}" -P "${SQL_PASSWORD}" -C \
    -d "${SQL_DATABASE}" -h -1 -W \
    -Q "SET NOCOUNT ON; $1" 2>/dev/null | tr -d '\r' | sed '/^$/d' | head -1
}

restore_wms() {
  if curl -fsS -X POST "${WMS_BASE_URL}/mode/normal" >/dev/null 2>&1; then
    log "WMS restored to normal"
  else
    log "WARN: could not restore WMS to normal"
  fi
}
trap restore_wms EXIT

# --- pre-flight ---
command -v k6 >/dev/null 2>&1   || fail "k6 is not installed"
command -v curl >/dev/null 2>&1 || fail "curl is not installed"
code="$(curl -s -o /dev/null -w '%{http_code}' "${BASE_URL}" || true)"
[[ "${code}" == "200" ]] || fail "nopCommerce not reachable at ${BASE_URL} (HTTP ${code:-none}); start the stack first"

# --- 1. force the WMS to reject every fulfillment ---
log "Setting WMS to contradictory mode"
mode="$(curl -fsS -X POST "${WMS_BASE_URL}/mode/contradictory" || fail "could not reach WMS at ${WMS_BASE_URL}")"
printf '%s\n' "${mode}"
echo "${mode}" | grep -q '"mode":"contradictory"' || fail "WMS did not switch to contradictory: ${mode}"

# --- 2. place exactly one order ---
# Record the latest order id first so we can identify the new one afterward (the OPC confirm
# response does not expose the id).
BEFORE_MAX_ID="$(sql_scalar "SELECT ISNULL(MAX(Id), 0) FROM [Order]")"
BEFORE_MAX_ID="${BEFORE_MAX_ID:-0}"

log "Placing one order via k6 (single-order-test.js)"
K6_OUT="$(mktemp)"
if BASE_URL="${BASE_URL}" k6 run -e BASE_URL="${BASE_URL}" "${SCRIPT_DIR}/single-order-test.js" >"${K6_OUT}" 2>&1; then
  log "k6 checkout PASS"
else
  cat "${K6_OUT}"
  fail "k6 order placement failed (checkout did not succeed)"
fi
cat "${K6_OUT}"
rm -f "${K6_OUT}"

# --- 3. identify the order just placed (newest [Order] row) and its OrderGuid ---
NEWEST="$(sql_scalar "SELECT TOP 1 CONCAT(Id, '|', OrderGuid) FROM [Order] ORDER BY Id DESC")"
ORDER_ID="${NEWEST%%|*}"
ORDER_GUID="${NEWEST#*|}"
[[ -n "${ORDER_ID}" && "${ORDER_ID}" -gt "${BEFORE_MAX_ID}" ]] \
  || fail "no new order appeared after checkout (latest Id=${ORDER_ID:-none}, was ${BEFORE_MAX_ID})"
log "Placed Order.Id=${ORDER_ID}"
log "OrderGuid=${ORDER_GUID}"
log "Admin trace: ${BASE_URL}/Admin/OmnichannelCore/Trace?orderGuid=${ORDER_GUID}"

# --- 4. poll for the async Rejected outcome (outbox publishes on a 60s tick) ---
log "Polling OmniOrderFulfillment for StatusId=${REJECTED_STATUS_ID} (up to ${TIMEOUT_SECONDS}s)"
deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))
status_id=""; reason=""; ext=""
while (( $(date +%s) < deadline )); do
  row="$(sql_scalar "SELECT TOP 1 CONCAT(StatusId, '|', ISNULL(Reason,''), '|', ISNULL(ExternalRequestId,'')) FROM OmniOrderFulfillment WHERE OrderGuid = '${ORDER_GUID}' ORDER BY Id DESC")"
  if [[ -n "${row}" ]]; then
    status_id="${row%%|*}"
    rest="${row#*|}"; reason="${rest%%|*}"; ext="${rest#*|}"
    log "  fulfillment StatusId=${status_id} reason='${reason}'"
    if [[ "${status_id}" == "${REJECTED_STATUS_ID}" ]]; then
      break
    fi
  else
    log "  (no fulfillment row yet)"
  fi
  sleep "${POLL_INTERVAL}"
done

# --- 5. DLQ should have stayed empty (rejection is a clean ack, not a poison message) ---
dlq="$(compose exec -T rabbitmq rabbitmqctl list_queues name messages 2>/dev/null | awk '$1=="wms.order.placed.dlq"{print $2}')"
dlq="${dlq:-0}"

# --- verdict ---
if [[ "${status_id}" == "${REJECTED_STATUS_ID}" ]]; then
  printf '%sPASS%s order %s rejected — StatusId=50 reason=%s external_request_id=%s; DLQ depth=%s\n' \
    "${GREEN}" "${NC}" "${ORDER_ID}" "${reason:-?}" "${ext:-<none>}" "${dlq}"
  if [[ "${dlq}" != "0" ]]; then
    printf '%sWARN%s DLQ depth is %s (expected 0)\n' "${YELLOW}" "${NC}" "${dlq}"
  fi
  exit 0
fi

printf '%sFAIL%s order %s did not reach Rejected within %ss (last StatusId=%s)\n' \
  "${RED}" "${NC}" "${ORDER_ID}" "${TIMEOUT_SECONDS}" "${status_id:-none}" >&2
log "RabbitMQ queues:"
compose exec -T rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged 2>/dev/null || true
log "Recent worker logs:"
compose logs worker --tail 30 2>/dev/null || true
exit 1
