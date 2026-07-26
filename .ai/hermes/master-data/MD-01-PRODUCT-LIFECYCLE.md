# MD-01 — Product Lifecycle (Implementation-Backed Canonical Architecture)

> Discipline: implementation is the ONLY source of truth. Every statement below is grounded in the
> cited source file:line. UI labels, DTO names, comments, controller names, and prior narrative docs
> are secondary evidence. Where code contradicts narrative, the code wins.
>
> Companion doc: `.ai/hermes/platform/product-catalog-architecture.md` (the catalog CQRS/projection
> view). This doc (MD-01) is the **master-data lifecycle** view: identity, aggregates, mutations,
> SQL transaction semantics, and the full Product relationship graph.

---

## 0. Executive Summary — What is a Product?

A **Product** (`PointOfSale.Core/Entities/Product.cs:9`) is the **catalog/parent aggregate** in
PostgreSQL. It is NOT the sellable unit. It owns:

- Display, SEO, and category placement (`Name`, `NameAr`, `Slug`, `MetaTitle`, `CategoryId`).
- A **publication/visibility** state machine (`PublishStatus`, `IsActive`, `IsOnlineVisible`,
  `IsSearchable`, `IsFeatured`, `AllowBackorder`).
- For **simple products** (`HasVariants == false`): the commerce mirror fields (`PurchasePrice`,
  `SalePrice`, `OnlinePrice`, `Barcode`, `SKU`) are kept 1:1 in sync with the single auto-created
  **default variant** (`ProductsController.cs:328-344`, `ProductVariantService.cs:251-279`,
  `ProductVariantCloneImportAuthority.cs:255-266`).
- For **variant products** (`HasVariants == true`): `Product.SalePrice=0`, `OnlinePrice=null`,
  `PurchasePrice=0`, `Barcode=null`, `SKU=null`, `TrackStock=false`. **All commerce fields live on
  the variants** (`ProductsController.cs:328-344`).

Why does it exist? To group one or more sellable **ProductVariants** under a single SEO/category
identity and a single default-variant mirror, so the storefront shows one "product page" while the
warehouse, pricing engine, and checkout operate on the **variant SKU** (`OrderItem.cs:8-13`).

How is it implemented? As an EF Core entity (`Products` table) implementing `ITenantOwned` +
`IProjectionVersioned`, written inside an explicit SQL transaction that also enlists MassTransit
outbox events (`ProductsController.cs:379-468`).

### How Product differs from its neighbours (implementation only)

| Concept | Implementation | Difference from Product |
|---------|---------------|--------------------------|
| **Variant** | `ProductVariant` (`ProductVariant.cs`) — own GUID PK, own `TenantId`, own SKU/barcode/price/stock/events | The SELLABLE SKU. Product is the container; Variant is what is actually sold, priced, stocked (`OrderItem.cs:9-10`). |
| **Category** | `Category` (`Category.cs`) — self-referencing tenant-owned tree | A classification node. Product references exactly one live category by `CategoryId` (`Product.cs:50`). A product is not a category; a category owns zero products as authority (`CategoryLineageAuthority.cs:32-40`). |
| **Unit** | `Unit` (`Unit.cs`) — base measure (Piece/Kg/Liter) | A unit-of-measure master. Product binds to it via `ProductUnit` (`ProductUnit.cs`) and the free string `Product.Unit` for legacy. Unit is not stocked; it converts quantities. |
| **Warehouse Stock** | `WarehouseStock` (`WarehouseStock.cs`) — product/legacy-compat | **NOT an inventory authority.** `ProductVariantStock` is the source of truth per `VariantId+WarehouseId` (`ProductVariantStock.cs:13-16`). |
| **Inventory Adjustment** | mutation through `ProductVariantStockMutationSupport` + `ProductVariantStockChangedEvent` | A write-path operation on `ProductVariantStock`, not a stored document. Adjustment is an event, not an entity. |
| **ProductImage / Media** | `ProductMedia` (`ProductMedia.cs`) — product-scoped gallery | A child list of the Product; variants point into it via `MediaId` (`ProductVariant.cs:117-121`). Product is the owner of the gallery. |

---

## 1. Discovery Phase — artifacts found in code

| Artifact | File | Table |
|----------|------|-------|
| `Product` | `PointOfSale.Core/Entities/Product.cs` | `Products` |
| `ProductVariant` | `PointOfSale.Core/Entities/ProductVariant.cs` | `ProductVariants` |
| `ProductVariantStock` | `PointOfSale.Core/Entities/ProductVariantStock.cs` | `ProductVariantStocks` |
| `ProductVariantAttribute` | `PointOfSale.Core/Entities/ProductVariantAttribute.cs` | `ProductVariantAttributes` |
| `ProductMedia` | `PointOfSale.Core/Entities/ProductMedia.cs` | `ProductMedia` |
| `ProductUnit` | `PointOfSale.Core/Entities/ProductUnit.cs` | `ProductUnits` |
| `Unit` | `PointOfSale.Core/Entities/Unit.cs` | `Units` |
| `WarehouseStock` | `PointOfSale.Core/Entities/WarehouseStock.cs` | `WarehouseStocks` (legacy/compat) |
| `Category` | `PointOfSale.Core/Entities/Category.cs` | `Categories` |
| `ProductCreatedEvent` … `ProductPublishStatusChangedEvent` | `PointOfSale.Core/Events/ProductEvents.cs` | events |
| `ProductVariantCreated/Updated/Deleted/StockChanged/PriceUpdatedEvent` | `PointOfSale.Core/Events/ProductVariantEvents.cs` | events |
| `ProductMediaChangedEvent` | `PointOfSale.Core/Events/ProductMediaChangedEvent.cs` | event |
| Write controllers | `ProductsController.cs`, `ProductVariantsController.cs`, `ProductMediaController.cs` | API |
| Services | `ProductVariantService.cs`, `ProductMediaService.cs`, `CategoryLineageAuthority.cs`, `ProductVariantStockMutationSupport.cs` | — |
| Authority guards | `ProductVariantCloneImportAuthority.cs`, `ProductVariantMergeSplitAuthority.cs` | — |
| Projection consumers | `ProductVariantProjectionConsumer.cs`, `ProductReadModelConsumer.cs`, `ProductSearchIndexConsumer.cs`, `ProductStockChangedConsumer.cs`, `ProductCacheInvalidationConsumer.cs`, `ProductMediaChangedEventConsumer.cs` | — |
| EF configs | `ProductConfiguration.cs`, `ProductVariantConfiguration.cs`, `ProductVariantStockConfiguration.cs`, `ProductMediaConfiguration.cs`, `ProductVariantAttributeConfiguration.cs` | — |
| Outbox/stocks | `ProductVariantStockRepository.cs`, `ProductVariantStockMutationSupport.cs` | — |

