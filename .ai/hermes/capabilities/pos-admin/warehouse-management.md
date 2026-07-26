# Warehouse Management (POS Admin ERP)

Reverse-engineered from implementation. Implementation is the source of truth;
documentation notes are reported only in the Documentation Validation section.

Scope: ERP Warehouses used by POS Admin (`PointOfSale` / `PointOfSale.API`).
Out of scope (NOT investigated): CommerceHub warehouses, Marketplace fulfillment
centers, Carrier warehouses, shipping providers, external WMS.

Key mental model:
  Warehouse owns LOCATION.
  Inventory (ProductVariantStock) owns QUANTITY.
  InventoryReservation owns per-warehouse RESERVATIONS.
  StockMovement owns the append-only audit of every mutation.

==========================================================
# Purpose
==========================================================

Warehouse Management exists to give POS Admin a tenant-scoped set of physical
inventory locations that anchor where stock physically sits. A Warehouse is an
internal ERP inventory authority: it is the spatial anchor for variant stock,
stock movements, reservations, and inter-warehouse transfers.

Business problem solved:
- Multi-location inventory: the same product variant can hold stock in different
  warehouses within one tenant.
- Operational traceability: every quantity change (purchase, sale, return,
  adjustment, transfer) is tied to a specific Warehouse + Variant.
- Controlled movement of goods between locations with approval gating.

What Warehouse owns:
- Location identity (Id, Name, Code, Address, Phone).
- Lifecycle state (IsActive, IsDefault).
- Tenant ownership (TenantId, NOT NULL — enforced at DB level).

What Warehouse does NOT own:
- Quantities. Those live in ProductVariantStock (authority).
- Reservations. Those live in InventoryReservation (authority), per warehouse.
- Money/accounting. Warehouse mutations are not ledger entries.
- Product/variant definitions. Those live in Product / ProductVariant.

==========================================================
# Business Scope
==========================================================

Reverse-engineered entities and behaviors belonging to Warehouse Management:

Warehouse (Core/Entities/Warehouse.cs)
  - Tenant-scoped (ITenantOwned, TenantId NOT NULL).
  - Name (required, max 150).
  - Code (nullable, max 50) — free text label, NOT a unique business key.
  - Address (nullable, max 300).
  - Phone (nullable, max 32).
  - IsActive (bool, default true). Inactive warehouses are hidden from the
    warehouse list and cannot receive opening stock, adjustment, reservation,
    or transfer mutations.
  - IsDefault (bool). Exists as a flag; no DB unique filter enforcing a single
    default per tenant was found in the scanned configuration (see Documentation
    Validation). Multiple warehouses can carry IsDefault=true in practice.
  - BaseEntity provides IsDeleted + global query filter (!IsDeleted && TenantId).

Warehouse Code
  - Free-text label on the Warehouse row. Not an authoritative lookup key.

Warehouse Name
  - Human label. Unique-enforced only by application validation (CreateWarehouse
    Name is [Required]); no DB unique constraint found.

Warehouse Status
  - Modeled as a boolean IsActive, NOT a multi-state enum. States: Active
    (IsActive=true), Inactive (IsActive=false). See Warehouse State Machine.

Warehouse Address / Phone
  - Optional descriptive metadata. No geocoding/authority behavior in scope.

Warehouse Type / Hierarchy
  - NOT implemented. No Main/Branch/Parent-Warehouse/Child-Warehouse fields or
    relationships exist on the Warehouse entity. There is no warehouse hierarchy.

Default Warehouse / Main Warehouse / Branch Warehouse
  - Only a single IsDefault boolean exists. No "Main" or "Branch" classification
    is implemented. Do not document a hierarchy that does not exist.

Warehouse Notes
  - No dedicated Notes field on Warehouse. Notes live on StockMovement and
    StockTransfer/StockTransferItem (movement/transfer reason), not on Warehouse.

Warehouse Activation
  - Via PUT /api/warehouses/{id} setting IsActive. Inactive blocks all current
    inventory mutation paths (opening stock, adjustment, reservation, transfer).

Warehouse Deletion
  - NO controller delete endpoint exists (no HttpDelete in WarehousesController).
    Soft-delete is available at framework level (BaseEntity.IsDeleted + global
    query filter) but is not exposed operationally for Warehouse. The
    WarehouseStock/StockMovement query filters also honor IsDeleted, but there is
    no Warehouse DELETE path in the API. Treat "delete" as: deactivate
    (IsActive=false); hard/soft deletion is not an implemented operation.

Warehouse Transfers
  - TWO distinct mechanisms exist (see Warehouse Transfers section):
    1. Direct ad-hoc transfer: POST /api/warehouses/transfer-stock
       (WarehousesController.TransferStock) — immediate mutate, no approval.
    2. Structured transfer lifecycle: POST /api/stocktransfers + approve/complete
       (StockTransfersController) — Pending -> Approved -> Completed.

