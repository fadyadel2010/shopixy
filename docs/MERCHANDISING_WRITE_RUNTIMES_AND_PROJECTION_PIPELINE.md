# POS Admin Merchandising Write Runtimes & Publishing/Projection Pipeline

**Scope:** Implementation-backed reverse engineering of the MERCHANDISING WRITE RUNTIMES (Catalog, Pricing, Inventory, Coupon/Promotion/SmartOffer) owned by POS Admin, plus the full publishing (transactional outbox) pipeline and projection consumers (Mongo/Elasticsearch/Redis writers).

**Method:** Implementation is the ONLY source of truth. Every claim is cited as `path:line`. Comments/markdown were ignored except where explicitly labeled.

**Authority model (verified):** PostgreSQL (EF Core `ApplicationDbContext`) is the single authoritative write model. MongoDB, Elasticsearch, and Redis are derivative projections written by MassTransit consumers. Multi-tenant via `TenantId` on every aggregate and every event.

---

## 0. Core Runtime Primitives

### 0.1 Transactional Outbox
- `PointOfSale.Core/Entities/OutboxMessage.cs:19` — outbox row: `EventType` (:29), `EventPayload` JSON (:34), `TenantId` (:39), `AggregateId` (:44), `EventVersion` (:56, monotonic aggregate version for stale-event rejection), `ProcessedAtUtc` (:62), `RetryCount` (:67), `MaxRetries = 5` (:98), `RequiresOperatorReconciliation` (:78). At-least-once delivery, consumers must be idempotent (:14-17 doc).
- The outbox is MassTransit's own outbox (bus outbox), driven through `IPublishEndpoint`. `MassTransitEventPublisher` writes the outbox row in the same DbContext/transaction as the domain mutation.

### 0.2 Event publisher (the publish authority)
- `PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs:13` implements `IEventPublisher`.
- `PublishAsync<T>` → `PublishIntegrationEventAsync` (:35-38, :93).
- Tenant enrichment: `EnrichEventWithTenantContext` sets `TenantId` from `ITenantContext` if not set (:99, :295-312); warns if publishing without tenant (:304-311).
- `EnrichAuthoritativeOrderingMetadataAsync` (:122, :341) resolves the authoritative `EventVersion` from the aggregate's `ProjectionMutationVersion` (tracked → persisted), rejecting any mismatch (:391-409). This makes `EventVersion` deterministic per aggregate mutation for stale-event fencing.
- `EnsureTransactionalPublishBoundary` (:124, :324-339) THROWS if an **authoritative** event is published without an open transaction or pending business mutation — enforcing that the outbox row is atomic with the SQL state.
- `EnsureAuthoritativeTenantScope` (:123) enforces tenant scope on authoritative events.
- `CanFlushOutboxImmediately` (:314-322): if there is no open transaction AND no pending business mutation, `SaveChangesAsync` is called immediately to flush the outbox row (:157-160) — supports legacy "publish after SaveChanges" producers.
- On publish failure, only NEW outbox entities created by the failed publish are detached; pre-existing queued messages are preserved (:70-80, :142-152).
- `PublishSensitiveAsync` (:222) bypasses the outbox via direct `_bus.Publish` but REFUSES authoritative event types (:226-230).
- `PublishBatchAsync` (:165) rolls back the whole batch's new outbox entries on any failure (:199-213).
- Alternative impl: `PointOfSale.Infrastructure/EventBus/InMemoryEventPublisher.cs` (used in non-bus/test configs).

### 0.3 Projection version helper & fencing
- Aggregate carries `long ProjectionMutationVersion` with a monotonic guard, e.g. `Product` `PointOfSale.Core/Entities/Product.cs:210` + `AdvanceProjectionMutationVersion` (:216); `Promotion.cs:147` + guard (:154-163, throws on regression).
- `ProductEventVersionHelper.ToProductEventVersion(...)` converts the long mutation version into the event's `int EventVersion` (used throughout consumers and the publisher).
- `ProjectionEventFence` (`PointOfSale.Infrastructure/Consumers/ProjectionConsumerEventFence.cs`) — `TryAcquireAsync` / `CompleteAsync` provides per-(consumer, event-type, aggregate) idempotency + active-replay collapse. Used by ProductSearchIndexConsumer (:107), VisibilityLifecycle (:112), Navigation, TenantCurrency, etc.

---

## 1. CATALOG WRITE RUNTIME

### 1.1 Write ownership
There is **no ProductService**; product write authority lives in the controller `PointOfSale.API/Controllers/ProductsController.cs` (2498 lines). Variant sub-authority is delegated to `PointOfSale.API/Services/ProductVariantService.cs`.

