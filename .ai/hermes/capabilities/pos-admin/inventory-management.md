# Inventory Management Capability (POS Admin)

> Reverse-engineered from the actual implementation. Where this document and any
> narrative/README disagree, the implementation wins. Implementation references are
> given as `file.cs` anchors so the findings can be verified without reading the whole
> codebase. SQL (`ProductVariantStocks`) is the single authoritative source of stock
> truth. Every other representation (Mongo product document, Elasticsearch, Redis,
> CommerceHub read model, Admin UI) is a derivative projection.

---

## Purpose

Inventory Management owns the **physical and available stock balance of each product
variant, in each warehouse, per tenant**. It exists to answer one question with
authority: *"For tenant T, variant V, warehouse W, how many units are physically on hand,
how many are reserved for orders, and how many are therefore sellable right now?"*

Business problems it solves:
- Prevent overselling by reserving stock at checkout before payment.
- Keep SQL-authoritative stock balances, reservation rows, stock-sync events, and movement
  ledger evidence aligned. `StockMovements` is append-only where written, but storefront
  checkout reservation/release/commit authority is primarily proven by
  `ProductVariantStocks`, `InventoryReservations`, and `StockSyncOutboxMessages` rather
  than by a movement row on every reservation mutation.
- Allow operators to correct stock, open balances, and move stock between warehouses.
- Recover stock automatically when reservations expire or payments fail.

What it owns:
- `ProductVariantStocks` — the per-(Tenant, Variant, Warehouse) balance authority.
- `InventoryReservations` — per-order-line reservations against that balance.
- `StockMovements` — the immutable audit ledger of every balance mutation.
- `StockSyncOutboxMessages` — the durable bridge to derivative projections.
- `StockTransfers` — warehouse-to-warehouse movement documents.
- The lifecycle rules below (reservation, release, commit, expiry, reconciliation).

What it does NOT own (authority boundary):
- Pricing (owned by Pricing/Product). Inventory carries `Price` only on opening-balance
  movements for valuation, not sell price.
- Product/Variant catalog truth (owned by Product domain). Inventory only *references*
  variant/warehouse and requires them to be current lineage to mutate.
- Orders/Checkout/Payments (owned by Order/Payment lifecycle). Inventory reacts to them
  via reservations and lifecycle hooks, but does not decide order/payment outcomes.
- Storefront/CommerceHub *display* of stock (derivative). Inventory publishes events;
  the read models project them.
- Accounting/Ledger/Treasury (owned by Finance). Inventory changes are not journal entries.
- Search ranking/merchandising (owned by Search). Inventory feeds a stock field only.

---

## Business Scope (implementation-proven)

Inventory is **Variant + Warehouse only**. A Product never holds stock directly. Stock is
held on `ProductVariantStock` rows keyed by `(TenantId, VariantId, WarehouseId)`
(`ProductVariantStock.cs`, unique constraint documented in `ProductVariantStockMutationSupport.cs`).

Entities in scope:
- **ProductVariantStock** (`ProductVariantStock.cs`): `Quantity` (sellable physical),
  `ReservedQuantity`, `DamagedQuantity`. Computed: `AvailableQuantity = Quantity -
  ReservedQuantity`; `TotalQuantity = Quantity + DamagedQuantity`.
- **Warehouse** (`Warehouse.cs`): `TenantId`, `IsActive`, `IsDefault`. Not an inventory
  authority source by itself — it is part of the lineage gate (see below).
- **WarehouseStock** (`WarehouseStock.cs`): LEGACY compatibility table only. NOT an
  inventory authority. All operational reads/writes use `ProductVariantStocks`.
- **InventoryReservation** (`InventoryReservation.cs`): `OrderId`, `OrderItemId`,
  `VariantId`, `WarehouseId`, `ReservedQuantity`, `CommittedQuantity`,
  `ReleasedQuantity`, `ReturnedQuantity`, `DamagedReturnedQuantity`, `Status`,
  `ExpiresAtUtc`. Computed: `RemainingReservedQuantity = Reserved - Committed -
  Released`; `RemainingDeliveredReturnableQuantity = Committed - Returned -
  DamagedReturned`.
- **StockMovement** (`StockMovement.cs`): immutable append-only ledger row for movement
  paths that write ledger evidence (admin adjustments, opening stock, transfers,
  CommerceHub reservation/commit/release/return, purchase/sales inventory effects). Carries
  after-state snapshots (`QuantityAfter`, `AvailableAfter`, `ReservedAfter`,
  `DamagedAfter`), `MovementType`, `Reference*`, optional `UnitId`/`ConversionFactor`
  (multi-unit snapshot), `IdempotencyKey`, `InventoryEventId`, `Source`. Storefront/POS
  checkout repository reservation/release/commit paths do not rely on a `StockMovement` for
  authority; inspect `ProductVariantStocks`, `InventoryReservations`, and
  `StockSyncOutboxMessages` for those paths.
- **StockTransfer** (`StockTransfer.cs`) + `StockTransferItem`: lifecycle
  `Pending → Approved → Completed` (also `Rejected`, `Cancelled`).

Inventory concepts owned:
- **Stock** = `Quantity` on a variant/warehouse row.
- **Available Stock** = `Quantity - ReservedQuantity` (`ProductVariantStock.AvailableQuantity`).
- **Reserved Stock** = `ReservedQuantity`, the sum of active order reservations.
- **Physical/Damaged Stock** = `Quantity` (sellable) + `DamagedQuantity` (non-sellable
  physical) = `TotalQuantity`.
