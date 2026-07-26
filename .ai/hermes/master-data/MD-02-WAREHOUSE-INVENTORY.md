# MD-02 — Warehouse & Inventory Master Architecture (Canonical, Implementation-Backed)

Status: PRODUCTION-CERTIFIABLE (after migration `20260607210000_PosWarehouseInventoryAuthorityHardening` is applied).
Authority reference: PASS P1-EB POS Warehouse & Inventory Authority Certification (`docs/architecture/pos-warehouse-inventory-authority-certification-findings.md`, 2026-06-07).

Every statement below is backed by the implementation. Where behavior is NOT implemented, it is called out explicitly. File:line citations are given as evidence.

---

## 1. Executive Summary

Shopixy is a multi-tenant ERP. Warehouse & Inventory operate on a **variant + warehouse** model where:

- A **Warehouse** is a storage *identity / location*. It owns NO inventory quantity.
- A **ProductVariantStock** row (table `ProductVariantStocks`) is the **only mutable inventory authority** — quantity, reserved quantity, and damaged quantity per (Tenant, Variant, Warehouse).
- **Product** and **ProductVariant** are *authority references* (parent lineage), not quantity holders. A product never holds stock directly; stock lives at the variant level.
- `WarehouseStock` is a **legacy compatibility table only** and is explicitly NOT an inventory authority.
- Stock mutations are SQL-authoritative, row-locked, transaction-bounded, and propagated to MongoDB through a durable stock-sync outbox. MongoDB (and any Redis/Elasticsearch read models) are **derivative projections**, never authority.

### Required answers

- **What is a Warehouse?** A tenant-scoped storage location/identity (`PointOfSale.Core/Entities/Warehouse.cs:8`). Holds no quantity itself.
- **Who owns Inventory?** `ProductVariantStock` owns inventory quantity, reserved quantity, and damaged quantity (`PointOfSale.Core/Entities/ProductVariantStock.cs:8`, `PointOfSale.API/Services/InventoryService.cs:14-18`).
- **Is Warehouse authoritative?** **No.** Warehouse is identity only. It is validated as part of the mutation lineage but holds no stock (`PointOfSale.Infrastructure/Reliability/ProductVariantInventoryLineageAuthority.cs:80-116`).
- **Is Product authoritative?** **No.** Product is a parent lineage owner only; it never holds quantity (`PointOfSale.API/Controllers/WarehousesController.cs:149-151` — "Inventory is Variant + Warehouse ONLY. Products never hold stock directly.").
- **Is Variant authoritative?** The **variant** is the inventory owner at the product level; quantity is stored per variant per warehouse in `ProductVariantStock` (`ProductVariantStock.cs:5-16`).
- **What is the inventory source of truth?** SQL table `ProductVariantStocks`. "ProductVariantStocks is the ONLY mutable stock source of truth" (`InventoryService.cs:14-18`, `InventoryHardeningExtensions.cs:17-22`).

---

## 2. Discovery (Implemented Entities)

| Entity | Table | Role | Evidence |
|--------|-------|------|----------|
| `Warehouse` | `Warehouses` | Storage identity, tenant-owned | `Core/Entities/Warehouse.cs:8` |
| `ProductVariantStock` | `ProductVariantStocks` | **Inventory authority** (qty / reserved / damaged) | `Core/Entities/ProductVariantStock.cs:8` |
| `WarehouseStock` | `WarehouseStocks` | Legacy compatibility table only, NOT authority | `Core/Entities/WarehouseStock.cs:13-15` |
| `StockMovement` | `StockMovements` | Append-only audit ledger of every mutation | `Core/Entities/StockMovement.cs:9` |
| `InventoryReservation` | `InventoryReservations` | Allocation of reserved stock to an order | `Core/Entities/InventoryReservation.cs:6` |
| `StockTransfer` | `StockTransfers` | Structured transfer lifecycle (Pending→Approved→Completed) | `Core/Entities/StockTransfer.cs:6` |
| `StockTransferItem` | `StockTransferItems` | Line items of a transfer | `Core/Entities/StockTransferItem.cs:3` |
| `StockTransferRequestIdempotency` | `StockTransferRequestIdempotency` | Durable idempotency for transfer lifecycle | `Core/Entities/StockTransferRequestIdempotency.cs:5` |
| `StockSyncOutboxMessage` | `StockSyncOutboxMessages` | Durable stock-sync event outbox | `Core/Entities/StockSyncOutboxMessage.cs:10` |
| `InventoryEventInbox` | `InventoryEventInboxes` | Inbox for inventory events | `Core/Entities/InventoryEventInbox.cs` |