SQL aggregates (owned tables in `ApplicationDbContext`):
- `Product` (`PointOfSale.Core/Entities/Product.cs`), `ProductVariant.cs`, `ProductVariantAttribute.cs`, `ProductMedia.cs`, `ProductUnit.cs`, `Unit.cs`, `Category.cs`, `Warehouse.cs`, `ProductVariantStock.cs`.
- Brand/Collection are NOT separate entities — `Brand` is a plain `string` column on `Product` (`Product.cs`, used at ProductsController.cs:366, 420). No Brand or Collection SQL table exists in `Entities/`.

### 1.2 CreateProduct — `ProductsController.cs:262-474`
1. `[HttpPost]`, `[Authorize(Roles="Owner,Admin,Manager")]` (:262-264).
2. Guards: `CategoryId != Guid.Empty` (:270-273); `CategoryLineageAuthority.RequireLiveCategoryAsync` verifies category is live with valid SQL parent lineage (:275-282, authority file `PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs`); barcode ownership conflict check via `ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync` (:284-299); SKU uniqueness via `ResolveUniqueSkuAsync` (:301-310).
3. Builds `Product` (:322-377). **Phase 11.5 rule:** if `HasVariants=true`, product is a container — barcode/SKU null, prices 0/null, `TrackStock=false`; variants own commerce fields (:328-345).
4. `BeginTransactionAsync` (:379), `_context.Products.Add` (:381).
5. `EnsureDefaultVariantAlignedAsync` creates a default variant so every product has a SKU-level record (:388-390).
6. `SaveChangesAsync` (:392) — SQL committed to change tracker.
7. **Publish inside the transaction, before commit:** `ProductCreatedEvent` (:406-440) with `EventVersion = ToProductEventVersion(product.ProjectionMutationVersion)` (:409). `OnlinePrice` is source-of-truth for storefront pricing; `SalePrice` is POS fallback (:415-416, event doc `ProductEvents.cs:39-50`).
8. `PublishVariantCreatedEventAsync(defaultVariant)` published AFTER the product event so `ProductCreatedEventConsumer` creates the Mongo `ProductDocument` first (:442-444).
9. `SaveChangesAsync` + `CommitAsync` (:446-447) — outbox rows atomic with SQL.
10. Overflow guard: `ArgumentOutOfRangeException` on version conversion rolls back the whole transaction and returns `409 EventVersionOverflow` (:449-468).
11. Audit + response (:470-473).

### 1.3 UpdateProduct — `ProductsController.cs:480-805`
- Same category/barcode/SKU guards (:487-551).
- Captures `previousPublishStatus`, `previousIsOnlineVisible`, `wasArchivedBeforeUpdate` (:562-564).
- Mutates entity fields incl. `PublishStatus`, `IsOnlineVisible`, `Brand`, prices (:610-629).
- **Dual outbox publish:**
  - Marketplace outbox: `_marketplaceEventOutbox.EnqueueAsync(Marketplace.ProductUpdatedEvent)` with full `ChangedFields` list (:666-682) — a separate marketplace outbox (`MarketplaceProjectionOutboxMessage`).
  - `SaveChangesAsync` (:684).
  - Storefront/POS outbox: `ProductUpdatedEvent` (:691-721) with `EventVersion` from `ProjectionMutationVersion` (:694).
  - If `PublishStatus` or `IsOnlineVisible` changed → `ProductPublishStatusChangedEvent` (:723-736).
  - Default-variant sync → `PublishVariantCreatedEventAsync` if created (:738-741) or `PublishDefaultVariantUpdatedAsync` if changed (:742-745).
  - If newly archived → `ProductArchivedEvent` (:747-755).
  - `SaveChangesAsync` + `CommitAsync` (:757-758).
- Same overflow rollback → `409` (:760-779).

### 1.4 DeleteProduct (soft delete) — `ProductsController.cs:813-905`
- `[Authorize(Roles="Owner,Admin")]` (:814).
- `_validationService.CanDeleteProductAsync` blocks delete if stock/history exists (:828-837).
- Transaction (:839); marks `IsDeleted=true, IsActive=false, IsOnlineVisible=false, PublishStatus=Archived` on product and all variants (:845-856).
- `SaveChangesAsync` (:858), then publishes `ProductDeletedEvent` (:860), `ProductArchivedEvent` (:866), and per-variant `ProductVariantDeletedEvent` + `VariantDeletedEvent` (:877-885), then `SaveChangesAsync` (:894).

