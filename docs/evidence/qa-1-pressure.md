# QA-1 Pressure Evidence

Resilience and recovery scenario: WMS returns HTTP `503` for 30 seconds while
checkout continues. After WMS returns to normal, the backlog drains without
manual intervention.

> **Status:** measured on 2026-06-02. The run confirms graceful degradation and
> eventual recovery, but the broker-side drain time still exceeded the original
> `<= 60 s` target. Checkout latency and the "no orders pending > 5 min" rule
> both passed.

## Source Scenario

QA-1 from `docs/part1/quality-attribute-scenarios.md`:

- Stimulus: WMS simulator switched to `unavailable` for 30 seconds.
- Degraded response: checkout completes; order is persisted; fulfillment remains
  pending/degraded while the worker retries.
- Recovery response: WMS returns to `normal`; worker drains the backlog.

## Thresholds

Baseline source: `docs/evidence/baseline.md`.

| Metric | Threshold |
|--------|-----------|
| Baseline P50 checkout latency | `1235 ms` |
| Baseline P95 checkout latency | `1390.05 ms` |
| Degraded checkout P95 limit | `2085 ms` (`1390.05 ms x 1.5`) |
| Checkout failures attributable to WMS | `0` |
| Backlog drain after WMS recovery | `<= 60 s` |
| Orders pending after recovery window | `0 pending > 5 min` |

## Pre-Run Checks

Start the stack:

```bash
docker compose up --build
```

Confirm service health:

```bash
docker compose ps
curl http://localhost:8081/health
curl http://localhost:8081/mode
curl http://localhost:8082/health
```

Confirm RabbitMQ queue visibility:

```bash
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

Confirm the plugin E2E prerequisites before running the measurement:

- `OutboxPublisherTask.PublishAsync(...)` is no longer a stub.
- `OmnichannelCallbackController` has a worker fulfillment callback endpoint.
- A normal storefront order produces an `OmniOrderFulfillment` row with state
  `Accepted`.

### Confirmed Normal-Flow Prerequisite

The following SQL capture, taken on 2026-06-02 after placing a storefront
order, confirms that the normal fulfillment path reached the plugin projection:

```sql
SELECT TOP 10
    Id,
    OrderGuid,
    OrderId,
    StatusId,
    ExternalRequestId,
    AcceptedOnUtc,
    UpdatedOnUtc
FROM OmniOrderFulfillment
ORDER BY Id DESC;
```

Observed result:

```text
Id          OrderGuid                            OrderId     StatusId    ExternalRequestId    AcceptedOnUtc                  UpdatedOnUtc
----------- ------------------------------------ ----------- ----------- -------------------- ------------------------------ ------------------------------
1002        CBADAE7B-1FDE-4809-B94A-F10D37440F15 2001        30          WMS-REQ-2001         2026-06-02 12:14:58.131000   2026-06-02 12:14:58.131000
```

Interpretation:

- `OmniOrderFulfillment` contains a row for a real order.
- `ExternalRequestId` was returned from WMS.
- The worker callback updated the plugin-side fulfillment projection.
- QA-1 can proceed, provided the stack remains healthy during the pressure run.

## Pressure Run Procedure

Set WMS to normal before starting:

```bash
curl -X POST http://localhost:8081/mode/normal
```

Switch WMS to unavailable:

```bash
date --iso-8601=seconds
curl -X POST http://localhost:8081/mode/unavailable
```

Run checkout load during the pressure window:

```bash
cd load-test
ORDER_TARGET=50 BASE_URL=http://localhost:8080 ./run-load-test.sh automated | tee /tmp/loadtest-qa1-pressure.log
```

Restore WMS after 30 seconds:

```bash
date --iso-8601=seconds
curl -X POST http://localhost:8081/mode/normal
```

Capture RabbitMQ state immediately after recovery and again after drain:

```bash
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
sleep 60
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

Capture worker logs:

```bash
docker compose logs worker --since 10m | tee /tmp/worker-qa1-pressure.log
```

## Results

