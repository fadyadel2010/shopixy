# Inventory Runtime — FINAL CANONICAL ARCHITECTURE

> Implementation-backed. Every claim is cited to `file:line` in the backend
> (`PointOfSale.API`, `PointOfSale.Core`, `PointOfSale.Infrastructure`). Prior
> markdown, code comments, and certification docs are treated as **secondary
> evidence only** and were re-verified in code. Where implementation evidence
> could not be found, the gap is stated explicitly and never fabricated.

---

## 1. Executive Summary

The Inventory Runtime is the **stock execution engine** of Shopixy. It is not an
inventory screen; it is the bounded context that owns physical/logical stock
authority, warehouse isolation, the inventory movement ledger, reservations,
availability computation, and the stock projection pipeline that feeds the
storefront, marketplace, and search read models.

Its foundational invariant: **`ProductVariantStock` (SQL, PostgreSQL) is the ONE
mutable source of truth for inventory** (`ProductVariantStock.cs:9-16`,
`InventoryService.cs:11-19`, `AdminInventoryService.cs:19-25`). Every other
representation — Mongo `ProductDocument.StockQuantity`, Redis caches, the
Elasticsearch index, the legacy `WarehouseStock` table — is a **derivative
projection** rebuilt from SQL and is never authoritative
(`ProductVariantStock.cs:13-15`, `ProductStockChangedConsumer.cs:22`).

Stock is tracked at the granularity **(TenantId, VariantId, WarehouseId)**, made
unique by `IX_ProductVariantStocks_TenantId_VariantId_WarehouseId`
(`ProductVariantStockConfiguration.cs:60-62`). The **variant** — not the product
— is the stock authority; products never hold stock directly
(`InventoryService.cs:208-210`, `AdminInventoryService.cs:51-53`).

Three live write surfaces exist and this is the single most important
architectural fact of the runtime:

1. **Operational/back-office engine** (POS sale, purchase, adjustment, transfer,
   opening balance) mutates `ProductVariantStock.Quantity` **directly** through
   `ProductVariantStockMutationSupport` with pessimistic row locks, non-negative
   validation, and a durable stock-sync outbox
   (`ProductVariantStockMutationSupport.cs:41-86`, `InventoryService.cs:36-99`).
2. **Reservation engine** (`InventoryConsistencyService`) handles the
   order-driven CommerceHub/checkout lifecycle — reserve → commit-on-delivery →
   receive-return — mutating `ReservedQuantity` (and `Quantity` on commit) inside
   `Serializable` transactions with `FOR UPDATE` row locks and an inbox-based
   idempotency ledger (`InventoryConsistencyService.cs:122-291`,
   `:1631-1673`, `:2128-2154`, `:1945+`).
3. **Storefront/admin-order reservation repository**
   (`ProductVariantStockRepository`) remains active for checkout, admin order,
   payment lifecycle, and cleanup flows. It mutates `ReservedQuantity` and commit
   `Quantity` under `Serializable` transactions with row locks, writes
   `StockMovement` ledger rows for reserve/release/commit, and stages stock-sync
   outbox rows in the same transaction (`ProductVariantStockRepository.cs`).

Availability is a **pure derived value**: `AvailableQuantity = Quantity −
ReservedQuantity` (`ProductVariantStock.cs:51-53`). Oversell is prevented at
three layers: application validation, `Serializable` + `FOR UPDATE` concurrency,
and PostgreSQL `CHECK` constraints that forbid negative and over-reserved stock
(`ProductVariantStockConfiguration.cs:18-30`).

**Confidence: High** for the variant-stock authority model, reservation
lifecycle, outbox, concurrency, and variant-level valuation authority.
**Divergences flagged**: a retained `WarehouseStock` legacy table, a dual
reservation implementation surface, movement-report vs current valuation cost
presentation differences, and the absence of FIFO/LIFO layers. See §29 Technical
Debt.

---

## 2. Architecture Vision

Inventory is architected as a **SQL-authoritative, fail-closed, event-projected
ledger**. The design goals expressed in code are:

- **Single mutable truth.** All stock reads for money-critical paths derive from
  `ProductVariantStock`; projections exist for read scale, not authority
  (`ProductVariantStock.cs:9-16`; `ProductStockChangedConsumer.cs:19-24`).
- **Fail closed.** Lineage guards reject stock mutation/reservation when the
  variant/warehouse/product is not current authority, rather than silently
  proceeding (`ProductVariantInventoryLineageAuthority.cs:80-116`).
- **Immutable audit.** Every quantity change appends a `StockMovement` ledger row
  capturing before/after buckets (`StockMovement.cs:48-66`,
  `AdminInventoryService.cs:23`).
- **Idempotent, replayable eventing.** Inbound order-driven events dedupe through
  `InventoryEventInbox`; outbound stock changes flow through a durable,
  version-ordered `StockSyncOutboxMessage` outbox
  (`InventoryEventInbox.cs`, `StockSyncOutboxMessage.cs`).
- **Tenant isolation everywhere.** `TenantId` is NOT NULL on every inventory
  entity and enforced at the DB level (`ProductVariantStock.cs:19-23`,
  `Warehouse.cs:10-14`, `StockMovement.cs:11-15`).

---

## 3. Inventory Runtime Purpose

To be the authoritative execution surface that answers: *How much of this variant
is physically present, reserved, damaged, and sellable in this warehouse for this
tenant right now — and to record every reason it changed.*

## 4. Inventory Runtime Scope

In scope: variant stock quantity, reserved/committed/damaged buckets, warehouse
records, the stock movement ledger, order reservations, availability derivation,
adjustments, transfers, opening balances, purchase/sale/return stock effects, the
stock-sync outbox and stock projections, reconciliation and cleanup jobs.

Out of scope (deferred to owning contexts): payment lifecycle, order lifecycle,
accounting GL/journal entries, shipping, pricing decisions, and catalog identity.
Inventory consumes/reacts to those but does not own them.

---

## 5. Inventory Runtime Owns (SQL truth it writes)

| Aggregate / table | Authority | Evidence |
|---|---|---|
| `ProductVariantStock` (Quantity/Reserved/Damaged) | Sole mutable stock truth | `ProductVariantStock.cs:9-58`; `ProductVariantStockConfiguration.cs:60-62` |
| `StockMovement` | Immutable inventory ledger/audit | `StockMovement.cs:6-105`; `StockMovementConfiguration.cs:75-81` |
| `InventoryReservation` | Order reservation state machine | `InventoryReservation.cs:6-29` |
| `Warehouse` | Warehouse records (tenant-scoped) | `Warehouse.cs:8-26` |
| `StockTransfer` / `StockTransferItem` | Inter-warehouse transfer docs | `StockTransfer.cs:6-40` |
| `InventoryEventInbox` | Inbound event idempotency ledger | `InventoryEventInbox.cs:6-23` |
| `StockSyncOutboxMessage` | Outbound stock-change outbox | `StockSyncOutboxMessage.cs:10-220` |
| `ProductVariant.CostPrice` | Current inventory valuation cost authority | `PurchasesController.cs:218-223`; `PurchaseReturnsController.cs:538-545`; `ReportsController.cs:277-283` |

## 6. Inventory Runtime Does NOT Own

- **Payment lifecycle** — deferred to `IPaymentLifecycleService`; the cleanup job
  calls `MarkPaymentExpiredAsync` rather than writing payment state
  (`InventoryReservationCleanupJob.cs:38`, `:199-202`).
- **Order lifecycle** — order/sub-order status is read, never written, by
  inventory (`InventoryReservationCleanupJob.cs:93-152`).
- **Accounting GL** — no journal-entry writes in any inventory service (verified:
  0 `JournalEntry`/`AccountingOutbox` writes in inventory paths).
- **Catalog identity** — variant/product identity owned by Product Catalog;
  inventory only reads current lineage (`ProductVariantInventoryLineageAuthority.cs:17-54`).
- **Pricing** — inventory snapshots cost onto movements but does not set sale price.

## 7. Architecture Principles (each code-cited)