NOT implemented as distinct entities: no separate `OpeningBalance` entity (opening stock is a `StockMovement` with `IsOpeningBalance=true` plus a `ProductVariantStock` row), no separate `StockAdjustment` entity (adjustment is a `StockMovement` of `StockMovementType.Adjustment`).

---

## 3. Warehouse Identity

- **Primary Key:** `Guid Id` (`Warehouse.cs:8`, `BaseEntity.cs:8`).
- **Business Identity:** `Name` (required, `Warehouse.cs:16`), optional `Code`, `Address`, `Phone`.
- **Tenant Ownership:** `TenantId` — `ITenantOwned`, NOT NULL, enforced at DB level (`Warehouse.cs:11-14`). All queries filter by `TenantId`.
- **Active/Inactive:** `IsActive` (default `true`, `Warehouse.cs:20`). Inactive warehouses **cannot receive** opening stock, adjustment, reservation, or transfer mutation (`pos-warehouse-inventory-authority-certification-findings.md:21`).
- **Soft Delete:** `IsDeleted` via `BaseEntity`; global query filter `!e.IsDeleted && e.TenantId == CurrentTenantId` (`ApplicationDbContext.cs:561-562`). Cross-tenant/soft-deleted warehouses fail lineage checks (`ProductVariantInventoryLineageAuthority.cs:88-116`).
- **Default Warehouse:** `IsDefault` boolean (`Warehouse.cs:21`). No code-enforced single-default invariant found; `IsDefault` is a settable flag, used as a hint (e.g. `WarehousesController` create/update accept it). No server-side "exactly one default" constraint observed.
- **Validation:** Stock mutation requires a current, active, same-tenant Warehouse (`ProductVariantInventoryLineageAuthority.cs:94-100`). Deletion path: **no `DELETE` endpoint exists** — `WarehousesController` exposes only GET/POST/PUT and stock/opening-stock endpoints (`WarehousesController.cs:46-501`). Warehouse "removal" is via `IsActive=false` (PUT) or soft-delete, not physical delete.
- **Relationships:** `Warehouse.Stock` → `WarehouseStock` (legacy, `Warehouse.cs:24`), `Warehouse.StockMovements` → `StockMovement` (`Warehouse.cs:25`). `ProductVariantStock.Warehouse` is the authoritative FK for inventory (`ProductVariantStockConfiguration.cs:54-57`, `OnDelete Restrict`).

---

## 4. Inventory Ownership

Inventory quantity lives **exclusively in `ProductVariantStock`** (`ProductVariantStocks`), keyed by `(TenantId, VariantId, WarehouseId)`:

- `Quantity` — sellable on-hand (`ProductVariantStock.cs:38`).
- `ReservedQuantity` — reserved for pending orders (`ProductVariantStock.cs:43`).
- `DamagedQuantity` — non-sellable physical stock (`ProductVariantStock.cs:48`).
- Derived: `AvailableQuantity = Quantity - ReservedQuantity` (`ProductVariantStock.cs:53`); `TotalQuantity = Quantity + DamagedQuantity` (`ProductVariantStock.cs:58`).

Why:
- The entity comment states it is "the new source of truth for inventory tracking" and "WarehouseStock is a compatibility table only, not an inventory authority" (`ProductVariantStock.cs:5-16`).
- `InventoryService.GetStockLevelsAsync` queries `ProductVariantStocks` explicitly ("NOT WarehouseStock (legacy)") (`InventoryService.cs:211-223`).
- `WarehousesController.GetWarehouseStock` maps from `ProductVariantStock` (`WarehousesController.cs:166-186`).
- Service-layer guard: "ProductVariantStocks is the ONLY mutable stock source of truth" (`InventoryService.cs:14-18`).

