# QA-3 Evidence - Order to Fulfillment Traceability

## Scenario

QA-3 proves that a placed order can be traced across the omnichannel integration path:

```text
nopCommerce order
  -> OmniOutboxMessage
  -> RabbitMQ / worker delivery
  -> WMS simulator
  -> fulfillment callback
  -> OmniInboxMessage + OmniOrderFulfillment
```

The trace uses three stable identifiers:

| Field | Purpose |
|-------|---------|
| `order_guid` / `OrderGuid` | business-level order correlation across plugin, worker and WMS |
| `message_id` / `MessageId` | technical event correlation across outbox, MQ, worker logs and inbox |
| `external_request_id` / `ExternalRequestId` | WMS-side fulfillment request correlation |

## Admin Click Path

The admin trace path is at most three clicks:

1. Open nopCommerce admin and go to `Configuration -> Local plugins -> Omnichannel Core -> Configure`.
2. Open `Trace`, paste the `OrderGuid`, and submit.
3. Inspect the `Outbox`, `Fulfillment projection`, and `Inbox` cards.

Direct URL used during evidence capture:

```text
http://localhost:8080/Admin/OmnichannelCore/Trace?orderGuid=2CB41857-3C7B-4984-8BBE-2C329A5DD25E
```

The page shows:

- `Outbox (commerce.order.placed.v1)` with status `Published`;
- `Fulfillment projection (OmniOrderFulfillment)` with status `Accepted`;
- `ExternalRequestId` such as `WMS-REQ-15`;
- `Inbox (idempotency ledger)` with status `Processed`.

Important caveat: worker delivery attempts are not stored in the plugin admin view. They are resolved by filtering worker logs on `message_id`, using the outbox `MessageId` shown in the Trace page. RabbitMQ queue/DLQ state is checked in the RabbitMQ UI or with `rabbitmqctl`.

## Runtime Evidence

Captured on `2026-06-02` on branch `develop` with the Compose stack:

```bash
docker compose up -d --build
```

Ten real orders were placed with the load-test script:

```bash
BASE_URL=http://localhost:8080 \
ORDER_TARGET=10 \
FIXED_VUS=1 \
MAX_DURATION=10m \
k6 run load-test/automated-order-placement.js
```

Observed k6 result:

| Metric | Value |
|--------|-------|
| iterations | `10/10` |
| `order_success_rate` | `100.00%` |
| `checks_succeeded` | `100.00%` |
| `http_req_failed` | `0.00%` |
| `order_placement_duration_ms avg` | `1386.4 ms` |
| `order_placement_duration_ms p95` | `3794.9 ms` |

The fresh orders were database order IDs `6` to `15`.

## Database Trace

Summary query:

```sql
SELECT COUNT(*) AS TraceableOrderCount
FROM dbo.[Order] o
JOIN dbo.OmniOutboxMessage ob
    ON ob.OrderGuid = o.OrderGuid AND ob.StatusId = 20
JOIN dbo.OmniOrderFulfillment f
    ON f.OrderGuid = o.OrderGuid AND f.StatusId = 30
JOIN dbo.OmniInboxMessage ib
    ON ib.OrderGuid = o.OrderGuid
   AND ib.EventType = 'fulfillment.status.changed.v1'
   AND ib.StatusId = 20
WHERE o.Id BETWEEN 6 AND 15;
```

Observed result:

```text
TraceableOrderCount = 10
```

Detailed query:

```sql
SELECT TOP (10)
    o.Id AS OrderId,
    o.OrderGuid,
    ob.MessageId AS OutboxMessageId,
    ob.StatusId AS OutboxStatusId,
    f.MessageId AS FulfillmentMessageId,
    f.ExternalRequestId,
    f.StatusId AS FulfillmentStatusId,
    ib.Id AS InboxId,
    ib.StatusId AS InboxStatusId
FROM dbo.[Order] o
JOIN dbo.OmniOutboxMessage ob ON ob.OrderGuid = o.OrderGuid
JOIN dbo.OmniOrderFulfillment f ON f.OrderGuid = o.OrderGuid
JOIN dbo.OmniInboxMessage ib
    ON ib.OrderGuid = o.OrderGuid
   AND ib.EventType = 'fulfillment.status.changed.v1'
ORDER BY o.Id DESC;
```

Observed rows:

