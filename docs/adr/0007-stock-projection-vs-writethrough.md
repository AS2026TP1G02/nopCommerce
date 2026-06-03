# ADR-0007 - Stock: Plugin Projection First, Defer Write-Through

## Status

Accepted.

## Context

POS stock updates arrive asynchronously and may be stale, duplicate, or contradict core stock state. The plugin must record them somewhere visible. Two options:

1. **Write-through**: plugin updates `Nop.Core.Domain.Catalog.ProductWarehouseInventory` directly when a POS event arrives.
2. **Projection-first**: plugin maintains its own `OmniStockSyncState` table; core stock is unchanged.

The decision affects ADR-0005 (boundary discipline) and the consistency guarantees in QA-2.

## Decision

**Projection-first** for the demo. Plugin records POS-originated stock state in `OmniStockSyncState`; core `ProductWarehouseInventory` is written only by nopCommerce's existing flows. Drift between projection and core is **observable** in the plugin admin view, not silently merged.

Write-through is **deferred** to Part 2 measurement. If projection drift proves operationally unacceptable in the demo, a controlled write-through path can be added in a later iteration without changing the integration contract.

## Part 2 outcome

Projection-only was **retained** — no write-through path was added. The QA-2 consistency runs (duplicate/stale POS events rejected, projection-only `OmniStockSyncState` updated) showed the projection is sufficient for the demo: drift is intentionally visible in the admin trace view rather than silently merged into core stock. The deferred write-through remains a documented, contract-preserving option for a future iteration if a real deployment needs storefront availability to reflect POS sales.

**Oversell handling (2026-06-02).** A direct consequence of projection-only stock is that the storefront can oversell a unit the physical store already sold: POS sales update `OmniStockSyncState` but do not decrement core web availability. Part 2 makes that contradiction *visible at fulfillment time* rather than silent — when the WMS rejects an unfulfillable order with HTTP 409 (`inventory_contradiction`), the worker maps it to a `fulfillment.status.changed` callback with `Status="rejected"`, so the order surfaces as `OmniFulfillmentStatus.Rejected` (with reason) in the admin trace instead of dead-lettering with the fulfillment stuck `Pending` (see `services/worker/WmsClient.cs`; journal 2026-06-02). This is *detect-after-the-fact*, consistent with this ADR's "drift is observable, not hidden" stance. Preventing the oversell (rather than detecting it) would still require the deferred write-through — or, more completely, a synchronous stock reservation at checkout against a single authoritative count — which remains out of scope for the demo.

## Consequences

- Core stock state remains owned by nopCommerce; ADR-0005 is preserved.
- Drift between channels is visible, not hidden — supports QA-3 (traceability).
- Demo shows "POS says X, core says Y" as a deliberate architectural state, not a bug.

## Tradeoffs

- Customers browsing the storefront see core stock, not POS stock; a sale that happens at a physical store does not immediately reduce web availability.
- Two views of stock means support must reason about both; mitigated by exposing both in the admin view.
- Adding write-through later is a real change (schema-impacting); deferring is not free.

## Rejected Alternatives

- **Immediate write-through**. Rejected: couples plugin to core schema invariants; a stale or contradictory POS event would directly corrupt core stock; harder to demonstrate degradation visibility.
- **Event-source the stock domain**. Rejected: scope creep; the rubric penalises scope inflation. Reasonable for a real production rollout, not for this assignment.
- **Read-through projection (no own table, query worker on demand)**. Rejected: violates ADR-0005 by coupling read paths across boundaries.
