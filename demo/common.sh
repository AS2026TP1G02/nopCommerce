#!/usr/bin/env bash

set -euo pipefail

DEMO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${DEMO_DIR}/.." && pwd)"

COMPOSE_BIN="${COMPOSE_BIN:-docker compose}"
WMS_BASE_URL="${WMS_BASE_URL:-http://localhost:8081}"
POS_BASE_URL="${POS_BASE_URL:-http://localhost:8082}"
NOP_BASE_URL="${NOP_BASE_URL:-http://localhost:8080}"
RABBITMQ_UI_URL="${RABBITMQ_UI_URL:-http://localhost:15672}"
SQLCMD_PATH="${SQLCMD_PATH:-/opt/mssql-tools18/bin/sqlcmd}"
SQL_DATABASE="${SQL_DATABASE:-nopCommerce}"
SQL_USER="${SQL_USER:-sa}"
SQL_PASSWORD="${SQL_PASSWORD:-Omni_Demo_Pass1}"

timestamp() {
  date +"%Y-%m-%dT%H:%M:%S%z"
}

log() {
  printf '[%s] %s\n' "$(timestamp)" "$*"
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    printf 'Missing required command: %s\n' "$1" >&2
    exit 1
  }
}

compose() {
  (
    cd "${REPO_ROOT}"
    ${COMPOSE_BIN} "$@"
  )
}

pretty_json() {
  if command -v jq >/dev/null 2>&1; then
    jq .
  else
    cat
  fi
}

curl_json() {
  curl -fsS "$@" | pretty_json
}

sql_query() {
  local query="$1"
  compose exec -T sqlserver "${SQLCMD_PATH}" \
    -S localhost \
    -U "${SQL_USER}" \
    -P "${SQL_PASSWORD}" \
    -C \
    -d "${SQL_DATABASE}" \
    -Q "${query}"
}

sql_query_tsv() {
  local query="$1"
  compose exec -T sqlserver "${SQLCMD_PATH}" \
    -S localhost \
    -U "${SQL_USER}" \
    -P "${SQL_PASSWORD}" \
    -C \
    -d "${SQL_DATABASE}" \
    -h -1 -W -s $'\t' \
    -Q "SET NOCOUNT ON; ${query}"
}

rabbitmq_queues() {
  compose exec -T rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
}
