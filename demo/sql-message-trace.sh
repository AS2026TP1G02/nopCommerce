#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

MESSAGE_ID="${1:-}"

if [[ -z "${MESSAGE_ID}" ]]; then
  printf 'Usage: %s <message-id>\n' "$0" >&2
  exit 1
fi

log "OmniInboxMessage row"
sql_query_tsv "
SELECT v.Line
FROM [dbo].[OmniInboxMessage] i
CROSS APPLY (VALUES
  (0, CONCAT('--- OmniInboxMessage Id=', i.Id, ' ---')),
  (1, CONCAT('MessageId: ', i.MessageId)),
  (2, CONCAT('CorrelationId: ', i.CorrelationId)),
  (3, CONCAT('OrderGuid: ', i.OrderGuid)),
  (4, CONCAT('EventType: ', i.EventType)),
  (5, CONCAT('Source: ', i.Source)),
  (6, CONCAT('StatusId: ', i.StatusId)),
  (7, CONCAT('ReceivedOnUtc: ', i.ReceivedOnUtc)),
  (8, CONCAT('ProcessedOnUtc: ', i.ProcessedOnUtc)),
  (9, CONCAT('UpdatedOnUtc: ', i.UpdatedOnUtc)),
  (10, '')
) v(Sort, Line)
WHERE i.[MessageId] = '${MESSAGE_ID}'
ORDER BY i.[Id] DESC, v.Sort"

log "OmniStockSyncState row"
sql_query_tsv "
SELECT v.Line
FROM [dbo].[OmniStockSyncState] s
CROSS APPLY (VALUES
  (0, CONCAT('--- OmniStockSyncState Id=', s.Id, ' ---')),
  (1, CONCAT('ProductId: ', s.ProductId)),
  (2, CONCAT('WarehouseId: ', s.WarehouseId)),
  (3, CONCAT('Sku: ', s.Sku)),
  (4, CONCAT('QuantityOnHand: ', s.QuantityOnHand)),
  (5, CONCAT('SourceVersion: ', s.SourceVersion)),
  (6, CONCAT('LastMessageId: ', s.LastMessageId)),
  (7, CONCAT('Source: ', s.Source)),
  (8, CONCAT('StatusId: ', s.StatusId)),
  (9, CONCAT('LastSeenOnUtc: ', s.LastSeenOnUtc)),
  (10, CONCAT('UpdatedOnUtc: ', s.UpdatedOnUtc)),
  (11, '')
) v(Sort, Line)
WHERE s.[LastMessageId] = '${MESSAGE_ID}'
ORDER BY s.[Id] DESC, v.Sort"