### 1.5 Publish/visibility lifecycle authority
- **SQL state is the authority**, NOT a UI toggle. Visibility is the conjunction: `IsDeleted=false AND IsActive=true AND IsOnlineVisible=true AND PublishStatus=Published`.
- Enum `PublishStatus` (`PointOfSale.Core/Enums/PublishStatus.cs`): `Draft=0, Published=1, Archived=2`.
- Entity defaults: `Product.cs:179 PublishStatus=Draft`, `:184 IsOnlineVisible=false`, `:94 IsActive=true`, `:112 HasVariants=false`.
- The exact-conjunction gate is re-checked at projection time in `ProductVisibilityLifecycleProjectionConsumer.cs:130-136` and in `ProductSqlAuthorityProjectionWriter.cs:94` (`!IsActive || !IsOnlineVisible` ⇒ tombstone). So even a stale/incorrect event cannot make a product visible unless SQL currently proves it.

### 1.6 Variant / Unit / Category / Warehouse writes
- **Variants:** `ProductVariantService.cs` — `EnsureDefaultVariantAlignedAsync`, `PublishVariantCreatedEventAsync`; events in `PointOfSale.Core/Events/ProductVariantEvents.cs` (`ProductVariantCreatedEvent`, `ProductVariantUpdatedEvent`, `ProductVariantDeletedEvent`, `ProductVariantStockChangedEvent`). Controller `ProductVariantsController.cs`.
- **Units:** managed inline in ProductsController (`AddProductUnit` :1606, `UpdateProductUnit` :1734, `DeleteProductUnit` :1835) and `UnitsController.cs`. Multi-unit migration path `:1891`.
- **Categories:** `CategoriesController.cs` — `CreateCategory` (:186, `new Category` :225, `SaveChangesAsync` :236), `UpdateCategory` (:259), `DeleteCategory` (:316). NOTE: category writes call `SaveChangesAsync` directly and do **not** publish product events here; category naming denormalization to products flows separately. Lineage authority: `CategoryLineageAuthority.cs`.
- **Warehouses:** `WarehousesController.cs`; warehouse stock in `WarehouseStock.cs` / `ProductVariantStock.cs`.
- **Media:** `ProductMediaService.cs` + `ProductMediaController.cs`, event `ProductMediaChangedEvent` (`PointOfSale.Core/Events/ProductMediaChangedEvent.cs`).

---

## 2. PRICING WRITE RUNTIME

### 2.1 SQL price authority
- **Product/variant prices are columns on the SQL aggregates**, not a separate price-list aggregate:
  - `Product.SalePrice` (`Product.cs:61`) — POS/legacy price.
  - `Product.OnlinePrice` (`Product.cs:67`, nullable) — **storefront source of truth**; falls back to `SalePrice` if null/0 (`ProductEvents.cs:45-50`, consumer mapping `ProductCreatedEventConsumer.cs:211-212`).
  - `Product.PurchasePrice`, `PriceIncludesTax`, `TaxRate` (set at ProductsController.cs:335-339).
  - Per-unit pricing via `ProductUnit` (`ProductUnitData.SalePrice`, `ProductEvents.cs:9-17`).
  - Variant-level prices live on `ProductVariant` and are projected via `ProductVariantEvents`/`ProductVariantProjectionConsumer`.
- There is **no dedicated price-list / offer-price aggregate** in `Entities/`. Discount pricing is modeled via Promotions, Coupons, and SmartOffers (sections 4).
- `ProductPriceChangedEvent` (`ProductEvents.cs:211-217`, `OldPrice`/`NewPrice`) is a dedicated price-delta event consumed by `ProductStockPriceSearchIndexConsumer` (:44) and `ProductCacheInvalidationConsumer` (:41).

### 2.2 Pricing services (read/compute, not authority)
The `PointOfSale.API/Services/Pricing/` directory holds **computation** services used by the storefront read path, NOT write authority:
- `StorefrontPricingService.cs` (+ `StorefrontPricingOptions.cs`) — resolves effective storefront price (applies OnlinePrice fallback, promotions).
- `TaxCalculationService.cs` — tax computation.
- `ShippingCalculationService.cs` — shipping cost.
These read from projections / SQL; price mutation happens only through the Product/Variant write paths in section 1.

---

## 3. INVENTORY WRITE RUNTIME

### 3.1 Stock authority
- `ProductVariantStock` (`PointOfSale.Core/Entities/ProductVariantStock.cs:23`) is **the ONLY mutable stock source of truth** (explicitly stated `InventoryService.cs:15`).
  - `Quantity` (:38) sellable bucket, `ReservedQuantity` (:43), `DamagedQuantity` (:48).
  - `AvailableQuantity => Quantity - ReservedQuantity` (:53).
  - `TotalQuantity => Quantity + DamagedQuantity` (:58).
  - `ProjectionMutationVersion` (:63) — monotonic for stock event fencing.
  - Unique constraint enforced by migration `add_product_variant_stock_unique_constraint.sql`.