Warehouse Capacity
  - NOT implemented. No capacity/min/max fields on Warehouse or stock.

Warehouse Permissions
  - Endpoints that mutate (Create, Update, Adjust, Transfer, Opening Stock,
    Transfer lifecycle) require roles Owner, Admin, or Manager.
  - Read endpoints (GET) require only [Authorize].

==========================================================
# AUTHORITY BOUNDARIES
==========================================================

WHO OWNS WHAT

Warehouse Truth
  - PostgreSQL "Warehouses" table = authority for location identity + lifecycle
    (Name/Code/Address/Phone/IsActive/IsDefault/TenantId).

Warehouse Metadata
  - Same: Warehouses table columns. Not derived from any projection.

Warehouse Status (Active/Inactive)
  - Warehouses.IsActive (DB authority).

Warehouse Availability (can it receive stock?)
  - Derived from Warehouses.IsActive AND the variant lineage check in
    ProductVariantInventoryLineageAuthority.RequireCurrentStockMutationOwnerAsync
    (warehouse must be active, same tenant, not deleted).

Inventory Quantities
  - PostgreSQL "ProductVariantStocks" = authority (Quantity, ReservedQuantity,
    DamagedQuantity). One row per (TenantId, VariantId, WarehouseId).
  - LEGACY "WarehouseStocks" table is explicitly NOT inventory authority
    (stated in ProductVariantStock.cs header). It is a compatibility table only.

Reservations
  - PostgreSQL "InventoryReservations" = authority. Each reservation is bound to
    a specific VariantId + WarehouseId + TenantId (and an Order/OrderItem).

Stock Movements
  - PostgreSQL "StockMovements" = append-only audit authority. Application
    rejects Update/Delete on StockMovement; the production hardening certification
    states a PostgreSQL trigger also enforces append-only across direct SQL.

Transfers
  - PostgreSQL "StockTransfers" + "StockTransferItems" = transfer document
    authority (state machine). Mutations applied during Complete mutate
    ProductVariantStocks atomically.

Accounting
  - Warehouse mutations do NOT post to ledger. Accounting is a separate authority
    (CommerceAccountingService / AccountingOutbox). No Warehouse->Ledger coupling
    in the inspected mutation paths.

Analytics
  - Derived/downstream. Not authority.

Notifications
  - Not triggered by Warehouse mutations in the inspected paths.

DISTINGUISH

Authority
  - Warehouses, ProductVariantStocks, InventoryReservations, StockMovements,
    StockTransfers (PostgreSQL, tenant-scoped, constraint-guarded).

Projection
  - Redis product cache (ProductCacheService invalidated on
    ProductVariantStockChangedEvent), and any read models fed by
    StockSyncOutboxMessage. NOT authority.

Evidence
  - StockSyncOutboxMessage rows, MassTransit events, InventoryEvents. They are
    proof of what happened; they do not decide current state.

Presentation
  - Frontend warehouse screens, DTOs, Swagger. Never authoritative.

EXPLICIT
  Warehouse owns location.
  Inventory (ProductVariantStock) owns quantity.

==========================================================
# WAREHOUSE LIFECYCLE
==========================================================

Reverse-engineered lifecycle (Create -> ... -> Deactivate):

Create (POST /api/warehouses)
  - Requires Owner/Admin/Manager. TenantId bound from ITenantContext.RequiredTenantId.
  - New warehouse defaults IsActive=true. Persisted via SaveChangesAsync.

Active
  - Default state. Visible in GET /api/warehouses (which filters IsActive).
  - Eligible to receive opening stock, adjustments, reservations, transfers.

Used by Opening Stock (POST /api/warehouses/opening-stock)
  - Variant-centric initial balance load. One-time per (variant, warehouse).

Used by Inventory (ProductVariantStock materialized in this warehouse)
  - Purchases, Sales, Returns, Adjustments, Transfers read/write this warehouse's
    ProductVariantStock rows.

Used by Purchases (POST /api/purchases)
  - Purchase invoice references WarehouseId; receipt increments
    ProductVariantStock for that warehouse (createIfMissing: true).

Used by Sales (POST /api/sales)
  - Sale references WarehouseId; dispatch decrements that warehouse's
    ProductVariantStock (validates AvailableQuantity). Reservations claimed
    against a specific warehouse.

Transferred (two mechanisms, see Warehouse Transfers)
  - Source decremented, target incremented.

Inactive (PUT IsActive=false)
  - Hidden from GET list. Blocks new opening stock / adjustment / reservation /
    transfer mutations via lineage authority check.

Deleted
  - Not implemented as an API operation. Soft-delete column exists but no
    controller path. Effective "removal" = set Inactive.

==========================================================
# IMPLEMENTATION DISCOVERY
==========================================================