1. **Variant + Warehouse is the only stock unit.** Products never hold stock
   (`InventoryService.cs:208-210`; `AdminInventoryService.cs:51-53`).
2. **Lock before mutate.** Every quantity change reloads the row under
   `FOR UPDATE` / `UPDLOCK,ROWLOCK,HOLDLOCK` before changing it
   (`ProductVariantStockMutationSupport.cs:397-447`; `InventoryService.cs:50-56`).
3. **Validate non-negative after lock, before mutate**
   (`ProductVariantStockMutationSupport.cs:335-386`; `InventoryService.cs:63-67`).
4. **Deterministic multi-row lock ordering (WarehouseId, VariantId)** prevents
   deadlocks (`ProductVariantStockMutationSupport.cs:277-325`).
5. **Mutations must run inside an explicit transaction**
   (`ProductVariantStockMutationSupport.cs:388-395`).
6. **Reservation engine is `Serializable`** (`InventoryConsistencyService.cs:2128-2154`).
7. **Fail-closed lineage authority** — SQL current-owner check gates mutation and
   reservation (`ProductVariantInventoryLineageAuthority.cs:56-116`).
8. **Idempotency is durable, not cache-based** — `InventoryEventInbox` for
   CommerceHub reservation events, reservation-state no-ops plus caller
   order/payment idempotency for the repository path, unique `IdempotencyKey` for
   adjustments; Redis is cache-only (`AdminInventoryService.cs:24-25`,
   `:169-189`, `:369-375`).
9. **Projections are recomposed from SQL and fail-closed on lineage loss**
   (`ProductVariantInventoryLineageAuthority.cs:118-154`).
10. **DB-level `CHECK` constraints are the last line of defence** against
    negative/over-reserved stock (`ProductVariantStockConfiguration.cs:18-30`).

## 8. Core Aggregates

- **ProductVariantStock** — `Quantity` (sellable on-hand), `ReservedQuantity`,
  `DamagedQuantity`; derived `AvailableQuantity = Quantity − ReservedQuantity`
  and `TotalQuantity = Quantity + DamagedQuantity`; monotonic
  `ProjectionMutationVersion` with `AdvanceProjectionMutationVersion` guard
  (`ProductVariantStock.cs:35-78`).
- **StockMovement** — append-only ledger; captures `QuantityChange`,
  `ReservedQuantityChange`, `DamagedQuantityChange` and post-state
  `QuantityAfter/AvailableAfter/ReservedAfter/DamagedAfter`, plus reference,
  reservation, inventory-event, correlation, idempotency, and cost fields
  (`StockMovement.cs:33-94`).
- **InventoryReservation** — buckets `ReservedQuantity`, `CommittedQuantity`,
  `ReleasedQuantity`, `ReturnedQuantity`, `DamagedReturnedQuantity`; derived
  `RemainingReservedQuantity` and `RemainingDeliveredReturnableQuantity`;
  `Status` (8-state enum), `ExpiresAtUtc`, `ReservationEventId`,
  `LastMutationEventId` (`InventoryReservation.cs:6-29`).

---

## 9. Stock Runtime

**Stock authority.** `ProductVariantStock` is the only mutable stock truth; the
legacy `WarehouseStock` table is explicitly a compatibility artifact and NOT an
authority (`ProductVariantStock.cs:13-15`; `WarehouseStock.cs:5-7`).

**Quantities.**
- Current on-hand sellable = `Quantity` (`ProductVariantStock.cs:35-38`).
- Reserved = `ReservedQuantity` (`:40-43`).
- Damaged (non-sellable on-hand) = `DamagedQuantity` (`:45-48`).
- Available (sellable) = `Quantity − ReservedQuantity` (`:50-53`).
- Total physical = `Quantity + DamagedQuantity` (`:55-58`).
- There is **no distinct "Committed" bucket on stock**; commitment is tracked on
  the reservation (`InventoryReservation.CommittedQuantity`) and realised by
  decrementing `Quantity` and `ReservedQuantity` at delivery-commit
  (`InventoryConsistencyService.cs:841-1019`).

**Stock mutation.** Three live paths:
1. Direct operational mutation via `ProductVariantStockMutationSupport`
   (`LockStockForMutationAsync` → `ValidateQuantityChange` → set `Quantity` →
   append `StockMovement` → `PublishCommittedStockEventsAsync`)
   (`ProductVariantStockMutationSupport.cs:43-86`, `:216-250`,
   `InventoryService.cs:36-99`).
2. Reservation mutation via `InventoryConsistencyService` (mutates
   `ReservedQuantity` on reserve/release, both buckets on commit)
   (`InventoryConsistencyService.cs:201-291`).
3. Storefront/admin-order reservation mutation via `ProductVariantStockRepository`
   (mutates `ReservedQuantity` on reserve/release, both buckets on commit, appends
   `StockMovement`, and stages stock-sync outbox rows under the same transaction).

**Stock validation / negative stock.** Negative stock is forbidden by default at
every layer:
- App: `ValidateQuantityChange` / `ValidateAvailableQuantity` throw
  `InsufficientStockException` unless `allowNegativeStock:true` is explicitly
  passed (`ProductVariantStockMutationSupport.cs:335-386`). Callers pass
  `allowNegativeStock:false` (`InventoryService.cs:66`, `:144`;
  `AdminInventoryService.cs:246`).
- DB: four `CHECK` constraints —
  `Quantity >= 0`, `ReservedQuantity >= 0`, `DamagedQuantity >= 0`, and
  `Quantity >= ReservedQuantity` (`ProductVariantStockConfiguration.cs:18-30`).
- Reservation engine additionally re-checks `currentTotalAvailable >= Quantity`
  and marks the event failed if insufficient
  (`InventoryConsistencyService.cs:186-190`, `:272-276`).

**Concurrency & consistency.** Pessimistic row locking (`FOR UPDATE` on Postgres,
`UPDLOCK,ROWLOCK,HOLDLOCK` on SQL Server) with PostgreSQL `xmin` optimistic token
selected in the lock query (`ProductVariantStockMutationSupport.cs:406-431`).
Reservation engine runs at `IsolationLevel.Serializable`
(`InventoryConsistencyService.cs:2139-2141`). Multi-row operations lock in
deterministic `(WarehouseId, VariantId)` order
(`ProductVariantStockMutationSupport.cs:300-304`).

## 10. Warehouse Runtime

**Isolation.** Every stock/movement/reservation row is scoped by `TenantId` +
`WarehouseId`; warehouse FK uses `DeleteBehavior.Restrict` so warehouses with
stock cannot be hard-deleted (`ProductVariantStockConfiguration.cs:54-57`).

**Authority & lifecycle.** `Warehouse` carries `IsActive` and `IsDefault`
(`Warehouse.cs:20-21`). Mutation/reservation require the warehouse to be current
authority: `RequireCurrentStockMutationOwnerAsync` verifies the warehouse exists,
is not deleted, and `IsActive` before any stock write
(`ProductVariantInventoryLineageAuthority.cs:80-116`); the stock-owner filter
requires `Warehouse.IsActive` and same-tenant ownership
(`:34-54`). Opening stock rejects inactive warehouses
(`InventoryService.cs:256-259`).

**Cross-warehouse rules.** Stock cannot be transferred to the same warehouse
(`InventoryService.cs:104-107`); transfers are two-legged (TransferOut +
TransferIn movements) within one transaction (§16).

## 11. Variant Runtime (Variant Inventory)

**Variant is the inventory authority.** Stock rows key on `VariantId`; product is
only a rollup for reporting (`InventoryService.cs:208-210`;
`AdminInventoryService.cs:51-53`). SKU and Barcode live on the variant
(`ProductVariant.cs:44`, `:50`) and are used for inventory search/lookup
(`AdminInventoryService.cs:92-96`).

**Unit conversion / multi-unit.** `StockMovement` snapshots the `UnitId` and a
`ConversionFactor` (default 1) at movement time; stock quantities are stored in
**base units** and callers convert to base quantity before mutating
(`StockMovement.cs:23-31`; `SalesController.cs:270`, `:280-282`;
`PurchasesController.cs:230`). Purchase-return restores `BaseQuantity`
(`SalesController.cs:2278`).