- `StockMovement` (`StockMovement.cs:15`) is the **append-only ledger** of every change: `MovementType` (:21), `QuantityChange` (:36), `QuantityAfter`/`AvailableAfter`/`ReservedAfter`/`DamagedAfter` snapshots (:51-66), `ReservationId` (:75), `ReferenceId`/`ReferenceNumber` (:71-77).
- `InventoryReservation` (`InventoryReservation.cs:6`) — order-scoped reservation aggregate: `ReservedQuantity`, `CommittedQuantity`, `ReleasedQuantity`, `ReturnedQuantity`, `DamagedReturnedQuantity` (:15-19); `Status` = `InventoryReservationStatus.Active` default (:20); `RemainingReservedQuantity => ReservedQuantity - CommittedQuantity - ReleasedQuantity` (:25).
- Other tables: `Warehouse.cs`, `WarehouseStock.cs`, `StockTransfer.cs` + `StockTransferItem.cs`, `InventoryEventInbox.cs` (idempotent inbox), `StockSyncOutboxMessage.cs`.

### 3.2 InventoryService — `PointOfSale.API/Services/InventoryService.cs`
- `AdjustStockAsync` (:36): resolves variant (`ProductVariantStockMutationSupport.ResolveVariantAsync` :39), **locks the stock row BEFORE mutation** (`LockStockForMutationAsync` :51-56) to serialize concurrent writers and prevent lost updates, validates change (`ValidateQuantityChange`, no negative stock :63-67), mutates `stock.Quantity` (:70), and appends a `StockMovement` (:73-92). Caller owns transaction/SaveChanges (doc :16-17).
- `TransferStockAsync` (:101): validates positive qty and distinct warehouses (:103-107), locks both source/target rows, records movements on both sides (:166-186).
- `SaveOpeningStockAsync` (:225), `GetStockLevelsAsync` (:211), `GetStockMovementsAsync` (:377).
- Hardening helpers: `PointOfSale.API/Services/Inventory/ProductVariantStockMutationSupport.cs` (row locking, validation) and `InventoryHardeningExtensions.cs`.
- POS quick adjust endpoint: `ProductsController.cs:1242 AdjustStock` → publishes `ProductStockChangedEvent` (via `ProductEvents.cs:222`) and marketplace `ProductUpdatedEvent` (:1383).

### 3.3 Reservation / release / commit
Reservations are created and committed at **checkout/payment** time, not by InventoryService:
- `PointOfSale.API/Services/Checkout/CheckoutService.cs` and `PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs` create `InventoryReservation` rows and increment `ProductVariantStock.ReservedQuantity`.
- `PointOfSale.API/Services/Payments/CheckoutInventoryAuthorityGuard.cs` is the authority guard verifying SQL reservation truth:
  - Loads reservations `FOR UPDATE` (Postgres) / `WITH (UPDLOCK,ROWLOCK,HOLDLOCK)` (SQL Server) — `LoadOrderReservationsForUpdateAsync` (:204-243).
  - Sums `RemainingReservedQuantity` per stock and asserts `ProductVariantStock.ReservedQuantity >= required` else throws (`ProductVariantStocksReservedQuantity`) (:260-298).
  - Only counts `Status == Active && RemainingReservedQuantity > 0` reservations (:168-183).
- `add_inventory_committed_to_payment_intent.sql` and `PAYMOB_INVENTORY_COMMITMENT_FIX.md` (impl-adjacent) confirm commit-on-payment.

### 3.4 InventoryConsistencyService (CommerceHub)
- `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs` — reconciles marketplace/CommerceHub reservations against `ProductVariantStock` authority.
- Companion reconciliation: `CommerceHubInventoryReconciliationJob.cs`, `CommerceHubPaymentRecoveryAuthorityService.cs`.
- Metrics: `PointOfSale.Infrastructure/Observability/InventoryReservationMetricsService.cs`.
- Hardening migration: `PointOfSale.Infrastructure/Migrations/Sql/20260326_ZZS_HardenCommerceHubInventoryConsistency.sql`.

### 3.5 Inventory events
`PointOfSale.Core/Events/InventoryEvents.cs`: `StockAdjustedEvent` (:8), `StockTransferredEvent` (:24), `StockReservedEvent` (:40), `StockReleasedEvent` (:53), `StockReceivedEvent` (:67). Product-facing stock projection uses `ProductStockChangedEvent` and `ProductVariantStockChangedEvent`.

---

## 4. COUPON / PROMOTION / SMARTOFFER WRITE RUNTIME