FRONTEND
  - PointOfSale.Web warehouse screens are operational surfaces only. They call
    the Warehouses and StockTransfers endpoints. Not authoritative.
  - Variant-centric stock/opening-stock UI maps to ProductVariantStocks.

CONTROLLERS (PointOfSale.API/Controllers)
  WarehousesController.cs
    - GET    /api/warehouses                (active only)
    - GET    /api/warehouses/{id}
    - POST   /api/warehouses                (Owner/Admin/Manager) CreateWarehouse
    - PUT    /api/warehouses/{id}           (Owner/Admin/Manager) UpdateWarehouse
    - GET    /api/warehouses/{id}/stock     GetWarehouseStock (variant-centric via
                                            ProductVariantStocks)
    - POST   /api/warehouses/adjust-stock    (Owner/Admin/Manager) AdjustStock
                                            (durable idempotency)
    - POST   /api/warehouses/transfer-stock  (Owner/Admin/Manager) TransferStock
                                            (direct ad-hoc, durable idempotency)
    - GET    /api/warehouses/opening-stock   GetOpeningStock (variant-centric)
    - POST   /api/warehouses/opening-stock   (Owner/Admin/Manager) SaveOpeningStock
  StockTransfersController.cs
    - POST   /api/stocktransfers             (Owner/Admin/Manager) CreateTransfer
                                            -> Pending
    - POST   /api/stocktransfers/{id}/approve (Owner/Admin/Manager) -> Approved
    - POST   /api/stocktransfers/{id}/reject  (Owner/Admin/Manager) -> Rejected
    - POST   /api/stocktransfers/{id}/complete (Owner/Admin/Manager) -> Completed
                                            (mutates stock)
    - GET    /api/stocktransfers
    - GET    /api/stocktransfers/{id}
  SalesController.cs / PurchasesController.cs
    - Consume request.WarehouseId; drive ProductVariantStock via the shared
      mutation engine.

ENDPOINTS SUMMARY
  - Warehouse CRUD + stock read + ad-hoc adjust/transfer + opening stock.
  - Structured StockTransfer lifecycle is a SEPARATE controller/route set.

APPLICATION SERVICES
  PointOfSale.API/Services/InventoryService.cs (IInventoryService)
    - AdjustStockAsync(...)   : variant resolve -> lock row -> validate non-negative
                                -> mutate Quantity -> write StockMovement(type=Adjustment).
    - TransferStockAsync(...) : validate qty>0 & different warehouses -> lock both
                                rows (source must exist, target createIfMissing) ->
                                validate source non-negative -> decrement source,
                                increment target -> write TransferOut + TransferIn
                                StockMovements.
    - GetStockLevelsAsync(...) : reads ProductVariantStocks (variant-centric).
    - SaveOpeningStockAsync(...) : tenant-scoped, positive quantities only, no
                                duplicate variants, warehouse must be active,
                                ONE-TIME (rejects if prior inventory authority
                                exists), writes OpeningBalance movement.
    - GetStockMovementsAsync(...) : returns StockMovements for a warehouse.

REPOSITORIES
  PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs
    - ReserveAsync / ReleaseAsync / CommitAsync / ReturnAsync operate on
      ProductVariantStock.ReservedQuantity for a SPECIFIC (tenant, variant,
      warehouse). Bound by AvailableQuantity and lineage authority.

ENTITIES (PointOfSale.Core/Entities)
  - Warehouse, WarehouseStock (legacy), ProductVariantStock (authority),
    StockMovement, InventoryReservation, StockTransfer, StockTransferItem,
    StockTransferRequestIdempotency.

DATABASE TABLES
  - Warehouses, WarehouseStocks (legacy compat), ProductVariantStocks (authority),
    StockMovements (append-only), InventoryReservations, StockTransfers,
    StockTransferItems, StockTransferRequestIdempotency.
  - Global query filters: Warehouse (!IsDeleted && TenantId), WarehouseStock,
    StockMovement.

INVENTORY INTEGRATION
  - All mutations go through ProductVariantStockMutationSupport + InventoryService.
  - ProductVariantStock unique key (TenantId, VariantId, WarehouseId) prevents
    duplicate stock rows. SQL CHECK constraints:
      Quantity >= 0, ReservedQuantity >= 0, DamagedQuantity >= 0,
      Quantity >= ReservedQuantity.
  - AvailableQuantity = Quantity - ReservedQuantity (never client-submitted).

PURCHASE INTEGRATION
  - Purchase invoice carries WarehouseId. Receipt locks target warehouse stock
    rows (createIfMissing: true) and increments Quantity.

SALES INTEGRATION
  - Sale carries WarehouseId. Dispatch decrements Quantity after validating
    AvailableQuantity; reservations are claimed against the specific warehouse.

TRANSFER INTEGRATION
  - See Warehouse Transfers. Both mechanisms mutate ProductVariantStocks in one
    transaction and write StockMovements.

