# Setup & Run

Build-and-run instructions for the Scenario C omnichannel demo stack.

## Phase Markers

- Phase 1: stack bootstrap, prerequisites, install flow, and smoke checks.
- Phase 2: normal order-to-fulfillment happy path.
- Phase 3: WMS pressure and recovery controls.
- Phase 4: POS consistency controls.
- Phase 5: traceability and operability checks.
- Phase 6: evidence capture and fresh-clone smoke.

## Prerequisites

- Docker + Docker Compose v2
- Local ports `8080`, `8081`, `8082`, `1433`, `5672`, and `15672` free
- Local dev only: `.NET SDK 10.0.100` from [nopCommerce/global.json](/home/diogu/UNI/nopCommerce/nopCommerce/global.json)
- Measurement only: `k6` for `load-test/run-load-test.sh`

## Stack

| Service | Path | Host port | Role |
|---------|------|-----------|------|
| nopCommerce | `nopCommerce/` | `8080` | storefront, admin, and `Misc.OmnichannelCore` plugin |
| SQL Server | image | `1433` | nopCommerce database |
| RabbitMQ | image | `5672`, `15672` | broker and management UI |
| Worker | `services/worker/` | — | consumes `commerce.order.placed.v1`, calls WMS, posts fulfillment callback |
| WMS sim | `services/wms-sim/` | `8081` | normal / slow / unavailable / contradictory warehouse modes |
| POS sim | `services/pos-sim/` | `8082` | normal / duplicate / stale stock events |
| Contracts | `services/contracts/` | — | shared envelope and topology names |

## Quick Start

From the repository root:

```bash
docker compose up --build
```

In a second terminal:

```bash
docker compose ps
```

Expected endpoints:

- Storefront: <http://localhost:8080>
- RabbitMQ UI: <http://localhost:15672> with `guest` / `guest`
- WMS simulator: <http://localhost:8081>
- POS simulator: <http://localhost:8082>

## First-Time Install

If the SQL/App_Data volumes are fresh, open <http://localhost:8080> and finish
the nopCommerce install wizard with these values:

| Field | Value |
|-------|-------|
| Database type | SQL Server |
| Server name | `sqlserver` |
| Database name | `nopCommerce` |
| SQL username | `sa` |
| SQL password | `Omni_Demo_Pass1` |
| Create database if it does not exist | enabled |

After install:

1. Sign in to admin.
2. Go to `Configuration -> Local plugins`.
3. Install `Misc.OmnichannelCore`.
4. Confirm the plugin admin page is reachable, or open `/Admin/OmnichannelCore/Configure` directly.

## Compose Wiring

`docker-compose.yml` now declares the omnichannel runtime settings explicitly:

- `OmnichannelCore__RabbitMqUri=amqp://guest:guest@rabbitmq:5672/`
- `OmnichannelCore__DemoToken=omni-demo-token`
- `Worker__RabbitMqUri=amqp://guest:guest@rabbitmq:5672/`
- `Worker__WmsBaseUrl=http://wms-sim:8080`
- `Worker__NopCommerceBaseUrl=http://nopcommerce:8080`
- `Worker__DemoToken=omni-demo-token`
- `NopCommerce__BaseUrl=http://nopcommerce:8080` for the POS simulator

That is the intended Phase 2/3/4/5 boundary wiring:

- plugin outbox publisher -> RabbitMQ exchange `commerce`
- worker consumer <- queue `wms.order.placed`
- worker -> WMS simulator HTTP
- worker -> plugin fulfillment callback HTTP
- POS simulator -> plugin stock callback HTTP

## Smoke Checks

Run these after `docker compose up --build`:

```bash
curl http://localhost:8081/health
curl http://localhost:8081/mode
curl http://localhost:8082/health
curl http://localhost:8082/mode
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

Open RabbitMQ UI and verify:

- Queues -> `wms.order.placed`
- Queues -> `wms.order.placed.dlq`
- Exchanges -> `commerce`
- Exchanges -> `commerce.dlx`

## Normal End-to-End Flow

Phase 2 is considered wired when this path succeeds:

1. Place an order in the storefront.
2. Plugin writes an `OmniOutboxMessage` row.
3. `OutboxPublisherTask` publishes it to RabbitMQ.
4. Worker consumes `commerce.order.placed.v1`.
5. Worker calls WMS simulator `POST /fulfillments`.
6. Worker posts `fulfillment.status.changed.v1` to `/omnichannel/callbacks/fulfillment/status-changed`.
7. Plugin writes or updates `OmniOrderFulfillment` with status `Accepted`.

Useful checks during that flow:

```bash
docker compose logs worker --since 10m
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

If the order remains stuck, first check that the plugin is installed and the
nopCommerce scheduled tasks are enabled.

## WMS Simulator

```bash
curl http://localhost:8081/health
curl http://localhost:8081/mode
curl -X POST http://localhost:8081/mode/normal
curl -X POST http://localhost:8081/mode/slow
curl -X POST http://localhost:8081/mode/unavailable
curl -X POST http://localhost:8081/mode/contradictory
```

| Mode | Behavior |
|------|----------|
| `normal` | returns HTTP `202` accepted |
| `slow` | waits `WMS_SLOW_DELAY_SECONDS`, then returns accepted |
| `unavailable` | returns HTTP `503` |
| `contradictory` | returns HTTP `409` |

## POS Simulator

```bash
curl http://localhost:8082/health
curl http://localhost:8082/mode
```

Normal event:

```bash
curl -X POST http://localhost:8082/emit \
  -H "Content-Type: application/json" \
  -d '{"mode":"normal","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":3}'
```

Duplicate event:

```bash
curl -X POST http://localhost:8082/emit \
  -H "Content-Type: application/json" \
  -d '{"mode":"duplicate","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":4}'
```

Stale event:

```bash
curl -X POST http://localhost:8082/emit \
  -H "Content-Type: application/json" \
  -d '{"mode":"stale","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":5}'
```

The simulator sends `X-Demo-Token: omni-demo-token` to the plugin callback.

## Measurement

Baseline checkout command:

```bash
cd load-test
ORDER_TARGET=50 BASE_URL=http://localhost:8080 ./run-load-test.sh automated
```

Baseline values from [docs/evidence/baseline.md](/home/diogu/UNI/nopCommerce/docs/evidence/baseline.md):

| Metric | Value |
|--------|-------|
| P50 checkout latency | `1235 ms` |
| P95 checkout latency | `1390.05 ms` |
| QA-1 P95 threshold | `2085 ms` |

## Demo Scenarios

- Normal order flow: storefront order -> outbox -> RabbitMQ -> worker -> WMS -> fulfillment callback.
- QA-1 pressure: set WMS to `unavailable`, place orders, restore `normal`, then measure backlog drain.
- QA-2 consistency: use POS `normal`, `duplicate`, and `stale`.
- QA-3 traceability: look up an `OrderGuid` in the plugin admin trace page.
- QA-4 operability: use RabbitMQ UI plus plugin admin counters.

## Fresh-Clone Smoke

Before presentation/demo, verify from a fresh checkout:

```bash
docker compose up --build
docker compose ps
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

Success condition:

- all six services healthy
- RabbitMQ UI reachable
- WMS and POS health endpoints reachable
- plugin install works on a fresh DB
- one normal order reaches `Accepted`