### 4.1 Coupon
- SQL aggregate `Coupon` (`PointOfSale.Core/Entities/Coupon.cs:7`): `Code` (private set, :28), `NormalizedCode` (:35, migration `add_coupon_normalized_code.sql`), `DiscountType`/`DiscountValue` (:45-50), `MinOrderAmount`/`MaxDiscountAmount` (:55-60), `MaxUsage`/`CurrentUsage`/`MaxUsagePerCustomer` (:65-75), `StartDate`/`EndDate` (:80-85), `IsActive` (:90), `FirstOrderOnly` (:95), scoped product/category id lists (:100-110), `uint RowVersion` optimistic concurrency (:22).
- Related SQL: `CouponUsage.cs`, `CouponReservation.cs`, `PromoCode.cs`/`PromoCodeUsage.cs`, `StorefrontCartCouponState.cs`.
- Write authority: `PointOfSale.API/Controllers/CouponsController.cs` (holds `IEventPublisher` :37).
  - `CreateCoupon` (:211): dup-code check (:250), `_context.Coupons.Add` (:260), **publish `CouponCreatedEvent` BEFORE `SaveChangesAsync`** (:264, :266) — outbox atomic.
  - `UpdateCoupon` (:282): dup check (:310), publish `CouponUpdatedEvent` before save (:354, :358).
  - `DeleteCoupon` (:379): publish `CouponDeletedEvent` before save (:406, :410).
  - `ToggleCouponStatus` (:430): publish `CouponUpdatedEvent` before save (:456, :460).
  - Builders: `BuildCouponCreatedEvent` (:501-560), `PublishCouponUpdatedEventAsync` (:561), `PublishCouponDeletedEventAsync` (:623).
- Events: `PointOfSale.Core/Events/CouponEvents.cs` (Created/Updated/Deleted/UsageChanged) + `CouponConsumedEvent.cs`.
- Validation/usage (SQL-backed, NOT projection): `PointOfSale.Core/Interfaces/ICouponValidationService.cs`, `PointOfSale.API/Services/Coupons/CouponUsageService.cs`, `CouponCustomerValidationRules.cs`. Accepted-coupon validation/pricing/reservation/usage stays SQL-backed (Program.cs:1741 comment).

### 4.2 Promotion (offers engine)
- SQL aggregate `Promotion` (`PointOfSale.Core/Entities/Promotion.cs`): `IsActive` (:89), `ProjectionMutationVersion` (:147) with monotonic guard (:154-163). Usage table `PromotionUsage.cs`. Ranking config via `add_promotion_ranking_config.sql`.
- Write authority: `PointOfSale.API/Controllers/PromotionsController.cs` (`IEventPublisher` :39).
  - `CreatePromotion` (:145): publish `PromotionCreatedEvent` before save (:213, :215).
  - `UpdatePromotion` (:231): publish `PromotionUpdatedEvent` before save (:309, :315).
  - `DeletePromotion` (:336): publish before save (:374, :381).
  - `TogglePromotionStatus` (:400): publish `PromotionUpdatedEvent` (:424, :430).
  - Builders: `BuildPromotionCreatedEvent` (:506), `PublishPromotionUpdatedEventAsync` (:533).
- Promotion computation engine (read/apply, not authority): `PointOfSale.API/Services/Promotions/PromotionEngine.cs` (+ `IPromotionEngine.cs`, `PromotionResult.cs`).
- Events: `PointOfSale.Core/Events/PromotionEvents.cs` (Created/Updated/Deleted/Expired/UsageChanged/Applied) + `PromotionAnalyticsEvents.cs`.

### 4.3 SmartOffer
- SQL aggregate `SmartOffer` (`PointOfSale.Core/Entities/SmartOffer.cs:11`): `Status` = `SmartOfferStatus.Inactive` default (:15), `Priority` (:16), `TriggerType`/`PagesScope`/`DelaySeconds`/`ScrollPercent` (:17-20), display caps (:21-22), popup content (:23-25), and an optional **generated coupon** linkage: `CouponCode` (:26), `GeneratedCouponId` (:27), coupon discount fields (:28-31).
- Write authority: `PointOfSale.API/Controllers/AdminSmartOffersController.cs` (`IEventPublisher` :26).
  - `CreateSmartOffer` (:128) → `SaveChangesAsync` (:185); `UpdateSmartOffer` (:199, save :271); `DeleteSmartOffer` (:284, save :312).
  - Publishes `SmartOfferCreatedEvent`/`SmartOfferUpdatedEvent`/`SmartOfferDeletedEvent` (:744-766) AND, because SmartOffers can mint a coupon, `CouponCreatedEvent`/`CouponUpdatedEvent`/`CouponDeletedEvent` (:777-823) for the generated coupon.
  - `SyncGeneratedCouponsToMongo` (:324) re-drives coupon projection via `SaveChangesAsync` (:346).
- Events: `PointOfSale.Core/Events/SmartOfferEvents.cs`. Redeem token security: `PointOfSale.API/Security/SmartOfferRedeemTokenService.cs`.

---

## 5. PUBLISHING PIPELINE — end-to-end product trace

