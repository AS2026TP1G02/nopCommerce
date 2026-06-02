---
marp: true
paginate: true
theme: gaia
footer: 'Part 2 · Omnichannel Commerce Core · AS 2025/26'
---

<style>
section { font-size: 24px; position: relative; }
h1 { font-size: 46px; }
h2 { font-size: 34px; }
table { font-size: 19px; }
.spk { position: absolute; bottom: 12px; right: 26px; font-size: 13px; opacity: .55; }
.small { font-size: 18px; opacity: .75; }
</style>

<!-- _class: lead -->
<!-- _paginate: false -->
<!-- _footer: '' -->

![bg opacity:.12](../diagrams/architecture_proposal.png)

# nopCommerce → Commerce Core
## Built & proven under stress

**Scenario C — Omnichannel Commerce Core**

António Alberto · Diogo Fernandes · João Roldão · João Varela
AS · TP1 Group 02 · 2025/26 · 3 June 2026

---

## The problem, in one slide

A **modular monolith** that must stay useful when **WMS and POS lag or fail** — *coordination under delay and degradation*, not feature accumulation.

**Addressed in three iterations:**

1. **Resilience** — checkout still succeeds when the WMS is slow or down
2. **Consistency** — duplicate or out-of-date events never corrupt state
3. **Traceability** — any order is explainable end-to-end from the admin view

<div class="spk">▶ António</div>

---

## How we attacked it

**ADD (Attribute-Driven Design)** — iterative, **one QA driver per iteration**.
Borrowed: **ACDM** go/no-go evaluation · **ADM** migration discipline.

**Attack the risky drivers first:**

| Driver | Business × Risk | When |
|---|---|---|
| QA-1 Resilience | High × High | **Iteration 1** |
| QA-2 Consistency | High × Medium | **Iteration 2** |
| QA-3 / 4 / 5 — trace · ops · perf | cheap wins | folded in |

**Order is deliberate:** resilience first *(gates everything)* → consistency *(pays the at-least-once debt)* → traceability *(instruments what now exists)*.

<div class="spk">▶ António</div>

---

## Target architecture — as built

![bg right:54%](../diagrams/architecture_proposal.png)

- **One plugin** in nopCommerce + **one independently-deployable worker**
- **Sync boundary:** checkout → outbox row *(same DB transaction, in-process)*
- **Async past the outbox:** RabbitMQ → worker → WMS
- **4 plugin-owned tables:** Outbox · Inbox · OrderFulfillment · StockSyncState
- **Two external systems:** WMS + POS simulators
- nopCommerce DB stays single-owner *(ADR-0005)*

<div class="spk">Roldão</div>

---

## Happy path — off the checkout thread

![bg right:50%](../diagrams/order_flow_normal.png)

**QA-5: integration never blocks checkout.**

1. Order placed → `OrderPlacedEvent`
2. Plugin writes **outbox row ≤ 100 ms** *(same transaction)*
3. Scheduled task publishes → RabbitMQ *(publisher confirms)*
4. Worker calls WMS → posts `fulfillment.status.changed.v1`
5. Plugin projects fulfillment → **accepted**

> 0 synchronous WMS / worker calls on the checkout thread.

<div class="spk">Roldão</div>

---

## The decisions that matter

| Decision | Tactic | Why it matters | ADR |
|---|---|---|---|
| **Transactional outbox + reconciler** | crash-safe event capture | order can't exist with no integration trail | 0003 · 0011 |
| **Idempotent inbox** | `messageId` dedup + `sourceVersion` guard | at-least-once delivery can't duplicate or apply stale data | 0006 |
| **Projection-first stock** | shadow table, write-through deferred | stale POS event can't corrupt core stock | 0007 |
| **Retry + circuit breaker + DLQ** | guards the worker's WMS call | WMS degradation contained, poison isolated | 0003 |

**Selective evolution:** monolith unchanged · no big-bang cutover.

<div class="spk">Roldão</div>

---

## Demo map — what you're about to watch

![bg right:46%](../diagrams/order_flow_unavailable.png)

