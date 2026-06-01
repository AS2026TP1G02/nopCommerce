# Setup & Run

Build-and-run instructions for the Scenario C omnichannel demo stack.

> **Status: infrastructure walkthrough current as of 2026-06-01.** Docker
> Compose starts the full service set. The true order-to-fulfillment E2E path is
> still blocked until the plugin outbox publisher sends to RabbitMQ and the
> worker fulfillment callback endpoint is implemented.

## Prerequisites

- Docker + Docker Compose v2
- (local dev only) .NET SDK `10.0.100` — see `nopCommerce/global.json`
- (local dev only) Python 3.12 for `services/wms-sim`
- (measurement only) `k6` for `load-test/run-load-test.sh`

## Components

| Service | Path | Port (host) | Role |
|---------|------|-------------|------|
| nopCommerce | `nopCommerce/` | 8080 | commerce core (storefront + admin + OmnichannelCore plugin) |
| SQL Server | (image) | 1433 | nopCommerce database |
| RabbitMQ | (image) | 5672 / 15672 | async order→WMS transport + management UI |
| Worker | `services/worker/` | — | consumes `commerce.order.placed.v1`, calls WMS, posts fulfillment status |
| WMS sim | `services/wms-sim/` | 8081 | warehouse boundary; modes normal/slow/unavailable/contradictory |
| POS sim | `services/pos-sim/` | 8082 | POS stock events; modes normal/duplicate/stale |
| Contracts | `services/contracts/` | — | shared message envelope (referenced by worker) |

## Quick start

From the repository root:

```bash
docker compose up --build
```

Keep this terminal open for logs. In another terminal, confirm the containers:

```bash
docker compose ps
```

Expected host endpoints:

- Storefront: <http://localhost:8080>
- RabbitMQ management: <http://localhost:15672> (`guest` / `guest`)
- WMS simulator: <http://localhost:8081>
- POS simulator: <http://localhost:8082>

## nopCommerce install

If the database volume is fresh, open <http://localhost:8080> and complete the
nopCommerce install wizard.

Use these Docker-internal database values:

| Field | Value |
|-------|-------|
| Database type | SQL Server |
| Server name | `sqlserver` |
| Database name | `nopCommerce` |
| SQL username | `sa` |
| SQL password | `Omni_Demo_Pass1` |
| Create database if it does not exist | enabled |

After installation, sign in to admin and install **Misc.OmnichannelCore** from
Admin -> Configuration -> Plugins.

## Infrastructure smoke checks

Run these from the repository root after `docker compose up --build`:

```bash
curl http://localhost:8081/health
curl http://localhost:8081/mode
curl http://localhost:8082/health
curl http://localhost:8082/mode
```

Check RabbitMQ queue state:

```bash
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

Latest local smoke capture: `2026-06-01T23:11:15+01:00` on commit
`207a628fd1`. `docker compose ps` showed `nopcommerce`, `sqlserver`,
`rabbitmq`, `worker`, `wms-sim`, and `pos-sim` all healthy. WMS and POS both
reported `normal` mode, and RabbitMQ showed `wms.order.placed` plus
`wms.order.placed.dlq` with zero messages.

Open <http://localhost:15672>, then inspect:

- Queues -> `wms.order.placed`
- Queues -> `wms.order.placed.dlq`
- Exchanges -> `commerce`
- Exchanges -> `commerce.dlx`

## WMS simulator controls

The WMS simulator is exposed on host port `8081`.

```bash
curl http://localhost:8081/health
curl http://localhost:8081/mode

curl -X POST http://localhost:8081/mode/normal
curl -X POST http://localhost:8081/mode/slow
curl -X POST http://localhost:8081/mode/unavailable
curl -X POST http://localhost:8081/mode/contradictory
```

Mode behavior:

| Mode | Behavior |
|------|----------|
| `normal` | `POST /fulfillments` returns HTTP `202` with `Accepted`. |
| `slow` | waits `WMS_SLOW_DELAY_SECONDS`, then returns accepted. |
| `unavailable` | returns HTTP `503`; used for QA-1 pressure. |
| `contradictory` | returns HTTP `409`; used for rejected/poison-path demos. |

## POS simulator controls

The POS simulator is exposed on host port `8082`.

```bash
curl http://localhost:8082/health
curl http://localhost:8082/mode
```

Emit one normal stock update:

```bash
curl -X POST http://localhost:8082/emit \
  -H "Content-Type: application/json" \
  -d '{"mode":"normal","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":3}'
```

Emit a duplicate update:

```bash
curl -X POST http://localhost:8082/emit \
  -H "Content-Type: application/json" \
  -d '{"mode":"duplicate","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":4}'
```

Emit a stale update:

```bash
curl -X POST http://localhost:8082/emit \
  -H "Content-Type: application/json" \
  -d '{"mode":"stale","productId":15,"sku":"LAPTOP-15","warehouseId":2,"quantityOnHand":5}'
```

The simulator sends `X-Demo-Token: omni-demo-token` to
`/omnichannel/callbacks/pos/stock-changed`.

## Load-test command

The checkout automation used for the baseline lives under `load-test/`.

```bash
cd load-test
ORDER_TARGET=50 BASE_URL=http://localhost:8080 ./run-load-test.sh automated
```

The baseline was captured in `docs/evidence/baseline.md`:

| Metric | Value |
|--------|-------|
| Baseline P50 checkout latency | `1235 ms` |
| Baseline P95 checkout latency | `1390.05 ms` |
| QA-1 P95 threshold | `2085 ms` |

## Demo scenarios

- **Normal order flow (Phase 2)** — place an order → `OmniOutboxMessage` row →
  worker → WMS → `OmniOrderFulfillment` state `Accepted`.
- **WMS pressure + recovery (Phase 3, QA-1)** —
  `curl -X POST http://localhost:8081/mode/unavailable`, place orders, observe
  worker retry → circuit breaker → backlog drain after
  `curl -X POST http://localhost:8081/mode/normal`. Other WMS modes:
  `curl -X POST http://localhost:8081/mode/slow` and
  `curl -X POST http://localhost:8081/mode/contradictory`.
- **POS consistency (Phase 4, QA-2)** — `services/pos-sim` `normal` / `duplicate`
  / `stale`; see `docs/evidence/qa-2-consistency.md`.
- **Traceability (Phase 5, QA-3)** — look up an `OrderGuid` in the plugin admin
  view; see `docs/evidence/qa-3-traceability.md`.

## Current E2E blocker

Compose starts the services, but the order-to-WMS-to-fulfillment path cannot be
claimed as complete until these plugin tasks land:

- `OutboxPublisherTask.PublishAsync(...)` publishes pending outbox payloads to
  RabbitMQ exchange `commerce` with routing key `commerce.order.placed.v1`.
- `OmnichannelCallbackController` accepts `fulfillment.status.changed.v1` from
  the worker and updates `OmniOrderFulfillment`.

Until then, the Compose infrastructure is ready at the container/network/
healthcheck level, while the Phase 2 E2E verification gate remains blocked by
plugin integration.

## Baseline measurement

See `docs/evidence/baseline.md` (captured before plugin install; referenced by
the QA-1 "≤ 1.5× baseline" gate).