### 5.1 Create flow (what actually happens)
1. `ProductsController.CreateProduct` opens a DB transaction (:379), inserts `Product` + default `ProductVariant` (:381-390), `SaveChangesAsync` (:392).
2. `_eventPublisher.PublishAsync(ProductCreatedEvent)` (:440) → `MassTransitEventPublisher.PublishIntegrationEventAsync` enriches tenant + correlation + idempotency (:99-120), resolves authoritative `EventVersion` from `ProjectionMutationVersion` (:122), enforces transactional boundary (:124), and calls `_publishEndpoint.Publish` which writes a **MassTransit outbox row in the same DbContext**.
3. `PublishVariantCreatedEventAsync(defaultVariant)` (:444) writes the variant event's outbox row AFTER the product event (ordering guarantee).
4. `SaveChangesAsync` + `transaction.CommitAsync` (:446-447) — SQL rows and outbox rows commit atomically.
5. MassTransit outbox delivery publishes each event to RabbitMQ; consumers run.

### 5.2 Consumer fan-out for `ProductCreatedEvent`
Registered at `Program.cs:1670-1802`. Three consumers subscribe to `ProductCreatedEvent`:
- **`ProductCreatedEventConsumer`** (Mongo write) — `ProductCreatedEventConsumer.cs:30`. Validates TenantId (:61-73) and ProductId (:76-84); maps event → `ProductDocument` (`MapToDocument` :175-253, `OnlinePrice` truth :212); idempotent upsert via `IProductSqlAuthorityProjectionWriter.ApplyAsync` (:88-96). Writes Mongo collection **`products`**.
- **`ProductSearchIndexConsumer`** (ES write) — `ProductSearchIndexConsumer.cs:35`. Acquires `ProjectionEventFence` (:107); **reads the Mongo `products` doc first** and throws `ProjectionNotReadyException` to retry if Mongo hasn't caught up (:124-138) — enforces Mongo-before-ES ordering; overlays SQL authority (`ProductSearchSqlAuthorityOverlay.ApplyAsync` :140); if product missing / not `IsActive` / not `IsOnlineVisible` → `_indexService.DeleteAsync` (:146-182); else maps (`ProductSearchDocumentMapper.MapFromProductDocument` :184) and `_indexService.IndexAsync` (:194). Writes the **products ES index**.
- **`ProductCacheInvalidationConsumer`** (Redis) — subscribes to Created/Updated/Deleted/StockChanged/PriceChanged/PublishStatusChanged (`.cs:37-42`); invalidates Redis JSON cache keys via `IRedisJsonCacheService.InvalidateAsync` (:177, :213, :244) using `RedisSettings.InstanceName` (:265).

Ordering within create: Mongo (`ProductCreatedEventConsumer`) must land before ES (`ProductSearchIndexConsumer`) — enforced by the ProjectionNotReady retry, not by broker ordering.

### 5.3 Consumer fan-out for `ProductUpdatedEvent`
- **`ProductUpdatedEventConsumer`** (`.cs:14`) — Mongo. Reads current Mongo projection or seeds a new one (:65-73), then **recomposes the full document from SQL authority** via `IProductSqlAuthorityProjectionWriter.ApplyAsync` (:75-83). Dispositions: `Upserted` / `Tombstoned` / `SkippedStale*` (:85-115). This is **repair-on-read / SQL-recompose**: the event is only a trigger; the doc content is rebuilt from SQL truth.
- **`ProductUpdatedSearchIndexConsumer`** (`.cs:29`) — ES. Reads Mongo `products` (:46), deletes ES doc if hidden/missing (:129-150), else `IndexAsync` (:167).
- **`ProductCacheInvalidationConsumer`** — Redis invalidation (as above).

### 5.4 Visibility / delete lifecycle
- **`ProductVisibilityLifecycleProjectionConsumer`** (`.cs:19-21`) subscribes to `ProductDeletedEvent`, `ProductArchivedEvent`, `ProductPublishStatusChangedEvent`. It **re-reads SQL** (`IgnoreQueryFilters`, :78-93) and applies the exact visibility conjunction (:130-136): if SQL proves visible → recompose Mongo doc; otherwise → **reviewed visibility tombstone** (:180-190). SQL is authoritative even for lifecycle events (`SqlTruthRebuildGuard.RequireSqlAuthority` :95-99).
- **`ProductDeletedSearchIndexConsumer`** (`.cs:23-25`) — ES: same event trio; indexes or deletes from ES based on SQL/Mongo truth (:141-153).

