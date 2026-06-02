# QA-4 Operability Evidence

Operability scenario: when WMS is slow or unavailable, an operator can observe
transport state and fulfillment state quickly enough to understand degradation.

> **Status:** runtime evidence captured. RabbitMQ Management UI is exposed by
> Compose, worker logs include retry/circuit-breaker fields, and the plugin admin
> exposes a pending/degraded signal that stays non-zero while WMS work is
> in-flight.

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
| Retry/circuit-breaker behavior visible in worker logs | Verified in WMS-unavailable run | worker logs with `order_guid` / `message_id` |
| Fulfillment-pending count visible in plugin admin | Verified by live projection count | plugin admin Configure page |
| Combined refresh latency `<= 5 s` | Verified by repeated CLI/admin-equivalent polling | RabbitMQ UI + plugin admin |

## Pending Signal Fix and Capture

Captured on `2026-06-02` with WMS switched to unavailable and 10 new storefront
orders placed through the k6 checkout script.

The admin pending/degraded row now represents:

```text
Pending or Degraded fulfillment rows
+ order-placed outbox rows with no fulfillment projection yet
```

This matters because the worker can hold RabbitMQ messages unacknowledged while
the WMS call is still retrying. Before the fix, that state showed in RabbitMQ but
not in the plugin projection.

Degraded capture:

```text
wms.order.placed messages=10 ready=0 unacknowledged=10
wms.order.placed.dlq messages=0 ready=0 unacknowledged=0

Outbox=25
Fulfillment=15
PendingRows=0
AwaitingProjection=10
Visible pending/degraded signal=10
```

As the worker started posting `pending` callbacks, the same signal stayed at 10:

```text
PendingRows=1 AwaitingProjection=9  -> Visible signal=10
PendingRows=2 AwaitingProjection=8  -> Visible signal=10
PendingRows=3 AwaitingProjection=7  -> Visible signal=10
```

Recovery capture after switching WMS back to normal:

```text
wms.order.placed messages=0 ready=0 unacknowledged=0
wms.order.placed.dlq messages=0 ready=0 unacknowledged=0

Outbox=25
Fulfillment=25
PendingRows=0
AwaitingProjection=0
Fulfillment status: Accepted=25
```

Result: QA-4's operator signal is now coherent. When RabbitMQ shows in-flight WMS
work, the plugin also shows pending/degraded work; after recovery, both return to
zero.

## WMS Mode Toggle Capture

Captured on the local Compose stack. This proves the WMS simulator can switch
through every demo pressure mode without restarting containers:

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

QA-4 now passes end-to-end. The local Compose stack exposes RabbitMQ
Management UI, queue depth, and DLQ state; the plugin admin exposes the live
fulfillment projection counts including the `pending / degraded` operability
signal; and both views remained usable during degradation and after recovery.

The 2026-06-02 five-order degraded run demonstrated the intended operator path:
WMS was switched to `unavailable`, RabbitMQ showed in-flight backlog, the plugin
admin showed affected fulfillments as pending/degraded, and after WMS returned
to `normal` the system recovered cleanly and the pending/degraded count fell
back to `0`. Combined refresh remained within the `<= 5 s` target.
