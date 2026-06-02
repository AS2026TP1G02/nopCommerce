# QA-1 Pressure Evidence

Resilience and recovery scenario: WMS returns HTTP `503` for 30 seconds while
checkout continues. After WMS returns to normal, the backlog drains without
manual intervention.

> **Status:** measurement template prepared. Runtime numbers are not captured
> yet, but the Phase 2 happy path prerequisite is now confirmed by a real
> `OmniOrderFulfillment` row captured on 2026-06-02. QA-1 still needs the
> degraded-window measurements, queue snapshots, and backlog-drain timing.

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
| WMS unavailable window | Pending | Pending |
| Checkout attempts | Pending | Pending |
| Successful checkouts | Pending | Pending |
| Failed checkouts attributable to WMS | Pending | Pending |
| Degraded P95 checkout latency | Pending | Pending |
| QA-1 P95 threshold | `2085 ms` | Reference |
| Queue depth at peak | Pending | Pending |
| Backlog drain time after WMS normal | Pending | Pending |
| Orders pending after 5 min | Pending | Pending |

## Evidence Artifacts

Planned capture paths:

- k6 log: `/tmp/loadtest-qa1-pressure.log`
- worker log: `/tmp/worker-qa1-pressure.log`
- RabbitMQ queue snapshots: paste into this file after the run
- plugin/admin screenshot or table query showing pending count after recovery

## Current Conclusion

Not fully measured yet. The WMS simulator and Compose infrastructure are ready,
and at least one normal order has already reached `OmniOrderFulfillment`.
Remaining work is the degraded-window run itself: capture checkout P95 under WMS
`503`, record queue buildup, measure drain time after recovery, and confirm no
orders stay pending for more than 5 minutes.