RESERVATION INTEGRATION
  - InventoryReservation.WarehouseId binds the reservation to one warehouse.
  - Reservation claim/release/commit are transactionally bound with order creation
    (Admin SalesController) and storefront checkout (CheckoutService /
    CommerceHubCheckoutService), each with durable idempotency.

ACCOUNTING
  - No Warehouse->ledger posting in inspected mutation paths.

MONGO
  - Not used as Warehouse authority. (CommerceHub/marketplace read models are out
    of scope.)

REDIS
  - ProductCacheService listens for ProductVariantStockChangedEvent (published via
    StockSyncOutbox) and invalidates product cache. Cache is a projection, not
    authority.

MASSTRANSIT / OUTBOX
  - StockSyncOutboxMessage (PointOfSale.Core/Entities/StockSyncOutboxMessage) is
    staged inside the mutation transaction, then published by
    StockSyncOutboxWorker -> MassTransit. Event type: ProductVariantStockChangedEvent.
  - Tenant-scoped; unique per (TenantId, VariantId, WarehouseId, EventVersion).
  - CHECK constraints: EventVersion > 0, WarehouseId not empty GUID,
    OccurredAtUtc/StockUpdatedAtUtc required.

ELASTICSEARCH
  - Not a Warehouse authority in inspected paths.

ANALYTICS
  - Downstream/derived; no authority relationship.

BACKGROUND JOBS
  - StockSyncOutboxWorker: publishes committed stock events.
  - InventoryReservationCleanupJob: expires stale reservations (ExpiresAtUtc),
    affecting reserved quantities in ProductVariantStock.

==========================================================
# BUSINESS RULES (implementation-backed)
==========================================================

1. Tenant isolation: Warehouse.TenantId NOT NULL; all queries filter by
   RequiredTenantId; cross-tenant warehouse/variant/stock ids fail lineage checks.

2. Role gating: All mutating warehouse + transfer endpoints require
   Owner, Admin, or Manager.

3. Inactive warehouse blocks mutation: opening stock, adjustment, reservation,
   and transfer all require an active current warehouse via lineage authority.

4. Non-negative stock: SQL CHECK constraints + ValidateQuantityChange/
   ValidateAvailableQuantity reject any result below zero. Available can never
   exceed physical (derived).

5. Opening stock is one-time: SaveOpeningStockAsync rejects if any prior inventory
   authority exists (Quantity/Reserved/Damaged != 0 or any StockMovement exists
   for that variant+warehouse). Positive quantities only; no duplicate variant per
   request.

6. Direct adjustment requires durable idempotency: Idempotency-Key or X-Request-Id
   required; fingerprint conflict -> 409; completed response replays without a
   second mutation. Negative-result adjustments fail before commit.

7. Transfer validation (both mechanisms): quantity must be > 0; source != target
   warehouse; both warehouses must exist and be active within the tenant; variant
   must be active and same-tenant.

8. Structured transfer lifecycle: create -> Pending; can only Approve from Pending;
   can only Complete from Approved (re-validates stock inside the mutation tx);
   Reject from Pending; Cancelled state exists in enum but no controller path sets
   it. Completion locks transfer state + stock rows deterministically; concurrent/
   replayed completion cannot double-apply.

9. Idempotency for transfers: StockTransferRequestIdempotency keyed by
   (TenantId, Key, Operation); ON CONFLICT DO NOTHING (Postgres) / NOT EXISTS
   (SqlServer).

10. Append-only movements: StockMovements cannot be updated/deleted by the app;
    hardening adds a PostgreSQL trigger enforcing the same.

11. Deterministic lock ordering: multi-row locks acquired (WarehouseId, VariantId)
    to prevent deadlocks.

12. Reservation cannot oversell: reserve bounded by locked SQL AvailableQuantity;
    cumulative commit/release prevents double-consume.

13. Lineage authority: current warehouse must be active + not deleted + same tenant;
    variant must be current (not deleted, active, not superseded) for any mutation
    or reservation consumption.

Fail-closed: negative stock, over-reservation, inactive warehouse, cross-tenant,
replayed keys, and same-warehouse transfer all fail closed (rejected/409).

==========================================================
# WAREHOUSE STATE MACHINE
==========================================================

Warehouse lifecycle is a BOOLEAN, not a rich enum:

  Created (IsActive=true)
     |
     v
  Active  <--------------------+
     |                          |
     | PUT IsActive=false       | PUT IsActive=true
     v                          |
  Inactive ---------------------+

  Deleted: not an implemented state (no controller delete).

StockTransfer (separate entity) state machine (TransferStatus enum):
  Pending -> Approved -> Completed
  Pending -> Rejected
  (Cancelled exists in enum; no controller path sets it.)

  Transitions enforced: Approve only from Pending; Reject only from Pending;
  Complete only from Approved.

==========================================================
# WAREHOUSE + INVENTORY RELATIONSHIP
==========================================================