`WarehouseStock` (legacy) still exists with unique `(WarehouseId, ProductId, ProductVariantId)` (`WarehouseStockConfiguration.cs:34-36`) but is superseded and not authoritative.

---

## 5. Inventory Lifecycle (Implemented Stages)

```
Opening Balance ──▶ (Purchase / Receipt) ──▶ Available
       │                                      │
       ▼                                      ▼
  Reservation  ──▶  Sale (Commit)  ──▶  Shipped / Consumed
       │                                      │
       └──▶ Release (cancel/expiry) ◀─────────┘
   Sales Return ──▶ compensating movement (revert)
   Purchase Return ──▶ compensating movement (revert)
   Adjustment ──▶ +/- correction (privileged, idempotent)
   Transfer ──▶ source decrement + target increment (atomic)
```

- **Opening Balance** — `InventoryService.SaveOpeningStockAsync`; one-time, positive-only, duplicate-variant-free, tenant-explicit, row-locked (`InventoryService.cs:225-375`).
- **Purchase / Receipt** — purchase invoice receipt increments `ProductVariantStock` (`StockReceivedEvent`, `PurchasesController.cs`; per PASS P1-EB certification `findings:42`).
- **Sale** — reservation **commit** on payment success decrements both `Quantity` and `ReservedQuantity` (`ProductVariantStockRepository.CommitOrderReservationsAsync`, invoked from `PaymentLifecycleService.cs:2073` and `:3430`).
- **Reservation** — `ProductVariantStockRepository.ReserveAsync` (`ProductVariantStockRepository.cs:81-229`).
- **Release** — `ReleaseAsync` / `ReleaseOrderReservationsAsync` on cancellation/expiry (`ProductVariantStockRepository.cs:231-415`).
- **Sales Return / Purchase Return** — revert via **compensating movements** (not deletion of history); `SalesReturnsController.cs`, `PurchaseReturnsController.cs` (`findings:42`).
- **Adjustment** — `InventoryService.AdjustStockAsync` + `WarehousesController.adjust-stock` + `AdminInventoryController.adjust` (`InventoryService.cs:36-99`, `WarehousesController.cs:188-293`).
- **Transfer** — `StockTransfersController` + `InventoryService.TransferStockAsync` (`StockTransfersController.cs`, `InventoryService.cs:101-205`).

---

## 6. Aggregate Analysis

| Aggregate | Root / Owner | Bounded children | Evidence |
|----------|--------------|------------------|----------|
| Warehouse | `Warehouse` | (none — identity only) | `Warehouse.cs` |
| Inventory Authority | `ProductVariantStock` | none (leaf) | `ProductVariantStock.cs` |
| Movement | `StockMovement` (append-only) | none | `StockMovement.cs`; append-only trigger `20260607210000_...cs` |
| Reservation | `InventoryReservation` | none | `InventoryReservation.cs` |
| Transfer | `StockTransfer` | `StockTransferItem` | `StockTransfer.cs:39` |
| Stock Sync | `StockSyncOutboxMessage` | none (durable) | `StockSyncOutboxMessage.cs` |

Ownership boundaries: **Warehouse** owns storage identity only. **ProductVariantStock** owns all quantity state. **InventoryReservation** owns allocation against a stock row (it mutates `ProductVariantStock.ReservedQuantity`). **StockTransfer** owns a structured, state-gated mutation of two stock rows. **StockMovement** is a dependent audit record, never a command target.

---

## 7. Warehouse Behavior (Reverse Engineered)

**Create** — `POST api/warehouses`, requires `Owner,Admin,Manager`; sets `TenantId` from `ITenantContext.RequiredTenantId` (`WarehousesController.cs:78-98`).

**Update** — `PUT api/warehouses/{id}`, requires `Owner,Admin,Manager`; partial update (only non-null fields), tenant-scoped lookup (`WarehousesController.cs:100-146`). Can toggle `IsActive` and `IsDefault`.