No dedicated `Brand`, `Attribute`, `ProductPrice`/`PriceTier`/`Wholesale`, or `Barcode` entity exists.
Brand/Manufacturer are free-text strings on `Product` (`Product.cs:234-239`); attributes are
denormalized name/value pairs on the variant; pricing is fully columnar (variant + unit). Any doc
implying a brand master, attribute taxonomy, or price-list table is **not backed by implementation**.

---

## 2. Product Identity (reverse-engineered)

| Aspect | Implementation evidence |
|--------|-------------------------|
| **Primary Key** | `Guid Id` (`BaseEntity.cs:8`), `ProductConfiguration.cs:11 HasKey(p => p.Id)`. |
| **Business Identity** | `SKU` (`Product.cs:25`) — but ONLY populated for simple products; forced `null` for variant products (`ProductsController.cs:329-330`). For variant products, business identity is the variant's SKU (`ProductVariant.cs:44`). |
| **Barcode** | `Product.Barcode` (`Product.cs:24`) — simple-products only; null when `HasVariants` (`ProductsController.cs:329`). Variant barcode is authoritative (`ProductVariant.cs:50`). Barcode search is variant-aware: `GET api/products/barcode/{barcode}` checks product first, then active variant (`ProductsController.cs:201-249`). |
| **Slug** | `Product.Slug` (`Product.cs:121`) — SEO URL fragment; stored SQL column (NOT computed). |
| **Search identity** | Name/SKU/Barcode `ILike` on `Products`, plus Mongo/Elasticsearch derived projections (`ProductsController.cs:78-84`, `:146-150`). |
| **Tenant identity** | `TenantId` NOT NULL, `ITenantOwned` (`Product.cs:15`, `ITenantOwned.cs`). Enforced by EF global query filter + DB NOT NULL. |
| **Soft delete** | `BaseEntity.IsDeleted` (`BaseEntity.cs:13`). Product soft-deleted; `ProductDeletedEvent` drives Mongo tombstone + ES delete + cache invalidation. |
| **Activation** | `IsActive` bool (`Product.cs:94`) — hard on/off, gates POS + storefront. |
| **Visibility** | `PublishStatus` (Draft/Published/Archived, `Product.cs:179`), `IsOnlineVisible` (`Product.cs:184`), `IsSearchable` (`Product.cs:189`), `IsFeatured` (`Product.cs:194`), `AllowBackorder` (`Product.cs:204`). |
| **Uniqueness** | SKU index `IX_Products_SKU` (`ProductConfiguration.cs:65`) and Barcode index `IX_Products_Barcode` (`:64`), non-unique. Cross-product + live-variant **uniqueness of SKU/barcode** is enforced by `ProductVariantCloneImportAuthority.IsSkuOwnedByAnotherSemanticEntityAsync`/`IsBarcodeOwnedByAnotherSemanticEntityAsync` (`ProductVariantCloneImportAuthority.cs:48-179`), checked in Create AND Update. |
| **Business rules** | `CategoryId` MUST NOT be `Guid.Empty` (entity/config/controller triple — `Product.cs:46-50`, `ProductConfiguration.cs:53-55`, `ProductsController.cs:269-273`). Category must be a **live** SQL lineage node (`CategoryLineageAuthority.RequireLiveCategoryAsync`, `ProductsController.cs:275-282`). |

---

## 3. Product Lifecycle (reverse-engineered, real steps)

```
Create Product (POST api/products)
  ├─ validate CategoryId != Guid.Empty; category must be live in SQL lineage
  ├─ (simple) resolve unique SKU; (variant) SKU/Barcode/Price = null
  ├─ begin SQL transaction
  ├─ INSERT Product (Products)
  ├─ EnsureDefaultVariantAlignedAsync → INSERT default ProductVariant (mirrors SKU/Barcode/Price for simple)
  ├─ SaveChangesAsync
  ├─ publish ProductCreatedEvent + ProductVariantCreatedEvent (outbox, same tx)
  ├─ commit
  ▼
Assign Units (POST product units / ProductUnit rows; ConversionFactor)
Assign Categories (already required at create; reassigned on update)
Upload Images (POST api/admin/products/{id}/media → ProductMedia rows; emits ProductMediaChangedEvent)
  ▼
Create Variants (POST api/products/{id}/variants → ProductVariant + ProductVariantAttribute + ProductVariantStock)
  - variant products only (simple products reject extra variant creation: "SimpleProductAdditionalVariantForbidden")
  - idempotency-keyed; attribute-combo uniqueness enforced
  ▼
Assign Prices (variant: Price/OnlinePrice/CostPrice; product mirror for simple; ProductUnit per-unit price)
Generate Barcode (supplied by caller; auto-collided SKU "AUTO-…" fallback — no code-generated barcode)
  ▼
Publish (PublishStatus=Published + IsOnlineVisible=true)
  → ProductPublishStatusChangedEvent; SQL-authority writer upserts/tombstones Mongo; ES indexed/deleted; Redis invalidated
  ▼
Available for Purchase (storefront: IsActive && Published && IsOnlineVisible && visible variant with price>0)
Available for Sale (POS: IsActive && TrackStock? stock check via ProductVariantStock)
  ▼
Inventory (ProductVariantStock per WarehouseId; mutations via ProductVariantStockMutationSupport → ProductVariantStockChangedEvent)
  ▼
Reports (sales/purchase/cart read OrderItem.VariantId snapshot; analytics read projections)
```

