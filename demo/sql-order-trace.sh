#!/usr/bin/env bash

set -euo pipefail

source "$(cd "$(dirname "$0")" && pwd)/common.sh"

ORDER_REF="${1:-}"
ORDER_GUID=""

if [[ -z "${ORDER_REF}" ]]; then
  printf 'Usage: %s <order-guid|order-id>\n' "$0" >&2
  exit 1
fi

if [[ "${ORDER_REF}" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  ORDER_GUID="${ORDER_REF}"
elif [[ "${ORDER_REF}" =~ ^[0-9]+$ ]]; then
  ORDER_GUID="$(compose exec -T sqlserver "${SQLCMD_PATH}" \
    -S localhost \
    -U "${SQL_USER}" \
    -P "${SQL_PASSWORD}" \
    -C \
    -d "${SQL_DATABASE}" \
    -h -1 -W \
    -Q "SET NOCOUNT ON; SELECT OrderGuid FROM [Order] WHERE Id = ${ORDER_REF}")"

  ORDER_GUID="${ORDER_GUID//$'\r'/}"

  if [[ -z "${ORDER_GUID}" ]]; then
    printf 'No OrderGuid found for Order.Id %s\n' "${ORDER_REF}" >&2
    exit 1
  fi

  log "Resolved OrderGuid ${ORDER_GUID} from Order.Id ${ORDER_REF}"
else
  printf 'Expected a GUID or numeric Order.Id, got: %s\n' "${ORDER_REF}" >&2
  exit 1
fi

log "Admin trace URL"
printf '%s/Admin/OmnichannelCore/Trace?orderGuid=%s\n' "${NOP_BASE_URL}" "${ORDER_GUID}"

log "OmniOutboxMessage rows"
sql_query_tsv "
SELECT v.Line
FROM [dbo].[OmniOutboxMessage] o
CROSS APPLY (VALUES
  (0, CONCAT('--- OmniOutboxMessage Id=', o.Id, ' ---')),
  (1, CONCAT('MessageId: ', o.MessageId)),
  (2, CONCAT('CorrelationId: ', o.CorrelationId)),
  (3, CONCAT('EventType: ', o.EventType)),
  (4, CONCAT('StatusId: ', o.StatusId)),
  (5, CONCAT('RetryCount: ', o.RetryCount)),
  (6, CONCAT('CreatedOnUtc: ', o.CreatedOnUtc)),
  (7, CONCAT('PublishedOnUtc: ', o.PublishedOnUtc)),
  (8, '')
) v(Sort, Line)
WHERE o.[OrderGuid] = '${ORDER_GUID}'
ORDER BY o.[Id] DESC, v.Sort"

log "OmniInboxMessage rows"
sql_query_tsv "
SELECT v.Line
FROM [dbo].[OmniInboxMessage] i
CROSS APPLY (VALUES
  (0, CONCAT('--- OmniInboxMessage Id=', i.Id, ' ---')),
  (1, CONCAT('MessageId: ', i.MessageId)),
  (2, CONCAT('CorrelationId: ', i.CorrelationId)),
  (3, CONCAT('EventType: ', i.EventType)),
  (4, CONCAT('StatusId: ', i.StatusId)),
  (5, CONCAT('ReceivedOnUtc: ', i.ReceivedOnUtc)),
  (6, CONCAT('ProcessedOnUtc: ', i.ProcessedOnUtc)),
  (7, CONCAT('UpdatedOnUtc: ', i.UpdatedOnUtc)),
  (8, '')
) v(Sort, Line)
WHERE i.[OrderGuid] = '${ORDER_GUID}'
ORDER BY i.[Id] DESC, v.Sort"

log "OmniOrderFulfillment row"
sql_query_tsv "
SELECT v.Line
FROM [dbo].[OmniOrderFulfillment] f
CROSS APPLY (VALUES
  (0, CONCAT('--- OmniOrderFulfillment Id=', f.Id, ' ---')),
  (1, CONCAT('OrderGuid: ', f.OrderGuid)),
  (2, CONCAT('OrderId: ', f.OrderId)),
  (3, CONCAT('MessageId: ', f.MessageId)),
  (4, CONCAT('CorrelationId: ', f.CorrelationId)),
  (5, CONCAT('ExternalRequestId: ', f.ExternalRequestId)),
  (6, CONCAT('TrackingNumber: ', f.TrackingNumber)),
  (7, CONCAT('StatusId: ', f.StatusId)),
  (8, CONCAT('Reason: ', f.Reason)),
  (9, CONCAT('AcceptedOnUtc: ', f.AcceptedOnUtc)),
  (10, CONCAT('UpdatedOnUtc: ', f.UpdatedOnUtc)),
  (11, '')
) v(Sort, Line)
WHERE f.[OrderGuid] = '${ORDER_GUID}'
ORDER BY f.[Id] DESC, v.Sort"