**Semantic lineage.** A variant is only reservable/mutable when its
`SemanticLifecycleState == Current`, not superseded, and not replaced
(`ProductVariantInventoryLineageAuthority.cs:21-32`, `:44-53`). Superseded/replaced
variants lose inventory authority — a fail-closed guard against stock attaching to
stale catalog identities (`ProductVariant.cs:89-115`).

---

## 12. Inventory Movement Runtime

Every quantity change appends an immutable `StockMovement`. Movement types
(`StockMovementType.cs:3-14`): `Purchase`, `Sale`, `PurchaseReturn`,
`SalesReturn`, `Adjustment`, `TransferIn`, `TransferOut`, `InitialStock`,
`OpeningBalance`.

| Movement | Writer | Stock effect | Evidence |
|---|---|---|---|
| Purchase | `PurchasesController` | `Quantity +=` | `PurchasesController.cs:230`, `:240` |
| Sale (POS) | `SalesController` | `Quantity -=` | `SalesController.cs:270`, `:279` |
| SalesReturn | `SalesController` | `Quantity +=` | `SalesController.cs:2278`, `:2291` |
| PurchaseReturn | `PurchasesController` | `Quantity -=` | `PurchasesController.cs:1443` |
| Adjustment | `AdminInventoryService` / `InventoryService` | `Quantity ±` | `AdminInventoryService.cs:251`, `:264` |
| TransferOut/In | `InventoryService.TransferStockAsync` | `Quantity ∓` both legs | `InventoryService.cs:148-194` |
| OpeningBalance | `InventoryService.SaveOpeningStockAsync` | sets `Quantity` | `InventoryService.cs:314`, `:324-325` |
| Reserve/Release/Commit/Return | `InventoryConsistencyService` / `ProductVariantStockRepository` | `ReservedQuantity`/`Quantity` | `InventoryConsistencyService.cs:238-257`; `ProductVariantStockRepository.cs` |

Note: reserve/release movements are written with `MovementType.Adjustment`; commit
movements use `MovementType.Sale`. All reservation movements carry an
`InventoryEventType` (`StockReserved/Released/Decreased/Increased/Damaged`) that
distinguishes them (`InventoryConsistencyService.cs:243`, `:253`;
`ProductVariantStockRepository.cs`; `InventoryEventType.cs:3-10`).

**Mutation / Authority.** Movements are never the authority — they are the audit
trail; the authority is the `ProductVariantStock` row state at commit
(`AdminInventoryService.cs:23`). **Ledger** rows record before/after for all four
buckets (`StockMovement.cs:48-66`). **Audit/history** is queryable per
warehouse/product/date (`AdminInventoryService.cs:377-455`;
`InventoryService.cs:377-400`).

## 13. Availability Runtime

- **Available (sellable) stock** = `Quantity − ReservedQuantity`
  (`ProductVariantStock.cs:50-53`). Read models compute the same and floor at 0:
  `Math.Max(0, Σ(Quantity − ReservedQuantity))`
  (`ProductVariantStockMutationSupport.cs:154`;
  `InventoryConsistencyService.cs:1820`).
- **Reserved stock** = `ReservedQuantity` bucket.
- **Display / sellable stock** = the projected `TotalStockQuantity` on
  `ProductVariantStockChangedEvent` (floored non-negative) that lands on the Mongo
  `ProductDocument` (`ProductVariantStockMutationSupport.cs:206`;
  `ProductStockChangedConsumer.cs:44-95`).
- **Oversell protection.** Reservation fails when
  `currentTotalAvailable < request.Quantity`
  (`InventoryConsistencyService.cs:186-190`); direct sales validate available
  before deduction; DB `CHECK` is the backstop.
- **Backorder / negative inventory.** No backorder path exists: default is
  fail-closed (`allowNegativeStock:false` everywhere in operational paths). An
  `allowNegativeStock` override parameter exists in the mutation helper but is not
  passed `true` by any inventory caller found (gap/flag §29).
- **Failure behaviour.** Insufficient availability throws
  `InsufficientStockException` (direct) or marks the inbox event failed and
  returns `false` (reservation) — never a silent partial write
  (`ProductVariantStockMutationSupport.cs:350-356`;
  `InventoryConsistencyService.cs:272-276`).

## 14. Reservation Runtime

Owned by Inventory, with two active implementations:
`InventoryConsistencyService` for CommerceHub marketplace events
(`InventoryConsistencyService.cs:102-120`) and `ProductVariantStockRepository`
for storefront checkout, admin order, payment lifecycle, and cleanup flows.

**Creation.** `ReserveOrderItemAsync` runs in a `Serializable` tx: begins an inbox
event (idempotency), validates the reservation request matches current SQL catalog
authority, loads stock rows `FOR UPDATE`, filters to current lineage owners,
checks availability, then distributes the reservation across warehouses (most-
available first when no warehouse specified), incrementing `ReservedQuantity` and
inserting an `InventoryReservation` per stock row
(`InventoryConsistencyService.cs:122-291`). Reservations carry `ExpiresAtUtc`,
`ReservationEventId`, `LastMutationEventId` (`:229-233`).

**Consumption (commit).** `CommitDeliveryAsync` computes `remainingToCommit =
Quantity − alreadyCommitted`, honours only non-expired reservations, and realises
the sale by moving quantity out of both `Quantity` and `ReservedQuantity`
(`InventoryConsistencyService.cs:841-1019`, esp. `:889`, `:907`).

**Release.** `ReleaseReservationAsync` / `ReleaseOrderItemReservationsAsync` /
`ReleaseMarketplaceOrderReservationsAsync` decrement `ReservedQuantity` and record
`ReleasedQuantity` (`InventoryConsistencyService.cs:293-675`).

**Storefront/admin-order repository path.** `ProductVariantStockRepository`
provides `ReserveAsync`, `ReleaseOrderReservationsAsync`,
`CommitOrderReservationsAsync`, and `BindOrderReservationsAsync` for the
storefront/admin-order payment lifecycle. It uses `Serializable` transactions,
provider-specific row locks, current-lineage stock filtering, same-transaction
stock-sync outbox staging, and now appends `StockMovement` rows for
reserve/release/commit. It does not consume the `InventoryEventInbox`; replay
dedupe is supplied by the calling order/payment lifecycle and by reservation
state no-ops on already released/committed rows.

**Expiration.** Status resolves to `Expired` when `ExpiresAtUtc <= now` and
remaining reserved > 0 (`ResolveReservationStatus`,
`InventoryConsistencyService.cs:2156-2202`).

**Cleanup / recovery.** `InventoryReservationCleanupJob` (Hangfire,
`DisableConcurrentExecution` 300s) releases stale/expired reservations that no
longer have an active payment flow, re-validating order + payment authority under
`Serializable` + `FOR UPDATE` before releasing, and delegating to
`IPaymentLifecycleService.MarkPaymentExpiredAsync` when a payment is still pending
(`InventoryReservationCleanupJob.cs:21-321`). It respects payment grace windows
and unresolved-webhook blocking statuses (`:24-30`, `:145-151`).

**Replay / reconciliation.** `CommerceHubInventoryReconciliationJob` repairs
missing delivery commits, missing return receipts, open reservations on completed
orders, and releases expired reservations; it reports invalid-stock-row counts and
duplicate replay counts as a health signal
(`CommerceHubInventoryReconciliationJob.cs:33-200`).

**Authority.** Only reservations whose full lineage (stock+warehouse+variant+
product) is current and active, non-expired, and still open are consumable
(`ProductVariantInventoryLineageAuthority.cs:156-236`).

**Divergence — POS vs. order reservations.** Direct POS sales
(`SalesController`) and purchases do NOT create `InventoryReservation` rows — they
deduct/add `Quantity` immediately in-transaction. Reservations exist only on the
**order-driven** (CommerceHub/checkout) path. This is an intentional architectural
split: synchronous counter sales need no reservation window, whereas async
online orders require a hold between checkout and payment/fulfillment
(`SalesController.cs:269-292` vs. `InventoryConsistencyService.cs:122-291`).