**Delete / Archive** — **No DELETE endpoint implemented.** "Removal" is `IsActive=false` (deactivation) or soft-delete. Deactivated/inactive warehouses lose mutation authority (`findings:21-23`).

**Default Warehouse** — `IsDefault` flag settable on create/update; no server-side enforcement of exactly-one-default found.

**Warehouse Selection** — reads filter `IsActive` (`GET api/warehouses` returns only `IsActive` warehouses, `WarehousesController.cs:50-58`); mutation paths resolve warehouse by lineage guard requiring active + same-tenant (`ProductVariantInventoryLineageAuthority.cs:94-100`).

**Validation** — name required; mutation requires active same-tenant Warehouse + Variant + Product (`findings:18-23`); cross-tenant warehouse ids rejected by lineage check (`ProductVariantInventoryLineageAuthority.cs:114-116`).

---

## 8. Inventory Operations

| Operation | Implemented | Owner | Evidence |
|-----------|-------------|-------|----------|
| Receiving (purchase) | Yes | `ProductVariantStock` (via purchase lifecycle) | `findings:42`; `PurchasesController.cs` |
| Shipping (sale commit) | Yes | `ProductVariantStock` | `CommitOrderReservationsAsync` (`ProductVariantStockRepository.cs:590+`); `PaymentLifecycleService.cs:2073` |
| Reservation | Yes | `InventoryReservation` → `ProductVariantStock.ReservedQuantity` | `ProductVariantStockRepository.ReserveAsync` (`cs:81`) |
| Release | Yes | `InventoryReservation` → `ProductVariantStock.ReservedQuantity` | `ReleaseAsync`/`ReleaseOrderReservationsAsync` (`cs:231`,`cs:332`) |
| Adjustment | Yes | `ProductVariantStock` (privileged, idempotent) | `InventoryService.AdjustStockAsync` (`cs:36`) |
| Transfer | Yes | `ProductVariantStock` ×2 (atomic) | `InventoryService.TransferStockAsync` (`cs:101`) |
| Consumption (commit) | Yes | `ProductVariantStock.Quantity` & `.ReservedQuantity` | `CommitReservationAsync` (`cs:480-588`) |
| Restoration (return) | Yes | `ProductVariantStock` (compensating movement) | `SalesReturnsController.cs`/`PurchaseReturnsController.cs` (`findings:42`) |

Ownership note: every operation mutates `ProductVariantStock` as the single authority; reservations and movements are side-records that reflect or allocate that authority.

---

## 9. Stock Reservation

**Status: IMPLEMENTED** (production-certified).

- **Lifecycle:** `Active` → (reserve) → `PartiallyCommitted`/`Committed` on sale; `PartiallyReleased`/`Released` on cancel; `PartiallyReturned`/`Returned` on return; `Expired` on TTL (`InventoryReservationStatus.cs`, `InventoryReservation.cs:20`).
- **Creation:** `ReserveAsync` — validates variant is current inventory lineage, locks stock rows in deterministic `WarehouseId` order, checks `AvailableQuantity >= quantity`, splits reservation across warehouses if `warehouseId` not specified, creates `InventoryReservation` rows, enqueues stock-sync outbox (`ProductVariantStockRepository.cs:81-229`).
- **SQL / Locking:** row locks via `LockStockForMutationAsync` (PostgreSQL `FOR UPDATE`, SQL Server `UPDLOCK, ROWLOCK, HOLDLOCK`) (`ProductVariantStockMutationSupport.cs:397-447`). Deterministic lock ordering `(WarehouseId, VariantId)` prevents deadlocks (`InventoryHardeningExtensions.cs:94-110`).
- **Concurrency:** reservation cannot oversell — bounded by locked `AvailableQuantity`; commits use cumulative `CommittedQuantity` so duplicate delivery cannot consume more than reserved (`findings:62-63`; `CommitReservationAsync.cs:544-560`).
- **Release:** decrements `ReservedQuantity` on stock, increments `ReleasedQuantity` on reservation, idempotent via `RemainingReservedQuantity` (`ProductVariantStockRepository.cs:264-330`).
- **Expiry:** `InventoryReservationCleanupJob` + `CommerceHubInventoryReconciliationJob` release expired reservations (`CommerceHubInventoryReconciliationJob.cs:89-114`).
- **Idempotency:** order-level reservation claim (`CheckoutService.cs:5135-5183`); release on failure path (`CheckoutService.cs:5230-5251`).