Phases observed in code: PHASE 11.1 (variants + variant stock), 11.2 (variant mgmt API), 11.3
(Mongo variant projection), 11.4 (variant-based checkout/reservation — `OrderItem.cs:6-11`), 11.5
(Shopify-grade authoring / `HasVariants` switch), 11.6 (variant-aware barcode + cart enforcement —
`ProductEvents.cs:113-115`). Legacy `Product.SalePrice`/`PurchasePrice` retained for backward
compatibility (`ProductVariant.cs:21-25`).

---

## 4. Aggregate Analysis

| Aggregate | Root | Mutations (write path) | Authority |
|-----------|------|------------------------|-----------|
| **Product** | `Product` | Create/Update/Delete in `ProductsController` (in explicit tx + outbox) | PostgreSQL (system of record) |
| **ProductVariant** | `ProductVariant` | Create/Update/Delete in `ProductVariantsController`; default-variant sync in `ProductVariantService` | PostgreSQL; unique filtered indexes own SKU/barcode/attr-combo identity |
| **ProductVariantStock** | `ProductVariantStock` | `ProductVariantStockMutationSupport` (pessimistic lock + check constraints) | PostgreSQL; `CHECK` constraints + unique `(TenantId,VariantId,WarehouseId)` |
| **ProductMedia** | `ProductMedia` | `ProductMediaController`/`ProductMediaService` | PostgreSQL; `ProductMediaChangedEvent` rebuilds Mongo `Media[]` |
| **ProductUnit** | `ProductUnit` | product-unit endpoint / `ProductUnit` rows | PostgreSQL; per-unit `ConversionFactor`/`SellingPrice`/`PurchasePrice` |
| **ProductVariantAttribute** | child of variant | created with variant; `IsDefault`/merge-split mutate parent variant | PostgreSQL; unique `(VariantId,Name)` filtered |
| **Category** | `Category` | category controller | PostgreSQL; `CategoryLineageAuthority` validates live lineage |
| **Search (ES)** | `ProductDocument`(Mongo) → ES index | `ProductVariantProjectionConsumer` + `ProductStockPriceSearchIndexConsumer` (SQL-recomposed) | Derived; **never authoritative** |
| **Media projection (Mongo)** | `ProductDocument.Media[]` | `ProductMediaChangedEventConsumer` | Derived; SQL-recomposed |

**Mutation ordering rule (deadlock prevention):** multi-row stock locks are acquired in deterministic
order `(WarehouseId, VariantId)` (`ProductVariantStockMutationSupport.cs:300-304`).
**Negative-stock protection:** validation AFTER lock, BEFORE mutation
(`ValidateAvailableQuantity`/`ValidateQuantityChange`, `:335-386`); DB `CHECK` constraints back it
(`ProductVariantStockConfiguration.cs:18-30`).

---

## 5. Variant Architecture (mandatory)

**Why variants exist:** A product page must show one identity, but commerce needs multiple sellable
SKUs (sizes/colors) each with its own price, stock, barcode, and image. The variant is the sellable
unit; the product is the container (`ProductVariant.cs:8-19`, `OrderItem.cs:8-13`).

**Variant identity:** own `Guid Id`, own `TenantId` (`ProductVariant.cs:26-37`). `SKU` and `Barcode`
are variant-owned; for simple products they mirror the product
(`ProductVariantCloneImportAuthority.cs:255-266`).

**Variant attributes:** `ProductVariantAttribute` (Name/Value/SortOrder) (`ProductVariantAttribute.cs`).
`AttributeCombinationKey` = SHA-256 of normalized, sorted attribute set, unique per
`(TenantId, ProductId)` (`ProductVariantAttributeCombinationKey.cs`, `ProductVariantConfiguration.cs:104-107`). Duplicate attribute *names* within a variant are rejected (`ProductVariantAttributeConfiguration.cs:38-42`).

**Default variant (mandatory):** Every product MUST have exactly one `IsDefault=true` **current**
variant. Enforced by unique filtered index `IX_ProductVariants_ProductId_IsDefault`
(`ProductVariantConfiguration.cs:109-113`) and by `ProductVariantService` throwing on multiple
defaults (`ProductVariantService.cs:210-219`). On product create, `EnsureDefaultVariantAlignedAsync`
creates it if missing (`ProductsController.cs:388`).

**Ownership matrix (implementation):**

| Concern | Owner | Evidence |
|---------|-------|----------|
| Barcode | Variant (product mirror for simple) | `ProductVariant.cs:50`, `ProductVariantCloneImportAuthority.cs:255-266` |
| SKU | Variant (product mirror for simple) | `ProductVariant.cs:44`, `:255-266` |
| Price (POS/storefront) | Variant `Price`/`OnlinePrice` | `ProductVariant.cs:61-67`; simple mirrored from product |
| Cost | Variant `CostPrice` (`ProductVariant.cs:72`); product `PurchasePrice` mirror for simple | `ProductVariantService.cs:277` |
| Stock | `ProductVariantStock` per `(VariantId,WarehouseId)` | `ProductVariantStock.cs:17-46` |
| Image | Product `ProductMedia` gallery; variant `MediaId`/`ImageUrl` override | `ProductVariant.cs:117-127`, `ProductMedia.cs` |
| Purchase | Order/sale line references `VariantId` (snapshot `SKU`, price, cost) | `OrderItem.cs:40-41`, `SalesInvoiceItem.cs:10`, `PurchaseInvoiceItem.cs:10` |
| Sales | `OrderItem.VariantId` is source of truth | `OrderItem.cs:9-10` |
| Returns | `InventoryReservation` keyed by `VariantId`; sales-return lines reference variant | `InventoryReservation.cs:13`; `SalesReturnsController.cs` |
| Media | Product-owned gallery; variant pointer | `ProductMedia.cs`, `ProductMediaService.cs:139-179` |

