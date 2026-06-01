# Omnichannel.Contracts

Shared message-envelope library for the omnichannel integration path. Referenced
by the worker (and, conceptually, by the plugin — the plugin currently keeps its
own copies of these shapes in `Models/Callbacks/` and `OmnichannelCoreDefaults`).

This is the **boundary contract** that `plan.md` freezes at the end of Phase 2.
Change it only through a cross-pair review (envelope, event types, topology).

## Contents

| File | Purpose |
|------|---------|
| `Envelope.cs` | `MessageEnvelope` — the FLAT envelope (messageId, correlationId, eventType, occurredOnUtc, source) that every message record inherits. |
| `Events.cs` | `EventTypes` constants + flat payload records (`CommerceOrderPlacedMessage`, `FulfillmentStatusChangedMessage`, `PosStockChangedMessage`). |
| `Topology.cs` | RabbitMQ exchange / queue / routing-key / DLX names. |

The records use a FLAT wire shape (envelope + domain fields at one JSON level, no
nested payload) to match `docs/evidence/sample-*-v1.json`, the WMS simulator schema
and the plugin callback models. Keep all of these in sync.

## Status

Scaffold (Phase 2 boundary work). The library compiles and is referenced by
`services/worker`. The plugin has not yet been switched over to consume it.