---

## 10. Warehouse Transfer

**Status: IMPLEMENTED** (structured lifecycle).

- **Business flow:** Create (Pending) → Approve → Complete (atomic stock move). Reject also supported (`StockTransfersController.cs:50-442`).
- **Authorization:** Create/Approve/Reject/Complete require `Owner,Admin,Manager` (`StockTransfersController.cs:51,266,305,344`).
- **Inventory mutation:** `CompleteTransfer` → per item `InventoryService.TransferStockAsync`: decrement source `Quantity`, increment target `Quantity`, write `TransferOut` + `TransferIn` `StockMovement`s (`InventoryService.cs:101-205`). All balance + movement writes share one transaction.
- **Validation:** non-positive quantity rejected; same-warehouse transfer rejected; inactive source/target rejected; invalid lineage rejected (`InventoryService.cs:103-107`, `StockTransfersController.cs:129-145`).
- **Accounting impact:** **No accounting/posting against transfers is implemented in this module.** Transfer mutates inventory only; no ledger entries were found in `StockTransfersController`/`InventoryService.TransferStockAsync`. (Marked NOT IMPLEMENTED for accounting.)
- **Events:** `StockTransferredEvent` defined (`Core/Events/InventoryEvents.cs:24-35`); completion also publishes `ProductVariantStockChangedEvent` via outbox (`StockTransfersController.cs:424-430`).
- **Idempotency:** `StockTransferRequestIdempotency` durable record + fingerprint conflict detection (`StockTransfersController.cs:59-103`).
- **Concurrency:** completion locks transfer state + stock rows in deterministic order, state-gated (`Status == Approved`), cannot double-apply a completed transfer (`findings:50-53`).

---

## 11. Opening Balance

- **Inventory Opening Balance** — `InventoryService.SaveOpeningStockAsync`, `POST api/warehouses/opening-stock` (Owner/Admin/Manager) and `GET api/warehouses/opening-stock` (`WarehousesController.cs:423-501`, `InventoryService.cs:225-375`).
- **Warehouse Opening Balance** — scoped per `(WarehouseId, VariantId)`; variant-centric (not product) (`WarehousesController.cs:485-501`).
- **Behavior:** writes a `ProductVariantStock.Quantity = item.Quantity`, creates a `StockMovement` with `IsOpeningBalance=true`, `ReferenceNumber` deterministic (`OB-VAR-...`), and idempotency key `opening-stock:{tenant}:{warehouse}:{variant}` (`InventoryService.cs:317-348`).
- **Mutations / Guard:** **one-time only** — rejected if any prior authority exists (quantity≠0, reserved≠0, damaged≠0, or any prior movement for that variant+warehouse). Positive quantities only; duplicate variant in request rejected (`InventoryService.cs:238-311`). Uses the shared locked stock mutation path (`LockStockForMutationAsync`, `cs:281-286`).

---

## 12. Inventory Constraints (Implementation-Backed)