**Semantic lifecycle (merge/split lineage):** `ProductVariantSemanticLifecycleState`
(`Current/Merged/SplitParent/Detached`, `ProductVariantSemanticLifecycleState.cs`). Only `Current`
variants may own SKU/barcode/stock/reservations/media/publication
(`ProductVariantMergeSplitAuthority.WhereCurrentSemanticOwner`, `:18-24`). Merge clears source
SKU/barcode/MediaId and sets `IsActive=false`; split clears parent's SKU/barcode and supersedes it
(`:186-275`). Replays are collapsed idempotently via `SemanticOperationId` (`:324-339`). Open
reservations block supersede (`:277-322`). A variant may not merge/split across tenants (`:341-350`).

---

## 6. Inventory Integration

Product connects to inventory **exclusively through the variant** (`ProductVariantStock`):

- **Purchase (inbound):** `PurchaseInvoiceItem.ProductVariantId` + `BaseQuantity` (= `Quantity *
  ConversionFactor`) increases `ProductVariantStock.Quantity`
  (`PurchaseInvoiceItem.cs:10,26,32`).
- **Purchase Return:** `PurchaseReturnsController` references variant; decreases stock.
- **Sales (outbound):** `SalesInvoiceItem.ProductVariantId` + `BaseQuantity` decreases stock
  (`SalesInvoiceItem.cs:10,26,32`). Order snapshot stores `VariantId`, `SKU`, price, cost
  (`OrderItem.cs:40-116`).
- **Sales Return:** `SalesReturnsController` references variant; returns stock.
- **Warehouse Opening Balance:** seeds `ProductVariantStock.Quantity` per warehouse (variant-aware).
- **Inventory Adjustment:** mutation through `ProductVariantStockMutationSupport` →
  `ProductVariantStockChangedEvent`.
- **Stock Reservation:** `InventoryReservation` is keyed by `VariantId`+`WarehouseId`
  (`InventoryReservation.cs:13-14`); `RemainingReservedQuantity = Reserved - Committed - Released`.
- **ProductVariantStock:** the authoritative per-warehouse quantity; `AvailableQuantity = Quantity -
  ReservedQuantity`, `TotalQuantity = Quantity + DamagedQuantity` (`ProductVariantStock.cs:50-58`).
- **Inventory Valuation:** `SalesInvoiceItem.CostPriceSnapshot`/`CostAmountSnapshot` are immutable
  variant-cost captures at commit (`SalesInvoiceItem.cs:59-68`) — valuation reads the snapshot, not
  live `CostPrice`.

`WarehouseStock` is a **compatibility table only** and is NOT an inventory authority
(`ProductVariantStock.cs:13-16`; migration backfills it into `ProductVariantStocks`,
`AddProductVariantsPhase11_1.cs:268-294`).

---

## 7. Pricing Architecture

No price-list/tier/wholesale entity exists. Pricing is **columnar** (variant + unit). PostgreSQL is
the authority; Mongo/ES carry a projection.

| Price | Column | Authority | Used by |
|-------|--------|-----------|---------|
| Cost / Purchase | `Product.PurchasePrice`, `ProductVariant.CostPrice`, `ProductUnit.PurchasePrice` | SQL | margin/COGS (snapshot at sale) |
| POS SalePrice (fallback) | `Product.SalePrice`, `ProductVariant.Price` | SQL | POS |
| Storefront OnlinePrice (source of truth) | `Product.OnlinePrice`, `ProductVariant.OnlinePrice`, `ProductUnit.SellingPrice` | SQL | storefront/CommerceHub |
| Effective storefront | `OnlinePrice ?? Price` (variant) | Derived at read | storefront |
| Unit price | `ProductUnit.SellingPrice` + `ConversionFactor` | SQL | multi-unit POS |

- **Server validation:** prices are persisted in the same transaction as the product/variant; no
  client-side price trust. Effective price resolved by `ProductVariant.EffectiveOnlinePrice`
  (`ProductVariant.cs:191-193`).
- **Tax:** `TaxRate` + `PriceIncludesTax` on `Product` (`Product.cs:72-77`); per-line `TaxRate`/
  `TaxAmount` on invoice items.
- **Discounts / Promotions / Coupons:** NOT catalog-owned. Catalog exposes base price + winning
  promotion overlay at projection time (`PromotionVisibilityProjectionSupport`). Coupon validation
  reads `CategoryId`; the empty-category guard exists to prevent category-scoped coupon bypass
  (`Product.cs:46-50`).
- **Pricing snapshots:** sales lines store immutable `UnitPrice`, `CostPrice`, `TaxRate`, totals
  (`OrderItem.cs:95-116`) — historical correctness independent of later price edits.
- **CommerceHub override:** `CommerceHubPriceOverride` may differ by design; underlying catalog
  remains SQL-owned by tenant.

---

## 8. Category Architecture

- **Parent-child hierarchy:** `Category.ParentCategoryId` (self-referencing, nullable),
  `SubCategories`, `Products` (`Category.cs`). No slug column; category slug computed from name at
  read time (`StorefrontCategoryService.GenerateSlug`).
- **Lineage / inheritance:** `CategoryLineageAuthority.RequireLiveCategoryAsync` walks the parent
  chain to prove the category (and all ancestors) are non-deleted and acyclic
  (`CategoryLineageAuthority.cs:32-108`). Product save **rejects** a category whose lineage is broken
  (`ProductsController.cs:275-282`).
- **SEO / visibility / navigation / search / filtering:** category name path is denormalized into
  projections by walking the live lineage at read time; breadcrumbs via `SeoService.BuildCategoryTrail`.
  A deleted/cyclic ancestor yields `null` → product is tombstoned, not mis-categorized.

---

## 9. Unit Architecture

- **Base unit:** `Unit` master (`Unit.cs`); `ProductUnit.IsBaseUnit` marks exactly one base unit per
  product (`ProductUnit.cs:42`). Default unit flagged by `IsDefault` (`:36`).
- **Conversion factor:** `ProductUnit.ConversionFactor` = base units per this unit
  (`ProductUnit.cs:15`). e.g. Carton(5) ⇒ 1 carton = 5 pieces.
- **Multi-unit support:** a product links many `ProductUnit` rows (each a different `Unit`), each
  with its own `PurchasePrice`/`SellingPrice`/`Barcode` (`ProductUnit.cs:19-31`).
