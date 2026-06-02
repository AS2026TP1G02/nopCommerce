# Diogu QA-1 Runbook

This file is the exact runbook for finishing QA-1.

QA-1 goal:

- WMS returns `503` for 30 seconds.
- Storefront checkout still works.
- Checkout P95 stays at or below `2085 ms`.
- RabbitMQ backlog drains within `60 s` after WMS returns to normal.
- No orders remain pending for more than `5 min` after recovery.

## Baseline Numbers

From [docs/evidence/baseline.md](/home/diogu/UNI/nopCommerce/docs/evidence/baseline.md):

- P50: `1235 ms`
- P95: `1390.05 ms`
- QA-1 P95 limit: `2085 ms`

## What Is Already Proven

- WMS simulator modes exist.
- Compose wiring exists.
- One normal storefront order already reached `OmniOrderFulfillment`.

Normal-flow proof already captured in
[docs/evidence/qa-1-pressure.md](/home/diogu/UNI/nopCommerce/docs/evidence/qa-1-pressure.md).

## Files You Will Update

- [docs/evidence/qa-1-pressure.md](/home/diogu/UNI/nopCommerce/docs/evidence/qa-1-pressure.md)
- Optional local temp artifacts:
  - `/tmp/loadtest-qa1-pressure.log`
  - `/tmp/worker-qa1-pressure.log`
  - `/tmp/qa1-before.txt`
  - `/tmp/qa1-after.txt`
  - `/tmp/qa1-after-60s.txt`
  - `/tmp/qa1-pending.txt`

## Before You Start

1. Start the stack.

```bash
docker compose up --build
```

2. Confirm health.

```bash
docker compose ps
curl http://localhost:8081/health
curl http://localhost:8081/mode
curl http://localhost:8082/health
curl http://localhost:8082/mode
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

3. Confirm normal flow still works before doing the pressure test.

- Place one normal order in the storefront.
- Verify a new fulfillment row appears:

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Omni_Demo_Pass1' -C -d nopCommerce \
  -Q "SELECT TOP 10 Id,OrderGuid,OrderId,StatusId,ExternalRequestId,AcceptedOnUtc,UpdatedOnUtc FROM OmniOrderFulfillment ORDER BY Id DESC"
```

If you do not get a new fulfillment row, stop. QA-1 is not worth running until
the happy path is live.

## Pressure Test Procedure

1. Put WMS in normal mode first.

```bash
curl -X POST http://localhost:8081/mode/normal
```

2. Record the exact start time, then switch WMS to unavailable.

```bash
date --iso-8601=seconds
curl -X POST http://localhost:8081/mode/unavailable
```

3. Immediately run the checkout load during that unavailable window.

```bash
cd load-test
ORDER_TARGET=50 BASE_URL=http://localhost:8080 ./run-load-test.sh automated | tee /tmp/loadtest-qa1-pressure.log
```

4. After roughly 30 seconds, restore WMS to normal and record the recovery time.

```bash
date --iso-8601=seconds
curl -X POST http://localhost:8081/mode/normal
```

5. Capture queue state right after recovery.

```bash
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged | tee /tmp/qa1-after.txt
```

6. Wait 60 seconds and capture queue state again.

```bash
sleep 60
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged | tee /tmp/qa1-after-60s.txt
```

7. Capture worker logs covering the pressure window.

```bash
docker compose logs worker --since 15m | tee /tmp/worker-qa1-pressure.log
```

8. Check whether any orders are still pending more than 5 minutes after recovery.

Use this after waiting long enough for the 5-minute window to matter:

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Omni_Demo_Pass1' -C -d nopCommerce \
  -Q "SELECT Id,OrderGuid,OrderId,StatusId,ExternalRequestId,AcceptedOnUtc,UpdatedOnUtc FROM OmniOrderFulfillment WHERE StatusId <> 30 ORDER BY Id DESC"
```

If your status mapping differs, inspect recent rows and adapt the query to the
actual “pending/degraded” status values used by the plugin.

## What To Extract From The Load Test

Open `/tmp/loadtest-qa1-pressure.log` and extract:

- total checkout attempts
- successful checkouts
- failed checkouts
- degraded-window P95 latency

What matters:

- failures attributable to WMS must be `0`
- P95 must be `<= 2085 ms`

## What To Extract From RabbitMQ

From `/tmp/qa1-after.txt` and `/tmp/qa1-after-60s.txt`, record:

- queue depth immediately after recovery
- queue depth 60 seconds later
- whether `wms.order.placed` drained to `0`
- whether DLQ grew unexpectedly

Pass condition:

- backlog drain `<= 60 s`

## What To Extract From Worker Logs

From `/tmp/worker-qa1-pressure.log`, look for:

- WMS failures while mode was `unavailable`
- retry / circuit-breaker behavior
- successful callback posts after recovery

Useful evidence lines mention:

- order GUID
- message ID
- external request ID
- callback success after recovery

## How To Update `docs/evidence/qa-1-pressure.md`

Replace the pending values in the Results table with:

- actual unavailable window start/end
- checkout attempts
- successful checkouts
- failed checkouts attributable to WMS
- degraded P95
- queue depth at peak
- backlog drain time
- orders pending after 5 minutes

Then paste in:

- the RabbitMQ queue snapshots
- a short excerpt from worker logs
- the SQL query/result for any pending-order check

## Pass / Fail Summary

QA-1 passes only if all of these are true:

- checkout P95 `<= 2085 ms`
- WMS-caused checkout failures = `0`
- backlog drains within `60 s`
- no orders remain pending for more than `5 min`

If one fails, still record the numbers. A measured failure is better than a
missing result.
