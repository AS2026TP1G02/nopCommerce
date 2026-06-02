# QA-4 Operability Evidence

Operability scenario: when WMS is slow or unavailable, an operator can observe
transport state and fulfillment state quickly enough to understand degradation.

> **Status:** complete. Re-measured on 2026-06-02 with a 5-order degraded run.
> RabbitMQ Management UI and the plugin admin page both exposed the required
> operability signals, and the combined refresh latency remained within the
> `<= 5 s` target.

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
| RabbitMQ Management UI reachable | Pass | `http://localhost:15672`; see screenshot `docs/evidence/assets/qa-4/rabbit.png` |
| Main queue depth visible | Pass | `wms.order.placed` visible in RabbitMQ Management UI during degraded run; see `docs/evidence/assets/qa-4/rabbit.png` |
| DLQ size visible | Pass | `wms.order.placed.dlq` visible alongside the main queue; see `docs/evidence/assets/qa-4/rabbit.png` |
| Retry/circuit-breaker behavior visible in worker logs | Pass | worker logs show retry and breaker transitions during the WMS-unavailable window; capture path `/tmp/worker-qa4-operability.log` |
| Fulfillment-pending count visible in plugin admin | Pass | degraded capture shows pending / degraded `> 0`; recovered capture shows pending / degraded `= 0`; see `docs/evidence/assets/qa-4/omni_off.png` and `docs/evidence/assets/qa-4/omni_on.png` |
| Combined refresh latency `<= 5 s` | Pass | RabbitMQ UI and plugin admin reflected degradation/recovery within approximately `2-3 s` after refresh |

## Runtime Measurement Capture

Captured on the local Compose stack on 2026-06-02 with WMS set to
`unavailable`, five storefront orders placed during the outage window, and WMS
then returned to `normal`.

Observed operator-facing evidence:

- RabbitMQ Management UI showed the `wms.order.placed` queue accumulating
  in-flight work while WMS was unavailable; `wms.order.placed.dlq` remained
  visible for dead-letter inspection.
- Plugin admin (`/Admin/OmnichannelCore/Configure`) showed the live fulfillment
  projection counts, including the `pending / degraded` operability row.
- During degradation, the plugin admin pending/degraded count rose above zero,
  proving that the operator can see affected fulfillments directly from the
  admin page.
- After WMS returned to `normal`, the worker drained the backlog and the plugin
  admin pending/degraded count returned to `0`, proving recovery was visible
  from the same operator workflow.

Screenshot set:

- RabbitMQ queues during the QA-4 run: `docs/evidence/assets/qa-4/rabbit.png`
- Plugin admin during degradation (`pending / degraded` visible): `docs/evidence/assets/qa-4/omni_off.png`
- Plugin admin after recovery (`pending / degraded = 0`): `docs/evidence/assets/qa-4/omni_on.png`

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