| OrderId | OrderGuid | OutboxMessageId | FulfillmentMessageId | ExternalRequestId | Status |
|---------|-----------|-----------------|----------------------|-------------------|--------|
| 15 | `2CB41857-3C7B-4984-8BBE-2C329A5DD25E` | `B91DD07B-D7F2-438B-9C60-5AC9FB6532F3` | `2861E4E0-D9E6-420B-AA1A-E98D73CA84FA` | `WMS-REQ-15` | Published / Accepted / Processed |
| 14 | `39E487FD-4C24-4F30-95EA-B4C3AE885ED7` | `70EEDB5C-BC76-4F52-9FF0-0F41AB06EA3B` | `DE586D47-22FD-49DD-B77C-487A198BD4F5` | `WMS-REQ-14` | Published / Accepted / Processed |
| 13 | `DE1A65FB-A639-432D-969E-CAB8CDBCA307` | `E6F3D28A-570C-4263-B160-00BDCDC7E7A9` | `C32D1FE2-D841-4EAE-99FF-7B6EAA263B5F` | `WMS-REQ-13` | Published / Accepted / Processed |
| 12 | `1719B0F9-25CC-4C23-945C-BDF77EFE6168` | `F08F982F-26A9-493B-AB3C-4C727B2AAD5A` | `F5621FAC-AF2C-4645-BB1C-83230FF60DC6` | `WMS-REQ-12` | Published / Accepted / Processed |
| 11 | `DA81462E-6A63-4855-B7D3-3716AB85E16E` | `28B84F1A-8BB6-4C3A-97B8-A9EE8C411617` | `B2CEB6CF-3070-4FC6-B643-689042E9B1D0` | `WMS-REQ-11` | Published / Accepted / Processed |
| 10 | `320E2123-0CD5-47F4-8466-F5D8F557FAD2` | `A17A18E6-0AED-4234-A2E8-A7C9095A4CA7` | `4FC3682C-9328-426A-ADEA-FE2616EA5855` | `WMS-REQ-10` | Published / Accepted / Processed |
| 9 | `18CC056F-431A-4F6D-8392-9A622E8C3269` | `9461406E-C939-40E3-A002-3B5F488F200A` | `51703513-5CAD-43CE-991B-E6B9CCE44362` | `WMS-REQ-9` | Published / Accepted / Processed |
| 8 | `9E96F7CD-C6AD-4A55-9111-857511FAFB18` | `8FF9CB34-729F-4078-BA0C-3F2B9B958F6F` | `6A5B388C-984D-4248-B190-FBCCCFB1CA6E` | `WMS-REQ-8` | Published / Accepted / Processed |
| 7 | `8977E6E7-7B35-4435-9079-2538E1B316ED` | `52B872BB-1FA5-44FD-B24A-59C0DF73D90B` | `ADAFFB0B-977B-4520-9891-7BC8DBA975FF` | `WMS-REQ-7` | Published / Accepted / Processed |
| 6 | `17688393-1E15-4984-91CE-0FAA4E9D928B` | `9369D779-B190-4C1D-ADBC-FF1E10160F35` | `0455540A-F344-4D29-9D25-EE8E05B4F182` | `WMS-REQ-6` | Published / Accepted / Processed |

Status meaning:

- `OutboxStatusId = 20`: published to RabbitMQ.
- `FulfillmentStatusId = 30`: WMS accepted fulfillment.
- `InboxStatusId = 20`: fulfillment callback processed.

## Worker and WMS Log Cross-Check

Worker logs include the same three fields:

```text
WMS accepted order_guid=2CB41857-3C7B-4984-8BBE-2C329A5DD25E message_id=B91DD07B-D7F2-438B-9C60-5AC9FB6532F3 external_request_id=WMS-REQ-15
Posted fulfillment callback order_guid=2CB41857-3C7B-4984-8BBE-2C329A5DD25E message_id=2861E4E0-D9E6-420B-AA1A-E98D73CA84FA external_request_id=WMS-REQ-15 status=Accepted
```

WMS simulator logs include the same field names:

```text
fulfillment accepted order_guid=2CB41857-3C7B-4984-8BBE-2C329A5DD25E message_id=B91DD07B-D7F2-438B-9C60-5AC9FB6532F3 external_request_id=WMS-REQ-15 status=Accepted mode=normal
```

Code cross-check:

```bash
rg -n "order_guid=|message_id=|external_request_id=" \
  nopCommerce/src/Plugins/Nop.Plugin.Misc.OmnichannelCore \
  services/worker \
  services/wms-sim
```

Confirmed field names:

- plugin logs: `order_guid`, `message_id`, `external_request_id`;
- worker logs: `order_guid`, `message_id`, `external_request_id`;
- WMS logs: `order_guid`, `message_id`, `external_request_id`.

This allows one operational search pattern across plugin code/logs, worker logs and WMS logs.

## RabbitMQ State

After the 10 orders:

```text
name                  messages  messages_ready  messages_unacknowledged
wms.order.placed      0         0               0
wms.order.placed.dlq  0         0               0
```

Meaning: all 10 order messages were consumed, acknowledged, and no message was left in the DLQ.

## QA-3 Result

Current status: **complete**.

| Requirement | Evidence | Result |
|-------------|----------|--------|
| trace 10 orders from order to fulfillment | `TraceableOrderCount = 10` | pass |
| admin trace usable in `<= 3` clicks | Configure -> Trace -> inspect cards | pass |
| each order links outbox, MQ message and fulfillment state | table above shows `OrderGuid`, outbox `MessageId`, fulfillment `MessageId`, `ExternalRequestId` | pass |
| worker attempts caveat documented | worker attempts resolved from logs by `message_id`, not persisted in admin view | pass |
| field names consistent end-to-end | `order_guid`, `message_id`, `external_request_id` cross-checked | pass |