---

## 15. Adjustment Runtime

`AdminInventoryService.AdjustStockAsync` (`AdminInventoryService.cs:147-346`):
non-zero adjustment + mandatory reason (`:159-167`); durable idempotency via
`StockMovements.IdempotencyKey` unique constraint checked before and enforced by
`UX_StockMovements_TenantId_IdempotencyKey` on race
(`:169-189`, `:329-336`, `:369-375`); warehouse + variant ownership verified
(`:191-217`); lock row → validate non-negative → mutate `Quantity` → append
immutable movement → publish stock-sync event → commit; Redis cache write is
best-effort **after** durable commit (`:300-310`). Types of adjustment are
free-text reasons (damage/loss/correction/count) recorded in `Notes`; there is a
dedicated `DamagedQuantity` bucket and `StockDamaged` inventory-event on the
reservation-return path (`InventoryConsistencyService.cs`, return receipt).
`InventoryService.AdjustStockAsync` is the lower-level POS variant that stages a
movement inside a caller-owned transaction (`InventoryService.cs:36-99`).

**Audit trail.** Every adjustment is an immutable `StockMovement` with reason,
correlation id, idempotency key, source `AdminInventoryAdjustment`
(`AdminInventoryService.cs:257-277`).

## 16. Transfer Runtime

Two-phase document workflow in `StockTransfersController` + atomic execution in
`InventoryService.TransferStockAsync`.

- **Lifecycle:** `Pending → Approved → Completed` (or `Rejected`)
  (`StockTransfersController.cs:219`, `:286`, `:325`, `:418`;
  `TransferStatus.cs`).
- **Approval:** explicit approve/reject with approver id + timestamp
  (`StockTransfersController.cs:267-333`).
- **Atomicity:** `CompleteTransfer` runs one DB transaction, processes items in
  deterministic `(VariantId, ProductId)` order, and for each calls
  `TransferStockAsync` which locks both source+target rows in deterministic order,
  validates source availability, decrements source, increments target, and writes
  paired TransferOut/TransferIn movements
  (`StockTransfersController.cs:345-416`; `InventoryService.cs:101-205`).
- **Validation:** requires `Approved` status, non-null `ProductVariantId` per
  item, positive quantity, different source/target
  (`StockTransfersController.cs:362-377`; `InventoryService.cs:103-107`).
- **Idempotency:** `StockTransferRequestIdempotency` guards duplicate create/
  complete requests (`StockTransfersController.cs:559-648`).
- **Failure behaviour:** any item failure rolls back the whole transfer
  (transaction rollback), so a transfer cannot partially succeed
  (`StockTransfersController.cs:352`, `:375`).

## 17. Opening Balance Runtime

`InventoryService.SaveOpeningStockAsync` (`InventoryService.cs:225-375`):
- One transaction; positive quantities only; no duplicate variant in a request
  (`:238-251`).
- Active warehouse required (`:256-259`).
- **Bootstrap-only invariant:** opening stock is rejected if ANY prior inventory
  authority exists for the (variant, warehouse) — non-zero quantity/reserved/
  damaged OR any prior `StockMovement` (`:294-311`). This makes opening balance a
  true one-time initialization, not a back-door adjustment.
- Sets `Quantity`, writes an `OpeningBalance` movement flagged
  `IsOpeningBalance = true` with a deterministic idempotency key and cost `Price`
  (`:314-335`).
- Publishes stock-sync events after commit (`:355-363`).
- Opening-balance movements are marked immutable by convention
  (`StockMovement.cs:91-94`).

`StockMovementType.InitialStock` also exists as a legacy/alternate seed type
(`StockMovementType.cs:12`).

## 18. Purchase Integration

`PurchasesController` receiving path (`PurchasesController.cs:228-260`, and a
second path at `:1098-1130`): locks stock rows (multi-row deterministic lock),
increments `Quantity += baseQuantity`, appends a `Purchase` movement snapshotting
cost, and stages stock-sync pending changes. **Cost basis update:** on receiving,
`ProductVariant.CostPrice` is recomputed as a variant-level moving weighted
average from locked stock quantity and the receipt base-unit cost
(`PurchasesController.cs:218-223`, `:1109-1114`; `VariantCostAuthority.cs`).
For simple products only, `Product.PurchasePrice` mirrors the default variant
cost for catalog compatibility (`PurchasesController.cs:775-780`).

**Accounting.** Inventory does not post journals; purchase/accounting linkage is
owned by the accounting context (no GL writes in the inventory receiving path).

**Events.** The outbound stock pipeline publishes **only**
`ProductVariantStockChangedEvent` via the outbox; the legacy `StockReceivedEvent`
is **defined but never published** (dead code — `InventoryEvents.cs:67`, no
publish site) and is not part of the live integration. Purchase receiving also
enqueues an accounting outbox (`EnqueuePosPurchaseAsync`) and posts supplier
ledger entries; **treasury balances are explicitly NOT mutated directly**
(`PurchasesController.cs:336`, `:370-372`).

## 19. Sale Integration

- **POS sale** (`SalesController.cs:269-292`): synchronous `Quantity -=`
  deduction inside the sale transaction, `Sale` movement with cost snapshot; no
  reservation. Unit conversion applied to base quantity (`:280-282`).
- **CommerceHub marketplace checkout**: async reservation via
  `InventoryConsistencyService.ReserveOrderItemAsync` at checkout, then
  `CommitDeliveryAsync` at delivery to realise the deduction
  (`InventoryConsistencyService.cs:122-291`, `:841-1019`).
- **Storefront/admin-order checkout**: async reservation via
  `ProductVariantStockRepository.ReserveAsync`, binding to order items through
  `BindOrderReservationsAsync`, and commit/release through payment lifecycle
  calls into the same repository.
- **Reservation → deduction:** reservation increments `ReservedQuantity`;
  commit-on-delivery decrements both `Quantity` and `ReservedQuantity`.
- **Replay / recovery:** inbox idempotency (`TryBeginEventAsync`) + reconciliation
  job repair of missing delivery commits
  (`InventoryConsistencyService.cs:1945+`;
  `CommerceHubInventoryReconciliationJob.cs:202-260`).

## 20. Return Integration

- **Sale return (POS):** `SalesController.cs:2278-2328` adds `BaseQuantity` back to
  `Quantity` and writes a `SalesReturn` movement.
- **Purchase return:** `PurchasesController.cs:1443` decrements `Quantity` and
  writes a `PurchaseReturn` movement; cost restored (`:1430`).
- **CommerceHub return:** `InventoryConsistencyService.ReceiveReturnAsync`
  (`:1021-1167`) splits returned quantity into resellable (`Quantity +=`,
  `StockIncreased`) and damaged (`DamagedQuantity +=`, `StockDamaged`), bounded by
  `RemainingDeliveredReturnableQuantity`, under inbox idempotency + lineage
  authority (`EnsureReturnStockLineageAuthorityAsync`, `:1168-1211`;
  `ApplyReturnedQuantity`, `:1212-1311`).
- **Validation / replay:** return receipts are idempotent by event id; the
  reconciliation job repairs missing return receipts
  (`CommerceHubInventoryReconciliationJob.cs:262-332`).

---

## 21. Inventory Valuation Runtime

**Cost storage authority.** `ProductVariant.CostPrice` is the current inventory
valuation authority (`ProductVariant.cs:72`). `Product.PurchasePrice` remains a
catalog/simple-product mirror and legacy display value; it is not a valuation
authority for inventory, COGS, or reports.