| Rule | Where |
|------|-------|
| One stock row per `(TenantId, VariantId, WarehouseId)` | `ProductVariantStockConfiguration.cs:60-62` (unique index); `add_product_variant_stock_unique_constraint.sql` |
| `Quantity >= 0` | `ProductVariantStockConfiguration.cs:18-20`; check constraint `CK_ProductVariantStocks_Quantity_NonNegative` |
| `ReservedQuantity >= 0` | `cs:21-23`; `CK_...ReservedQuantity_NonNegative` |
| `DamagedQuantity >= 0` | `cs:24-26`; `CK_...DamagedQuantity_NonNegative` |
| `Quantity >= ReservedQuantity` | `cs:27-29`; `CK_...ReservedQuantity_Within_Quantity` |
| Available cannot exceed physical | Derived `Quantity - ReservedQuantity`; never client-submitted (`findings:30-32`) |
| Cannot go negative on mutation | `ValidateQuantityChange` (`InventoryService.cs:63-67`, `InventoryHardeningExtensions.cs:70-92`) after lock, before mutation |
| Duplicate opening variant rejected | `InventoryService.cs:243-251` |
| Opening stock positive only | `InventoryService.cs:238-241` |
| Opening stock one-time | `InventoryService.cs:294-311` |
| Transfer positive, distinct, active warehouses | `InventoryService.cs:103-107`; `StockTransfersController.cs:112-145` |
| Inactive warehouse cannot mutate stock | `findings:21`; `ProductVariantInventoryLineageAuthority.cs:94-100` |
| Cross-tenant identifiers rejected | `ProductVariantInventoryLineageAuthority.cs:114-116`; `findings:82-87` |
| StockMovements append-only | App `DbContext` rejects update/delete; PostgreSQL trigger `trg_stock_movements_append_only` (`20260607210000_PosWarehouseInventoryAuthorityHardening.cs`) |
| Reservation invariant `Reserved <= Quantity` | `ProductVariantStockRepository.cs:202-208`, `544-549` |
| TenantId required on stock + outbox + reservation | `ProductVariantStock.cs:23`, `StockSyncOutboxMessage.cs:27`, `InventoryReservation.cs:8` |

---

## 13. SQL Transactions

- **Transaction boundary:** every mutation requires an explicit relational transaction; `EnsureMutationTransaction` throws if none (`ProductVariantStockMutationSupport.cs:388-395`). Controllers use `BeginTransactionAsync` (`WarehousesController.cs:229`; `StockTransfersController.cs:89`).
- **Locks:** pessimistic row lock — PostgreSQL `FOR UPDATE` (`xmin` selected for version), SQL Server `UPDLOCK, ROWLOCK, HOLDLOCK`, in-memory fallback (`ProductVariantStockMutationSupport.cs:397-447`).
- **Deterministic ordering:** multi-row ops lock by `(WarehouseId, VariantId)` to prevent deadlocks (`LockMultipleStocksAsync`, `cs:292-325`; `InventoryHardeningExtensions.OrderForLocking`, `cs:103-110`).
- **FOR UPDATE / UPDLOCK:** implemented per provider (above).
- **Optimistic concurrency:** `StockSyncOutboxMessage.RowVersion` `[Timestamp]` (`StockSyncOutboxMessage.cs:49-50`); DB-level check constraints are the final guard.
- **Rollback:** controllers rollback on idempotency conflict / exception (`WarehousesController.cs:237,342`; `StockTransfersController.cs:98,259`); `StockMutationTransaction.RollbackAsync` (`InventoryHardeningExtensions.cs:232-245`).
- **Outbox:** durable `StockSyncOutboxMessages` staged BEFORE commit (`ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync`, `cs:216-250`).
- **Retry:** outbox retry with exponential backoff, terminal failure after threshold, compensation (`StockSyncOutboxMessage.RecordFailure/RecordNonTerminalProjectionFailure`, `cs:162-190`; `StockSyncOutboxProcessor.cs:269-353`).
- **Atomicity:** transfer completion commits source/target/ movements/state together (`findings:51-52`).

---

## 14. Events