| Metric | Result | Pass/Fail |
|--------|--------|-----------|
| WMS unavailable window | `2026-06-02T20:43:26+01:00` -> `2026-06-02T20:44:35+01:00` | Reference |
| Checkout attempts | `50` | Pass |
| Successful checkouts | `50` | Pass |
| Failed checkouts attributable to WMS | `0` | Pass |
| Degraded P95 checkout latency | `1822.2 ms` | Pass |
| QA-1 P95 threshold | `2085 ms` | Reference |
| Queue depth at peak | `27` unacknowledged on `wms.order.placed` | Observed |
| Backlog drain time after WMS normal | `~5m33s` until queue returned to `0/0/0` | Fail |
| Orders pending after 5 min | `0` | Pass |

## Observed Run

### k6 Summary

Source: `/tmp/loadtest-qa1-pressure.log`

- `50 / 50` orders succeeded.
- `order_success_rate = 100%`
- `order_placement_duration_ms p(95) = 1822.2 ms`
- `http_req_failed = 0.00%`

This satisfies the degraded user-facing part of QA-1:

- checkout stayed available during WMS outage
- no checkout failures were attributable to WMS
- degraded checkout P95 stayed below the `2085 ms` threshold

### Worker Recovery Evidence

Source: `/tmp/worker-qa1-pressure.log`

Observed sequence:

- repeated WMS `503` responses during the unavailable window
- `WMS circuit breaker OPENED for 30s`
- repeated `Circuit breaker open, requeuing message after delay ...`
- `WMS circuit breaker CLOSED — draining backlog`
- accepted fulfillment callbacks resumed with real `external_request_id` values

Representative log excerpt:

```text
WMS circuit breaker OPENED for 30s — marking fulfillments degraded
Circuit breaker open, requeuing message after delay order_guid=0561a9c4-9e24-4e57-87b9-aa1c95f364b2 ...
WMS circuit breaker CLOSED — draining backlog
Posted fulfillment callback order_guid=0561a9c4-9e24-4e57-87b9-aa1c95f364b2 ... external_request_id=WMS-REQ-1002 status=Accepted
```

### RabbitMQ Queue Snapshots

Immediately after recovery:

```text
name                messages  messages_ready  messages_unacknowledged
wms.order.placed    6         0               6
wms.order.placed.dlq 0        0               0
```

Approx. 60 seconds into recovery:

```text
name                messages  messages_ready  messages_unacknowledged
wms.order.placed    27        0               27
wms.order.placed.dlq 0        0               0
```

At `2026-06-02T20:50:08+01:00`:

```text
name                messages  messages_ready  messages_unacknowledged
wms.order.placed    0         0               0
wms.order.placed.dlq 0        0               0
```

Interpretation:

- no poison messages were sent to the DLQ
- the worker now keeps breaker-open orders in play by requeuing them
- the queue did eventually drain, but not within the original `<= 60 s` target

### Fulfillment Recovery Evidence

Source: `OmniOrderFulfillment`

After recovery, all outage-created fulfillment rows transitioned back to
accepted. The recovered cohort (`OrderId 1002` through `1026`) shows:

```text
MinAcceptedOnUtc = 2026-06-02 19:44:41.410000
MaxAcceptedOnUtc = 2026-06-02 19:46:43.112000
AcceptedCount    = 25
```

Five-minute pending check:

```text
SELECT ... FROM OmniOrderFulfillment WHERE StatusId <> 30

(0 rows affected)
```

This satisfies the second recovery rule:

- no orders remained pending more than 5 minutes after WMS recovery

## Evidence Artifacts

Planned capture paths:

- k6 log: `/tmp/loadtest-qa1-pressure.log`
- worker log: `/tmp/worker-qa1-pressure.log`
- RabbitMQ queue snapshots: paste into this file after the run
- plugin/admin screenshot or table query showing pending count after recovery

## Current Conclusion

QA-1 is now measured.

Outcome: **partial pass**.

- The resilience behavior works from a user and data-consistency perspective:
  checkout stayed healthy, the worker degraded cleanly, and pending fulfillments
  were eventually reconciled.
- The strict broker-side recovery target is still not met: `wms.order.placed`
  did not return to zero within `60 s` after WMS recovery.

This should be presented as an implementation improvement over the earlier
failed run, but not as a full pass against the original QA-1 drain-time gate.