**Method — incremental weighted-average at variant level.** Purchase receiving
updates `ProductVariant.CostPrice` with `VariantCostAuthority.ApplyPurchaseReceipt`
using the locked variant stock quantity and the receipt base-unit cost
(`PurchasesController.cs:218-223`, `:1109-1114`;
`VariantCostAuthority.cs`). Purchase return/revert uses
`VariantCostAuthority.ApplyPurchaseReturn` with the original receipt cost
snapshot before stock is decremented (`PurchaseReturnsController.cs:521-545`;
`PurchasesController.cs:1437-1444`). For simple products only,
`Product.PurchasePrice` is mirrored from the default variant so legacy catalog
surfaces stay coherent without becoming a parallel authority
(`PurchasesController.cs:775-780`; `ProductVariantService.cs:277,367`).

**Current valuation.** The inventory valuation report does not compute an
average; it multiplies current `Variant.CostPrice` by
`(Quantity + DamagedQuantity)` per warehouse (`ReportsController.cs:277`, `:283`).
Damaged stock is included in valuation (`ReportsController.cs:277,279,284`).

**Historical valuation / COGS.** Sales and returns use immutable per-line
`CostPriceSnapshot`/`CostAmountSnapshot` captured from the variant cost at the
time of sale/return (`SalesController.cs:239-258`, `:1431-1451`;
`SalesReturnsController.cs:720-788`). Accounting COGS consumes those snapshots
first and uses live `ProductVariant.CostPrice` only as a legacy fallback when a
snapshot is absent (`CommerceAccountingService.cs:1540-1564`).

## 22. Inventory Analytics Runtime

Analytics are **read-side reports** over SQL: inventory valuation, product
movement report (`ProductMovementReportDto.cs`), sales/return cost impact
(`ReportsController.cs:1198`, `:1634`, `:2248`). No separate analytics store owns
inventory truth; analytics query `StockMovement`/`ProductVariantStock` directly.
Search/Mongo projections carry `StockQuantity` for browse/display only
(`ProductStockChangedConsumer.cs:83-95`).

## 23. CQRS Model

**Write authority:** PostgreSQL `ProductVariantStock` + `StockMovement` +
`InventoryReservation` (SQL). All money-critical inventory reads (checkout,
reservation, adjustment, transfer) read SQL directly with row locks
(`InventoryConsistencyService.cs:1631-1673`;
`ProductVariantStockMutationSupport.cs:397-447`).

**Read authority:**
- Browse/display stock → Mongo `ProductDocument.StockQuantity`
  (`ProductStockChangedConsumer.cs:39`, `:83-95`).
- Search → Elasticsearch via `ProductStockPriceSearchIndexConsumer`.
- Redis → cache only, never authority (`AdminInventoryService.cs:24-25`).

**Projection version / staleness rejection.** Both `ProductVariantStock` and
`ProductVariant` carry a monotonic `ProjectionMutationVersion`
(`ProductVariantStock.cs:60-78`; `ProductVariant.cs:164`). Stock-sync events carry
`EventVersion` derived from it (`ProductVariantStockMutationSupport.cs:209`); the
outbox processor orders by `EventVersion` and rejects/holds stale events
(`StockSyncOutboxProcessor.cs:50`, `:440-507`).

**Repair-on-read / recompose.** Projections are recomposed from SQL authority; the
lineage authority suppresses stock/reservations whose SQL lineage is lost, and the
event build recomputes totals from current SQL owners
(`ProductVariantInventoryLineageAuthority.cs:118-236`;
`ProductVariantStockMutationSupport.cs:146-156`).

**Projection freshness / replay.** Reconciliation + cleanup jobs continuously
converge SQL truth and detect drift (`CommerceHubInventoryReconciliationJob.cs`).

## 24. Projection Model

`ProductVariantStockChangedEvent` is the canonical stock projection event, built
after commit from current SQL owners with `TotalStockQuantity` (floored),
`StockUpdatedAtUtc`, `EventVersion`, SKU/warehouse metadata
(`ProductVariantStockMutationSupport.cs:125-214`). It is consumed by
`ProductStockChangedConsumer` (Mongo `ProductDocument.StockQuantity`,
`LastStockUpdatedAtUtc`) with an event fence to reject out-of-order updates
(`ProductStockChangedConsumer.cs:136-198`) and by the search-index consumer for
Elasticsearch. Legacy `ProductStockChangedEvent` is also handled for backward
compatibility (`ProductStockChangedConsumer.cs:26-28`, `:44`).

## 25. Mongo / Redis / Elasticsearch Integration

- **Mongo** (`ProductDocument`): projected stock at BOTH variant level
  (`Variants[].TotalStock/InStock`) and product level (`StockQuantity`,
  recomputed as `max(0, Σ active variants.TotalStock)`); NOT authority; updated
  idempotently by `(TenantId, ProductId)` with monotonic staleness rejection
  (`ProductStockChangedConsumer.cs:83-124`, `:268-288`) and a repair-on-read
  wait-for-upstream (`ProjectionNotReadyException` → retry until the variant
  consumer has caught up). The variant consumer also **recomposes from SQL
  authority** before applying (`ProductVariantProjectionConsumer.cs:817-818`).
- **Redis:** **no stock document is written** — Redis is used only for product
  **cache invalidation** and Admin adjustment idempotency, never as a stock
  projection/authority (`ProductVariantProjectionConsumer.cs:946`;
  `AdminInventoryService.cs:24-25`, `:300-310`).
- **Elasticsearch:** stock/price search index via
  `ProductStockPriceSearchIndexConsumer`; search is a read projection rebuilt
  from Mongo + SQL authority overlay (wait-for-Mongo then re-index; delete if SQL
  product missing/inactive), SQL remains business truth
  (`ProductStockPriceSearchIndexConsumer.cs:322`, `:451-468`, `:536-550`).

## 26. Outbox Model

Entity `StockSyncOutboxMessage` (`StockSyncOutboxMessage.cs:10-220`):
- **Creation:** staged in the same transaction as the stock mutation via
  `PublishCommittedStockEventsAsync` (`ProductVariantStockMutationSupport.cs:216-250`).
  Reservation engines stage via `EnqueueStockSyncOutboxMessagesAsync`
  (`InventoryConsistencyService.cs:1787-1888`; `ProductVariantStockRepository.cs`).
- **Validation on create:** requires TenantId, CorrelationId, UTC timestamps,
  positive `EventVersion`, non-empty `WarehouseId`
  (`StockSyncOutboxMessage.cs:56-135`).
- **Claiming:** `TryClaim` lease (entity default 30s; processor passes
  `ClaimLeaseSeconds` = **60s**) plus a PostgreSQL **advisory lock** keyed by
  `(TenantId, VariantId, WarehouseId)` prevents multi-instance double-publish
  (`StockSyncOutboxMessage.cs:147-160`; `StockSyncOutboxProcessor.cs:100-159`).
- **Ordering:** processed `OrderBy(EventVersion)`; warehouse-scoped event identity;
  the processor holds a message if an earlier unpublished version for the same
  variant exists, and rejects stale events when
  `variantAuthority.ProjectionMutationVersion > message.EventVersion`
  (`StockSyncOutboxProcessor.cs:50`, `:440-507`).
- **Retry / backoff:** `RecordFailure` exponential backoff capped at 300s, terminal
  `IsFailed` after `MaxRetryAttempts` (default 8); non-terminal projection failures
  use a separate 900s-capped backoff (`StockSyncOutboxMessage.cs:162-190`;
  `StockSyncOutboxProcessor.cs:276`).
- **Dead-letter:** there is **no dedicated DLQ/dead-letter table**. On reaching the
  terminal retry threshold the processor invokes
  `IProjectionFailureCompensationService.HandleTerminalFailureAsync`, which rebuilds
  a fresh `ProductVariantStockChangedEvent` snapshot from authoritative SQL state,
  inserts a NEW outbox row, and marks the original `MarkCompensated` (`IsFailed`).
  So "dead-letter" = SQL-authoritative replay, not a drop
  (`StockSyncOutboxProcessor.cs:287-312`; `StockSyncOutboxMessage.cs:207-219`).
  Operator recovery via `RetryFailedMessageAsync`/`ResetForRetry`
  (`StockSyncOutboxProcessor.cs:356-383`).
