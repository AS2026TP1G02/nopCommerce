# Omnichannel Worker

Independently deployable .NET Worker Service that owns the **send-out** edge of
the omnichannel integration (Pair B). It is the assignment's *independently
deployable subsystem* and carries the *async workflow* + *explicit reliability
decision* (retry + circuit breaker + DLQ).

## Flow

```
RabbitMQ (commerce.order.placed.v1)
        │  consume (manual ack)
        ▼
   OrderPlacedConsumer ──► WmsClient ──► WMS sim  POST /fulfillments
        │                     │  (Polly retry + circuit breaker)
        │                     │  on success: status=Accepted
        │                     │  on circuit open: status=pending
        │                     ▼
        │            FulfillmentStatusChanged
        ▼
  POST fulfillment.status.changed.v1 → nopCommerce plugin callback
        │  (with X-Demo-Token auth)
        │
   accepted/completed success → BasicAck
   pending/degraded success → BasicNack(requeue:true) after breaker delay
   on failure → BasicNack(requeue:false) → DLQ
```

## RabbitMQ topology

Declared idempotently on startup (`OrderPlacedConsumer.DeclareTopologyAsync`),
names centralised in `contracts/Topology.cs`:

| Object | Name | Notes |
|--------|------|-------|
| Exchange (topic) | `commerce` | order events from the plugin outbox |
| Queue | `wms.order.placed` | bound on `commerce.order.placed.v1`; dead-letters to the DLX |
| Dead-letter exchange | `commerce.dlx` | poison messages |
| Dead-letter queue | `wms.order.placed.dlq` | inspected during QA-4 (operability) |
| Exchange (topic) | `fulfillment` | reserved for fulfillment results |

Main-queue args: `x-dead-letter-exchange=commerce.dlx`,
`x-dead-letter-routing-key=commerce.order.placed.v1`.

## Reliability decision (QA-1)

`Resilience/WmsResiliencePipeline.cs` builds: **exponential backoff retry** (jitter,
`MaxRetryAttempts=5`) → **circuit breaker** (`FailureRatio=0.9`, `MinimumThroughput=5`,
`BreakDuration=30s`).

**Behavior:**
- Retries WMS call up to 5 times with exponential backoff (500ms base delay + jitter)
- Circuit breaker opens after 90% failure rate over 30s sampling window
- When WMS is unavailable or the circuit opens: returns `status=pending` instead of throwing
- Pending status is posted to the plugin before the message is requeued, so the admin projection shows the degraded order while RabbitMQ retains the retry backlog
- Circuit stays open for 30s, then attempts half-open probe
- On circuit close: backlog drains automatically

**Thresholds (configured in `WorkerOptions`):**
- `MaxRetryAttempts`: 5
- `CircuitBreakerFailureThreshold`: 5 (minimum throughput)
- `CircuitBreakerBreakSeconds`: 30

**Verified behavior:**
- WMS unavailable → retries/circuit breaker → status=pending → callback succeeds → message requeues
- Circuit closes after 30s → backlog drains → normal operation resumes
- Messages with circuit-open status are requeued for recovery, not dead-lettered

## Configuration

Bound from the `Worker` section of `appsettings.json` or `Worker__*` env vars
(see `WorkerOptions.cs`). Defaults target the docker-compose service names.

## Build & run

```bash
# from services/ (Dockerfile expects worker/ and contracts/ in build context)
docker build -f worker/Dockerfile -t omni-worker ..
```

Normally started via the root `docker-compose.yml` alongside RabbitMQ + wms-sim.
