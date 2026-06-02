# QA-2 Evidence - POS Consistency

## Scenario

POS/store-originated stock updates arrive at nopCommerce through the omnichannel plugin callback:

```text
POST /omnichannel/callbacks/pos/stock-changed
X-Demo-Token: omni-demo-token
```

The plugin must handle at-least-once delivery safely:

- duplicate `messageId` is detected by `OmniInboxMessage`;
- stale `sourceVersion` is ignored by `OmniStockSyncState`;
- POS stock events do not create fulfillment rows;
- core nopCommerce stock is not overwritten in this iteration, per ADR-0007 projection-first stock.

## Implemented Controls

| Control | Implementation |
|---------|----------------|
| Internal callback auth | `X-Demo-Token` checked by `OmnichannelCallbackController`. |
| Inbox idempotency | `OmniInboxService.TryBeginProcessingAsync` checks `MessageId` before processing. |
| Duplicate handling | duplicate request returns `result = duplicate` and does not call stock projection logic. |
| Stale handling | `OmniStockSyncService` ignores updates where `sourceVersion <= stored SourceVersion`. |
| POS simulator | `services/pos-sim` supports `normal`, `duplicate`, and `stale` modes. |

## Runtime Evidence

Captured on `2026-06-02` on branch `develop` with the Compose stack:

```bash
docker compose up -d --build
```

Services used:

- nopCommerce: `http://localhost:8080`
- POS simulator: `http://localhost:8082`
- SQL Server: `localhost:1433`, database `nopCommerce`

## Normal Update

Command:

```bash
curl -sS -w '\nTIME_TOTAL=%{time_total}\n' \
  -X POST http://localhost:8082/emit \
  -H 'Content-Type: application/json' \
  -d '{"mode":"normal","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":3}'
```

Observed result:

| Field | Value |
|-------|-------|
| `messageId` | `b9d279b8-d08b-471f-be75-0dbc04c56a7b` |
| `sourceVersion` | `41` |
| `quantityOnHand` | `3` |
| plugin result | `applied` |
| HTTP status | `200` |
| POS round trip | `269.237 ms` |

Meaning: a valid POS update is accepted, recorded in `OmniInboxMessage`, and applied to the stock projection.

## Duplicate Update

Command:

```bash
curl -sS -w '\nTIME_TOTAL=%{time_total}\n' \
  -X POST http://localhost:8082/emit \
  -H 'Content-Type: application/json' \
  -d '{"mode":"duplicate","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":4}'
```

Observed result:

| Call | Field | Value |
|------|-------|-------|
| first | `messageId` | `0159d12e-2607-4dcc-b695-64a6a0d71595` |
| first | plugin result | `applied` |
| first | `InboxId` | `2` |
| second | `messageId` | `0159d12e-2607-4dcc-b695-64a6a0d71595` |
| second | plugin result | `duplicate` |
| second | `InboxId` | `2` |
| second | `Duplicate` | `true` |

The simulator round trip for both posts was `55.449 ms`. To measure the duplicate rejection itself, the same callback payload was posted directly to the plugin twice.

Direct duplicate timing:

```bash
curl -sS -w '\nTIME_TOTAL=%{time_total}\n' \
  -X POST http://localhost:8080/omnichannel/callbacks/pos/stock-changed \
  -H 'Content-Type: application/json' \
  -H 'X-Demo-Token: omni-demo-token' \
  -d '<same payload as first request>'
```

Observed direct duplicate result:

| Field | Value |
|-------|-------|
| `messageId` | `851de464-832c-48f9-b0d1-ae4f892ae2cf` |
| plugin result | `duplicate` |
| `InboxId` | `5` |
| `Duplicate` | `true` |
| direct callback duration | `17.485 ms` |

Acceptance result: duplicate rejected in `17.485 ms`, which is below the `<= 50 ms` QA-2 target.

SQL evidence:

```sql
SELECT COUNT(*) AS FulfillmentRowsAfterPos
FROM dbo.OmniOrderFulfillment;

SELECT MessageId, COUNT(*) AS InboxRowsForDuplicate
FROM dbo.OmniInboxMessage
WHERE MessageId = '0159d12e-2607-4dcc-b695-64a6a0d71595'
GROUP BY MessageId;

SELECT MessageId, COUNT(*) AS InboxRowsForDirectDuplicate
FROM dbo.OmniInboxMessage
WHERE MessageId = '851de464-832c-48f9-b0d1-ae4f892ae2cf'
GROUP BY MessageId;
```

Observed SQL result:

| Check | Result |
|-------|--------|
| fulfillment rows created by POS scenario | `0` |
| inbox rows for duplicate simulator `messageId` | `1` |
| inbox rows for direct duplicate `messageId` | `1` |

Meaning: at-least-once delivery can retry the same message, but the plugin processes it once.

## Stale Update

Command:

```bash
curl -sS -w '\nTIME_TOTAL=%{time_total}\n' \
  -X POST http://localhost:8082/emit \
  -H 'Content-Type: application/json' \
  -d '{"mode":"stale","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":5,"sourceVersion":45}'
```

Observed result:

| Call | Field | Value |
|------|-------|-------|
| first | `messageId` | `aec353d1-00df-4f44-b155-9d3ee88e21e9` |
| first | `sourceVersion` | `45` |
| first | `quantityOnHand` | `5` |
| first | plugin result | `applied` |
| second | `messageId` | `bceec24e-609f-4ca5-ad25-ff32c52e2e10` |
| second | `sourceVersion` | `44` |
| second | `quantityOnHand` | `12` |
| second | plugin result | `stale_ignored` |
| POS round trip | both posts | `25.623 ms` |

SQL evidence:

```sql
SELECT TOP (1)
    ProductId,
    WarehouseId,
    QuantityOnHand,
    SourceVersion,
    LastMessageId,
    StatusId
FROM dbo.OmniStockSyncState
WHERE ProductId = 15 AND WarehouseId = 2
ORDER BY Id DESC;
```

Observed SQL result:

| Field | Value |
|-------|-------|
| `ProductId` | `15` |
| `WarehouseId` | `2` |
| `QuantityOnHand` | `5` |
| `SourceVersion` | `45` |
| `LastMessageId` | `BCEEC24E-609F-4CA5-AD25-FF32C52E2E10` |
| `StatusId` | `20` |

Meaning: the stale event was recorded for traceability, but it did not overwrite the newer `QuantityOnHand = 5` or `SourceVersion = 45`.

## QA-2 Result

Current status: **complete**.

| Requirement | Evidence | Result |
|-------------|----------|--------|
| POS normal update applies | normal simulator mode returned `applied` | pass |
| duplicate rejected `<= 50 ms` | direct duplicate callback took `17.485 ms` | pass |
| duplicate does not create duplicate inbox rows | duplicate `messageId` count is `1` | pass |
| POS does not create fulfillment rows | `OmniOrderFulfillment` count after POS scenario is `0` | pass |
| stale `sourceVersion` ignored | stored state remains quantity `5`, version `45` after stale version `44` | pass |