- **Stale-event suppression:** before publishing, the processor re-checks SQL
  variant authority and silently `MarkPublished` (never sends) when lineage is
  lost, the mutation version is stale, or the warehouse stock owner no longer
  exists (`StockSyncOutboxProcessor.cs:421-477`).
- **Idempotency:** message `Id = EventId` (PK collision on re-emit);
  unique `(TenantId, WarehouseId, EventId)` index; `RowVersion` optimistic
  concurrency (`StockSyncOutboxMessage.cs:14`, `:49-50`, `:117`;
  `StockSyncOutboxMessageConfiguration.cs:47-50`).
- **Worker:** `StockSyncOutboxWorker` (`BackgroundService`) polls every **5s**,
  running up to `MaxBatchesPerPoll` (3) batches of `ProcessPendingMessagesAsync`
  (batch 100) with a 100ms inter-batch delay
  (`StockSyncOutboxWorker.cs:8-75`; `StockSyncOutboxProcessor.cs:34`;
  registered `Program.cs:405`).

**Inbound idempotency (inbox).** `InventoryEventInbox` dedupes CommerceHub
order-driven events by unique `(TenantId, EventId)`
(`UX_InventoryEventInbox_TenantId_EventId`,
`InventoryEventInboxConfiguration.cs:32-34`); `TryBeginEventAsync` returns
already-handled for replays and verifies payload hash matches the original
(`InventoryConsistencyService.cs:1945-2067`). The storefront/admin-order
repository path is not event-inbox-driven; it relies on caller-level order/payment
idempotency plus reservation state transitions that no-op already committed or
released rows.

---

## 27. Event Flow

```
Mutation (purchase/sale/adjust/transfer/opening OR reserve/commit/return)
  → lock ProductVariantStock row(s) [FOR UPDATE / Serializable]
  → validate (non-negative / availability / lineage authority)
  → mutate Quantity/ReservedQuantity/DamagedQuantity
  → append immutable StockMovement (before/after buckets)
  → TouchVariantStockTimestamps + advance ProjectionMutationVersion
  → stage StockSyncOutboxMessage (SAME transaction)
  → COMMIT
  → StockSyncOutboxWorker claims (lease) → publishes ProductVariantStockChangedEvent (EventVersion-ordered)
      → ProductStockChangedConsumer → Mongo ProductDocument.StockQuantity (event-fenced)
      → ProductStockPriceSearchIndexConsumer → Elasticsearch
  → Reconciliation + Cleanup jobs converge drift, release stale reservations
```

## 28. Read Models / Write Models

**Write models:** `ProductVariantStock`, `StockMovement`, `InventoryReservation`,
`StockTransfer`, `Warehouse`, `InventoryEventInbox`, `StockSyncOutboxMessage`
(all PostgreSQL).

**Read models:** admin inventory list + movements (SQL, AsNoTracking)
(`AdminInventoryService.cs:54-140`, `:377-455`); stock levels
(`InventoryService.cs:211-223`); inventory valuation (`ReportsController.cs:253+`);
Mongo `ProductDocument.StockQuantity`; Elasticsearch stock/price index.

## 29. Runtime Authority Matrix

| Concept | Owner | Mutation authority | Read authority | Projection authority | Runtime truth |
|---|---|---|---|---|---|
| Warehouse | Inventory | `WarehousesController`/service | SQL | — | SQL `Warehouses` |
| Stock qty | Inventory | `ProductVariantStockMutationSupport` / `InventoryConsistencyService` / `ProductVariantStockRepository` | SQL (locked for money paths) | outbox→Mongo/ES | SQL `ProductVariantStocks` |
| Variant (as stock unit) | Product Catalog (identity) / Inventory (stock) | Catalog owns identity | SQL | — | SQL `ProductVariants` |
| Reserved qty | Inventory | `InventoryConsistencyService` / `ProductVariantStockRepository` | SQL | — | SQL `ProductVariantStocks.ReservedQuantity` |
| Reservation | Inventory | `InventoryConsistencyService` / `ProductVariantStockRepository` | SQL | — | SQL `InventoryReservations` |
| Availability | Inventory (derived) | n/a (computed) | SQL derived | Mongo `StockQuantity` | `Quantity−Reserved` |
| Movement/ledger | Inventory | writers append-only | SQL | — | SQL `StockMovements` |
| Transfer | Inventory | `StockTransfersController`+`InventoryService` | SQL | — | SQL `StockTransfers` |
| Adjustment | Inventory | `AdminInventoryService` | SQL | — | SQL `StockMovements` |
| Opening balance | Inventory | `InventoryService` (bootstrap-only) | SQL | — | SQL `StockMovements` |
| Valuation | Inventory/Purchasing | `ProductVariant.CostPrice` | `ReportsController`/COGS snapshots | — | derived from `Variant.CostPrice` |
| SKU / Barcode | Product Catalog | Catalog | SQL | Mongo/ES | SQL `ProductVariants` |

## 30. Cross-Domain Authority Graph

| Peer | Inventory Reads | Inventory Writes | Events | Ownership / Sync |
|---|---|---|---|---|
| POS Runtime (Sales) | variant, stock | `Quantity` (Sale/SalesReturn) | stock-sync outbox | Inventory owns stock; POS drives deduction |
| Purchases | variant, stock | `Quantity` (Purchase/PurchaseReturn), `ProductVariant.CostPrice` | stock-sync outbox; legacy `StockReceivedEvent` is defined but not published | Inventory owns stock; variant cost basis co-written |
| CommerceHub / Storefront | stock, reservations | `ReservedQuantity`, `Quantity` (reserve/commit/return) | CommerceHub inbox in; stock-sync out | Inventory owns stock; CommerceHub/storefront own orders |
| Payment lifecycle | order/payment status | — (never writes payment) | calls `IPaymentLifecycleService` | Payment owns lifecycle; inventory defers |
| Accounting | — | — (no GL writes) | — | Accounting owns GL |
| Product Catalog | variant/product lineage | — (identity) | consumes catalog lineage | Catalog owns identity; inventory owns stock |
| Search (ES) | — | — | consumes stock-sync | Inventory→ES projection only |
| Mongo read store | — | — | consumes stock-sync | Inventory→Mongo projection only |
| SuperAdmin | reconciliation/repair triggers | via inventory services | — | governance, not a parallel store |

## 31. Business Invariants (implementation-backed)

1. **Variant+Warehouse uniqueness:** exactly one stock row per
   (TenantId, VariantId, WarehouseId) (`ProductVariantStockConfiguration.cs:60-62`).
2. **Non-negative stock:** `Quantity ≥ 0`, `Reserved ≥ 0`, `Damaged ≥ 0`,
   `Quantity ≥ Reserved` (DB CHECK) (`ProductVariantStockConfiguration.cs:18-30`).
3. **Available = Quantity − Reserved** (`ProductVariantStock.cs:50-53`).
4. **Lock-before-mutate** on every quantity change
   (`ProductVariantStockMutationSupport.cs:397-447`).
5. **Mutations require an explicit transaction**
   (`ProductVariantStockMutationSupport.cs:388-395`).
6. **Reservations run Serializable** (`InventoryConsistencyService.cs:2139-2141`).
7. **Opening balance is bootstrap-only** (`InventoryService.cs:294-311`).
8. **Movements are immutable audit records** (`AdminInventoryService.cs:23`;
   `StockMovement.cs:91-94`).
9. **Inbound event idempotency** by unique (TenantId, EventId)
   (`InventoryEventInboxConfiguration.cs:32-34`).
10. **Adjustment idempotency** by unique (TenantId, IdempotencyKey)
    (`StockMovementConfiguration.cs:79-81`).
11. **Fail-closed lineage authority** on mutation/reservation/projection
    (`ProductVariantInventoryLineageAuthority.cs:80-236`).
12. **Monotonic projection version** — cannot regress
    (`ProductVariantStock.cs:69-78`).
13. **Transfers are all-or-nothing** (transaction rollback on any item failure)
    (`StockTransfersController.cs:345-416`).

---

## 32. Architectural Decisions (WHY)