### 5.5 The SQL-authority projection writer (repair-on-read core)
- `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:42`.
- `ApplyAsync` validates tenant/product match (:59-79), then calls `ProductSearchSqlAuthorityOverlay.ApplyAsync` to reload authoritative SQL fields (:88-92).
- If SQL projection is null / `!IsActive` / `!IsOnlineVisible` → writes a **ReviewedVisibilityTombstone** through `IStorefrontProductProjectionMutationBoundary.ApplyAsync` (:94-112).
- Else stamps `LastEventId/LastEventOccurredAtUtc/LastEventVersion` and writes a **ReviewedFullDocumentUpsert** (:114-128).
- Stale writes rejected via version comparison → `SkippedStale*` (:108-133). This is the single choke point that guarantees Mongo can never contradict SQL.

### 5.6 Stock & price projection
- **`ProductStockChangedConsumer`** (`.cs:27-28`, subscribes `ProductStockChangedEvent` + `ProductVariantStockChangedEvent`) — updates Mongo `products` stock fields with stale-guard on `LastStockEventVersion` (:90-93, :280-307).
- **`ProductStockPriceSearchIndexConsumer`** (`.cs:43-45`, subscribes `ProductStockChangedEvent`, `ProductVariantStockChangedEvent`, `ProductPriceChangedEvent`) — waits for Mongo catch-up (`ProjectionNotReadyException` :309-316, :457-464), then re-indexes/deletes in ES (:336-501).
- **`ProductVariantProjectionConsumer`** (`.cs:45-48`, subscribes VariantCreated/Updated/Deleted/StockChanged) — embeds variants into Mongo `products` doc (`Variants[]`, `GetCollection<ProductDocument>("products")` :70) AND invalidates Redis (`IConnectionMultiplexer` :71, instance name :73). Rejects events not backed by SQL variant/media authority (:129-141, :342-349).
- **`ProductMediaChangedEventConsumer`** (`.cs:15`) — updates Mongo `products` `Media[]` with `LastMediaEventVersion` stale-guard (:118, :318-327), then re-indexes ES (`_searchIndexService.IndexAsync` :210). SQL media authority check (:77-87).

---

## 6. PROJECTION CONSUMER CATALOG (merchant/catalog scope)

Registration: `PointOfSale.API/Program.cs:1665-1802` (+ marketplace/affiliate 1867-1922). Each consumer is idempotent, TenantId-validated, and event-version fenced. "Repair-on-read" = re-reads SQL and rebuilds the doc rather than trusting event payload.