- **Purchase unit / Sales unit / Inventory unit:** any linked unit may be used at transaction time;
  the chosen `UnitId` + `ConversionFactor` snapshot is stored on the invoice line
  (`SalesInvoiceItem.cs:15,26,32`; `PurchaseInvoiceItem.cs:15,26,32`). **Inventory is always tracked
  in base units** via `BaseQuantity`.
- **Conversion rules / rounding:** `BaseQuantity = Quantity * ConversionFactor`
  (`SalesInvoiceItem.cs:29-32`). No rounding logic found in the unit entity; arithmetic is decimal
  with `HasPrecision(18,2)` storage.

---

## 10. Warehouse Integration

- **Warehouse ownership:** `Warehouse` is tenant-owned; `ProductVariantStock.WarehouseId` is a
  restricted FK (`ProductVariantStockConfiguration.cs:54-57`).
- **Variant stock:** `ProductVariantStock` per `(VariantId, WarehouseId)`; unique index
  `IX_ProductVariantStocks_TenantId_VariantId_WarehouseId` (`ProductVariantStockConfiguration.cs:60-62`).
- **Warehouse stock (legacy):** `WarehouseStock` has a `ProductVariantId` column but is explicitly a
  compatibility table, not authority (`WarehouseStock.cs:18`, `ProductVariantStock.cs:13-16`).
- **Cross-warehouse:** stock is per-warehouse; totals aggregated by summing across warehouses
  (`ProductVariantStockChangedEvent.TotalStockQuantity`,
  `ProductVariantStockMutationSupport.BuildCommittedStockEventsAsync:146-156`).
- **Transfers:** variant-aware stock transfer uses `LockMultipleStocksAsync` with `(WarehouseId,
  VariantId)` ordering to prevent deadlocks (`ProductVariantStockMutationSupport.cs:292-325`).
- **Opening balance:** seeds `ProductVariantStock` via the same mutation support (create-if-missing
  + pessimistic lock).
- **Stock identity:** `(TenantId, VariantId, WarehouseId)` is the stock identity; exactly one row
  (`UNIQUE`, enforced; `EnsureStockRowExistsAsync` uses `INSERT … ON CONFLICT DO NOTHING` /
  `IF NOT EXISTS … INSERT` for idempotency, `ProductVariantStockMutationSupport.cs:449+`).

---

## 11. Media Architecture

- **Storage:** `ProductMedia` rows in PostgreSQL (`ProductMedia.cs`) — `Id`, `TenantId`, `ProductId`,
  `MediaType`(`image`|`video`), `Url`, `Provider`, `Position`, `CreatedAtUtc`.
- **Ordering:** `Position` ascending, ties by `CreatedAtUtc`; reorder resequences 1..N
  (`ProductMediaService.cs:189-230`, `:262-268`).
- **Default/primary image:** resolved by first `Media[]` image by `Position`, else legacy
  `Product.Images` fallback.
- **Variant image:** variant points at one gallery image via `MediaId` (preferred over `ImageUrl`,
  `ProductVariant.cs:117-121`).
- **Thumbnail / gallery / delete / replacement:** gallery = all product media ordered by position;
  delete (`RemoveMediaAsync`) clears `MediaId`/`ImageUrl` on every variant that referenced it and
  republishes `ProductVariantUpdatedEvent` (`ProductMediaService.cs:121-181`); replacement = add new
  media + reorder. `ProductMediaChangedEvent` rebuilds Mongo `Media[]` from SQL
  (`ProductMediaChangedEvent.cs`, `ProductMediaChangedEventConsumer.cs`).
- **Validation/hardening:** image extensions restricted to jpg/jpeg/png/webp; video providers
  youtube/vimeo/mp4; URL must be absolute or root-relative (`ProductMediaService.cs:304-416`).

---

## 12. Search Architecture

- **Indexing pipeline:** PostgreSQL (system of record) → events → Mongo `ProductDocument` (embedded
  `Variants[]`) → SQL-recomposed → Elasticsearch `products` index → Redis cache. Elasticsearch is a
  **search acceleration index**, never authoritative (`product-catalog-architecture.md §8`).
- **Barcode search:** `GET api/products/barcode/{barcode}` — product first, then active variant
  (`ProductsController.cs:201-249`).
- **SKU search:** `ILike` on `Products.SKU` + variant SKU ownership checks
  (`ProductsController.cs:78-84`; `ProductVariantCloneImportAuthority.cs:48-105`).
- **Category search:** by `CategoryId`; live lineage resolved at read (`CategoryLineageAuthority.cs`).
- **Variant search:** variant SKU/barcode/attr-combo; Mongo embeds variants, keyed by `VariantId`
  (`ProductVariantProjectionConsumer.cs`).
- **ElasticSearch / Mongo projection / Caching:** `ProductStockPriceSearchIndexConsumer` waits for
  Mongo freshness (throws `ProjectionNotReadyException` to retry) then recomposes SQL authority
  (`ProductStockPriceSearchIndexConsumer.cs`); `ProductCacheInvalidationConsumer` invalidates
  list/detail/homepage/promotion keys (`ProductCacheInvalidationConsumer.cs:244-313`).

---

## 13. Business Rules (implementation-enforced)