- **Inventory events (domain):** `StockAdjustedEvent`, `StockTransferredEvent`, `StockReservedEvent`, `StockReleasedEvent`, `StockReceivedEvent` (`Core/Events/InventoryEvents.cs`).
- **Warehouse events:** no dedicated `WarehouseXEvent` entity found; warehouse changes are not event-sourced.
- **Reservation events:** `InventoryReservationReleasedEvent` raised on release (`CheckoutService.cs:8394`); reservation lifecycle internal.
- **Transfer events:** `StockTransferredEvent` (`InventoryEvents.cs:24-35`).
- **Stock-sync projection events:** `ProductVariantStockChangedEvent` built AFTER commit (`ProductVariantStockMutationSupport.BuildCommittedStockEventsAsync`, `cs:125-214`) → durable `StockSyncOutboxMessage` → MassTransit `IPublishEndpoint` (`StockSyncOutboxProcessor.PublishAsync`, `cs:385-419`).
- **Projection updates:** `ProductStockChangedConsumer` updates MongoDB `products` document (`ProductStockChangedConsumer.cs:26-228`) — **read-only projection, not authority**.
- **Consumers:** `ProductStockChangedConsumer` (Mongo), `ProductVariantProjectionConsumer`, `CommerceHubProjectionConsumer`, `ProductStockPriceSearchIndexConsumer`, `Affiliate...ProjectionConsumer` (search/index). All derivative.

---

## 15. Runtime Architecture

```
SQL (PostgreSQL)  ── authority ──┐
   ProductVariantStocks           │
   StockMovements (append-only)   │
   InventoryReservations          │
   StockTransfers                 │
        │ mutation                │
        ▼                         │
StockSyncOutboxMessages ── durable, warehouse-scoped, claim-leased
        │ StockSyncOutboxProcessor (PG advisory xact lock, ordered, deferred, suppressed)
        ▼
MassTransit / RabbitMQ (IPublishEndpoint)
        ▼
MongoDB products projection  (READ-ONLY: StockQuantity, variants.TotalStock)
Redis: reservation cache (NON-FATAL, debug only)  (CheckoutService.cs:5188-5215)
```

- SQL is **authoritative**; Mongo/Redis/Elasticsearch are **derivative**.
- Ordering prevents stale projection: outbox selects by `EventVersion → StockUpdatedAtUtc → OccurredAtUtc → CreatedAt → Id` and **defers** an event if an earlier same-variant/warehouse event is unpublished (`StockSyncOutboxProcessor.cs:479-501`).
- **Suppression:** an outbox message is suppressed (marked published without dispatch) if the variant/stock lineage is lost or the event is stale vs current `ProjectionMutationVersion` (`StockSyncOutboxProcessor.ResolveStockSyncSuppressionReasonAsync`, `cs:421-477`).
- No Elasticsearch stock-authority path found; Elasticsearch is used for product *search* (`ProductSearchSqlAuthorityOverlay`), not inventory authority.

---

## 16. Browser Test Specification

Scenarios (frontend: `PointOfSale.Web/src/pages/Warehouses.jsx`, `WarehouseOpeningStock.jsx`; tests: `warehouseFrontendCertification.test.ts`):

1. **Create warehouse** (Owner/Admin/Manager) → appears in active warehouse list; tenant-isolated.
2. **Save Opening Stock** for variant in active warehouse → quantity becomes available; re-submitting same variant is rejected (one-time).
3. **Adjust stock** positive/negative → movement recorded; negative that would breach `Quantity>=0` rejected.
4. **Transfer** source→target (both active, distinct) → source decreases, target increases, two movements; replayed completion idempotent.
5. **Inactive warehouse** → cannot receive opening stock / adjustment / transfer (`findings:21`).
6. **Cross-tenant** warehouse/variant id → mutation rejected (lineage guard).
7. **Stock display** (storefront) reflects MongoDB projection after outbox dispatch; lag acceptable, but SQL remains truth.
8. **Reservation oversell** → checkout rejects when `AvailableQuantity < requested` (`CheckoutService.cs:5123-5131`).
9. **Cancel order** → reserved quantity released back to available (`ReleaseOrderReservationsAsync`).
10. **Payment success** → reserved committed, `Quantity` and `ReservedQuantity` both decrease (`CommitOrderReservationsAsync`).

Business assertions: available never exceeds physical; stock never negative on certified paths; transfers never duplicate; reservations never oversell; deleted/inactive warehouses hold no authority.

---

## 17. Domain Ownership Matrix