- **Inventory Movements / Ledger** = `StockMovement` (append-only, never updated/deleted)
  where the runtime writes a movement row. It is an audit ledger, not the universal proof
  of every reservation mutation.
- **Stock Adjustments** = operator corrections via `AdminInventoryService.AdjustStockAsync`
  and `InventoryService.AdjustStockAsync`.
- **Transfers** = `StockTransfersController` (create/approve/reject/complete).
- **Opening Stock** = one-time seed via `InventoryService.SaveOpeningStockAsync`.
- **Variant Stock** = the unit of stock (never the Product).
- **Multi-unit** = per-movement `UnitId` + `ConversionFactor` snapshot; conversion is
  captured at movement time, not re-derived.
- **Low Stock** = derivative flag: `Quantity <= Product.MinimumStockLevel`
  (`AdminInventoryService.ListInventoryAsync`, DTO `IsLowStock`). Not a stored column;
  recomputed on read.
- **Backorders** = NOT handled by inventory reservation. Items with
  `Product.AllowBackorder == true` (or `TrackStock == false`) skip reservation entirely
  (`CheckoutService.ValidateAndReserveInventoryAsync`, `CheckoutInventoryAuthorityGuard`).
- **Stock Visibility / CommerceHub Stock / Storefront Stock** = derivative projections of
  SQL available sellable stock (`sum(Quantity - ReservedQuantity)` across current
  stock-owner warehouses), not physical stock and not owned by Inventory presentation layers.
- **Reservations** = owned by Inventory (see Reservation section).

---

## Authority Boundaries

| Concern            | Authority                        | Evidence (not authority)                          | Presentation (not authority)        |
|--------------------|----------------------------------|---------------------------------------------------|-------------------------------------|
| Stock truth        | SQL `ProductVariantStocks`       | StockSync events, Mongo `products` doc            | Admin UI, Storefront, CommerceHub    |
| Reservations       | SQL `InventoryReservations`      | Reservation rows replicated nowhere               | Cart/checkout indicators             |
| Availability       | SQL `AvailableQuantity`          | `ProductDocument.StockQuantity`                   | Storefront "In Stock"               |
| Pricing            | Pricing/Product domain           | —                                                 | Product page                        |
| Products/Variants  | Product domain (catalog)         | Variant projection                               | Catalog UI                          |
| Orders/Checkout    | Order domain                     | OrderItem rows referenced by reservations         | Checkout                            |
| CommerceHub vis.   | SQL (projected to CommerceHub read model) | CommerceHub projection                    | CommerceHub storefront              |
| Storefront vis.    | SQL (projected to Mongo)         | Mongo `products`                                  | Storefront                          |
| Search             | SQL (projected to ES)            | ES stock field                                    | Search results                      |
| Analytics          | SQL (read models)                | Aggregated snapshots                              | Dashboards                          |
| Accounting         | Finance domain                   | —                                                 | Ledger                             |

Authority vs projection vs evidence vs presentation:
- **Authority**: the row in `ProductVariantStocks` / `InventoryReservations` inside the
  committed PostgreSQL transaction. SQL constraints (non-negative `Quantity`,
  `ReservedQuantity`, `DamagedQuantity`; `Quantity >= ReservedQuantity`) are the final
  guard (`docs/.../pos-warehouse-inventory-authority-certification-findings.md`,
  migration `20260607210000_PosWarehouseInventoryAuthorityHardening`).
- **Projection**: Mongo `products` document, CommerceHub projection, Elasticsearch —
  updated asynchronously from `ProductVariantStockChangedEvent` via the stock-sync
  outbox. They converge to SQL but can lag.
- **Evidence**: `InventoryReservations`, stock rows, order state, replay payloads, cache
  are *evidence only*. They are accepted as authority only after current SQL lineage
  recomposition approves them (`ProductVariantInventoryLineageAuthority`).
- **Presentation**: Admin UI, Storefront, CommerceHub, Redis cache — display only; never
  feed back into stock authority.

**Critical lineage gate** (`ProductVariantInventoryLineageAuthority.cs`): a stock row,
reservation, or stock-sync event is only "current" if the full ownership chain is valid:
same-tenant Warehouse (active, not deleted) → Variant (active, not deleted,
`SemanticLifecycleState == Current`, no supersession) → Product (active, not deleted). If
any link is broken (variant superseded, warehouse deactivated, product deleted), the stock
is no longer *authority* — cleanup jobs release reservations, outbox messages are
suppressed, and availability reads exclude it. This is why deleted/inactive entities can
still have undeleted SQL rows yet must not contribute to sellable stock.

---

## Operational Lifecycle

End-to-end for an inventory mutation (adjustment shown; transfer/sale/receive follow the
same shape):

1. **Trigger** — operator calls `POST /api/admin/inventory/adjust` or code path calls
   `InventoryService.AdjustStockAsync` / `AdminInventoryService.AdjustStockAsync`.
2. **Validation (pre-lock)**
   - Tenant resolved via `ITenantContext.RequiredTenantId`.
   - Variant resolved and required to be current lineage (`ResolveVariantAsync`,
     `RequireCurrentStockMutationOwnerAsync`).
   - Warehouse required active + same-tenant (`AdminInventoryService.AdjustStockAsync` /
     `StockTransfersController`).
   - Idempotency key checked durably in SQL (`StockMovements.IdempotencyKey` unique
     constraint `UX_StockMovements_TenantId_IdempotencyKey`); Redis is cache-only, SQL is
     the authority (`AdminInventoryService`).
3. **Transaction open** — every mutation requires an explicit relational transaction
   (`EnsureMutationTransaction`); checkout re-locks at `Serializable`.
