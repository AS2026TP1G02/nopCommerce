#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

require_cmd curl

MODE="${1:-}"
QUANTITY="${2:-3}"
PRODUCT_ID="${3:-15}"
WAREHOUSE_ID="${4:-2}"
SKU="${5:-LAPTOP-15}"

if [[ -z "${MODE}" ]]; then
  printf 'Usage: %s <normal|duplicate|stale> [quantity] [productId] [warehouseId] [sku]\n' "$0" >&2
  exit 1
fi

log "Emitting POS event mode=${MODE} productId=${PRODUCT_ID} warehouseId=${WAREHOUSE_ID} quantity=${QUANTITY}"
curl_json -X POST "${POS_BASE_URL}/emit" \
  -H "Content-Type: application/json" \
  -d "{\"mode\":\"${MODE}\",\"productId\":${PRODUCT_ID},\"sku\":\"${SKU}\",\"warehouseId\":${WAREHOUSE_ID},\"quantityOnHand\":${QUANTITY}}"