| # | Scenario | Proves |
|---|---|---|
| 1 | Normal order → accepted | QA-5 |
| 2 | WMS down 30 s, checkout survives | QA-1 *(degraded)* |
| 3 | WMS recovers, backlog drains | QA-1 *(recovery)* |
| 4 | POS duplicate + stale ignored | QA-2 |
| 5 | Trace one order + ops dashboard | QA-3 · QA-4 |

<div class="spk">▶ Diogo </div>

---

## ▶ LIVE — normal + degradation

![bg right:40%](../diagrams/order_flow_unavailable.png)

**1 · Normal** — order placed → fulfillment **accepted**

**2 · WMS 503 for 30 s** — checkout still completes · outbox accrues · fulfillment **pending** · worker retries → **circuit breaker trips**

**3 · Recovery** — WMS back to normal → backlog **drains ≤ 60 s** → pending → **accepted**

Watch live: RabbitMQ **queue depth** · worker **logs** *(retry / breaker state)*

<div class="spk">▶ Diogo drives · Varela narrates</div>

---

## ▶ LIVE — consistency + traceability

**4 · Consistency (QA-2)** — POS simulator:
- **Duplicate** `messageId` → rejected **≤ 50 ms**, 0 duplicate fulfillment rows
- **Stale** `sourceVersion` → ignored, projection unchanged

**5 · Traceability + operability (QA-3 · QA-4)**
- Admin **Trace by `OrderGuid`** → outbox row · MQ `messageId` · fulfillment state in **≤ 3 clicks**
- RabbitMQ UI: **queue depth · retry count · DLQ size**, refresh **≤ 5 s**

<div class="spk">▶ Diogo drives · Varela narrates</div>

---

## Demo safety net

> Used only if the live stack misbehaves — recovery is still **shown**, not skipped.

- Recorded end-to-end run: `‹link / file path›`
- Screenshots: WMS-down queue growth · breaker open · backlog drain · Trace view · DLQ
- Captured from a clean run on `docker compose up`

<div class="spk">▶ Diogo</div>

---

## Evidence — measured, not promised

| QA | Target | Result |
|---|---|---|
| **Baseline** | — | P50 **1235 ms** · P95 **1390 ms** ✅ |
| **QA-1** resilience | P95 ≤ **2085 ms** under 30 s 503 · drain ≤ 60 s · 0 pending > 5 min | `TBD` |
| **QA-2** consistency | dup ≤ 50 ms · 0 dup rows · stale ignored | `TBD` |
| **QA-3** traceability | link by `OrderGuid` · ≤ 3 clicks | `TBD` |
| **QA-4** operability | queue / retry / DLQ / pending in one view · refresh ≤ 5 s | `TBD` |
| **QA-5** performance | outbox ≤ 100 ms · 0 sync HTTP in checkout | `TBD` |

<span class="small">Numbers land from today's measurement runs in <code>docs/evidence/</code>.</span>

<div class="spk">▶ Diogo</div>

---

## Honest limits — by design

- **At-least-once** delivery → mitigated by idempotent inbox *(not eliminated)*
- **Demo-token auth** on internal callbacks → demo-grade, not production
- **No DB unique constraint** on `messageId` → dedup is application-level
- **Projection can drift** from core stock → surfaced as drift, not silently merged
- **Inbox / outbox uncapped** → fine for demo, needs retention in prod
- **WMS / POS are simulators** → contracts versioned for real swap-out

**Decisions:** Iteration 1 **Go** · Iteration 2 **Go** (inbox) / **Partial-go** (projection-only stock).

<div class="spk">▶ Varela</div>

---

## Future work — limitation → next step

| Today (limit) | Next step |
|---|---|
| Projection can drift from core stock | **Stock write-through** once drift is measured *(ADR-0007)* |
| App-level `messageId` dedup | **DB unique constraint** on `messageId` *(defense in depth)* |
| Inbox / outbox uncapped | **Retention + capping** policy |
| WMS / POS are simulators | **Swap in real systems** via the versioned contracts |
| Demo-token auth on callbacks | **Real auth** (signed tokens / mTLS) |