| Rule | Enforcement | Evidence |
|------|-------------|----------|
| Category required, not empty GUID | Controller guard + config NOT NULL | `ProductsController.cs:269-273`, `ProductConfiguration.cs:53-55` |
| Category must be live in SQL lineage | `CategoryLineageAuthority.RequireLiveCategoryAsync` | `ProductsController.cs:275-282` |
| Barcode unique across products + live variants + product units | `IsBarcodeOwnedByAnotherSemanticEntityAsync` (Create+Update) + DB unique filtered index | `ProductVariantCloneImportAuthority.cs:107-179`, `ProductVariantConfiguration.cs:99-102` |
| SKU unique (collision auto-suffixed) | `ResolveUniqueSkuAsync` + DB unique filtered index | `:14-46`, `ProductVariantConfiguration.cs:94-97` |
| Variant attribute combination unique | `AttributeCombinationKey` SHA-256 + unique index | `ProductVariantAttributeCombinationKey.cs`, `ProductVariantConfiguration.cs:104-107` |
| Exactly one default current variant | unique filtered index + service throws on multi-default | `ProductVariantConfiguration.cs:109-113`, `ProductVariantService.cs:210-219` |
| Simple product may own only its default variant | `SimpleProductAdditionalVariantForbidden` (400) | `ProductVariantsController.cs:151-162` |
| Variant product must null product commerce fields | controller write path | `ProductsController.cs:328-344` |
| Stock non-negative; reserved ≤ quantity | DB `CHECK` constraints | `ProductVariantStockConfiguration.cs:18-30` |
| One stock row per (tenant,variant,warehouse) | UNIQUE index + idempotent insert | `:60-62` |
| Tenant isolation | `ITenantOwned` + global filter + every query filtered by `RequiredTenantId` | `ITenantOwned.cs`, `ProductsController.cs` |
| Variant merge/split within tenant; open reservations block | `ProductVariantMergeSplitAuthority` | `:277-350` |
| Media must be product-owned image for variant use | `CanInheritProductMediaAsync` | `ProductVariantCloneImportAuthority.cs:237-253` |

---

## 14. SQL Transaction Analysis

- **Transaction boundary:** product/variant/media writes run inside `await _context.Database.BeginTransactionAsync()`
  with MassTransit **outbox** events published in the same transaction
  (`ProductsController.cs:379-468`, `ProductVariantsController.cs:129-446`).
- **Atomicity:** Product INSERT + default-variant INSERT + event outbox rows commit together; on
  event-version overflow the whole tx is rolled back and a deterministic `Conflict` is returned
  (`ProductsController.cs:449-468`).
- **Rollback / recovery:** any `DbUpdateException` on variant identity conflict rolls back and
  returns `Conflict` (`ProductVariantsController.cs:405-429`). Stock mutations REQUIRE an explicit
  transaction or throw (`ProductVariantStockMutationSupport.cs:388-395`).
- **Concurrency:** variant rows use PostgreSQL `xmin` as `RowVersion` (optimistic concurrency,
  `ProductVariantConfiguration.cs:53-56`). Stock uses **pessimistic row locks** (`FOR UPDATE` /
  `UPDLOCK,ROWLOCK,HOLDLOCK`) reloaded after materialization
  (`ProductVariantStockMutationSupport.cs:397-447`).
- **Optimistic locking (projection):** `ProjectionMutationVersion` is advanced by the DbContext save
  pipeline on every add/modify (`ApplicationDbContext.cs:5217-5252`), monotonic and reject-on-regress
  (`Product.cs:216-225`); downstream consumers reject stale projections via `LastEventVersion` /
  `StockUpdatedAtUtc` gates (`ProductVariantProjectionConsumer.cs:368-385`, `:188-190`).

---

## 15. Event Analysis

- **Published events:** `ProductCreatedEvent`, `ProductUpdatedEvent`, `ProductDeletedEvent`,
  `ProductPriceChangedEvent`, `ProductStockChangedEvent`, `LowStockAlertEvent`,
  `ProductPublishStatusChangedEvent` (`ProductEvents.cs`); variant family
  `ProductVariantCreated/Updated/Deleted/StockChanged/PriceUpdatedEvent`
  (`ProductVariantEvents.cs`); `ProductMediaChangedEvent` (`ProductMediaChangedEvent.cs`).
- **Projection updates:** `ProductVariantProjectionConsumer` embeds variants into Mongo;
  `ProductReadModelConsumer` builds storefront `ProductDocument`; `ProductMediaChangedEventConsumer`
  rebuilds `Media[]`; `ProductStockChangedConsumer`/`ProductStockPriceSearchIndexConsumer` sync stock
  + index; `ProductCacheInvalidationConsumer` invalidates Redis.
- **Inventory synchronization:** `ProductVariantStockChangedEvent` carries `MutationSource`
  (`POS`/`CommerceHub`), `TotalStockQuantity`, `StockUpdatedAtUtc` for cross-boundary reconciliation
  (`ProductVariantEvents.cs:30-41`, `:194-208`) and is staged to a durable `StockSyncOutboxMessages`
  table before commit (`ProductVariantStockMutationSupport.PublishCommittedStockEventsAsync:216-250`).
- **Search synchronization:** via SQL-recomposed Mongo → ES.
- **Media synchronization:** `ProductMediaChangedEvent` → Mongo `Media[]`.
- **Analytics:** variant stock/price events feed analytics projections; order lines carry immutable
  variant snapshots for SKU-level money/COGS.

---

## 16. Security Analysis

- **Permissions:** endpoints require `Owner,Admin,Manager` for writes; `Cashier` read-only on product
  list/detail; `ProductMediaController` requires `Owner,Admin,Manager`
  (`ProductsController.cs:63,263,481`, `ProductVariantsController.cs:112`, `ProductMediaController.cs:10`).
- **Tenant isolation:** `ITenantOwned.TenantId` NOT NULL; EF global query filter; `RequiredTenantId`
  throws `SecurityException` if missing (`ITenantOwned.cs`). Every repository query is tenant-scoped.
- **Media ownership:** media validated to belong to the product+tenant and be an image before a
  variant may reference it (`CanInheritProductMediaAsync`,
  `ProductVariantsController.cs:229-235`).
- **Warehouse ownership:** stock mutation validates warehouse belongs to tenant
  (`ProductVariantsController.cs:312-321`).
- **Cross-tenant isolation:** SKU/barcode uniqueness and variant merge/split are tenant-scoped;
  cross-tenant merge rejected (`ProductVariantCloneImportAuthority.cs:48-179`,
  `ProductVariantMergeSplitAuthority.cs:341-350`).
- **Replay prevention:** `ProjectionConsumerEventFence` dedupes event replays; `SemanticOperationId`
  collapses idempotent merge/split replay; delete-tombstones prevent variant resurrection
  (`ProductVariantProjectionConsumer.cs:100-150`; `ProductVariantMergeSplitAuthority.cs:324-339`).
