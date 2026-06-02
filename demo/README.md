# Demo Scripts

Helpers for the live flow described in [tmp.md](/home/diogu/UNI/nopCommerce/tmp.md).

## Prerequisites

- Start the stack from the repo root with `docker compose up --build`
- For QA-1 load generation, install `k6`
- For SQL lookups, keep the default Compose service names and SQL password, or override env vars before running

## Scenario Map

1. Normal order -> accepted
   Place one order in the storefront manually, then run `./demo/sql-order-trace.sh <order-guid>`
2. WMS down 30 s, checkout survives
   Run `./demo/qa1-pressure.sh`
3. WMS recovers, backlog drains
   `qa1-pressure.sh` also captures immediate and +60s queue snapshots plus worker logs
4. POS duplicate + stale ignored
   Run `./demo/pos-emit.sh duplicate` and `./demo/pos-emit.sh stale`
5. Trace one order + ops dashboard
   Run `./demo/rabbitmq-state.sh`, `./demo/worker-logs.sh`, `./demo/sql-order-trace.sh <order-guid>`

## Demo Walkthrough

This is the tighter live runbook for [tmp.md](/home/diogu/UNI/nopCommerce/tmp.md:1).

Open these in advance:

- Storefront: `http://localhost:8080`
- RabbitMQ UI: `http://localhost:15672`
- Admin trace page pattern: `http://localhost:8080/Admin/OmnichannelCore/Trace?orderGuid=<order-guid>`

### 0. Prep

Run these first:

```bash
docker compose up --build
./demo/health.sh
./demo/wms-mode.sh normal
```

What you show:
- all services are up
- WMS starts in `normal`
- RabbitMQ queues are visible

### 1. Normal order -> accepted

What you do:

1. Place one order manually in the storefront.
2. Copy the `OrderGuid` for that order.
3. Run:

```bash
./demo/sql-order-trace.sh <order-guid>
```

4. Open:

```text
http://localhost:8080/Admin/OmnichannelCore/Trace?orderGuid=<order-guid>
```

What you show:
- normal checkout succeeds
- the order is traceable by `OrderGuid`
- the outbox/inbox/fulfillment chain exists
- fulfillment should end up at `Accepted`

### 2. WMS down for 30 seconds, checkout survives

Run:

```bash
./demo/qa1-pressure.sh
```

While it is running:
- keep RabbitMQ UI open on the queues page
- point out `wms.order.placed` queue depth increasing
- mention that checkout still completes while WMS is unavailable

What this single script does:
- sets WMS to `unavailable`
- runs the checkout load
- restores WMS to `normal` after 30 seconds
- captures queue state immediately after recovery
- waits 60 seconds
- captures queue state again
- captures worker logs

### 3. WMS recovers, backlog drains

After `./demo/qa1-pressure.sh` finishes, run:

```bash
./demo/rabbitmq-state.sh
./demo/worker-logs.sh 15m
```

Then check the generated artifact folder:

```bash
ls -td demo/artifacts/* | head -n 1
LATEST="$(ls -td demo/artifacts/* | head -n 1)"
cat "$LATEST/queues-after-recovery.txt"
cat "$LATEST/queues-after-60s.txt"
```

If you want to confirm fulfillment recovery for the latest orders, run:

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Omni_Demo_Pass1' -C -d nopCommerce \
  -Q "SELECT TOP 10 Id,OrderGuid,OrderId,StatusId,ExternalRequestId,AcceptedOnUtc,UpdatedOnUtc FROM OmniOrderFulfillment ORDER BY Id DESC"
```

Important:
- during the outage, `StatusId = 10` is expected because that means `Pending`
- after recovery and drain, the recovered rows should move to `StatusId = 30`
- `ExternalRequestId` should stop being `NULL` once WMS acceptance completes

What you show:
- WMS is back to `normal`
- the backlog drains automatically
- pending fulfillment rows eventually become accepted

### 4. POS duplicate + stale ignored

Run duplicate:

```bash
./demo/pos-emit.sh duplicate
```

Copy one returned `messageId`, then run:

```bash
./demo/sql-message-trace.sh <duplicate-message-id>
```

Run stale:

```bash
./demo/pos-emit.sh stale
```

Copy one returned `messageId`, then run:

```bash
./demo/sql-message-trace.sh <stale-message-id>
```

What you show:
- duplicate `messageId` does not create duplicate processing
- stale `sourceVersion` is ignored
- stock projection stays correct

### 5. Trace one order + ops dashboard

Run:

```bash
./demo/rabbitmq-state.sh
./demo/worker-logs.sh 10m
./demo/sql-order-trace.sh <order-guid>
```

Open:

```text
http://localhost:8080/Admin/OmnichannelCore/Trace?orderGuid=<order-guid>
```

What you show:
- one order can be traced by `OrderGuid`
- queue depth and DLQ are visible
- worker logs, outbox rows, inbox rows, and fulfillment rows line up

## Exact Command Order

Run these in this order during the demo:

```bash
docker compose up --build
./demo/health.sh
./demo/wms-mode.sh normal
```

Then place one normal order manually and run:

```bash
./demo/sql-order-trace.sh <order-guid>
```

Then run the degradation + recovery scenario:

```bash
./demo/qa1-pressure.sh
./demo/rabbitmq-state.sh
./demo/worker-logs.sh 15m
LATEST="$(ls -td demo/artifacts/* | head -n 1)"
cat "$LATEST/queues-after-recovery.txt"
cat "$LATEST/queues-after-60s.txt"
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'Omni_Demo_Pass1' -C -d nopCommerce \
  -Q "SELECT TOP 10 Id,OrderGuid,OrderId,StatusId,ExternalRequestId,AcceptedOnUtc,UpdatedOnUtc FROM OmniOrderFulfillment ORDER BY Id DESC"
```

Then run the POS consistency checks:

```bash
./demo/pos-emit.sh duplicate
./demo/sql-message-trace.sh <duplicate-message-id>
./demo/pos-emit.sh stale
./demo/sql-message-trace.sh <stale-message-id>
```

Then finish with traceability + operability:

```bash
./demo/rabbitmq-state.sh
./demo/worker-logs.sh 10m
./demo/sql-order-trace.sh <order-guid>
```

## Helper Scripts

- `./demo/health.sh`
  Smoke check for Compose services, WMS, POS, and RabbitMQ queues.
- `./demo/wms-mode.sh <normal|slow|unavailable|contradictory>`
  Toggle the WMS simulator without restarting containers.
- `./demo/rabbitmq-state.sh`
  Print queue depth for the main queue and DLQ.
- `./demo/worker-logs.sh [since]`
  Show worker logs for the recent demo window. Default: `10m`.
- `./demo/pos-emit.sh <normal|duplicate|stale> [quantity] [productId] [warehouseId] [sku]`
  Push a POS stock event through the real callback path.
- `./demo/sql-order-trace.sh <order-guid>`
  Query outbox, inbox, and fulfillment rows for one order and print the admin trace URL.
- `./demo/sql-message-trace.sh <message-id>`
  Query inbox and stock projection rows for a POS callback message.
- `./demo/qa1-pressure.sh [durationSeconds] [orderTarget]`
  Automate the WMS-unavailable window, load test trigger, queue snapshots, and worker log capture.

## Notes

- `sql-order-trace.sh` is the quickest way to support QA-3 before opening the admin page at:
  `http://localhost:8080/Admin/OmnichannelCore/Trace?orderGuid=<guid>`
- `qa1-pressure.sh` writes artifacts under `demo/artifacts/<timestamp>/`