| Capability | Owner |
|------------|-------|
| Warehouse Identity | `Warehouse` |
| Inventory Quantity | `ProductVariantStock` |
| Reserved Quantity | `ProductVariantStock` (allocated by `InventoryReservation`) |
| Damaged Quantity | `ProductVariantStock` |
| Availability Derivation | `ProductVariantStock.AvailableQuantity` (derived) |
| Stock Movement History | `StockMovement` (append-only) |
| Reservation | `InventoryReservation` |
| Transfer Lifecycle | `StockTransfer` (+ `StockTransferItem`) |
| Adjustment | `ProductVariantStock` (via `InventoryService`) |
| Opening Balance | `ProductVariantStock` (+ `StockMovement.IsOpeningBalance`) |
| Search / Display Projection | MongoDB `products` (derivative) |
| Event Durability | `StockSyncOutboxMessage` |

---

## 18. Canonical Pattern

```
Warehouse      → Storage Identity (no quantity)
ProductVariantStock → Inventory Authority (qty / reserved / damaged)
InventoryReservation → Allocation against authority
StockMovement  → Append-only History
StockTransfer  → State-gated Atomic Mutation of two authorities
StockSyncOutbox → Durable Projection Bridge (SQL → Mongo)
MongoDB        → Read-only Projection (never authority)
```

---

## 19. Documentation Validation (Misconceptions Corrected)

- ❌ Warehouse owns inventory. ✅ Warehouse is identity only; `ProductVariantStock` owns inventory (`findings:18-22`; `Warehouse.cs`).
- ❌ Product owns stock. ✅ Product never holds stock; variant+warehouse does (`WarehousesController.cs:149-151`).
- ❌ `WarehouseStock` is the inventory table. ✅ It is legacy compatibility only; `ProductVariantStocks` is authority (`ProductVariantStock.cs:13-15`).
- ❌ Available quantity is stored. ✅ It is derived `Quantity - ReservedQuantity` (`ProductVariantStock.cs:53`).
- ❌ Stock mutations can run outside a transaction. ✅ Explicit transaction required (`ProductVariantStockMutationSupport.cs:388-395`).
- ❌ StockMovement is editable. ✅ Append-only (app + PG trigger) (`20260607210000_...cs`).
- ❌ MongoDB is source of truth for stock. ✅ PostgreSQL is; Mongo is projection (`ProductStockChangedConsumer.cs:21-24`).
- ❌ Transfers post to accounting. ✅ Not implemented in this module (inventory-only).
- ❌ Opening stock is repeatable. ✅ One-time, before any prior authority (`InventoryService.cs:294-311`).

---

## 20. Final Architecture Conclusion

- **Warehouse Authority:** Warehouse is a tenant-scoped *storage identity*. It holds no quantity and has no delete endpoint; deactivation removes its mutation authority.
- **Inventory Authority:** `ProductVariantStocks` (SQL) is the single mutable source of truth, keyed `(TenantId, VariantId, WarehouseId)`, with non-negative and reserved-within-quantity constraints enforced at DB level.
- **Runtime Flow:** SQL mutation (row-locked, transactional) → durable `StockSyncOutboxMessages` (warehouse-scoped, claim-leased, ordered, deferred, suppressed by lineage) → MassTransit → MongoDB read-only projection. Redis holds non-fatal reservation cache.
- **Aggregate Ownership:** Warehouse (identity), ProductVariantStock (quantity), InventoryReservation (allocation), StockTransfer (structured 2-row mutation), StockMovement (audit, append-only).
- **Transaction Model:** explicit relational transactions; provider-specific pessimistic row locks (`FOR UPDATE` / `UPDLOCK`); deterministic `(WarehouseId, VariantId)` lock ordering; DB check constraints as final guard; durable outbox for projection consistency with retry/compensation.
- **Projection Strategy:** MongoDB and search indexes are derivative; outbox ordering + `ProjectionMutationVersion` monotonicity + lineage suppression guarantee convergence without granting them authority.

**Verdict (PASS P1-EB):** Production-certifiable after applying migration `20260607210000_PosWarehouseInventoryAuthorityHardening`. Cross-tenant access, negative stock, oversell, duplicate transfer, and replay are all closed on certified paths.