| Consumer | Event(s) consumed | Writes to | Repair/rebuild |
|---|---|---|---|
| `ProductCreatedEventConsumer` | ProductCreatedEvent | Mongo `products` | Yes — via `ProductSqlAuthorityProjectionWriter` (SQL overlay) |
| `ProductUpdatedEventConsumer` | ProductUpdatedEvent | Mongo `products` | Yes — SQL full-recompose (`.cs:75-83`) |
| `ProductVisibilityLifecycleProjectionConsumer` | ProductDeleted, ProductArchived, ProductPublishStatusChanged | Mongo `products` (upsert/tombstone) | Yes — re-reads SQL w/ IgnoreQueryFilters (`.cs:78-93`) |
| `ProductSearchIndexConsumer` | ProductCreatedEvent | ES products index | Yes — Mongo-gated + SQL overlay (`.cs:124-182`) |
| `ProductUpdatedSearchIndexConsumer` | ProductUpdatedEvent | ES products index | Reads Mongo, delete-if-hidden (`.cs:129-167`) |
| `ProductDeletedSearchIndexConsumer` | ProductDeleted, ProductArchived, ProductPublishStatusChanged | ES products index | Index/delete by SQL truth (`.cs:141-153`) |
| `ProductStockChangedConsumer` | ProductStockChanged, ProductVariantStockChanged | Mongo `products` (stock fields) | Stale-guard `LastStockEventVersion` (`.cs:90-93,280-307`) |
| `ProductStockPriceSearchIndexConsumer` | ProductStockChanged, ProductVariantStockChanged, ProductPriceChanged | ES products index | Mongo catch-up gate (`.cs:309-316,457-464`) |
| `ProductVariantProjectionConsumer` | VariantCreated/Updated/Deleted/StockChanged | Mongo `products.Variants[]` + Redis invalidation | SQL variant/media authority reject (`.cs:129-141,342-349`) |
| `ProductMediaChangedEventConsumer` | ProductMediaChangedEvent | Mongo `products.Media[]` + ES re-index | Stale-guard `LastMediaEventVersion` (`.cs:318-327`) |
| `ProductCacheInvalidationConsumer` | ProductCreated/Updated/Deleted/StockChanged/PriceChanged/PublishStatusChanged | Redis (invalidate JSON cache keys) | Invalidation-only (`.cs:177,213,244`) |
| `ProductPromotionSearchIndexConsumer` | PromotionCreated/Updated/Deleted/Expired | ES products index (promotion fields for boosting) | SQL promotion authority reject (`.cs:245-255`); version from `ProjectionMutationVersion` (:516) |
| `CouponProjectionConsumer` | CouponCreated/Updated/Deleted/UsageChanged/Consumed | Mongo `CouponDocument` collection (`.cs:54`) | Stale-guard `LastEventVersion` (`.cs:455,650`); tombstone on delete (:543) |
| `PromotionProjectionConsumer` | PromotionCreated/Updated/Deleted/Expired/UsageChanged/Applied | Mongo `PromotionDocument` (:66), plus updates `products` (:67) & `categories` (:68) docs w/ promo badges | Rejects if not SQL-backed (`.cs:112,187,337,400,473`); version from SQL `ProjectionMutationVersion` (:353,734) |
| `SmartOfferProjectionConsumer` | SmartOfferCreated/Updated/Deleted | Mongo `storefront_configs` doc `.SmartOffers[]` (`.cs:27,59`) | Full re-sync from SQL `_dbContext.SmartOffers` (`.cs:46-60`) — rebuild-on-event |
| `TenantCurrencyProjectionConsumer` | TenantCurrencyChangedEvent | Mongo `storefront_configs` `.Currency` (:42) + Redis invalidation (:154,187) | Stale-guard `LastCurrencyEventVersion` (`.cs:102,234) |
| `NavigationProjectionConsumer` | Navigation/StoreConfig events | Mongo `storefront_navigation` (:44) + Redis invalidation (:196-215) | Stale-guard `LastEventVersion` (`.cs:152,369`); fence (:94) |
| `CmsPageProjectionConsumer` | CmsPage events | Mongo `storefront_pages` (:35) + Redis (:594) | Stale-guard `LastEventVersion` (`.cs:429,534`) |
| `StorefrontConfigProjectionConsumer` | StoreConfig/StoreProfile events | Mongo `storefront_configs` | SQL-driven upsert |
| `StorefrontFooterProjectionConsumer` | Footer/homepage-section events | Mongo storefront footer doc | SQL-driven |
| `ShippingMethodProjectionConsumer` | ShippingMethod events | Mongo shipping methods | SQL-driven |
| `PaymentMethodProjectionConsumer` | PaymentMethod events | Mongo payment methods (NO secrets, Program.cs:1733) | SQL-driven |

Related marketplace/affiliate (out of core catalog but adjacent): `MarketplaceProductUpdatedConsumer` (Program.cs:1922), `AffiliateProductPolicyProjectionConsumer` (:1867), `AffiliateProductIntelligenceProjectionConsumer` (:1873), `CommerceHubProjectionConsumer`/`CommerceHubOrderProjectionConsumer` (:1794-1795).

### 6.1 Mongo collections written (catalog scope)
- `products` (ProductDocument, incl. embedded `Variants[]`, `Media[]`, stock, promo badges)
- `categories` (CategoryDocument — promo badge updates)
- `CouponDocument` collection, `PromotionDocument` collection
- `storefront_configs` (SmartOffers[], Currency, config), `storefront_navigation`, `storefront_pages`

### 6.2 Elasticsearch indices
- products search index (via `IProductSearchIndexService.IndexAsync/DeleteAsync`) — created, updated, stock/price, promotion, media, deleted/visibility consumers all converge here.

### 6.3 Redis
- Product JSON cache keys (invalidated by `ProductCacheInvalidationConsumer`, `ProductVariantProjectionConsumer`), navigation cache, storefront config/currency cache. Keyed by `RedisSettings.InstanceName`.

---

## 7. KEY ARCHITECTURAL INVARIANTS (evidence-backed)
1. **PostgreSQL is the only write authority.** All merchandising mutations go through EF `ApplicationDbContext`; Mongo/ES/Redis are never written directly by controllers.
2. **Transactional outbox atomicity.** `MassTransitEventPublisher` writes outbox rows inside the same DbContext/transaction as the SQL mutation; authoritative events throw if published outside a transaction (`MassTransitEventPublisher.cs:324-339`).
3. **Deterministic stale-event rejection** via `EventVersion` derived from aggregate `ProjectionMutationVersion` (publisher :341-409; every consumer version-guards).
4. **Repair-on-read / SQL recompose.** Update/lifecycle consumers rebuild the projection from current SQL truth rather than trusting the event body (`ProductSqlAuthorityProjectionWriter`, `ProductUpdatedEventConsumer`, `ProductVisibilityLifecycleProjectionConsumer`).
5. **Visibility is a SQL conjunction**, re-verified at projection time — never a naked UI toggle.
6. **Ordering across stores** (Mongo→ES) enforced by `ProjectionNotReadyException` retry, not broker guarantees.
7. **Idempotency** via TenantId/ProductId validation, `ProjectionEventFence`, and `LastEvent*Version` guards in every consumer.
