# nopCommerce Load Test

K6 scripts for generating storefront traffic and placing real orders through the nopCommerce web endpoints.

This folder is intentionally self-contained. It does not start Docker, `docker compose`, or any supporting services for you.

## Prerequisites

- `k6` installed
- A running nopCommerce instance for this repo
- Sample products and checkout settings configured so guest checkout can place orders

## Default target

The scripts default to:

```bash
http://localhost:8080
```

Override with:

```bash
BASE_URL=http://localhost:5000 k6 run automated-order-placement.js
```

## Scripts

- `automated-order-placement.js`: full add-to-cart + one-page-checkout + order placement flow
- `simple-order-test.js`: smoke test that generates HTTP traffic only
- `single-order-test.js`: places exactly one guest-checkout order (deterministic; prints the order id)
- `single-order-rejected.sh`: end-to-end WMS-contradiction → fulfillment `Rejected` check (see below)
- `verify-nopcommerce-config.sh`: basic readiness checks for automated ordering
- `run-load-test.sh`: convenience wrapper around `k6`

## Usage

```bash
cd load-test
./verify-nopcommerce-config.sh
./run-load-test.sh automated
```

Or run directly:

```bash
cd load-test
k6 run automated-order-placement.js
```

## Rejected scenario (WMS contradiction)

Verifies that an order the warehouse cannot fulfil ends up `Rejected` rather than silently
dead-lettered (see ADR-0007). Requires the full `docker compose` stack running.

```bash
cd load-test
./single-order-rejected.sh
```

It sets the WMS simulator to `contradictory` (it returns HTTP 409 for every fulfillment), places one
order, polls `OmniOrderFulfillment` until `StatusId = 50` (`Rejected`) with
`Reason = inventory_contradiction`, checks the dead-letter queue stayed empty, and always restores
the WMS to `normal`. The rejection is asynchronous — the outbox publishes on a 60 s tick — so allow
up to ~2 minutes (tune with `TIMEOUT_SECONDS`, `POLL_INTERVAL`).

To place a single order on its own (any WMS mode), without the rejection check:

```bash
k6 run single-order-test.js
```

## Required nopCommerce settings

- Guest checkout enabled
- One-page checkout enabled
- At least one working payment method
- Sample products with stock available

If your store does not use the default sample data, update the product IDs and SEO URLs in `automated-order-placement.js`.