4. **Row lock (pessimistic)** — `LockStockForMutationAsync` runs
   `SELECT ... FOR UPDATE` (Postgres) / `UPDLOCK,ROWLOCK,HOLDLOCK` (SQL Server) on the
   exact `(TenantId, VariantId, WarehouseId)` row (`ProductVariantStockMutationSupport`).
   Multi-row ops lock in deterministic order `(WarehouseId, VariantId)` to prevent
   deadlocks (`LockMultipleStocksAsync`).
5. **Business rule check (post-lock, pre-mutation)** — availability-consuming paths use
   `ValidateAvailableQuantity` / repository reservation checks to reject insufficient
   available stock. Direct signed quantity changes use `ValidateQuantityChange`, which
   prevents `Quantity < 0`; the SQL check constraint `Quantity >= ReservedQuantity` is the
   final fail-closed guard when a direct decrease would make available stock negative.
6. **Mutation** — `stock.Quantity += change`; recompute after-state; append a
   `StockMovement` (SQL-computed after-state, immutable).
7. **Commit** — `SaveChangesAsync` (balance + movement in one transaction).
8. **Outbox stage** — `PublishCommittedStockEventsAsync` builds
   `ProductVariantStockChangedEvent` from the *committed* state, advances
   `Variant.StockUpdatedAtUtc` / `ProjectionMutationVersion`, and inserts a
   `StockSyncOutboxMessage` (durable, in the same transaction).
9. **Commit transaction** — only now is the event durable.
10. **Outbox dispatch** — `StockSyncOutboxProcessor` claims the message (PG advisory
    lock per tenant/variant/warehouse to prevent races), enforces canonical ordering so
    older events in the same stream don't publish after newer ones
    (`HasUnpublishedEarlierVariantEventAsync`), re-checks lineage suppression, publishes
    to MassTransit, marks `PublishedAtUtc`.