Required chain:

  Warehouse (location)
     |
     v  (1 Warehouse : many ProductVariantStock rows)
  ProductVariantStock  (quantity authority, per Tenant+Variant+Warehouse)
     |
     v  (1 Variant : many Stock rows across warehouses)
  ProductVariant  (sellable unit that owns stock)
     |
     v  (many Variants : 1 Product)
  Product
     |
     v  (Stock mutation writes audit)
  StockMovement  (append-only, per Warehouse)
     |
     v  (reservation claims available stock in a warehouse)
  InventoryReservation  (per Warehouse + Variant + OrderItem)

WHO OWNS WHAT
  - Warehouse owns location/identity/lifecycle.
  - ProductVariantStock owns quantity/reserved/damaged (per warehouse).
  - InventoryReservation owns the reservation of a warehouse's available stock.
  - StockMovement owns the immutable record of each change.
  - Product/ProductVariant own the catalog definition (not quantity).

CRITICAL HIDDEN DETAIL: there are TWO stock tables.
  - ProductVariantStock = THE authority (variant-centric).
  - WarehouseStock = legacy compatibility table, explicitly NOT authority.
  - The controller's GET /api/warehouses/{id}/stock and opening-stock endpoints
    read ProductVariantStocks (variant-centric, correct).
  - BUT the controller's map of WarehouseDetailDto.Stock still reads from the
    legacy WarehouseStock navigation (Warehouse.Stock). This is an internal
    inconsistency: the detail DTO exposes legacy-stock quantities while the
    stock endpoint exposes authoritative variant stock. See Documentation Validation.

Variant-centric rule: "Inventory is Variant + Warehouse ONLY. Products never hold
stock directly." Adjustments/transfers default to the variant marked IsDefault when
no ProductVariantId is supplied.

==========================================================
# WAREHOUSE TRANSFERS
==================================================

TWO transfer mechanisms are implemented. Do not conflate them.

MECHANISM A — Direct ad-hoc transfer
  Endpoint: POST /api/warehouses/transfer-stock (WarehousesController.TransferStock)
  - Immediate: decrements source ProductVariantStock, increments target, writes
    TransferOut + TransferIn StockMovements, in ONE transaction.
  - No approval step. Requires Owner/Admin/Manager.
  - Durable idempotency (Idempotency-Key / X-Request-Id; fingerprint conflict 409).
  - Validations: qty>0, source != target, both warehouses active + same tenant,
    variant active + same tenant. Source stock must already exist.
  - Publishes ProductVariantStockChangedEvent via StockSyncOutbox.

MECHANISM B — Structured transfer lifecycle
  Endpoints: POST /api/stocktransfers (+ approve / reject / complete)
  Entity: StockTransfer (TenantId, TransferNumber, FromWarehouseId,
          ToWarehouseId, Status, RequestedById, ApprovedById, CompletedById, Items)
  - Create -> Pending (validates items, qty>0, warehouses active+same-tenant,
    variant active; locks source for advisory availability check).
  - Approve -> Approved (Owner/Admin/Manager).
  - Reject  -> Rejected (from Pending).
  - Complete -> Completed (from Approved): for each item, locks source+target in
    deterministic order, calls TransferStockAsync, sets transfer Completed,
    publishes stock events. All balance + movement writes are one transaction.
  - Idempotency via StockTransferRequestIdempotency (TenantId, Key, Operation).

Source Warehouse: where stock is removed.
Destination Warehouse: where stock is added.
Reservation: transfers do NOT reserve; availability is validated at create (advisory)
and re-validated at complete (authoritative) inside the mutation transaction.
Movement: TransferOut (source) + TransferIn (target) StockMovements.
Inventory Mutation: source Quantity-=qty, target Quantity+=qty on ProductVariantStock.
Accounting: none in transfer paths.
Validation: positive qty, distinct active same-tenant warehouses, current variant.
Concurrency: deterministic lock order (WarehouseId, VariantId) + single transaction;
replayed/ concurrent completion cannot double-apply (state gate + row locks).
Lifecycle: Mechanism A is instant; Mechanism B is Pending->Approved->Completed.

==========================================================
# CROSS-SYSTEM EFFECTS
==========================================================

Inventory
  - Every warehouse mutation changes ProductVariantStock; StockMovement records it.
  - StockSyncOutbox publishes ProductVariantStockChangedEvent -> Redis cache
    invalidation + downstream projections.

Purchases
  - Purchase invoice WarehouseId anchors receipt stock increments in that warehouse
    (createIfMissing). No effect on other warehouses.

Purchase Returns
  - Return reverts stock to the original invoice's warehouse (compensating movement,
    not deletion).

Sales
  - Sale WarehouseId anchors dispatch decrement in that warehouse; reservations are
    claimed against the specific warehouse.

Sales Returns
  - Return reverts stock to the original warehouse via compensating movement.