- **Why variant is the stock authority.** Multi-variant/multi-unit products need
  per-variant stock; keying stock on variant avoids ambiguous product-level totals
  and lets SKU/barcode identify the sellable unit
  (`ProductVariantStock.cs:8-16`; `InventoryService.cs:208-210`).
- **Why reservations exist (only on the order path).** Async online checkout has a
  window between intent and payment where stock must be held without being sold;
  synchronous POS sales have no such window, so they deduct immediately.
  Reservation implementations are split between CommerceHub events and
  storefront/admin-order payment lifecycle flows, but both mutate the same SQL
  truth (`InventoryConsistencyService.cs:122-291`;
  `ProductVariantStockRepository.cs`; `SalesController.cs:269-292`).
- **Why warehouse isolation exists.** Physical stock is location-bound; tenant +
  warehouse scoping enables multi-location fulfillment and prevents cross-location
  oversell (`ProductVariantStockConfiguration.cs:60-62`).
- **Why SQL remains authority.** Money-correctness (oversell = lost money or
  overselling) demands ACID + row locks + CHECK constraints that Mongo/Redis/ES
  cannot provide; projections are for read scale only
  (`ProductVariantStock.cs:13-15`; `AdminInventoryService.cs:19-25`).
- **Why the movement ledger exists.** Auditability and reconstructability: every
  quantity delta with before/after buckets makes drift detectable and stock
  rebuildable (`StockMovement.cs:48-66`;
  `CommerceHubInventoryReconciliationJob.cs`).
- **Why replay/outbox exists.** Decouples SQL commit from projection publish so
  stock truth is never blocked by downstream availability; version ordering keeps
  projections eventually consistent and stale-safe
  (`StockSyncOutboxProcessor.cs:440-507`).
- **Why fail-closed inventory.** A lost lineage or insufficient availability
  should stop the operation, never silently oversell or attach stock to stale
  identity (`ProductVariantInventoryLineageAuthority.cs:80-116`;
  `ProductVariantStockMutationSupport.cs:350-356`).

## 33. Technical Debt (evidence-backed)

1. **Retained legacy stock table.** `WarehouseStock` still exists as a
   "compatibility only" table alongside `ProductVariantStock`. Live affiliate/cart
   admission reads were migrated to `ProductVariantStock`; remaining risk is
   future accidental reads or writes unless the table is retired
   (`WarehouseStock.cs:5-7`; `ProductVariantStock.cs:13-15`).
2. **No FIFO/LIFO cost layers + no persisted valuation snapshot.** Current
   implementation uses variant-level weighted-average; valuation is a live
   recompute (`PurchasesController.cs:218-223`; `ReportsController.cs:253-329`).
3. **Movement-report cost source presentation can differ from current valuation.** `GetProductMovement`
   values `TotalValue = Price * |QuantityChange|` (per-movement base-unit cost),
   whereas valuation uses `Variant.CostPrice` — the two reports can disagree on unit
   value (`ReportsController.cs:582` vs `:277`).
4. **Dead `StockReceivedEvent`.** Defined (`InventoryEvents.cs:67`) but never
   published; live integration uses `ProductVariantStockChangedEvent` (no publish site).
5. **`InitialStock` enum value unused.** Opening stock writes `OpeningBalance` +
   `IsOpeningBalance=true`; no path writes `InitialStock` (`StockMovementType.cs:12-13`;
   `InventoryService.cs:324-325`).
6. **`allowNegativeStock` override exists but unused.** The mutation helper accepts
   `allowNegativeStock:true`; no inventory caller passes it, so it is latent
   surface area for accidental oversell if wired up
   (`ProductVariantStockMutationSupport.cs:335-386`).
7. **Reservation movement taxonomy is overloaded.** Reserve/release movements use
   `MovementType.Adjustment`; commit movements use `MovementType.Sale`; all are
   disambiguated by `InventoryEventType`. A reader ignorant of
   `InventoryEventType` can still misclassify reservation activity
   (`InventoryConsistencyService.cs:243`, `:253`; `ProductVariantStockRepository.cs`).
8. **Redis holds NO stock document.** Redis is cache-invalidation + admin
   idempotency only; the stock projection lives in Mongo/ES
   (`ProductVariantProjectionConsumer.cs:946`; `AdminInventoryService.cs:24-25`).
9. **`AdminVariantNotFoundException` TODO.** Adjustment throws
    `AdminProductNotFoundException` for a missing variant
    (`AdminInventoryService.cs:216`).

## 34. Implementation Follow-up Items

| Title | Current Implementation | Why | Future Improvement | Priority | Status |
|---|---|---|---|---|---|
| Retire `WarehouseStock` | Kept as compat table; reviewed affiliate/cart admission reads now use `ProductVariantStock` | Future accidental authority drift | Drop table after compatibility migration | High | Mitigated / Open |
| Consolidate reservation implementations | `InventoryConsistencyService` and `ProductVariantStockRepository` both mutate reservations | More surfaces to audit; repository path is not inbox-driven | Unify behind one reservation runtime or shared reservation mutation core | Medium | Open |
| Unify valuation cost basis | Purchase/return writes `ProductVariant.CostPrice`; reports/COGS read variant cost or immutable snapshots | Eliminates Product↔Variant valuation drift | Keep `Product.PurchasePrice` as simple-product mirror only | High | Complete |
| Persist valuation snapshots | Recomputed each report | No point-in-time value | Periodic valuation snapshot ledger | Medium | Open |
| Reconcile movement-report cost source | `Price` base-unit cost vs `Variant.CostPrice` | Two reports disagree on unit value | Single cost source for both | Medium | Open |
| Remove dead `StockReceivedEvent` | Defined, never published | Dead code / confusion | Delete or wire into stock-sync outbox | Low | Open |
| Deprecate unused `InitialStock` enum | Opening uses `OpeningBalance` | Ambiguity | Remove enum value | Low | Open |
| Gate `allowNegativeStock` | Latent parameter | Oversell risk if wired | Remove or centralise behind policy | Medium | Open |
| Distinct reservation movement type | Reserve/release use `Adjustment`; commit uses `Sale`; `InventoryEventType` disambiguates | Misclassification risk | Add explicit `Reservation` movement type | Low | Open |
| Add `AdminVariantNotFoundException` | Reuses product exception | Wrong error semantics | Add variant-specific exception | Low | Open |

## 35. Inventory Lifecycle (required)

```
Product → Variant → Warehouse → Opening Balance → Purchase → Available Stock
  → Reservation → Sale (commit) → Shipment → Return → Adjustment
  → Inventory Count → Analytics → Search Projection → Replay
```

| Step | Runtime Owner | Runtime/SQL Authority | Projection Authority | Events Published | Events Consumed | Sync / Replay | Failure Behaviour | Availability Gate |
|---|---|---|---|---|---|---|---|---|
| Product/Variant | Catalog | SQL `ProductVariants` | Mongo/ES | catalog events | — | catalog projection | reject stock on non-current lineage | lineage check |
| Warehouse | Inventory | SQL `Warehouses` | — | — | — | — | inactive→reject | `IsActive` |
| Opening Balance | Inventory | SQL `StockMovements`+stock | outbox→Mongo | stock-sync | — | bootstrap-only | reject if prior authority | positive qty |
| Purchase | Purchases+Inventory | SQL stock+`ProductVariant.CostPrice` | outbox | stock-sync; legacy `StockReceivedEvent` not published | — | outbox | rollback tx | n/a |
| Available Stock | Inventory (derived) | SQL `Quantity−Reserved` | Mongo `StockQuantity` | — | — | recompute from SQL | floored ≥0 | derived |
| Reservation | Inventory | SQL `ReservedQuantity`+`InventoryReservations` | — | stock-sync | inbox | Serializable+FOR UPDATE | insufficient→fail event | `available≥qty` |
| Sale/Commit | POS or CommerceHub | SQL stock | outbox | stock-sync | inbox | reconcile missing commits | rollback / fail | non-negative |
| Shipment | Fulfillment | (order ctx) | — | — | delivery events | — | defers | — |
| Return | Inventory | SQL stock+`Damaged` | outbox | stock-sync | inbox | reconcile missing receipts | bounded by returnable | returnable ≤ committed |
| Adjustment | Inventory | SQL stock | outbox | stock-sync | — | durable idempotency | reject negative | non-negative |
| Inventory Count | Inventory (Adjustment) | SQL stock | outbox | stock-sync | — | idempotent | reject negative | non-negative |
| Analytics | Inventory (read) | SQL | — | — | — | — | — | — |
| Search Projection | Inventory→ES | ES (read) | ES | — | stock-sync | event-fenced | held on stale version | — |
| Replay | Inventory jobs | SQL | Mongo/ES | — | — | reconcile+cleanup | logs mismatch | — |

