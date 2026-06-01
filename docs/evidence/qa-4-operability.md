# QA-4 Operability Evidence

Operability scenario: when WMS is slow or unavailable, an operator can observe
transport state and fulfillment state quickly enough to understand degradation.

> **Status:** infrastructure evidence prepared. RabbitMQ Management UI is
> exposed by Compose and worker logs include retry/circuit-breaker fields. Full
> QA-4 runtime measurement still depends on the Phase 2/5 plugin work that
> creates fulfillment records and exposes pending fulfillment state.

## Source Scenario

QA-4 from `docs/part1/quality-attribute-scenarios.md`:

- Stimulus: WMS simulator switched to `slow` or `unavailable`.
- Response: operator observes queue depth rising, retry count greater than zero,
  circuit breaker state, DLQ size if persistent failure occurs, and
  fulfillment-pending count.
- Response measure: queue depth, retry count, DLQ size and fulfillment-pending
  count visible in one dashboard view with refresh latency `<= 5 s`.

## Compose Exposure

RabbitMQ is exposed in `docker-compose.yml`:

| Capability | Endpoint |
|------------|----------|
| RabbitMQ AMQP | `localhost:5672` |
| RabbitMQ Management UI | `http://localhost:15672` |
| Credentials | `guest` / `guest` |

The WMS and POS controls are exposed for demos:

| Service | Endpoint |
|---------|----------|
| WMS simulator | `http://localhost:8081` |
| POS simulator | `http://localhost:8082` |

## Operator Walkthrough

Start the stack:

```bash
docker compose up --build
```

Open RabbitMQ Management:

```text
http://localhost:15672
username: guest
password: guest
```

Inspect these objects:

| View | Expected object | Why it matters |
|------|-----------------|----------------|
| Queues | `wms.order.placed` | Main backlog of order fulfillment work. |
| Queues | `wms.order.placed.dlq` | Poison/dead-lettered messages. |
| Exchanges | `commerce` | Main order event exchange. |
| Exchanges | `commerce.dlx` | Dead-letter exchange. |

CLI equivalent:

```bash
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_ready messages_unacknowledged
```

## Degradation Procedure

Switch WMS to slow mode:

```bash
curl -X POST http://localhost:8081/mode/slow
```

Switch WMS to unavailable mode:

```bash
curl -X POST http://localhost:8081/mode/unavailable
```

Return WMS to normal:

```bash
curl -X POST http://localhost:8081/mode/normal
```

Capture worker logs during degradation:

```bash
docker compose logs worker --since 10m | tee /tmp/worker-qa4-operability.log
```

Relevant worker log fields:

| Field | Purpose |
|-------|---------|
| `order_guid` / `OrderGuid` | Links the order through the integration path. |
| `message_id` / `MessageId` | Links RabbitMQ message and callback message. |
| `external_request_id` / `ExternalRequestId` | Links WMS acceptance to fulfillment projection. |
| retry attempt | Shows WMS retry behavior. |
| circuit breaker open/closed | Shows degraded/recovered state. |

## Results

| Operability item | Current status | Evidence |
|------------------|----------------|----------|
| RabbitMQ Management UI reachable | Verified by Compose port exposure and healthy container | `http://localhost:15672` |
| Main queue depth visible | Verified via `rabbitmqctl` | `wms.order.placed` queue |
| DLQ size visible | Verified via `rabbitmqctl` | `wms.order.placed.dlq` queue |
| Retry/circuit-breaker behavior visible in worker logs | Implemented in worker; runtime capture pending | `/tmp/worker-qa4-operability.log` |
| Fulfillment-pending count visible in plugin admin | Blocked by plugin admin/fulfillment work | Plugin admin view |
| Combined refresh latency `<= 5 s` | Pending measurement | RabbitMQ UI + plugin admin |

## WMS Mode Toggle Capture

Captured by Diogu on the local Compose stack. This proves the WMS simulator can
switch through every demo pressure mode without restarting containers:

```bash
curl -X POST http://localhost:8081/mode/slow
curl http://localhost:8081/mode
curl -X POST http://localhost:8081/mode/unavailable
curl http://localhost:8081/mode
curl -X POST http://localhost:8081/mode/contradictory
curl http://localhost:8081/mode
curl -X POST http://localhost:8081/mode/normal
```

Observed output:

```json
{"mode":"slow","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0,"previousMode":"normal"}
{"mode":"slow","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0}
{"mode":"unavailable","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0,"previousMode":"slow"}
{"mode":"unavailable","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0}
{"mode":"contradictory","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0,"previousMode":"unavailable"}
{"mode":"contradictory","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0}
{"mode":"normal","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0,"previousMode":"contradictory"}
```

## Infrastructure Smoke Capture

Captured: `2026-06-01T23:11:15+01:00`  
Commit: `207a628fd1`

`docker compose ps` showed all six services healthy:

| Service | Status |
|---------|--------|
| `nopcommerce` | healthy |
| `sqlserver` | healthy |
| `rabbitmq` | healthy |
| `worker` | healthy |
| `wms-sim` | healthy |
| `pos-sim` | healthy |

Simulator health:

```text
WMS /health: {"status":"healthy","service":"wms-sim","mode":"normal"}
WMS /mode: {"mode":"normal","supportedModes":["contradictory","normal","slow","unavailable"],"slowDelaySeconds":3.0}
POS /health: {"status":"ok","simulator":"pos-sim","mode":"normal"}
POS /mode: {"mode":"normal"}
```

RabbitMQ queue snapshot:

```text
name                    messages  messages_ready  messages_unacknowledged
wms.order.placed        0         0               0
wms.order.placed.dlq    0         0               0
```

## Current Conclusion

Compose satisfies Diogu's infrastructure part of QA-4: RabbitMQ Management UI is
published, all services reached healthy state, WMS/POS controls respond, WMS
mode toggles work for every pressure mode, and queue/DLQ state can be inspected.
The final QA-4 pass/fail measurement must wait for the plugin to publish real
order messages, accept fulfillment callbacks, and expose pending fulfillment
state in the admin view.
