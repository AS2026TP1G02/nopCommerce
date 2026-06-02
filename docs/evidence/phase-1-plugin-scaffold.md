# Phase 1 Evidence - Plugin Scaffold and Tables

## Scope

This phase implements only the first roadmap step: the nopCommerce plugin foundation and its database schema. It does not implement RabbitMQ publication, a worker service, WMS/POS simulators, retries, circuit breakers, DLQ handling or a real order workflow.

## Implemented Artefacts

| Area | Artefact | Purpose |
|------|----------|---------|
| Plugin lifecycle | `nopCommerce/src/Plugins/Nop.Plugin.Misc.OmnichannelCore/Nop.Plugin.Misc.OmnichannelCore.csproj`, `plugin.json`, `OmnichannelCorePlugin.cs` | Makes the omnichannel module a standard nopCommerce plugin. |
| Schema migration | `Data/Migrations/SchemaMigration.cs` | Creates and drops the four Phase 1 plugin tables during plugin install/uninstall. |
| Table mapping | `Data/Mapping/*` | Keeps table names explicit and defines key column sizes/indexes. |
| Domain model | `Domains/*` | Defines outbox, inbox, fulfillment and stock projection entities needed by later ADD iterations. |
| Admin shell | `Controllers/OmnichannelCoreController.cs`, `Views/Configure.cshtml` | Shows table counts and makes clear that this phase is foundation-only. |
| Registration | `Infrastructure/PluginNopStartup.cs`, `Services/EventConsumer.cs`, solution entry | Registers the read service and adds the plugin to the nopCommerce admin plugin menu. |

## Schema Created

### `OmniOutboxMessage`

Purpose: durable outbound event ledger for `commerce.order.placed.v1` and later integration events.

Key columns:

- `MessageId`: integration message identifier, indexed for traceability.
- `EventType`: versioned event name.
- `CorrelationId`: cross-component correlation key.
- `OrderGuid` / `OrderId`: nopCommerce order link.
- `Payload`: serialized event body.
- `StatusId`, `RetryCount`, `LastError`, `PublishedOnUtc`, `NextAttemptOnUtc`: later publisher/retry state.

### `OmniInboxMessage`

Purpose: inbound message ledger for idempotency when the worker or POS simulator calls back into nopCommerce.

Key columns:

- `MessageId`: external message identifier, indexed for duplicate detection.
- `EventType`, `CorrelationId`, `Source`: message envelope metadata.
- `StatusId`, `LastError`, `ReceivedOnUtc`, `ProcessedOnUtc`: processing state.

### `OmniOrderFulfillment`

Purpose: plugin-owned fulfillment projection, avoiding changes to nopCommerce core order enums.

Key columns:

- `OrderGuid` / `OrderId`: link back to the nopCommerce order.
- `MessageId`: integration event link.
- `ExternalRequestId`: future WMS request identifier.
- `StatusId`, `TrackingNumber`, `Reason`: fulfillment state visible to the demo/admin view.

### `OmniStockSyncState`

Purpose: projection-first cross-channel stock visibility, matching ADR-0007.

Key columns:

- `ProductId`, `Sku`, `WarehouseId`: stock item identity.
- `QuantityOnHand`: latest accepted source quantity.
- `SourceVersion`: stale update guard for POS-originated events.
- `LastMessageId`, `Source`, `StatusId`, `LastSeenOnUtc`: traceability and reconciliation state.

## Why This Phase Exists

The assignment asks for architectural evolution, not a rewrite. This phase creates a small plugin-owned boundary where later phases can add asynchronous integration without changing nopCommerce checkout, order status enums or catalog ownership. It supports:

- ADR-0003: keep checkout/order/catalog inside the monolith.
- ADR-0004 and ADR-0006: use outbox + asynchronous messaging instead of synchronous WMS calls.
- ADR-0007: store POS stock visibility as a projection first.
- ADR-0008: preserve traceability fields from the start.

## Explicitly Excluded

