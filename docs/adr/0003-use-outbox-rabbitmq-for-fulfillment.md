# ADR-0003 - Use Outbox and RabbitMQ for Fulfillment Integration

## Status

Accepted.

## Context

Scenario C requires nopCommerce checkout to remain useful when the external WMS is slow, unavailable, or returns contradictory state (QA-1 Resilience, QA-3 Traceability). Today the integration boundary has no buffer: when an order is placed, the system must decide between calling WMS inline during checkout, polling for new orders elsewhere, or handing the order to a buffered asynchronous path before WMS is touched. The final demo must show normal flow, induced WMS degradation, and observable recovery — with **no lost or duplicated fulfillment**.

Prior decisions constrain the choice: ADR-0002 keeps the commerce core inside nopCommerce (integration lives in a plugin + worker); ADR-0005 forbids a shared database across service boundaries; ADR-0009 fixes the plugin/worker boundary as the integration surface. The decision pressure is therefore: **how does an order placed in nopCommerce reach WMS without making checkout depend on WMS, and without violating boundary discipline?**

The options considered:

| Option                                        | Basic move                                            |
|-----------------------------------------------|-------------------------------------------------------|
| A. Synchronous HTTP from checkout to WMS      | Call WMS inline on `OrderPlacedEvent`                 |
| B. Worker polls nopCommerce DB for new orders | Background worker reads `Order` rows and pushes to WMS |
| C. Outbox in plugin + RabbitMQ to worker      | Durable hand-off; broker absorbs the WMS dependency   |
| D. Fire-and-forget MQ publish from web thread | Publish to RabbitMQ inline; no outbox table           |

## Decision

Adopt **Option C — a durable outbox in the plugin + RabbitMQ to the worker**.

On `OrderPlacedEvent` the plugin writes `OmniOutboxMessage` in the same DB transaction as nopCommerce's own order side effects. A scheduled publisher drains pending rows to RabbitMQ with publisher confirms. The worker consumes, calls WMS behind a Polly **retry (exponential backoff) + circuit breaker**, and posts results back to the plugin via HTTP for projection updates; messages that exhaust retries are dead-lettered (DLQ) rather than blocking the queue.

The decision is not "async is better"; it is that the dominant forces are **fault isolation** between checkout and WMS (QA-1 cannot tolerate WMS taking down checkout) and **supportability** of in-flight fulfillment (QA-3 must answer "why is order X pending?" from durable artefacts). Option C is the only option that satisfies both without breaking ADR-0005. The at-least-once consequence it introduces is already absorbed by ADR-0006 (idempotency).

## Consequences

- Checkout completes as soon as the outbox row is durable; transport and retry live outside the web request thread.
- Every fulfillment intent has a replayable record, so the demo's recovery scenario is inspectable (queue/DLQ state + outbox rows are visible).
- The broker provides ack, redelivery, and DLQ semantics rather than reinventing them on the database.
- Delivery is at-least-once, so every consumer must be idempotent (ADR-0006).

## Tradeoffs

- RabbitMQ becomes a runtime dependency to deploy, monitor, and recover.
- The scheduled outbox publisher becomes a code path with its own health signal (lag, run frequency, error rate).
- Recovery time after a RabbitMQ outage depends on backlog drain rate, which the architecture does not itself bound.

## Rejected Alternatives

Comparison across the forces that dominate this decision:

| Force            | A. Sync HTTP                 | B. DB poll               | C. Outbox + MQ                            | D. Fire-and-forget        |
|------------------|------------------------------|--------------------------|-------------------------------------------|---------------------------|
| Data consistency | strong but fragile           | weak (no ack)            | at-least-once + idempotency               | weak (drops possible)     |
| Fault isolation  | poor (checkout breaks)       | medium                   | strong (broker absorbs WMS)               | poor (no replay)          |
| Supportability   | poor (failures in user path) | poor (DB-side reasoning) | strong (queue/DLQ + outbox rows visible)  | very poor (events vanish) |
| Team ownership   | one team                     | unclear (DB shared)      | plugin owns outbox, worker owns transport | unclear                   |

- **A. Synchronous HTTP from checkout to WMS** — rejected: checkout latency and availability would inherit WMS's worst case; a WMS outage breaks checkout. Fails QA-1.
- **B. Worker polls nopCommerce DB for new orders** — rejected: reading nopCommerce internals violates ADR-0005 and reinvents ack/retry/back-pressure on top of SQL state.
- **D. Fire-and-forget MQ publish from web thread** — rejected: a crash or broker outage between DB commit and publish silently drops the fulfillment intent; the demo cannot show recovery from state that does not exist.

## Triggers to revisit

Reopen this decision if any of the following becomes true:

- Checkout latency or error rate becomes dominated by the outbox publisher rather than by nopCommerce itself.
- Broker operational cost (queue lag, DLQ size, redelivery rate) becomes the top support burden — a transactional-outbox CDC approach (e.g., Debezium) or a different transport may be warranted.
- A second consumer of `commerce.order.placed.v1` (e.g., analytics) emerges and forces revisiting the fan-out shape.
- WMS gains a synchronous SLA strong enough to weaken the case for buffering.
- Duplicated fulfillment side effects appear (must remain 0); non-zero means ADR-0006 has regressed and this ADR's accepted-damage assumption no longer holds.