- **Validation:** server-side only; prices/stock validated in SQL tx, never trusted from client.

---

## 17. Hermes Browser Test Specification (deterministic)

### 17.1 Create simple product → default variant
- **Preconditions:** authenticated `Owner`; live category exists; barcode not owned by another
  entity.
- **User Journey:** POST `api/products` with `HasVariants=false`, `SKU`, `Barcode`, `SalePrice`,
  `PurchasePrice`, `CategoryId`, `Unit`.
- **Expected Runtime State:** 201; body has `id`; `Product.SKU==SKU`.
- **Expected DB State:** `Products` row inserted; `ProductVariants` row with `IsDefault=true`,
  `SKU==SKU`, `Price==SalePrice`, `CostPrice==PurchasePrice`, `Barcode==Barcode`;
  `ProductVariantStocks` not auto-created (TrackStock may be false for simple).
- **Expected Variant State:** exactly one default variant.
- **Expected Inventory State:** variant stock rows created only if initial stock supplied.
- **Expected Search State:** `ProductCreatedEvent`+`ProductVariantCreatedEvent` in outbox; Mongo
  `ProductDocument` + embedded default variant; ES doc after publish.
- **Expected Media State:** none yet.
- **Assertions:** GET `api/products/{id}` returns mirror SKU/barcode/price; GET variant returns
  default with same SKU.

### 17.2 Create variant product + variant
- **Preconditions:** product with `HasVariants=true` (commerce fields null).
- **User Journey:** POST `api/products/{id}/variants` with attributes `{Color:Red,Size:XL}`, price,
  stock in 2 warehouses.
- **Expected DB State:** `ProductVariants` row (not default unless first); 2 `ProductVariantStocks`
  rows; `ProductVariantAttributes` rows; `AttributeCombinationKey` set.
- **Negative:** POST second variant with same attribute combo → 400
  `VariantAttributeCombinationConflict` (`ProductVariantsController.cs:176-194`). POST variant with
  duplicate barcode → 400 `VariantSemanticBarcodeOwnershipConflict` (`:205-222`). POST extra variant
  on a simple product → 400 `SimpleProductAdditionalVariantForbidden` (`:151-162`).

### 17.3 Barcode search variant-aware
- **Journey:** GET `api/products/barcode/{variantBarcode}`.
- **Assertions:** returns product with `matchedVariantId` set; if only product barcode,
  `matchedVariantId` null (`ProductsController.cs:201-256`).

### 17.4 Media add/delete/reorder
- **Journey:** POST `api/admin/products/{id}/media` (image url) → 201; DELETE one → 204; reorder.
- **Expected DB State:** `ProductMedia` rows with `Position` resequenced; deleting media nulls
  `MediaId`/`ImageUrl` on referencing variants and republishes
  `ProductVariantUpdatedEvent` (`ProductMediaService.cs:121-181`).
- **Negative:** non-image extension → 400; video without provider → 400; media from another product
  as variant `MediaId` → 400 (`ProductMediaService.cs:304-416`,
  `ProductVariantsController.cs:229-235`).

### 17.5 Publish + visibility
- **Journey:** PUT `api/products/{id}` set `PublishStatus=Published`, `IsOnlineVisible=true`.
- **Expected Search State:** Mongo upsert requires `IsActive && IsOnlineVisible`
  (`StorefrontProductProjectionMutationBoundary.cs`); ES doc indexed; Redis invalidated.
- **Negative:** `CategoryId=Guid.Empty` → 400 (`ProductsController.cs:269-273`); dead category → 400
  (`:275-282`).

### 17.6 Variant stock mutation concurrency
- **Journey:** two concurrent sales of same variant/warehouse.
- **Assertions:** stock decremented exactly once per unit; pessimistic `FOR UPDATE` prevents
  lost-update; if quantity insufficient → `InsufficientStockException`
  (`ProductVariantStockMutationSupport.cs:335-357`).

---

## 18. Product Domain Map

```
Product (Products)  [TenantId, CategoryId, HasVariants, PublishStatus, IsActive, IsOnlineVisible]
├── Category (Categories) 1─* (live SQL lineage required)
├── Variants (ProductVariants) 1─*  [IsDefault exactly 1 current; SemanticLifecycleState]
│   ├── Attributes (ProductVariantAttributes) 1─*  [Name/Value; unique (VariantId,Name)]
│   ├── Stocks (ProductVariantStocks) 1─*  [per WarehouseId; AvailableQty=Qty-Reserved]
│   │   └── Warehouse (Warehouses) *─1
│   ├── Media pointer (MediaId → ProductMedia) + ImageUrl override
│   └── Events: Created/Updated/Deleted/StockChanged/PriceUpdated (VariantId-scoped)
├── Media (ProductMedia) 1─*  [product-scoped gallery; Position ordering]
├── ProductUnits (ProductUnits) 1─*  → Unit (Units) [ConversionFactor; per-unit price/barcode]
├── Sales   ← OrderItem.VariantId (immutable snapshot: SKU, price, cost)  (SalesInvoiceItem)
├── Purchases ← PurchaseInvoiceItem.VariantId (BaseQuantity)              (PurchaseInvoiceItem)
├── Returns ← SalesReturn/PurchaseReturn lines reference VariantId
├── Reservations ← InventoryReservation.VariantId + WarehouseId
└── Reports/Analytics ← projections read variant snapshots + stock/price events
```

---

## 19. Canonical Pattern (summary)

