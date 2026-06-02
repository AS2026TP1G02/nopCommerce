# QA-5 Outbox Latency Evidence

Performance scenario: integration is kept **off the checkout critical path**. When
`OrderPlacedEvent` fires, the plugin writes a durable outbox row and returns — no
synchronous WMS or worker call on the checkout thread.

> **Status:** measured and **passing** on the merged `develop` stack
> (2026-06-02). Captured with the `Misc.OmnichannelCore` plugin **installed**.

## Source Scenario

QA-5 from `docs/part1/quality-attribute-scenarios.md`:

- Stimulus: `OrderPlacedEvent` fired.
- Artifact: plugin `OrderPlacedEvent` consumer + outbox table.
- Response: plugin writes outbox row and returns; no synchronous WMS or worker call
  on the checkout thread.
- Response measure: **outbox row written ≤ 100 ms after `OrderPlacedEvent`**;
  **0 synchronous external HTTP calls in the checkout trace**.

## Method

- Stack: `docker compose` (nopCommerce + SQL Server + RabbitMQ + worker + WMS/POS
  sims); plugin installed; WMS in `normal` unless stated.
- Storefront URL: `http://localhost:8080`; tool: `load-test/run-load-test.sh automated`
  (k6 v0.49.0) — the same harness used for `docs/evidence/baseline.md`.
- Instrumentation: `OrderPlacedOutboxConsumer.HandleEventAsync` wraps the
  build + `InsertAsync` in a `Stopwatch` and logs `elapsed_ms` on the existing
  structured line (ADR-0008/ADR-0010). The line lands in `dbo.Log`:
  `OmnichannelCore outbox queued order_guid=… message_id=… order_id=… elapsed_ms=N`.
- Sample: **101 orders** placed across the runs (`/tmp/qa5-50order.log`,
  `/tmp/qa5-50order-warm.log`, plus singles), 100% placed (0 failed checkouts,
  `http_req_failed = 0%`, `checks = 100%`).

### Outbox-write latency query

```sql
SELECT COUNT(*) AS cnt, MIN(ms) AS min_ms, AVG(ms*1.0) AS avg_ms,
       MAX(ms) AS max_ms, SUM(CASE WHEN ms > 100 THEN 1 ELSE 0 END) AS over_100ms
FROM (
  SELECT TRY_CAST(SUBSTRING(ShortMessage, CHARINDEX('elapsed_ms=', ShortMessage) + 11, 10) AS int) AS ms
  FROM dbo.Log
  WHERE ShortMessage LIKE '%outbox queued%elapsed_ms=%'
) e;
```

## Results

### Measure 1 — outbox row written ≤ 100 ms after `OrderPlacedEvent`

| Metric (n = 101 orders) | Value | Threshold | Pass/Fail |
|-------------------------|-------|-----------|-----------|
| Min outbox-write latency | 5 ms | — | — |
| **Avg outbox-write latency** | **10.3 ms** | ≤ 100 ms | **PASS** |
| **Max outbox-write latency** | **59 ms** | ≤ 100 ms | **PASS** |
| Orders over 100 ms | **0 / 101** | 0 | **PASS** |

Because the **max** across 101 orders is 59 ms, P50/P95/P100 are all < 100 ms.

### Measure 2 — 0 synchronous external HTTP calls on the checkout thread

- **Code path** — `OrderPlacedOutboxConsumer.HandleEventAsync` does only:
  `OutboxMessageFactory.BuildOrderPlacedMessageAsync` (in-process order/product
  reads) → `IRepository.InsertAsync` (one DB row) → structured log. There is **no
  `HttpClient`** and no WMS/worker call. The RabbitMQ publish happens later in the
  scheduled `OutboxPublisherTask`, off the checkout thread. The plugin's only other
  consumer, `EventConsumer`, handles the **admin-menu-created** event only — it
  never runs on checkout.
- **Empirical** — with WMS forced to `slow` (3 s artificial delay) a guest checkout
  still confirmed in **311 ms** (`orderDuration`), i.e. **no ~3 s penalty**. If
  checkout called WMS synchronously, the 3 s delay would appear on the checkout
  thread. It does not → checkout is decoupled from WMS.

  ```text
  POST /mode/slow → {"mode":"slow","slowDelaySeconds":3.0,...}
  ORDER PLACED: SUCCESS  orderDuration=311ms  totalDuration=1091ms
  POST /mode/normal → restored
  ```

### End-to-end happy path (gate confirmation)

A fresh order on merged `develop` completed the chain with no errors: outbox row
written → published by the 60 s publisher tick → worker → WMS (`WMS-REQ-1001`) →
`fulfillment.status.changed.v1` callback → `OmniOrderFulfillment` = `Accepted`
(worker→fulfillment ≈ 272 ms after publish).

## Context — full-flow checkout latency (controlled A/B)

To separate the plugin's cost from machine drift, a same-machine A/B was run with the
**worker stopped** (no async contention in either arm) and a 15-order **warm-up before
each measured run** (a cold first run otherwise produced a single ~5 s outlier). 50
guest-checkout orders per measured run; `order_placement_duration_ms`:

| Arm | P50 (per run) | P95 |
|-----|---------------|-----|
| **With plugin** | 1530 / 1633 / 1644 ms | 1796–1881 ms |
| **Without plugin** | 1535 ms | 1838 ms |

**Result:** the two arms are statistically indistinguishable — the run-to-run spread
with the plugin (~110 ms) exceeds any systematic difference, and the best with-plugin
run (1530 ms) equals the no-plugin run (1535 ms). Installing the plugin adds **≈ 0** to
checkout latency at the median; the precise synchronous cost is the **~10 ms** outbox
write measured above. No outliers after warm-up. The ~400 ms gap vs the 2026-06-01
baseline (1235 ms, `docs/evidence/baseline.md`) appears in **both** arms → it is machine
drift on the single demo host, not the plugin.

So QA-5's "integration off the checkout path" holds three independent ways: the
synchronous outbox write is ~10 ms; checkout latency is unchanged whether the plugin is
installed or not (A/B above); and a 3 s WMS delay does not appear on the checkout thread
(WMS-slow test above).

## Evidence Artifacts

- k6 logs: `/tmp/qa5-50order.log`, `/tmp/qa5-50order-warm.log`, `/tmp/qa5-e2e-1order.log`
- `dbo.Log` rows: `... outbox queued ... elapsed_ms=N` (101 orders)
- Instrumentation: `Services/OrderPlacedOutboxConsumer.cs` (`Stopwatch` → `elapsed_ms`)

## Conclusion

**QA-5 PASS.** Outbox row written ≤ 100 ms after `OrderPlacedEvent` (avg 10.3 ms,
max 59 ms, 0/101 over 100 ms) and 0 synchronous external HTTP on the checkout thread
(code path + WMS-slow decoupling test). Integration is off the checkout critical
path as designed (ADR-0003, ADR-0011).