Orders
  - Admin order creation claims durable idempotency, then commits order +
    reservations (warehouse-bound) transactionally.

Checkout (Storefront / CommerceHub)
  - CheckoutService / CommerceHubCheckoutService reserve against specific
    warehouse stock using serializable transactions, deterministic locking, and
    bounded cumulative settlement.

Pricing / Products
  - Warehouse changes do NOT alter pricing or product definitions. Product cache
    invalidation only refreshes stock-derived availability.

Accounting
  - No direct Warehouse->ledger coupling in inspected mutation paths.

Analytics
  - Downstream/derived only.

CommerceHub / Affiliate / Storefront
  - Consume warehouse stock availability via the same ProductVariantStock authority
    and reservation engine. They are consumers, not owners, of warehouse stock.

Notifications
  - Not triggered by warehouse mutations in inspected paths.

WHAT DOES NOT CHANGE
  - Warehouse mutations do not change product definitions, pricing, tenant config,
    or post ledger entries directly.

==========================================================
# VALIDATION CHECKLIST
==========================================================

After warehouse created
  - Row exists in Warehouses with TenantId, IsActive=true. Appears in GET list.

After activation (IsActive flip)
  - Inactive: hidden from GET list; cannot receive opening stock / adjustment /
    reservation / transfer (lineage check fails). Re-activate to restore.

After opening stock saved
  - ProductVariantStock row exists for each variant+warehouse with Quantity set;
    OpeningBalance StockMovement written; event published. Second attempt rejected.

After direct adjustment
  - ProductVariantStock.Quantity changed; Adjustment StockMovement written;
    idempotency record stored (replay returns same 200).

After structured transfer created
  - StockTransfer Status=Pending; no stock moved yet; TransferNumber generated.

After transfer approved
  - Status=Approved; still no stock moved.

After transfer completed
  - Source Quantity-=qty, target Quantity+=qty; TransferOut+TransferIn movements;
    Status=Completed; events published.

After purchase
  - Target warehouse ProductVariantStock incremented (createIfMissing); Purchase
    StockMovement written.

After sale
  - Warehouse ProductVariantStock decremented; AvailableQuantity reduced; Sale
    movement; reservations committed/released.

After reservation
  - InventoryReservation row with WarehouseId; ProductVariantStock.ReservedQuantity
    increased; AvailableQuantity reduced.

After deactivation / "delete"
  - IsActive=false (no API delete). New mutations blocked; historical movements kept.

==========================================================
# COMMON FAILURE SCENARIOS
==========================================================

Warehouse not found
  - GET returns 404 when id missing or not in tenant. Verify TenantId match and
    that the warehouse is not soft-deleted.

Inactive warehouse
  - Opening stock / adjustment / reservation / transfer rejected. Cause: IsActive=false.
  - Investigation: read Warehouses.IsActive for the id.

Transfer failure (same warehouse)
  - Both mechanisms reject source==target. Check FromWarehouseId != ToWarehouseId.

Transfer failure (insufficient stock)
  - CompleteTransfer (and direct transfer) throws InsufficientStockException when
    source available < requested. Check ProductVariantStock.AvailableQuantity.

Reservation conflict / oversell
  - ReserveAsync bounded by locked AvailableQuantity; if unavailable, partial or
    full rejection. Check ReservedQuantity vs Quantity and active reservations.

Negative stock
  - Blocked by SQL CHECK + ValidateQuantityChange. If seen, suspect direct SQL
    bypass or missing migration.

Delete blocked / no delete
  - There is NO warehouse delete endpoint. "Delete" must be deactivate. If a
    process expects a delete, it is misconfigured.

Wrong warehouse
  - Route id does not establish ownership; tenant/lineage checks enforce it. A
    404 or lineage rejection indicates cross-tenant or inactive warehouse.

Concurrency conflict
  - Row locks + deterministic ordering + idempotency keys prevent drift. If a
    duplicate mutation slipped through, check idempotency key reuse / fingerprint
    mismatch (returns 409).

INVESTIGATION ORDER (Hermes should check first)
  1. Warehouses table: existence, TenantId, IsActive, IsDeleted.
  2. ProductVariantStock for (TenantId, VariantId, WarehouseId): Quantity,
     ReservedQuantity, DamagedQuantity, AvailableQuantity.
  3. StockMovements for the warehouse/variant: confirm history (append-only).
  4. InventoryReservations for the warehouse/variant: Reserved/Committed/Released.
  5. StockTransfers + Items + Status (for transfer issues).
  6. StockSyncOutboxMessage: confirm event published (downstream propagation).
  7. StockTransferRequestIdempotency / adjustment idempotency records (replay).

==========================================================
# DEBUGGING GUIDE
==========================================================

If a Warehouse bug arrives:

Start at the Warehouse entity + table
  - Confirm the warehouse exists for the tenant, IsActive, not IsDeleted.
  - A missing/inactive warehouse explains most "not found" / "blocked" reports.