| Concern | Strategy (implementation) |
|---------|---------------------------|
| **Identity** | Product = catalog/parent GUID; Variant = sellable SKU GUID. Simple product mirrors identity onto its single default variant. |
| **Variant** | Every product has exactly one current default variant; variant owns SKU/barcode/price/stock/media; semantic lifecycle (Current/Merged/SplitParent/Detached) with SQL-authoritative ownership + tombstones. |
| **Inventory** | `ProductVariantStock` per `(VariantId,WarehouseId)` is the single authority; pessimistic locks + CHECK constraints; `WarehouseStock` legacy-only. |
| **Pricing** | Columnar (variant + unit); `OnlinePrice` = storefront source of truth; immutable snapshot on order lines; no price-list entity. |
| **Media** | Product-owned `ProductMedia` gallery; variant `MediaId` pointer; SQL-recomposed Mongo `Media[]`; strict validation. |
| **Category** | Self-referencing tenant tree; live-lineage validation; slug computed from name at read. |
| **Unit** | `Unit` master + `ProductUnit` links with `ConversionFactor`; multi-unit; base-unit stock. |
| **Search** | PostgreSQL → Mongo (embedded variants) → SQL-recomposed → Elasticsearch → Redis; ES never authoritative. |
| **Concurrency** | `xmin` optimistic row version on variants; `FOR UPDATE`/`UPDLOCK` pessimistic stock locks ordered `(WarehouseId,VariantId)`; monotonic `ProjectionMutationVersion` for projections. |
| **Lifecycle** | Explicit SQL tx + MassTransit outbox; events drive Mongo/ES/Redis projections; SQL authority overlay recomposes on read/index; tombstones on divergence. |

---

## 20. Documentation Validation (correct misleading docs)

1. **"Product is the sellable unit."** FALSE per implementation. `OrderItem.cs:8-13`: "A Product is
   NEVER sold. A ProductVariant (SKU) is ALWAYS sold." Product is the catalog container.
2. **"Barcode/SKU are unique per product."** Incomplete. Uniqueness is enforced across products AND
   live variants AND product units (`ProductVariantCloneImportAuthority.cs:48-179`); for variant
   products the product barcode/SKU are intentionally null.
3. **"WarehouseStock is inventory."** FALSE. `ProductVariantStock.cs:13-16` states `WarehouseStock`
   is a compatibility table only; `ProductVariantStock` is the source of truth.
4. **"There is a price tier / wholesale table."** FALSE. Pricing is fully columnar
   (variant + unit). No tier/wholesale entity exists.
5. **"Brand is a master entity."** FALSE. `Product.Brand`/`Manufacturer` are free-text strings only.
6. **"Category has a slug column."** FALSE. Category slug is computed from `Name` at read time
   (`StorefrontCategoryService.GenerateSlug`). Only `Product.Slug` is a stored column.
7. **"Attributes are a taxonomy."** FALSE. Attributes are denormalized name/value pairs on each
   variant (`ProductVariantAttribute`), keyed by a SHA-256 combination hash.
8. **"Elasticsearch/MongoDB are the product source of truth."** FALSE. They are derived projections
   re-owned by SQL on every read/index (`product-catalog-architecture.md §0`, §8).

---

## 21. Final Architecture Question — How Shopixy models a Product

Shopixy models a Product as a **two-tier, tenant-scoped aggregate** in PostgreSQL where the Product
is a catalog/parent identity and the **ProductVariant is the sellable SKU**. Every other store
(MongoDB, Elasticsearch, Redis) is a derived, SQL-recomposed projection. The design preserves the
required properties through concrete mechanisms:

- **Identity** — Product GUID + tenant; Variant GUID + tenant; SKU/barcode owned by variant (product
  mirrors for simple). Unique filtered indexes + `ProductVariantCloneImportAuthority` enforce global
  SKU/barcode uniqueness (`ProductVariantConfiguration.cs:94-102`,
  `ProductVariantCloneImportAuthority.cs:48-179`).
- **Variant consistency** — exactly one current default variant per product (unique filtered index +
  service guard, `ProductVariantConfiguration.cs:109-113`,
  `ProductVariantService.cs:210-219`); semantic lifecycle state prevents superseded variants from
  owning public surfaces (`ProductVariantMergeSplitAuthority.cs:18-24`); attribute-combo hash prevents
  duplicate variants.
- **Inventory consistency** — `ProductVariantStock` per `(VariantId,WarehouseId)` is the single
  authority; pessimistic row locks + deterministic `(WarehouseId,VariantId)` ordering + DB CHECK
  constraints prevent race/lost-update/negative stock
  (`ProductVariantStockMutationSupport.cs`, `ProductVariantStockConfiguration.cs:18-30`).
- **Pricing correctness** — columnar variant/unit prices with `OnlinePrice` as storefront truth;
  immutable cost/price snapshots on order lines decouple historical correctness from later edits
  (`OrderItem.cs:95-116`, `SalesInvoiceItem.cs:59-68`); server-validated in the same SQL tx.
- **Search correctness** — PostgreSQL → Mongo (embedded variants) → SQL-recomposed → Elasticsearch;
  ES is never authoritative; monotonic `LastEventVersion`/`StockUpdatedAtUtc` gates reject stale
  updates; `ProjectionNotReadyException` retries until Mongo catches up
  (`ProductStockPriceSearchIndexConsumer.cs`, `ProductVariantProjectionConsumer.cs:368-385`).
- **Media integrity** — product-owned gallery; variant `MediaId` pointer; delete cascades to clear
  variant refs and republish; strict URL/type validation (`ProductMediaService.cs`,
  `ProductMediaChangedEvent.cs`).
- **Multi-warehouse support** — stock is per-warehouse by construction; totals aggregated; transfers
  lock in deterministic order to avoid deadlocks.
- **Multi-unit support** — `ProductUnit` links with `ConversionFactor`; transactions snapshot
  `UnitId`+`ConversionFactor`; inventory always in base units (`SalesInvoiceItem.cs:15,26,32`).
- **Scalability** — CQRS with event-driven, SQL-recomposed projections; outbox pattern makes writes
  atomic; read scaling via Mongo/ES/Redis without coupling write correctness to them.
- **Auditability** — `BaseEntity` audit fields (CreatedAt/By, UpdatedAt/By, IsDeleted); immutable
  order-line snapshots; monotonic `ProjectionMutationVersion` + event fences + tombstones give a
  replay-safe, tenant-isolated audit trail
  (`ApplicationDbContext.cs:5217-5252`, `ProductVariantProjectionConsumer.cs:100-150`).

**Every statement above is grounded in the cited implementation. No narrative doc was treated as
authoritative over code.**