> One plugin + one worker keep a modular monolith **useful under degradation** and **recoverable with full traceability**.

<div class="spk">▶ Varela</div>

---

<!-- _class: lead -->
<!-- _paginate: false -->

# Appendix
### Technical-defense backup

---

## Challenges & how we solved them

- **`OrderPlacedEvent` fires *after* persistence** → an order could exist with no integration trail → added the **reconciler task** as a safety net *(ADR-0011)*.
- **Publisher-confirms semantics** → publish awaits the broker ack and **throws on nack / timeout**, so the outbox row retries — no silent loss.
- **At-least-once with no DB unique key** → idempotency enforced at the **application layer** (`messageId` inbox + `sourceVersion` guard), covered by unit tests.
- **Breaker thresholds tuned for tests ≠ demo** → thresholds documented and **QA-1 reproduced in demo conditions**, not just CI.
- **.NET 10 + nopCommerce plugin tooling** → **Dockerized build** for a reproducible toolchain across the team.

---

## ADR rejected alternatives (1/2)

| ADR | Decision | Rejected — why |
|---|---|---|
| 0001 | Scenario C (Omnichannel) | A / B: org & policy modeling weakly supported by nopCommerce |
| 0002 | Keep commerce core in nopCommerce | Extract Order/Catalog as microservices: rewrite risk |
| 0003 | Outbox + RabbitMQ | Sync HTTP from checkout: WMS down breaks checkout; fire-and-forget: loses events on crash |
| 0004 | WMS + POS simulators | Full ERP: too much ops scope; static mocks: no runtime pressure |
| 0005 | No shared database | Worker SQL into nopCommerce: hidden coupling, breaks constraint |
| 0006 | `messageId` inbox + `sourceVersion` | Queue-level dedup only: not portable; last-write-wins: corrupts stock |

---

## ADR rejected alternatives (2/2)

| ADR | Decision | Rejected — why |
|---|---|---|
| 0007 | Projection-first stock | Write-through: couples plugin to core schema; stale event corrupts stock |
| 0008 | 3-ID correlation propagation | Timestamp correlation: clock skew; OpenTelemetry: scope not justified for demo |
| 0009 | Plugin + worker boundary | Microservice extraction: rewrite + cutover risk; fat plugin: retry on web thread |
| 0010 | Structured logs + admin view | OTel / Jaeger: tech for its own sake; APM-only: no nopCommerce control |
| 0011 | Outbox: consumer + reconciler | Consumer-only: silent loss on swallowed exception; polling-only: scan latency |

---

## C4 — containers & plugin components

**L2 (containers):** nopCommerce [Web · Services · Plugin · SQL] · RabbitMQ · Worker · WMS-sim · POS-sim. Plugin consumes `OrderPlacedEvent`; worker bridges to WMS / POS.

**L3 (plugin):**
`OrderPlacedEvent → Outbox → PublisherTask` *(send side)*
`Callback API → Inbox → Fulfillment / StockSync projection` *(receive side)*

<span class="small">Full Mermaid source (C4 L1–L3, context map, sequences): <code>docs/part1/diagrams.md</code>.</span>

---

## Boundaries & migration path

**4 bounded contexts:** nopCommerce Core · Omnichannel Integration *(ACL on WMS/POS)* · WMS · POS. Core ↔ Omnichannel via versioned event contracts.

**5-stage evolution (no big-bang):**
1. Plugin scaffold + 4 tables
2. RabbitMQ + worker + normal flow
3. Resilience: retry · breaker · DLQ + WMS modes
4. Consistency: POS sim · inbox · projection
5. Traceability: 3-ID correlation · admin view · dashboard

---

## Baseline method — for "are the numbers real?"

- **50 orders** against vanilla nopCommerce, plugin **not** installed
- Storefront `http://localhost:8080` · product pool [3, 5, 6, 9, 22]
- **P50 1235 ms · P95 1390.05 ms** · 0 failures · captured 2026-06-01
- QA-1 gate = **P95 × 1.5 = 2085 ms**

<span class="small">Source: <code>docs/evidence/baseline.md</code>.</span>