- No `OrderPlacedEvent` consumer yet.
- No scheduled outbox publisher yet.
- No RabbitMQ dependency yet.
- No worker service yet.
- No WMS/POS simulators yet.
- No retry, circuit breaker or DLQ behavior yet.
- No modification to nopCommerce core order, checkout or catalog services.

## Verification Status

Static checks completed:

- Plugin project is present under `nopCommerce/src/Plugins/Nop.Plugin.Misc.OmnichannelCore`.
- `plugin.json` uses system name `Misc.OmnichannelCore`.
- `NopCommerce.sln` references the plugin project.
- Migration creates and drops the four planned tables.
- `jq . nopCommerce/src/Plugins/Nop.Plugin.Misc.OmnichannelCore/plugin.json` succeeds.
- `rg "OmnichannelCore|OmniOutboxMessage|OmniInboxMessage|OmniOrderFulfillment|OmniStockSyncState"` confirms the plugin, solution and evidence references.

Build checks completed:

- Host `dotnet --info` fails in the current shell with `command not found`.
- `docker build --target build -t nopcommerce-omni-phase1-check .` from `nopCommerce/` succeeds.
- The Docker build emits 3 existing nopCommerce warnings and 0 errors.
- The build output includes `Nop.Plugin.Misc.OmnichannelCore.dll`.

Manual runtime checks completed:

- Plugin installed successfully in the local nopCommerce instance.
- Admin page is visible at `http://localhost/Admin/OmnichannelCore/Configure`.
- The four `Omni%` tables are visible from DBeaver in the nopCommerce database.

Runtime checks completed:

- Uninstall round-trip confirmed (2026-06-02): uninstall drops all four `Omni%` tables + the index (DB clean); reinstall recreates them, and a post-reinstall order flows. See **Uninstall DB gate** below.

## Database Inspection with DBeaver

The local Docker setup currently uses the default `nopCommerce/docker-compose.yml`, which starts SQL Server, not PostgreSQL. The active database container is `nopcommerce_mssql_server`.

Use these DBeaver settings:

| Field | Value |
|-------|-------|
| Driver | Microsoft SQL Server |
| Host | Container IP, for example `172.29.0.2` |
| Port | `1433` |
| Database | `nopcommerce` |
| Username | `sa` |
| Password | `nopCommerce_db_password` |
| SSL option | Trust server certificate, or disable encryption if needed |

The container IP can change after recreating containers. Confirm the current IP with:

```bash
docker inspect -f '{{range.NetworkSettings.Networks}}{{.IPAddress}}{{end}}' nopcommerce_mssql_server
```

If the team wants a stable `localhost` connection from DBeaver, expose SQL Server in `nopCommerce/docker-compose.yml`:

```yaml
nopcommerce_database:
    image: "mcr.microsoft.com/mssql/server:2019-latest"
    container_name: nopcommerce_mssql_server
    ports:
        - "1433:1433"
    environment:
        SA_PASSWORD: "nopCommerce_db_password"
        ACCEPT_EULA: "Y"
        MSSQL_PID: "Express"
```

After recreating the containers, DBeaver can use:

| Field | Value |
|-------|-------|
| Host | `localhost` |
| Port | `1433` |
| Database | `nopcommerce` |
| Username | `sa` |
| Password | `nopCommerce_db_password` |

Useful verification queries:

```sql
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME LIKE 'Omni%';
```

```sql
SELECT TOP (50) *
FROM [dbo].[OmniOutboxMessage];

SELECT TOP (50) *
FROM [dbo].[OmniInboxMessage];

SELECT TOP (50) *
FROM [dbo].[OmniOrderFulfillment];

SELECT TOP (50) *
FROM [dbo].[OmniStockSyncState];
```

The tables are expected to be empty in Phase 1, because no event consumer, publisher, worker, WMS simulator or POS simulator exists yet.

## Uninstall DB gate (mechanism + procedure)