When to check Inventory (ProductVariantStock)
  - Wrong quantity, negative stock, availability mismatch. This is the authority.
  - Check the unique (TenantId, VariantId, WarehouseId) row; verify CHECK
    constraints.

When to check Stock
  - For any quantity question, always go to ProductVariantStock, NOT WarehouseStock
    (legacy). WarehouseStock is compatibility-only.

When to check Reservation (InventoryReservation)
  - Oversell, "available but can't sell", stuck reserved quantity. Verify
    ReservedQuantity, Status, ExpiresAtUtc, and lineage (warehouse active + variant
    current).

When to check Movement (StockMovement)
  - Audit/trace a quantity change. Append-only; never edited. Compare QuantityAfter
    across entries to reconstruct balance.

When to check Transfer (StockTransfer / StockTransferItem)
  - Pending-but-not-completed, or duplicate/partial transfer. Inspect Status
    transitions and the per-item movements created at Complete.

When to check Accounting
  - Usually NOT relevant: warehouse mutations do not post ledger entries. Only
    investigate accounting if a financial discrepancy is reported alongside a
    warehouse operation (e.g., purchase receipt) — then the purchase/sale lifecycle,
    not Warehouse, owns the ledger coupling.

When to check Outbox (StockSyncOutboxMessage / Redis)
  - If a quantity changed but downstream (cache/search) is stale. Confirm the
    ProductVariantStockChangedEvent was published and consumed. Redis is a
    projection; fix the source (ProductVariantStock), not the cache.

==========================================================
# RELATED KNOWLEDGE
==========================================================

Core Guides (in-repo)
  - .ai/hermes/capabilities/pos-admin/inventory-management.md
  - .ai/hermes/capabilities/pos-admin/purchase-management.md
  - .ai/hermes/capabilities/pos-admin/sales-management.md
  - .ai/hermes/capabilities/pos-admin/purchase-return-management.md
  - .ai/hermes/capabilities/pos-admin/sales-return-management.md
  - .ai/hermes/capabilities/pos-admin/order-management.md

System Guides
  - docs/architecture/pos-warehouse-inventory-authority-certification-findings.md
  - docs/architecture/product-inventory-reservation-certification-findings.md

Related Capabilities
  - Inventory (ProductVariantStock authority, reservations).
  - Purchasing / Sales (consume WarehouseId).
  - Checkout (storefront reservation engine).

Related Domains
  - ProductVariant / Product (catalog). Redis product cache (projection).
  - StockSyncOutbox / MassTransit (event propagation).

==========================================================
# DOCUMENTATION VALIDATION
==========================================================

Findings comparing implementation to existing docs. Reporting only
implementation-backed issues. (Existing docs reviewed:
docs/architecture/pos-warehouse-inventory-authority-certification-findings.md,
WAREHOUSE-OPENING-STOCK-IMPLEMENTATION.md, WAREHOUSE-OPENING-STOCK-SUMMARY.md.)

1. MISSING AUTHORITY BOUNDARY: WarehouseStock (legacy) is still mapped into the
   Warehouse detail DTO (WarehouseDetailDto.Stock reads Warehouse.Stock /
   WarehouseStock), while the stock and opening-stock endpoints read the
   authoritative ProductVariantStocks. A reader of the detail endpoint sees
   legacy-stock quantities, not variant-stock authority. Inconsistent surface.

2. HIDDEN IMPLEMENTATION BEHAVIOR: TWO transfer mechanisms exist (direct ad-hoc
   WarehousesController.TransferStock vs structured StockTransfersController
   lifecycle). Docs/UI that describe "warehouse transfer" without distinguishing
   them will mislead operators about approval gating.

3. INCORRECT LIFECYCLE ASSUMPTION: Warehouse has no rich status enum and NO delete
   endpoint. Any doc implying warehouse "deletion" or a multi-state workflow
   (Created/Active/Inactive/Deleted) overstates the implementation. Real states:
   Active/Inactive boolean + soft-delete column without an API path.

4. MISSING TRANSFER BEHAVIOR: The "Cancelled" transfer status exists in the enum
   but has no controller path that sets it; documenting it as reachable is wrong.

5. INCORRECT WAREHOUSE RULES: IsDefault is a plain boolean with no found unique
   filter enforcing a single default per tenant. Docs implying "exactly one default
   warehouse" are unsupported by the scanned DB configuration.

6. OUTDATED DOCUMENTATION: WarehouseStock entity/docs predate the variant-centric
   inventory model (PHASE 11.1 / 11.5). References to WarehouseStock as inventory
   authority are stale; ProductVariantStock is the authority.

7. MISSING DOCUMENTATION: Opening stock one-time rule (rejects if any prior
   inventory authority exists for the variant+warehouse, including a zero-qty
   movement) is a non-obvious operational constraint not surfaced to operators.

