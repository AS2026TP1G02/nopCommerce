# Architectural Evolution of nopCommerce

This folder contains the architecture artefacts for Assignment 2. Scenario C — Omnichannel Commerce Core. Method: ADD primary, with ACDM go/no-go and ADM Phase-F migration borrowed.

**Start here**: [roadmap.md](../roadmap.md) — seven-phase delivery plan from checkpoint to final demo.

**During Part 2**: every code change goes in [journal.md](../journal.md) with its driver (ADR/QA) and verification.

**Current implementation evidence**: [Phase 1 plugin scaffold](evidence/phase-1-plugin-scaffold.md) records the code scope, schema created, and exclusions; the install/uninstall gate passed on 2026-06-02.

**Current consistency and traceability evidence**: [QA-2 POS consistency](evidence/qa-2-consistency.md) and [QA-3 order-to-fulfillment traceability](evidence/qa-3-traceability.md) document João Varela's closed QA-2 / QA-3 evidence track.

## Part 1 checkpoint

- [Architecture checkpoint](part1/architecture-checkpoint.md) — primary artefact, organised by 3 ADD iterations.
- [Quality attribute scenarios](part1/quality-attribute-scenarios.md) — five SEI 6-part scenarios with numeric measures.
- [Context map and C4 diagrams](part1/diagrams.md)
- [Current-state analysis](architecture.md) — pressure points feeding Iteration 1.
- [Presentation script](part1/presentation-script.md); decks: [Part 1](part1/AS%20-%201st%20Group%20Presentation.pdf), [Part 2](part2/presentation.pdf).
- [Feasibility spike (experiment charter)](evidence/feasibility-spike.md)
- [ADRs](adr/) — **11 ADRs**, all Accepted. Each ADR documents Status, Context, Decision, Consequences, Tradeoffs, and Rejected Alternatives; load-bearing decisions (ADR-0003 outbox+RabbitMQ, ADR-0009 plugin+worker boundary, ADR-0010 structured-log observability) additionally record Triggers to revisit. Use [template.md](adr/template.md) for new ADRs.

## Part 2 — delivered

The focused omnichannel evolution is implemented and demonstrated. nopCommerce remains the commerce core; fulfillment and stock propagation run through asynchronous messaging (outbox + RabbitMQ + worker); WMS and POS are simulators; failure and recovery are runtime evidence, not only documentation.

- **Build & run**: [setup.md](setup.md) — `docker compose up` (nopCommerce + SQL Server + RabbitMQ + worker + WMS/POS sims).
- **Architecture report**: [part2/architecture-report.md](part2/architecture-report.md) — scenario, drivers, ADD application, target arch, evolution, limits.
- **Evidence pack**: [baseline](evidence/baseline.md), [QA-1 pressure](evidence/qa-1-pressure.md), [QA-2 consistency](evidence/qa-2-consistency.md), [QA-3 traceability](evidence/qa-3-traceability.md), [QA-4 operability](evidence/qa-4-operability.md), [QA-5 outbox latency](evidence/qa-5-outbox-latency.md).
- **Demo**: [demo/README.md](../demo/README.md) — runbook for the 5 scenarios.
- **Slides**: [part2/presentation.pdf](part2/presentation.pdf).