11. **Projection** — `ProductStockChangedConsumer` / `ProductVariantStockChangedEvent`
    consumer updates Mongo `products` (variant `TotalStock` and aggregate `StockQuantity`
    = sum of active variants' projected available stock). `ProductVariantProjectionConsumer` /
    `CommerceHubProjectionConsumer` update CommerceHub. ES consumer updates search.
    Projection event fences + `StockUpdatedAtUtc`/`EventVersion` reject stale updates.
12. **Derivative visibility** — Storefront shows updated "In Stock"; CommerceHub shows
    updated stock; Analytics reads the new aggregate.
13. **Operator sees updated stock** — Admin list reads `ProductVariantStocks` directly
    (SQL authority), so it is consistent immediately on commit.

Reservation-specific lifecycle:
- **Reserve** — checkout (`ProductVariantStockRepository.ReserveAsync`,
  `CheckoutService.ValidateAndReserveInventoryAsync`) or CommerceHub
  (`InventoryConsistencyService.ReserveOrderItemAsync`) increments `ReservedQuantity`
  (locked, lineage-filtered) and writes `InventoryReservation` rows. With no `warehouseId`,
  storefront/POS checkout repository allocation uses deterministic `WarehouseId` then stock
  id order; CommerceHub allocation uses largest `AvailableQuantity` first, then
  `WarehouseId`, then stock id.
- **Commit (delivery)** — `CommitReservationAsync` / `CommitOrderReservationsAsync` /
  `CommerceHubInventoryReconciliationJob.RepairMissingDeliveryCommitsAsync` move reserved
  → committed (decrement `Quantity`, mark `CommittedQuantity`). Uses cumulative applied
  quantities so duplicate delivery cannot over-consume.
- **Release** — on cancel/expiry/failure: `ReleaseAsync`, `ReleaseOrderReservationsAsync`,
  `InventoryConsistencyService.ReleaseReservationAsync` decrement `ReservedQuantity` and
  increment `ReleasedQuantity`.
- **Expire/Cleanup** — `InventoryReservationCleanupJob` (Hangfire,
  `[DisableConcurrentExecution]`) finds stale/expired/orphan reservations, reloads order +
  payment + CommerceHub state, and releases only when no blocking payment status exists
  (`PendingResolution`, `PendingTenantResolution`, `RoutedToSuspense`,
  `SuspenseMatchedPaymentPendingClearance`). For `Pending`/`Authorized`/`AwaitingVerification`
  payments it routes through `IPaymentLifecycleService.MarkPaymentExpiredAsync` (fail
  closed via lifecycle, not a direct stock release).
- **Reconciliation** — `CommerceHubInventoryReconciliationJob` repairs missing delivery
  commits, missing return receipts, open reservations on completed orders, expired
  reservations, and negative/invalid stock rows.
- **Returns** — `ReceiveReturnAsync` restores resellable/damaged quantity to
  `ProductVariantStocks` only after revalidating current stock ownership (rejects
  inactive-warehouse / superseded-variant replay).

---

## Implementation Discovery (who owns what)

**Frontend** (do not document UI; listed only for traceability)
- `PointOfSale.Web/src/pages/Inventory.jsx`, `InventoryValuation.jsx` — Admin inventory
  screens; call `AdminInventoryController`.

**Controllers / API**
- `AdminInventoryController.cs` (`/api/admin/inventory`, roles Owner/Admin/Manager):
  `GET` list, `POST /adjust`, `GET /movements`. Requires `X-Tenant-Id`, durable
  `X-Request-Id` for adjust.
- `StockTransfersController.cs` (`/api/StockTransfers`): create/approve/reject/complete/
  list. Mutations require Owner/Admin/Manager.
- `ProductsController` / `ProductVariantsController` also mutate stock on create/update
  (variant stock mirror) through the same locked path.

**Application services**
- `AdminInventoryService.cs` — admin list/adjust/movements; durable idempotency; SQL
  lineage; locked mutation.
- `InventoryService.cs` — `AdjustStockAsync`, `TransferStockAsync`, `GetStockLevelsAsync`
  (variant-centric), `SaveOpeningStockAsync` (one-time), `GetStockMovementsAsync`.
- `ProductVariantStockRepository.cs` (`IProductVariantStockRepository`) — the operational
  reservation engine: `ReserveAsync`, `ReleaseAsync`, `ReleaseOrderReservationsAsync`,
  `GetAvailableQuantity(s)Async`, `CommitReservationAsync`,
  `CommitOrderReservationsAsync`, `BindOrderReservationsAsync`.
- `CommerceHub/InventoryConsistencyService.cs` (`IInventoryConsistencyService`) —
  CommerceHub marketplace reservation/commit/release/return with inbox idempotency
  (`InventoryEventInbox`) and lineage filtering.
- `Payments/CheckoutInventoryAuthorityGuard.cs` — re-validates active SQL reservations +
  reserved stock rows cover the order before payment intent work; fails closed with
  `CHECKOUT_INVENTORY_AUTHORITY_LOST`.
- `CommerceHub/CommerceHubInventoryReconciliationJob.cs`, `InventoryEventIdFactory.cs`,
  `CommerceHub/CommerceHubCheckoutService.cs`,
  `CommerceHub/CommerceHubPaymentRecoveryAuthorityService.cs`.

**Domain helpers (the hardening core)**
- `Inventory/ProductVariantStockMutationSupport.cs` — lock, materialize row safely
  (`ON CONFLICT DO NOTHING` / `IF NOT EXISTS`), validate, build events, stage outbox,
  touch `StockUpdatedAtUtc`.
- `Inventory/InventoryHardeningExtensions.cs` — `ValidateStockAvailability`,
  `ValidateQuantityChange` (negative-stock guards), `OrderForLocking` (deadlock
  prevention), `StockMutationTransaction` (SaveChanges→Commit→publish-after-commit),
  `InsufficientStockException`.
- `Reliability/ProductVariantInventoryLineageAuthority.cs` — current-lineage filters
  (`WhereCurrentInventoryStockOwner`, `WhereInventoryReservableLineage`,
  `FilterCurrentConsumableReservationsAsync`, `IsVariantReservableForInventoryAsync`,
  `RequireCurrentStockMutationOwnerAsync`). The heart of "evidence → authority".
- `Infrastructure/Jobs/InventoryReservationCleanupJob.cs` — stale reservation release.
- `Infrastructure/Observability/InventoryReservationMetricsService.cs`,
  `Instrumentation/InventoryReservationTelemetry.cs` — metrics.

**Domain entities / enums**
- `ProductVariantStock`, `Warehouse`, `WarehouseStock` (legacy), `InventoryReservation`,
  `InventoryEventInbox`, `StockMovement`, `StockTransfer`, `StockTransferItem`.
- Enums: `InventoryReservationStatus` (Active, PartiallyReleased, Released,
  PartiallyCommitted, Committed, PartiallyReturned, Returned, Expired),
  `InventoryEventType` (StockReserved, StockReleased, StockDecreased, StockIncreased,
  StockDamaged), `TransferStatus`, `StockMovementType`.

**Events / Outbox**
- `ProductVariantStockChangedEvent` (`ProductVariantEvents.cs`) — the canonical stock
  event; carries `VariantId`, `ProductId`, `WarehouseId`, `OldQuantity`, `NewQuantity`,
  `TotalStockQuantity` (despite the name, the projected available sellable quantity:
  `sum(Quantity - ReservedQuantity)` across current stock-owner warehouses),
  `MutationSource` (POS/CommerceHub), `StockUpdatedAtUtc`, `EventVersion`, `CorrelationId`.
- `InventoryEvents.cs` — `StockAdjustedEvent`, `StockTransferredEvent`, `StockReservedEvent`,
  `StockReleasedEvent`, `StockReceivedEvent` (domain notifications).
- `StockSyncOutboxMessage` + `StockSyncOutboxProcessor` + `StockSyncOutboxWorker` —
  durable exactly-once-ish dispatch with lease, retry, terminal-failure compensation
  (`ProjectionFailureCompensation`).
- `InventoryEventInbox` — per-event idempotency for CommerceHub consistency service.

**Projections / Read models**
- `Consumers/ProductStockChangedConsumer.cs` — Mongo `products`: variant `TotalStock` and
  aggregate `StockQuantity` (= sum of active variants' projected available stock), gated
  by `StockUpdatedAtUtc`/`EventVersion` and `ProjectionEventFence`.
- `Consumers/ProductVariantProjectionConsumer.cs`, `Consumers/CommerceHubProjectionConsumer.cs` —
  CommerceHub read models.
- `Consumers/ProductStockPriceSearchIndexConsumer.cs` — Elasticsearch.
- `Consumers/Affiliate/...` — affiliate intelligence projection (uses current stock-owner
  lineage, never raw rows).

**Caches / Messaging**
- Redis — only a non-authoritative cache (idempotency hint, reservation debug key). Never
  read back as authority.
- MassTransit — event bus for stock-sync and domain events.
- MongoDB — derivative storefront stock. Elasticsearch — derivative search stock.

---

## Business Rules (implementation-backed only)

1. **Single source of truth**: `ProductVariantStocks` is the only mutable stock authority.
   `WarehouseStock` is legacy/compatibility only. (Every inventory service header comment.)
2. **Variant + Warehouse key**: exactly one stock row per `(TenantId, VariantId,
   WarehouseId)` (UNIQUE constraint; safe materialization via `ON CONFLICT DO NOTHING`).
3. **No negative stock**: availability-consuming paths reject insufficient available stock
   after locking. Direct signed quantity changes reject `Quantity < 0` through
   `ValidateQuantityChange`; if the resulting `Quantity` would fall below
   `ReservedQuantity`, SQL constraint `CK_ProductVariantStocks_ReservedQuantity_Within_Quantity`
   is the final fail-closed guard and rolls back the transaction.
4. **No over-reservation**: reservation never exceeds SQL available
   (`AvailableQuantity`). `ReserveAsync`/`ReserveOrderItemAsync` fail closed when
   `totalAvailable < quantity`.
5. **Available is derived, not submitted**: `AvailableQuantity = Quantity -
   ReservedQuantity`. Clients cannot set an authoritative available balance.
6. **Explicit transaction required**: every mutation must run inside a relational
   transaction; `EnsureMutationTransaction` throws otherwise.
7. **Pessimistic lock before mutation**: `FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK` on the
   exact stock row prevents lost updates.
8. **Deterministic lock ordering**: multi-row ops lock in `(WarehouseId, VariantId)` order
   to prevent deadlocks (`LockMultipleStocksAsync`, transfer completion).
9. **Lineage gate**: a stock row / reservation / stock-sync event is authority only if
   Warehouse (active, same-tenant) → Variant (current semantic owner, not deleted) →
   Product (active, not deleted) all hold. Otherwise it is evidence only and is released /
   suppressed / excluded.
10. **Durable idempotency for adjustments**: `StockMovements.IdempotencyKey` unique
    constraint; the request is rejected as duplicate even under a race
    (`IsStockAdjustmentIdempotencyUniqueViolation`). Redis alone is not authority.
11. **Opening stock is one-time**: `SaveOpeningStockAsync` rejects any variant/warehouse
    that already has quantity, reservation, damaged qty, or any prior movement.
12. **StockMovements are append-only**: updates/deletes are rejected in
    `ApplicationDbContext` and by a PostgreSQL trigger. Reverts use compensating movements.
    Do not assume every storefront/POS checkout reservation mutation has a movement row;
    repository reservation authority is in stock/reservation rows plus stock-sync outbox.
13. **Reservations are cumulative-settled**: release/commit use `ReleasedQuantity` /
    `CommittedQuantity` accumulators; duplicate delivery/release cannot exceed the
    reservation authority.
14. **Backorder / non-tracked skip reservation**: items with `AllowBackorder == true` or
    `TrackStock == false` are not stock-validated or reserved
    (`ValidateAndReserveInventoryAsync`, `CheckoutInventoryAuthorityGuard`).
15. **Reservation TTL**: storefront reservations expire after `InventoryReservationTtlMinutes = 10`
    minutes (`CheckoutService`); CommerceHub/explicit reservations carry their own
    `ExpiresAtUtc`.
16. **Stale reservation cleanup fails closed**: `InventoryReservationCleanupJob` will not
    release when a blocking unresolved payment status exists
    (`PendingResolution`, `PendingTenantResolution`, `RoutedToSuspense`,
    `SuspenseMatchedPaymentPendingClearance`); for Pending/Authorized/AwaitingVerification it
    routes through payment lifecycle expiry, not a direct stock release.
17. **Outbox ordering & suppression**: stock-sync publish respects canonical ordering per
    (tenant, variant, warehouse) stream; events are suppressed if the variant/stock is no
    longer current lineage, or if a newer event version already applied
    (`StockSyncOutboxProcessor.ResolveStockSyncSuppressionReasonAsync`).
18. **Transfer rules**: create rejects non-positive qty, same-warehouse, invalid lineage,
    inactive source/target warehouses; completion re-validates inside the final locked
    transaction and is idempotent/state-gated (`StockTransfersController`).
19. **Cross-tenant isolation**: tenant is enforced on every query and in lineage checks;
    foreign-tenant warehouse/product/variant/stock/reservation identifiers fail lineage
    validation. `X-Tenant-Id` is stripped and replaced by `ITenantContext.RequiredTenantId`.
20. **Product never holds stock**: all stock is on variants; product-level stock in Mongo is
    an aggregate of active variant projected available stock (`TotalStock` in the Mongo
    document, sourced from SQL `Quantity - ReservedQuantity`).

---

## Cross-System Effects

- **Products / Variants**: Inventory references them and requires current lineage to
  mutate or reserve. Deleting/superseding a variant or deactivating its product collapses
  its stock authority (cleanup releases reservations, outbox suppressed, availability
  excluded). Inventory does NOT delete products.
- **Pricing**: No stock change alters price. Stock `Price` field exists only on
  opening-balance movements for valuation.
- **Orders / Checkout**: Checkout reserves stock (decrements available) and commits on
  delivery; cancellation releases. `CheckoutInventoryAuthorityGuard` re-verifies before
  payment. Storefront publication state does NOT gate inventory; inventory reservation can
  occur even if storefront later disabled (publication is display-only).
- **Payments**: `InventoryReservationCleanupJob` collaborates with
  `IPaymentLifecycleService` — unresolved/blocking payment states prevent stock release;
  expired reservations on pending payments route through payment expiry. Payment does not
  own stock but gates its release.
- **Storefront (Mongo)**: `ProductVariantStockChangedEvent` ultimately updates per-variant
  `TotalStock` and aggregate `products.StockQuantity`. These fields represent projected
  available sellable stock (`Quantity - ReservedQuantity`), not physical stock. "In Stock" /
  "Add to Cart" derive from this projection, which lags SQL by outbox latency.
- **CommerceHub**: marketplace orders reserve/commit/release via
  `InventoryConsistencyService`; CommerceHub projection consumes stock events. CommerceHub
  payment recovery re-validates current stock/lineage authority (no stale reservation
  reuse).
- **Affiliate**: affiliate intelligence projection consumes stock via current stock-owner
  lineage only (no raw row sums).
- **Search (Elasticsearch)**: stock field updated via `ProductStockPriceSearchIndexConsumer`;
  search overlay recomposes variant totals from current stock-owner lineage.
- **Analytics**: dashboards read SQL aggregates (e.g. `DashboardController` low-stock /
  inventory-value queries on `ProductVariantStocks`). These reflect SQL truth, not
  projections.
- **Accounting / Treasury / Ledger**: NOT affected by inventory mutations directly. Stock
  changes are not journal entries. Only the SQL balance + movement ledger change.
- **Notifications / Identity**: no inventory-owned notifications; identity (tenant/role)
  is consumed via `ITenantContext` and `[Authorize]`; does not own identity.

What changes vs what does NOT:
- Changes: `ProductVariantStocks`, `InventoryReservations`, `StockMovements`,
  `StockSyncOutboxMessages`, then (async) Mongo/ES/CommerceHub projections, Redis cache.
- Does NOT change: pricing, product catalog, orders/payments state (except reservations),
  accounting, identity.

---

## Validation Checklist

**After receiving stock** (purchase invoice / `StockReceivedEvent`)
- `Quantity` increases; a `StockMovement` (Receipt/Increase) is appended; outbox event
  fires; Mongo/ES/CommerceHub stock increases.
- Reserved quantity unchanged; available increases by the received amount.

**After reserving stock** (checkout / CommerceHub)
- `ReservedQuantity` increases; `AvailableQuantity` decreases; `InventoryReservation` rows
  created (status Active); outbox event fires; derivative available stock drops.
  CommerceHub reservation paths also append `StockMovement` evidence; storefront/POS
  checkout repository reservation paths prove authority through stock/reservation rows and
  stock-sync outbox messages.
- `Quantity` unchanged until commit.

**After releasing a reservation** (cancel / cleanup / expiry)
- `ReservedQuantity` decreases; `AvailableQuantity` increases; reservation
  `ReleasedQuantity`/status updated; outbox event fires; derivative available stock rises.
  Do not require a `StockMovement` row for storefront/POS checkout repository release
  paths; CommerceHub release paths do append movement evidence.

**After committing a reservation** (delivery)
- `Quantity` decreases; `CommittedQuantity` increases; `ReservedQuantity` decreases
  (reserved→committed); available unchanged at commit; outbox fires. CommerceHub delivery
  commit appends movement evidence; storefront/POS checkout repository commit authority is
  proven by stock/reservation rows and stock-sync outbox messages.

**After an adjustment** (admin)
- `Quantity` changes by the signed delta (if positive, row is materialized); movement
  appended; idempotency key stored; outbox fires; derivative updates.

**After a transfer** (complete)
- Source `Quantity` decreases, target `Quantity` increases (atomic, locked, same
  transaction); two movements (TransferOut/TransferIn); outbox fires for both warehouses;
  derivative stock changes on both sides.

**After a return** (resellable/damaged)
- Resellable qty returns to `Quantity` (or damaged to `DamagedQuantity`) only after
  current stock-ownership revalidation; `ReturnedQuantity`/`DamagedReturnedQuantity`
  increase; movement evidence is written by the CommerceHub return path; outbox fires.

**After a cancellation**
- Open reservations for the order are released (reserved→available); no `Quantity` change
  unless already committed.

---

## Common Failure Scenarios

1. **Stock differs between SQL and Storefront**
   - Cause: outbox lag, suppressed stale event, projection fence not satisfied, or Mongo
     `products` document missing the variant.
   - Investigate: `StockSyncOutboxMessages` for the variant/warehouse (published? failed?
     suppressed?). Check `ProductDocument.Variants` exists. Confirm `StockUpdatedAtUtc`
     advanced.
2. **Reservation stuck / not released**
   - Cause: cleanup skipped due to blocking payment status
     (`PendingResolution`/`RoutedToSuspense`/…), or order state not in a releasable set.
   - Investigate: `InventoryReservationCleanupJob` candidates + `Orders.PaymentStatus` /
     `CommerceHubPayments` status; unresolved webhooks.
3. **Negative stock**
   - Cause: should be impossible on certified paths; if seen, a raw SQL write or a path that
     bypassed `ValidateQuantityChange`/`FOR UPDATE`.
   - Investigate: `ReconciliationJob` `invalidStockRowCount`; recent movements; which code
     path wrote the row.
4. **Double reservation / oversell**
   - Cause: race outside locked path, or a path not using `ReserveAsync`/
     `ReserveOrderItemAsync`.
   - Investigate: `InventoryReservations` per order item; `CheckoutInventoryAuthorityGuard`
     re-check; whether `Serializable` was used.
5. **Projection stale / Search stale**
   - Cause: outbox worker not running, consumer retry exhausted, `EventVersion`/fence
     rejecting, or Mongo/ES down.
   - Investigate: outbox `IsFailed`/`AttemptCount`; consumer logs; `LastStockEventVersion`.
6. **Warehouse mismatch (stock in inactive warehouse still "available")**
   - Cause: reading raw undeleted rows instead of `WhereCurrentInventoryStockOwner`.
   - Investigate: warehouse `IsActive`; confirm the read path uses lineage filter.
7. **Variant mismatch (superseded variant still reservable)**
   - Cause: not filtering `SemanticLifecycleState == Current`.
   - Investigate: variant `SemanticSupersededAtUtc`/`SemanticReplacementVariantId`;
     `IsVariantReservableForInventoryAsync`.
8. **Inventory movement missing**
   - Cause: first distinguish expected absence from defect. Storefront/POS checkout
     repository reservation/release/commit paths do not require `StockMovement` rows;
     authority is in `ProductVariantStocks`, `InventoryReservations`, and
     `StockSyncOutboxMessages`. For movement-writing paths (admin adjustment, opening
     stock, transfer, CommerceHub reservation/commit/release/return, purchase/sales
     inventory effects), absence of an expected movement indicates the mutation path failed
     or bypassed the ledger.
   - Investigate: for checkout reservation bugs, start with reservation and stock rows;
     for movement-writing paths, check `StockMovements` for the reference id and the
     PostgreSQL append-only trigger/application guard.
9. **Transfer incomplete / partial**
   - Cause: completion failed mid-transaction (rolled back atomically) or was replayed on an
     already-Completed transfer (state-gated, idempotent).
   - Investigate: `StockTransfer.Status`; whether `CompleteTransfer` committed.

---

## Debugging Guide (where to look first)

1. Start at **SQL** (`ProductVariantStocks`): it is the authority. Confirm `Quantity`,
   `ReservedQuantity`, `AvailableQuantity`. If SQL is wrong, everything downstream is wrong.
2. Check **reservation tables** (`InventoryReservations`): are there active reservations
   covering the gap? Is `ExpiresAtUtc` passed? Is `RemainingReservedQuantity` correct?
3. Check **`StockMovements`** when the path is expected to write movement evidence
   (adjustment, opening stock, transfer, CommerceHub reservation/commit/release/return,
   purchase/sales inventory effects). For storefront/POS checkout repository reservation
   bugs, a missing movement row is not by itself a defect; inspect `InventoryReservations`,
   `ProductVariantStocks`, and `StockSyncOutboxMessages` first.
4. Check **events / outbox** (`StockSyncOutboxMessages`): was the change published? Failed?
   Suppressed by lineage? Is `EventVersion`/`StockUpdatedAtUtc` ordered correctly?
5. Check **projections** (Mongo `products`, ES, CommerceHub): do they lag or reject due to
   `EventVersion`/fence? Are they missing the variant?
6. Check **cache (Redis)**: only a hint — never trust it as authority; confirm SQL first.
7. Check **CommerceHub / payment state** for stuck reservations: `Orders.PaymentStatus`,
   `CommerceHubPayments`, unresolved webhooks, reconciliation job output.
8. Check **lineage**: is the Warehouse active, the Variant current semantic owner, the
   Product active? If any link is broken, inventory "disappears" from authority even though
   SQL rows remain.

---

## Related Knowledge

Core Guides (`.ai-context/`):
- `CROSS_SYSTEM_AUTHORITY_HARDENING.md` — inventory/warehouse/reservation authority rules.
- `ARCHITECTURE_TRUTH.md` — SQL-authoritative system of record statement.
- `KNOWN_RISKS.md` — inventory risk debt.
- `PROJECT_CONTEXT.md` — multi-tenant inventory overview.
- `CODEX_RULES.md` — do-not-bypass rules for inventory paths.

System Guides (`docs/architecture/`):
- `product-inventory-reservation-certification-findings.md`
- `pos-warehouse-inventory-authority-certification-findings.md`
- `marketplace-variant-inventory-certification-findings.md`

Related Capabilities:
- Checkout / Order lifecycle, Payment lifecycle, CommerceHub marketplace, Storefront
  projection, Search indexing, Product/Variant catalog, Warehouse management, Returns,
  Fulfillment.

Related Domains:
- Product (`Product`, `ProductVariant`), Warehouse, Order (`Order`, `OrderItem`), Payment
  (`PaymentIntent`, `CommerceHubPayments`), Projection (Mongo/ES/CommerceHub consumers),
  Reliability (`ProductVariantInventoryLineageAuthority`, `ProjectionEventFence`).

---

## Documentation Validation (implementation vs docs)

Findings (implementation-backed; only discrepancies/risks reported — not a rewrite):

1. **`ProductVariantStockChangedEvent` doc comments are stale.** The class comment still
   says "Emitted but NOT projected to Mongo yet (Phase 11.3)" while `ProductStockChangedConsumer`
   already consumes it and updates Mongo. Implementation is ahead of the comment.
2. **`StockMovement` / `InventoryEvents.cs` domain events
   (`StockAdjustedEvent`, `StockTransferredEvent`, etc.) are defined but the live mutation
   path does NOT publish them** — it publishes `ProductVariantStockChangedEvent` via the
   outbox. The older `InventoryEvents` appear to be legacy/notification events not on the
   authoritative sync path. Risk: a reader may assume those events drive projections; they
   do not.
3. **Admin inventory help article** (`PointOfSale.Web/src/admin/help/helpArticles/inventory-balance.md`)
   was not reviewed for accuracy against variant-centric stock; given the PHASE 11.5 rewrite
   (Product no longer holds stock, WarehouseStock is legacy), any article describing
   product-level or `WarehouseStock`-based balances is now incorrect. Flag for review.
4. **`InventoryService.AdjustStockAsync` is a "staged" mutation** — it returns a `StockMovement`
   but does NOT own the transaction/commit/outbox; callers must. `AdminInventoryService`
   and `StockTransfersController` wrap it correctly. Risk: a new caller using
   `AdjustStockAsync` without a transaction will hit `EnsureMutationTransaction` and fail.
   This contract is implicit and easy to misuse.
5. **`AddInventoryCommittedToPaymentIntent.sql`** (repo root) and `add_inventory_committed_to_payment_intent.sql`
   suggest a payment-intent ↔ inventory-commit linkage outside the core services; verify it
   is wired and not orphaned SQL.
6. **Hidden behavior — outbox per-(tenant,variant,warehouse) ordering with PG advisory lock**:
   a newer stock event for the same variant/warehouse will be *deferred* until an older
   unpublished event dispatches (`HasUnpublishedEarlierVariantEventAsync`). This means a
   "missing" derivative update may simply be waiting on an earlier failed event; it is not a
   bug. Not obvious from the event surface.
7. **Hidden behavior — cleanup job "blocking unresolved payment statuses"**:
   `PendingResolution`, `PendingTenantResolution`, `RoutedToSuspense`,
   `SuspenseMatchedPaymentPendingClearance` keep a reservation alive even after `ExpiresAtUtc`.
   Operators may see "stuck" reserved stock that is intentionally held for suspense
   reconciliation.
8. **Hidden behavior — `AvailableQuantity` excludes `DamagedQuantity` semantics correctly
   (`Quantity - Reserved`), but `TotalQuantity = Quantity + DamagedQuantity`**: damaged stock
   is physically present but never sellable and never reserved. Some dashboards that sum
   `Quantity` as "on hand" must decide whether to include damaged.
9. **Authority gap noted by certification but NOT closed**: no universal warehouse
   reassignment / bulk stock migration framework exists. Transfer is item-level only;
   physical reassignment at scale needs path-specific certification (documented debt in
   `pos-warehouse-inventory-authority-certification-findings.md`).
10. **Test-harness debt**: `InventoryServiceTests` fixtures are stale (lineage) and some
    SQLite transfer controller tests fail on PostgreSQL-specific constraints before reaching
    hardened paths. These are test failures, not production inventory defects — but they can
    mask real regressions if treated as green.

---

## Summary of discoveries
- Inventory is a **variant + warehouse** balance system; SQL `ProductVariantStocks` is the
  sole authority, `WarehouseStock` is legacy.
- Stock is mutated only inside explicit transactions, under pessimistic row locks, with
  deterministic lock ordering and post-lock negative-stock validation.
- A full **lineage gate** (Warehouse→Variant→Product current ownership) determines whether a
  stock row / reservation / event is *authority* or merely *evidence*.
- Reservations are cumulative-settled (reserved→committed→released→returned) and the cleanup
  job fails closed, deferring to payment lifecycle for blocking suspense states.
- All derivative sync flows through a durable **stock-sync outbox** with ordering,
  suppression, and terminal-failure compensation; Mongo/ES/CommerceHub are projections.
- `StockMovements` is append-only (app + PostgreSQL trigger) where movement rows are part
  of the path; storefront/POS checkout repository reservation authority is not universally
  represented as movement rows. Adjustments are durably idempotent.

## Business rules discovered
(See Business Rules section — 20 rules. Highlights: no negative stock, no over-reservation,
derived available, explicit tx + row lock + deterministic ordering, lineage gate, durable
idempotency, append-only ledger, one-time opening stock, backorder/non-tracked skip, stale
reservation cleanup fails closed, transfer validation.)

## Cross-system dependencies discovered
- Downstream of every SQL mutation: `StockSyncOutboxMessages` → MassTransit → Mongo
  `products` / Elasticsearch / CommerceHub projection / affiliate projection.
- Checkout/Payment gate reservation release (suspense-aware cleanup).
- CommerceHub reservation/commit/release/return reuse the same SQL authority + lineage.
- Product/Variant deletion or supersession, and warehouse deactivation, collapse stock
  authority via the lineage gate.
- Pricing, accounting, identity are NOT affected by inventory mutations.

## Documentation inconsistencies found
1. Stale `ProductVariantStockChangedEvent` "not projected to Mongo yet" comment (it is).
2. `InventoryEvents.cs` domain events (`StockAdjustedEvent` etc.) are not on the authoritative
   sync path — risk of misunderstanding.
3. Admin `inventory-balance.md` help article likely stale after variant-centric rewrite.
4. `InventoryService.AdjustStockAsync` staged-mutation contract (no own tx) is implicit and
   easy to misuse.
5. Orphaned-looking `add_inventory_committed_to_payment_intent.sql` at repo root.
6. Certifications document remaining debt (no bulk warehouse reassignment framework; stale
   `InventoryServiceTests` fixtures; SQLite transfer test failures).

## Hidden implementation behavior found
- Outbox defers newer events behind older unpublished ones per (tenant,variant,warehouse)
  via PG advisory lock + canonical ordering.
- Cleanup job intentionally holds reservations alive for suspense/`PendingResolution`/…
  payment states.
- `TotalQuantity = Quantity + DamagedQuantity`; damaged stock is never sellable/reservable.
- Opening stock is one-time and rejects any prior balance/movement.
- Mongo product `StockQuantity` is an aggregate of active variants' projected available
  sellable stock (`Quantity - ReservedQuantity`), gated by `StockUpdatedAtUtc` /
  `EventVersion` and projection fences.

## Confirmation
- Only this file was created:
  `.ai/hermes/capabilities/pos-admin/inventory-management.md`
- No other files were modified.
- No memory writes occurred.
- No code was generated, refactored, or implemented.