---

## 36. Inventory Decision Matrix (required)

| Decision | Business Rule | Runtime Owner | Authority | Validation Layer | Failure Behaviour | Evidence |
|---|---|---|---|---|---|---|
| Can stock become negative? | No (default) | Inventory | SQL CHECK + app | app validate + DB CHECK | throw `InsufficientStockException` / DB reject | `ProductVariantStockMutationSupport.cs:335-386`; `ProductVariantStockConfiguration.cs:18-30` |
| Can stock be edited directly? | Only via locked mutation helpers | Inventory | `ProductVariantStock` | lock-before-mutate | throw if no tx / no lock owner | `ProductVariantStockMutationSupport.cs:388-395`, `:80-116` |
| Can warehouse change after posting? | No — movement is immutable | Inventory | `StockMovements` | append-only | n/a | `StockMovement.cs:91-94`; `AdminInventoryService.cs:23` |
| Can a reservation expire? | Yes | Inventory | `InventoryReservations` | `ExpiresAtUtc` | status→Expired, released by cleanup | `InventoryConsistencyService.cs:2156-2202`; `InventoryReservationCleanupJob.cs:66-84` |
| Can a transfer partially succeed? | No | Inventory | `StockTransfers` | one transaction | full rollback | `StockTransfersController.cs:345-416` |
| Can adjustment bypass validation? | No | Inventory | `ProductVariantStock` | validate after lock | throw | `AdminInventoryService.cs:243-247` |
| Can deleted/superseded variant own stock? | No | Inventory | lineage authority | `WhereInventoryReservableLineage` | reject/suppress | `ProductVariantInventoryLineageAuthority.cs:17-116` |
| Can inactive warehouse receive stock? | No | Inventory | lineage authority | `IsActive` check | throw / reject opening | `ProductVariantInventoryLineageAuthority.cs:94-101`; `InventoryService.cs:256-259` |
| Can inventory be rebuilt? | Yes (from ledger + reconcile) | Inventory | SQL | reconciliation job | logs mismatch, repairs | `CommerceHubInventoryReconciliationJob.cs:33-200` |
| Can a stock movement be deleted? | No (immutable audit) | Inventory | `StockMovements` | append-only | n/a | `StockMovement.cs:91-94` |
| Can valuation be recalculated? | Yes (derived read) | Inventory (read) | `ReportsController` | on-demand compute | n/a | `ReportsController.cs:253-329` |
| Can the same event apply twice? | No for CommerceHub event path; repository path is state/idempotency guarded by callers | Inventory | inbox / idempotency key / reservation state | unique constraints + state checks | dedupe or no-op already-handled rows | `InventoryEventInboxConfiguration.cs:32-34`; `StockMovementConfiguration.cs:79-81`; `ProductVariantStockRepository.cs` |
| Can oversell happen under concurrency? | No | Inventory | Serializable + FOR UPDATE | lock + re-check | fail event / rollback | `InventoryConsistencyService.cs:2139-2141`, `:186-190`; `ProductVariantStockRepository.cs` |

## 37. Architectural Synthesis

The Inventory Runtime instantiates the platform-wide Shopixy patterns as a single
coherent stock ledger:

- **Runtime Authority + SQL Authority:** PostgreSQL `ProductVariantStock` is the
  single mutable truth; everything else is derivative — mirroring the Merchant/
  CommerceHub rule that SQL owns money-critical state.
- **CQRS + Outbox + Projection:** writes hit SQL under locks; a durable,
  version-ordered `StockSyncOutboxMessage` fans out `ProductVariantStockChangedEvent`
  to Mongo (browse) and Elasticsearch (search), consumed with event fencing —
  identical in shape to the product/order projection backbones.
- **Repair-on-Read / Recompose from SQL:** projections and event payloads are
  recomposed from current SQL lineage owners; stale lineage is suppressed
  fail-closed, echoing the storefront SQL-authority overlay pattern.
- **Fail-Closed Design + Availability Gates:** insufficient availability, lost
  lineage, inactive warehouse, or superseded variant all stop the operation.
- **Cross-Domain Isolation / Ownership Boundaries:** inventory owns stock but
  defers payment lifecycle, order lifecycle, accounting GL, catalog identity, and
  shipping — reading their state but never writing it.
- **Idempotency everywhere:** inbound inbox dedup + outbound idempotent outbox +
  durable adjustment idempotency keys.

The *why*: inventory is where oversell becomes lost money, so the runtime trades
projection convenience for SQL-authoritative, lock-guarded, fail-closed,
ledger-audited correctness — and pushes all eventual-consistency concerns into the
replayable outbox/projection layer where mistakes are recoverable.

## 38. Certification

- **Implementation Confidence:** HIGH for variant-stock authority, mutation/lock
  discipline, reservation lifecycle, outbox/inbox idempotency where event-driven,
  CHECK constraints, and concurrency isolation (all read directly from source).
- **Architecture Completeness:** HIGH. All required sections covered from
  implementation; valuation and purchase/return paths corroborated by parallel
  implementation-only investigation of the purchase/return/valuation source files.
- **Runtime Authority Verification:** Verified `ProductVariantStock` is the sole
  mutable truth; live affiliate/cart admission reads now derive availability from
  `ProductVariantStock`; `WarehouseStock` remains legacy/compat only.
- **Cross-Domain Verification:** Verified inventory writes no payment/order/GL
  state; defers to `IPaymentLifecycleService` and reads order/payment status only.
- **Verified Capabilities:** variant stock authority; per-warehouse isolation;
  immutable movement ledger; reserve/commit/release/return lifecycle; expiry +
  cleanup + reconciliation; Serializable + FOR UPDATE concurrency; non-negative &
  over-reserve DB CHECKs; durable outbox with version ordering + advisory-lock
  claiming + backoff + SQL-authoritative compensation (no DLQ table); inbound inbox
  idempotency; storefront/admin-order repository reservations now append
  `StockMovement` rows; SQL-recomposed projections (variant consumer overwrites
  event from SQL, ES rebuilt from Mongo+SQL overlay); two-legged atomic transfers;
  bootstrap-only opening balance; variant-level incremental weighted-average
  purchase cost with immutable COGS snapshots.
- **Missing Capabilities:** FIFO/LIFO cost layers; persisted valuation snapshots;
  backorder support; a distinct reservation movement type; a Redis stock document.
- **Technical Debt Summary:** retained legacy stock table; split reservation
  implementations; no FIFO/LIFO or persisted valuation snapshots; movement-report
  historical cost presentation vs current valuation cost presentation; dead `StockReceivedEvent`;
  unused `InitialStock` enum; latent `allowNegativeStock`; overloaded reservation
  movement taxonomy; TODO variant-not-found exception (§33).
- **Architectural Observations:** The runtime uses one SQL stock truth with three
  live mutation surfaces: operational/back-office mutation, CommerceHub
  event-driven reservation, and storefront/admin-order reservation repository.
  The repository path now has ledger coverage, and the outbox/projection/
  reconciliation triad gives strong recoverability. The valuation authority split
  is resolved; the remaining valuation limitations are live recomputation and no
  FIFO/LIFO cost layers.

---

*Authored from direct implementation reads across the inventory source tree;
valuation, purchase/return, and CQRS/outbox paths corroborated by parallel
implementation-only investigation. Prior markdown/certification docs treated as
secondary evidence and re-verified in code.*