**Mechanism (source-proven).** On uninstall, nopCommerce runs
`PluginService.UninstallPluginsAsync` → `IMigrationManager.ApplyDownMigrations(pluginAssembly)`
(`src/Libraries/Nop.Services/Plugins/PluginService.cs:581`). `ApplyDownMigrations`
runs the `Down()` of every **applied** migration in the plugin assembly
(`src/Libraries/Nop.Data/Migrations/MigrationManager.cs:123-128`; the call passes
`isApplied: true` — it is *not* limited to schema migrations). The plugin's
`SchemaMigration.Down()` deletes `IX_OmniStockSyncState_ProductId_WarehouseId`, then
drops `OmniStockSyncState`, `OmniOrderFulfillment`, `OmniInboxMessage`,
`OmniOutboxMessage`. **Trigger:** the uninstall is applied by the admin
**"Restart application to apply the changes"** action (`PluginController.ReloadList` →
`UninstallPluginsAsync`, `src/Presentation/Nop.Web/Areas/Admin/Controllers/PluginController.cs:361`)
— **not** by a plain container restart. Install, by contrast, is processed on app
startup (`AppStartedConsumer` → `InstallPluginsAsync`).

**Procedure (run with admin access).** Current DB connection: host `localhost:1433`,
database `nopcommerce`, user `sa`, password `Omni_Demo_Pass1` (SQL Server container
`as_group_project-sqlserver-1`).

1. **Before** — confirm the four tables + index exist:
   ```bash
   docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa \
     -P 'Omni_Demo_Pass1' -C -Q "SELECT name FROM nopcommerce.sys.tables WHERE name LIKE 'Omni%' ORDER BY name; SELECT name FROM nopcommerce.sys.indexes WHERE name='IX_OmniStockSyncState_ProductId_WarehouseId';"
   ```
   Expect the 4 tables + the index.
2. **Uninstall** — Admin → Configuration → Local plugins → *Omnichannel Core* → **Uninstall**.
3. **Apply** — click the admin **"Restart application to apply the changes"** banner (the *Apply changes* button). This runs `UninstallPluginsAsync` → `ApplyDownMigrations` (drops the tables) then restarts. ⚠️ A plain `docker compose restart nopcommerce` does **not** trigger the uninstall.
4. **After** — re-run the query in step 1. Expect **0** `Omni%` tables and **0** rows for the index → DB clean.
5. **Reinstall** — Admin → Local plugins → *Omnichannel Core* → **Install** → click **"Restart application to apply the changes"** (install is processed on the ensuing startup).
6. **Confirm restored** — re-run step 1 (4 tables back), open `/Admin/OmnichannelCore/Configure` (counts render), place one order to confirm E2E still works.

> Note: uninstall is processed **only** by the admin *Apply changes* action
> (`UninstallPluginsAsync` is invoked there, not on startup), so the admin UI is the
> reliable path for the drop. Install *is* processed on startup, so queuing
> `PluginNamesToInstall` + restart also reinstalls. Back up the DB before either
> (`BACKUP DATABASE nopcommerce`).

**Results (captured 2026-06-02).** Via admin **Uninstall → Apply changes (restart)**,
then **Install → Apply changes (restart)**. DB backed up first to
`/var/opt/mssql/data/pre_uninstall_gate.bak`.

| Check | Before | After uninstall | After reinstall |
|-------|--------|-----------------|-----------------|
| `Omni%` tables | 4 | **0** ✅ | **4** ✅ |
| `IX_OmniStockSyncState_…` index | present | **absent** ✅ | **present** ✅ |
| Plugin in `InstalledPlugins` | yes | **no** ✅ | **yes** ✅ |
| `PluginNamesToUninstall` queue | — | processed → `[]` | — |

Post-reinstall sanity: a guest-checkout order produced a fresh `OmniOutboxMessage`
row (`Id=1`, status `Pending`), confirming the `OrderPlacedEvent` consumer is live on
the reinstalled plugin.

## Go/No-Go

Current status: **Done** (uninstall DB gate passed 2026-06-02).

Install/table/admin checks observed locally; Part 2 proved the full E2E path on
`develop` (`docs/evidence/qa-5-outbox-latency.md`); and the **Uninstall DB gate**
round-trip is now empirically confirmed — install → 4 tables, uninstall → 0 tables
(DB clean, index dropped), reinstall → 4 tables, with a post-reinstall order
producing a fresh outbox row.