8. ARCHITECTURE INCONSISTENCY: Production hardening certification states migration
   `20260607210000_PosWarehouseInventoryAuthorityHardening` MUST be applied before
   the hardened runtime accepts production traffic. If that migration is not
   applied in an environment, the append-only StockMovement trigger and other
   guards may be absent.

No recommendation to rewrite docs is made here; only implementation-backed findings
are reported.

==========================================================
# QUALITY REQUIREMENTS — COVERAGE
==========================================================

This document lets Hermes:
- Understand Warehouse Management as a tenant-scoped location authority.
- Navigate implementation: WarehousesController, StockTransfersController,
  InventoryService, ProductVariantStockMutationSupport, ProductVariantStockRepository.
- Understand Warehouse authority vs Inventory authority.
- Understand Stock ownership (ProductVariantStock) and Warehouse ownership (location).
- Understand Reservations (InventoryReservation, per warehouse).
- Understand Transfers (two mechanisms; structured lifecycle + direct ad-hoc).
- Debug Warehouse via the ordered investigation path.

==========================================================
# SUMMARY OF DISCOVERIES
==========================================================

- Warehouse Lifecycle discovered:
  Create (IsActive=true) -> Active -> Used by OpeningStock/Inventory/Purchases/
  Sales/Transfers -> Inactive (PUT IsActive=false). No delete endpoint.

- Warehouse State Machine discovered:
  Boolean IsActive (Active/Inactive). Soft-delete column exists but no API path.
  StockTransfer separate machine: Pending -> Approved -> Completed; Pending ->
  Rejected. (Cancelled exists in enum, no setter.)

- Warehouse Authority discovered:
  Warehouses table = location/lifecycle truth. ProductVariantStocks = quantity
  truth. InventoryReservations = reservation truth (per warehouse). StockMovements
  = append-only audit. StockTransfers = transfer document. Redis/Mongo/ES/DTOs =
  projections, not authority.

- Warehouse/Inventory relationship discovered:
  Warehouse 1:N ProductVariantStock (per Tenant+Variant+Warehouse). ProductVariant
  owns stock (variant-centric; product never holds stock). WarehouseStock is a
  legacy non-authority table. Two stock tables coexist; the detail DTO still maps
  legacy WarehouseStock while stock endpoints read ProductVariantStocks.

- Transfer behavior discovered:
  Mechanism A (direct, instant, idempotent, no approval) + Mechanism B (structured
  Pending->Approved->Completed, idempotent). Both mutate ProductVariantStocks in
  one transaction, write TransferOut+TransferIn movements, publish stock events.
  Validations: qty>0, distinct active same-tenant warehouses, current variant.

- Reservation behavior discovered:
  InventoryReservation bound to WarehouseId+VariantId+OrderItem. ReserveAsync locks
  stock, increments ReservedQuantity bounded by AvailableQuantity. Commit/release
  use cumulative applied quantities. Lineage authority requires active warehouse +
  current variant. Expiry via InventoryReservationCleanupJob.

- Business rules discovered:
  Tenant isolation; Owner/Admin/Manager gating; inactive-warehouse blocks mutation;
  non-negative stock (SQL CHECK + validation); one-time opening stock; durable
  idempotency on adjustment/transfer; structured transfer state gating; append-only
  movements; deterministic lock ordering; reservation cannot oversell; fail-closed
  on all invalid paths.

- Cross-system dependencies discovered:
  Purchases/Sales/Returns/Orders/Checkout consume WarehouseId + ProductVariantStock
  + reservation engine. StockSyncOutbox -> Redis cache invalidation + downstream.
  No direct Warehouse->ledger coupling. CommerceHub/Affiliate/Storefront are
  consumers of the same stock authority.

- Documentation inconsistencies found:
  (1) detail DTO exposes legacy WarehouseStock while stock endpoints use
  ProductVariantStocks; (2) two transfer mechanisms undocumented as distinct;
  (3) no delete/multi-state lifecycle despite possible doc assumptions; (4) Cancelled
  transfer status unreachable; (5) IsDefault not unique-per-tenant; (6) WarehouseStock
  docs stale vs variant-centric model; (7) one-time opening-stock rule hidden;
  (8) required hardening migration may be unapplied.

- Hidden implementation behavior found:
  Legacy WarehouseStock still surfaced in detail DTO; opening stock one-time guard
  rejects even a zero-quantity prior movement; direct ad-hoc transfer bypasses
  approval; append-only StockMovement enforced by app + PostgreSQL trigger;
  variant defaults to IsDefault when no ProductVariantId supplied.

- Confirmation that only this file was created:
  YES. Only .ai/hermes/capabilities/pos-admin/warehouse-management.md was written.

- Confirmation that no other files were modified:
  YES. No source, config, doc, or test file was changed.

- Confirmation that no memory writes occurred:
  YES. No memory tool calls were made.
