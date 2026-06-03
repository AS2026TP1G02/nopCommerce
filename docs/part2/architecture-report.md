# Architecture Report — Omnichannel Commerce Core (Scenario C)

Part 2 final report. Short and focused: scenario, drivers, framework application, target
architecture, evolution path, and limits. Companion to the Part 1 checkpoint and the ADR
set; this report is the "what we actually built and why" view.

- Part 1 design: [docs/part1/architecture-checkpoint.md](part1/architecture-checkpoint.md)
- Current-state analysis: [docs/architecture.md](architecture.md)
- Decisions: [docs/adr/](adr/) · Quality scenarios: [docs/part1/quality-attribute-scenarios.md](part1/quality-attribute-scenarios.md)
- Run it: [docs/setup.md](setup.md) · Evidence: [docs/evidence/](evidence/)

## 1. Scenario & business drivers

VerdeMart Retail runs nopCommerce as its web store and is making it the **commerce core**
of a wider ecosystem (web, store ops, warehouse execution, POS). The architectural problem
is not "connect more systems" — it is **how the commerce core behaves when surrounding
systems lag, disagree, or go down**. Drivers: cross-channel order/stock visibility; remain
useful when the WMS is unavailable; absorb at-least-once and stale external events safely.

## 2. Current-state pressure points (what forced the change)

From `docs/architecture.md`, the load-bearing gaps:

- **In-process events only** — `IEventPublisher.PublishAsync` awaits `IConsumer<T>` in the
  same process (`EventPublisher.cs:20`). `OrderPlacedEvent` has no durable, replayable path
  to external systems.
- **Synchronous checkout** — `OrderProcessingService.PlaceOrderAsync` (`:1617`) has no
  decoupled hand-off; any warehouse call would be on the checkout critical path.
- **No outbox / inbox / idempotency** model for external integration.
- Stock models are internal only; no model for external (POS/WMS) stock or staleness.

The plugin seam is the correct, selective extension point — no core library is modified.

## 3. Framework application (ADD)

We applied **ADD** across three iterations, each driven by a quality-attribute scenario
(see checkpoint §6):

- **Iteration 1 — Resilience (QA-1):** decouple checkout from the warehouse; tolerate WMS
  degradation. → outbox + async worker + retry/circuit-breaker/DLQ.
- **Iteration 2 — Consistency (QA-2):** at-least-once and stale external events must not
  duplicate or corrupt state. → inbox `messageId` dedup + `sourceVersion` projection guard.
- **Iteration 3 — Traceability (QA-3/QA-4):** a delayed/recovering order is explainable. →
  standard envelope (3 IDs) + admin lookup + queue/DLQ visibility.

## 4. Target architecture (as built)

```
nopCommerce (commerce core, modular monolith — retained, ADR-0002)
  └─ Nop.Plugin.Misc.OmnichannelCore   (the only change inside the monolith)
       OrderPlacedEvent → OmniOutboxMessage (in-process, no external HTTP on checkout)
       OutboxPublisherTask  ── publisher confirms ─▶ RabbitMQ  exchange "commerce"
       OutboxReconcilerTask (ADR-0011 crash safety net)
       Callbacks (internal HTTP, X-Demo-Token):
         POST /omnichannel/callbacks/pos/stock-changed       → inbox + OmniStockSyncState
         POST /omnichannel/callbacks/fulfillment/status-changed → inbox + OmniOrderFulfillment
       Admin: Trace by OrderGuid → outbox + inbox + fulfillment chain

Worker (independent deployable, .NET) ── consumes commerce.order.placed.v1
       → WMS call (Polly retry + circuit breaker) → posts fulfillment.status.changed.v1 back
       poison → commerce.dlx / wms.order.placed.dlq

Simulators: WMS (normal/slow/unavailable/contradictory), POS (normal/duplicate/stale)
Shared contract: services/contracts (flat envelope + event types + topology)
```

**Data ownership (ADR-0005, no shared DB):** the plugin owns four tables
(`OmniOutboxMessage`, `OmniInboxMessage`, `OmniOrderFulfillment`, `OmniStockSyncState`).
The worker and simulators never touch the nopCommerce DB; all cross-boundary traffic is
RabbitMQ + internal HTTP. Core order/stock state stays owned by nopCommerce (ADR-0002/0007).

**Sync vs async:** checkout→outbox is synchronous but in-process and DB-only (QA-5: no
external HTTP on the checkout thread). Everything past the outbox is asynchronous.

## 5. Reliability decisions (the explicit ones)

- **Transactional outbox** + **reconciler** (ADR-0011): the consumer records intent in the
  order request; the publisher ships it with **publisher confirms**; the reconciler
  back-fills any order missing a row, because `EventPublisher` swallows consumer exceptions.
- **Idempotent inbox** (ADR-0006): every callback dedupes on `messageId` before acting.
- **Projection-first stock** (ADR-0007): `sourceVersion` guard drops stale updates; core
  stock is never corrupted by external events.
- **Retry + circuit breaker + DLQ** (worker): bounded retries, breaker on sustained WMS
  failure, poison messages dead-lettered.

## 6. Evolution path / what coexists during transition

The monolith keeps running unchanged; the plugin adds the integration edge beside it. The
worker can be deployed and scaled independently. No big-bang cutover: if the worker or broker
is down, orders still complete and outbox rows accumulate (drained on recovery). This is the
"selective evolution" the rubric asks for — one plugin + one extracted service, no rewrite.

## 7. Limits (honest)

- **Demo-grade callback auth** — shared `X-Demo-Token`, not mTLS/signed messages (ADR-0005).
- **No DB unique constraint on `OmniInboxMessage.MessageId`** — dedup is service-level, so a
  simultaneous duplicate is a residual race outside the demo path (ADR-0006).
- **Projection can diverge from core stock** — intentional (ADR-0007); consumers must read
  the projection for the cross-channel figure.
- **Inbox/outbox growth is uncapped** — acceptable for the demo; no retention job.
- **Worker typed-clients captured in a singleton hosted service** — fine for the demo; would
  use a scope factory in production.
- **Runtime QA numbers** (QA-1…QA-5) are captured in `docs/evidence/` at measurement time;
  thresholds are stated in `quality-attribute-scenarios.md`.

## 8. Traceability (drivers → implementation)

| Driver | Decision | Code |
|--------|----------|------|
| QA-1 resilience | retry/breaker/DLQ, outbox | `services/worker/Resilience/`, `ScheduleTasks/OutboxPublisherTask.cs` |
| QA-2 consistency | inbox dedup, sourceVersion guard | `Services/OmniInboxService.cs`, `Services/OmniStockSyncService.cs` |
| QA-3 traceability | 3-ID envelope, admin trace | `Controllers/OmnichannelCoreController.cs` (Trace), `Views/Trace.cshtml` |
| QA-4 operability | queue/DLQ + pending count | RabbitMQ mgmt UI, `OmnichannelCoreService.GetPendingFulfillmentCountAsync` |
| QA-5 outbox latency | in-process outbox write | `Services/OrderPlacedOutboxConsumer.cs` |
