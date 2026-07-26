# Product Catalog — Implementation-Backed Architecture

> Source of truth: implementation only. Every claim below is grounded in the code at the cited
> path. No README, markdown spec, or narrative doc was treated as authoritative. Where code
> contradicts narrative docs, the code wins.
>
> Status: REVERIFIED AGAINST CODE ON 2026-07-21 — HIGH CONFIDENCE (all sections read directly from source).

---

## 0. Executive Summary

Shopixy/PointOfSale models the catalog as a **two-tier aggregate**: `Product` (catalog/SEO/parent
identity) and `ProductVariant` (the sellable SKU that owns price, stock, barcode, and media
reference). Both live in **PostgreSQL** as the single system of record. Every other store
(MongoDB read model, Elasticsearch search index, Redis cache) is a **derived projection** of
PostgreSQL, reconciled through MassTransit events and — critically — **re-owned by SQL on every
read** via a "SQL authority overlay" that rebuilds the projected document from PostgreSQL before
it is served or indexed.

The single most important architectural fact: **MongoDB/Elasticsearch/Redis are never the authority
for product truth on the public path.** A dedicated `ProductSqlAuthorityProjectionWriter` and
`ProductSearchSqlAuthorityOverlay` recombine the current PostgreSQL `Product` + `Variants` +
`Stocks` + `Media` + `Promotions` into the read document on write and on read, and **tombstone /
delete** the projection when SQL no longer proves visibility. Catalog therefore runs as a CQRS
system with a hardened "SQL-recomposed read model" rather than a fire-and-forget event projection.

| Tier | Store | Authority | Lifecycle |
|------|-------|-----------|-----------|
| Write model | PostgreSQL (`Products`, `ProductVariants`, `ProductVariantStocks`, `ProductMedia`, `ProductUnits`, `Categories`, `Units`) | **System of record** | Direct EF Core in `ProductsController`/`ProductVariantService` |
| Variant projection | MongoDB `products` (embedded `Variants[]`) | Derived (SQL-owned) | `ProductVariantProjectionConsumer` |
| Storefront read model | MongoDB `ProductDocument` | Derived (SQL-owned) | `ProductSqlAuthorityProjectionWriter` → `StorefrontProductProjectionMutationBoundary` |
| Search index | Elasticsearch `products` index | Derived (SQL-owned) | `ProductStockPriceSearchIndexConsumer` → `ProductSearchSqlAuthorityOverlay` |
| Read cache | Redis (JSON + key/value) | Derived (SQL-validated) | `ProductCacheInvalidationConsumer` + per-request SQL recompose |
| CommerceHub listing | MongoDB `commercehubproducts` | Derived (SQL-owned) | `CommerceHubProjectionConsumer` |
| Affiliate discovery | MongoDB `publicaffiliateproducts` + ES | Derived (SQL-owned) | Affiliate projection consumers |

---

## 1. Entity Inventory (reverse-engineered)

Every catalog entity found in the implementation:

| Entity | File | DB Table | Tenant-owned | Soft-delete |
|--------|------|----------|--------------|-------------|
| `Product` | `PointOfSale.Core/Entities/Product.cs` | `Products` | Yes (`ITenantOwned`, `TenantId` NOT NULL) | Yes (`BaseEntity.IsDeleted`) |
| `ProductVariant` | `PointOfSale.Core/Entities/ProductVariant.cs` | `ProductVariants` | Yes | Yes |
| `ProductVariantStock` | `PointOfSale.Core/Entities/ProductVariantStock.cs` | `ProductVariantStocks` | Yes | Yes |
| `ProductVariantAttribute` | `PointOfSale.Core/Entities/ProductVariantAttribute.cs` | `ProductVariantAttributes` | (via variant) | Yes |
| `ProductMedia` | `PointOfSale.Core/Entities/ProductMedia.cs` | `ProductMedia` | Yes | No (hard lifecycle via events) |
| `ProductUnit` | `PointOfSale.Core/Entities/ProductUnit.cs` | `ProductUnits` | (via product + `Unit`) | Yes |
| `Category` | `PointOfSale.Core/Entities/Category.cs` | `Categories` | Yes | Yes |
| `Unit` | `PointOfSale.Core/Entities/Unit.cs` | `Units` | Yes | Yes |
| `Brand` | **NO entity** — only `Product.Brand` string field + `Product.Manufacturer` string | `Products.Brand` | — | — |
| `Attribute` / `AttributeValue` | **NO first-class entity** — only `ProductVariantAttribute` (name/value pairs on variant); `FeaturesJson`, `FaqJson`, `TagsJson`, `Tags` are JSON/comma fields on `Product` | — | — | — |
| `ProductPrice` / price lists / tiers / wholesale | **NO dedicated entity** — pricing is columnar on `Product` (`SalePrice`, `PurchasePrice`, `OnlinePrice`, `TaxRate`, `PriceIncludesTax`) and `ProductVariant` (`Price`, `OnlinePrice`, `CostPrice`), plus per-unit `ProductUnit.SellingPrice`/`PurchasePrice`. No tier/wholesale price table exists. | — | — | — |
| `ProductReview` | `PointOfSale.Core/Entities/ProductReview.cs` | `ProductReviews` | review entity (separate subsystem) | Yes |

**Conclusion on "missing" catalog concepts:** There is **no `Brand` table, no `Attribute`/`AttributeValue`
reference tables, no `ProductPrice`/price-list/tier entity, and no dedicated "wholesale price" store.**
Brand is a free-text string on `Product` (used only for faceting/display). Attributes are stored
denormalized as name/value pairs on each variant. Pricing is fully columnar (variant-level +
unit-level). Any documentation implying a brand master, attribute taxonomy, or price-list subsystem
is **not backed by the implementation**.

---

## 2. Product Model (canonical definition)

### 2.1 What the canonical Product owns

`Product` (`PointOfSale.Core/Entities/Product.cs`) is the **catalog/parent** object. It holds:

- **Identity/SEO:** `Name`, `NameAr`, `Slug`, `MetaTitle`, `MetaDescription`, `MetaKeywords`,
  `ProductTemplate`.
- **Category:** `CategoryId` (NOT NULL — enforced at entity, DTO, DB level; empty GUID rejected to
  prevent category-scoped coupon bypass — `Product.cs:46-50`, `ProductConfiguration.cs:53-55`).
- **Pricing (legacy/container):** `PurchasePrice`, `SalePrice`, `OnlinePrice`, `TaxRate`,
  `PriceIncludesTax`. For variant products these are **0/null** (enforced in write path).
- **POS inventory container:** `Unit` (string), `MinimumStockLevel`, `TrackStock`, `Images`.
- **Shipping:** `Weight`, `Length`, `Width`, `Height`, `WeightUnit`, `DimensionUnit`.
- **Publication/visibility:** `PublishStatus` (Draft/Published/Archived), `IsActive`,
  `IsOnlineVisible`, `IsSearchable`, `IsFeatured`, `LowStockThreshold`, `AllowBackorder`.
- **Marketing/structured content:** `Brand`, `Manufacturer`, `Tags`, `TagsJson`, `FeaturesJson`,
  `FaqJson`, `RelatedProductIds`, `VideoUrl`, `AiGenerated`, `AiMetadataJson`, `AmazonASIN`.
- **Barcode/SKU (simple-product-only):** `Barcode`, `SKU` — set to null when `HasVariants`.
- **Runtime authority:** `ProjectionMutationVersion` (monotonic, used by downstream read models to
  reject stale projections — `Product.cs:210-225`).
- **Navigation:** `Category`, `Variants`, `Media`, `WarehouseStocks` (legacy), `ProductUnits`.

### 2.2 Variant vs Product ownership — the decisive rule

The write controller enforces a hard split (PHASE 11.5 "Shopify-grade authoring",
`ProductsController.cs:317-377`, `:567-629`):

- **`HasVariants == false` (simple product):** `Product` carries the legacy/container commerce
  fields; the system **auto-creates exactly one default `ProductVariant`** and mirrors
  `Product.SKU/Barcode/SalePrice/OnlinePrice/PurchasePrice/IsActive` onto it
  (`ProductVariantService.EnsureDefaultVariantAlignedAsync`, `ProductsController.cs:388-390`,
  `:635-638`). `ProductCloneImportAuthority.ApplySimpleProductIdentityFromDefaultVariant` keeps
  identity in sync both directions (`ProductVariantCloneImportAuthority.cs:255-266`). Storefront
  simple-product pricing is not taken from the mirrored variant; it is the SQL default
  `ProductUnit.SellingPrice`, projected to `ProductDocument.DefaultUnit`.
- **`HasVariants == true` (variant product):** `Product.PurchasePrice=0`, `SalePrice=0`,
  `OnlinePrice=null`, `Barcode=null`, `SKU=null`, `TrackStock=false`. **All commerce fields live on
  the variants** (`ProductsController.cs:328-344`, `:578-596`).

**Therefore:**
- **Inventory identity** belongs to **Variant** (`ProductVariantStock` per `VariantId`+`WarehouseId`),
  except a legacy `WarehouseStock` compatibility table that is explicitly *not* an inventory
  authority (`ProductVariantStock.cs:13-16`).
- **Pricing** belongs to **Variant** (`Price`/`OnlinePrice`/`CostPrice`) for variant products. For
  simple products, product and default-variant prices remain synchronized for backward
  compatibility, while storefront/cart/search display price is the SQL default
  `ProductUnit.SellingPrice` (`ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice`).
- **Barcode/SKU** belong to **Variant**; the product copy is only a mirror for simple products
  (`ProductVariantCloneImportAuthority.cs:255-266`).
- **Media** belongs to **Product** (`ProductMedia` is product-scoped, `ProductMedia.cs`), with a
  per-variant `MediaId` pointer + optional `ImageUrl` override on the variant
  (`ProductVariant.cs:117-127`).
- **SEO/slug** belongs to **Product** (`Product.Slug`); variants are not individually slugged.

### 2.3 Lifecycle invariants

- Every product MUST have exactly one `IsDefault=true` semantic-current variant
  (`ProductVariantConfiguration.cs:109-113` unique filtered index; `ProductVariantService`
  throws on multiple defaults — `ProductVariantService.cs:210-219`).
- `CategoryId` MUST NOT be `Guid.Empty` (`ProductConfiguration.cs:53-55`, controller guard
  `ProductsController.cs:269-273`, `:487-490`).
- SQL-unique barcode/SKU across products **and** live variants is enforced by
  `ProductVariantCloneImportAuthority.IsBarcodeOwnedByAnotherSemanticEntityAsync` /
  `IsSkuOwnedByAnotherSemanticEntityAsync` (`ProductVariantCloneImportAuthority.cs:48-179`),
  checked in both Create and Update.

---

## 3. Variant Architecture

### 3.1 Identity

`ProductVariant` (`PointOfSale.Core/Entities/ProductVariant.cs`) is a **first-class aggregate**, not a
child value object. It has its own GUID PK, its own `TenantId`, its own `ProductVariantStock` rows,
its own events (`ProductVariantCreatedEvent`, `ProductVariantUpdatedEvent`,
`ProductVariantStockChangedEvent`, `ProductVariantDeletedEvent` — all `IAggregateScopedIntegrationEvent`
keyed by `VariantId`), and its own unique SKU/barcode indexes.

`VariantId` is the **only identity used by cart and checkout** (`ProductVariantDocument` doc comment
`ProductVariantDocument.cs:60-63`; `ProductDocument.cs:390-394`).

### 3.2 Variant lifecycle / semantic ownership

Beyond active/inactive, variants carry a SQL-authoritative **semantic lifecycle state**
`ProductVariantSemanticLifecycleState` (`ProductVariant.cs:86-115`):

- `Current = 0` — owns public purchasability, stock, SKU/barcode, channel mappings.
- `Merged = 1`, `SplitParent = 2`, `Detached = 3` — lineage records that **must fail closed on
  public surfaces** (enum doc `ProductVariantSemanticLifecycleState.cs:3-7`).

Lineage is tracked by `SemanticReplacementVariantId`, `SemanticLineageRootVariantId`,
`SemanticOperationId`, `SemanticOperationKind`, `SemanticSupersededAtUtc`
(`ProductVariant.cs:92-115`). Uniqueness filters (`IsDeleted=false AND SemanticLifecycleState=0`)
ensure only current variants can own SKU/barcode/attribute-combo uniqueness
(`ProductVariantConfiguration.cs:94-107`). The `AttributeCombinationKey` is a SHA-256 hash of the
normalized attribute set (`ProductVariantAttributeCombinationKey.cs`), unique per
`TenantId+ProductId` (`:104-107`).

### 3.3 SKU / barcode generation

- **Not auto-generated by a code generator.** SKU is resolved by
  `ProductVariantCloneImportAuthority.ResolveUniqueSkuAsync`: uses the preferred SKU, else barcode,
  else `AUTO-{productId:N}` (truncated to 13 chars), suffixing `-N` on collision
  (`ProductVariantCloneImportAuthority.cs:14-46`). For simple products the product-level SKU/barcode
  is preferred and mirrored onto the default variant (`ProductVariantService.cs:251-279`).
- **Uniqueness is SQL-enforced** via unique filtered indexes on `SKU` and `Barcode`
  (`ProductVariantConfiguration.cs:94-102`), scoped to current, non-deleted variants. Barcode is
  additionally checked against `ProductUnits.Barcode` to avoid cross-table collisions
  (`ProductVariantCloneImportAuthority.cs:170-179`), and product-unit barcode writes are guarded by
  application checks plus the PostgreSQL `TR_*_CatalogBarcodeAuthority` triggers from
  `20260607190000_PosProductAuthorityHardening.cs`.
- Barcode search is variant-aware: `GET api/products/barcode/{barcode}` checks product barcode first,
  then active variant barcode (`ProductsController.cs:201-249`).

### 3.4 Image inheritance

- Product-level `ProductMedia` gallery is the source; variant points at one via `MediaId`
  (`ProductVariant.cs:117-121`), falling back to `ImageUrl`, then to product images
  (`ProductVariantDocument.cs:111-122`).
- `ProductMediaChangedEvent` rebuilds the MongoDB `Media[]` from SQL; removing a media also clears
  `MediaId`/`ImageUrl` on every variant that referenced it and republishes
  `ProductVariantUpdatedEvent` (`ProductMediaService.cs:121-181`).
- Variant image projection uses `MediaId` precedence over `ImageUrl`
  (`ProductStorefrontVisibilityAuthority` / `ProductVariantProjectionConsumer` applies SQL media
  authority before emitting).

### 3.5 Pricing overrides, stock ownership, publishing, visibility

- Variant price overrides product: storefront resolves `EffectiveOnlinePrice = OnlinePrice ?? Price`
  (`ProductVariant.cs:191-193`).
- **Stock is owned by the variant** (`ProductVariantStock` per warehouse; `AvailableQuantity =
  Quantity - ReservedQuantity`, `TotalQuantity = Quantity + DamagedQuantity` —
  `ProductVariantStock.cs:50-58`). Variant stock events carry `MutationSource` ∈ {`POS`,
  `CommerceHub`} for cross-boundary reconciliation (`ProductVariantEvents.cs:30-41`, `:194-198`).
- Publishing/visibility is **product-level** (`PublishStatus`, `IsOnlineVisible`); the variant
  contributes only `IsActive` and effective price presence. A variant product is hidden if no active
  variant has `EffectivePrice > 0` (`ProductSearchSqlAuthorityOverlay.cs:285-289`).

---

## 4. Category System

`Category` (`PointOfSale.Core/Entities/Category.cs`) is a **tenant-owned, self-referencing hierarchy**:
`ParentCategoryId` (nullable) → `ParentCategory` / `SubCategories` / `Products`. No slug column exists
on the `Category` table; the **slug is computed at read time** from the name via
`StorefrontCategoryService.GenerateSlug` (lowercase, strip non-alphanumerics — `StorefrontCategoryService.cs:408-419`).
Category lookups by slug resolve SQL by comparing `GenerateSlug(Name)` (`StorefrontCategoryService.cs:388-405`).

**Runtime authority for categories:** SQL is authoritative. `CategoryLineageAuthority.RequireLiveCategoryAsync`
rejects categories whose parent lineage is broken or deleted (`ProductsController.cs:275-282`,
`:492-499`), and `ProductSearchSqlAuthorityOverlay.ResolveLiveCategoryNameAsync` walks the parent
chain to denormalize a live `CategoryName`, returning null (→ tombstone) if the chain contains a
deleted or cyclic node (`ProductSearchSqlAuthorityOverlay.cs:431-490`).

Navigation, storefront category pages, and breadcrumbs are all derived by walking the SQL category
lineage at read time (`SeoService.BuildCategoryTrail`, `SeoService.cs:473-490`). Ordering/visibility
of categories is not defined by a sort-order column in `Category`; presentation ordering is derived
in the read services.

---

## 5. Media Architecture

- **Storage:** `ProductMedia` rows in PostgreSQL (`ProductMedia.cs`) — `Id`, `TenantId`, `ProductId`,
  `MediaType` (`image`|`video`), `Url`, `Provider` (nullable for image; `youtube`/`vimeo`/`mp4` for
  video), `Position`, `CreatedAtUtc`. There is **no CDN/thumbnail-generation code** in the catalog
  implementation — `Url` is an absolute URL or root-relative path validated by `ProductMediaService`
  (image extensions restricted to jpg/jpeg/png/webp; video providers validated —
  `ProductMediaService.cs:304-416`).
- **Primary image:** resolved by `ProductSearchDocumentMapper.GetPrimaryImageUrl` — first
  `Media[]` image by `Position`, else legacy `Images` JSON/comma field
  (`ProductSearchDocumentMapper.cs:73-112`).
- **Gallery ordering:** `Position` ascending, ties broken by `CreatedAtUtc`
  (`ProductSearchSqlAuthorityOverlay.cs:217-229`, `ProductMediaService.cs:64-68`). Reorder resequences
  positions 1..N (`ProductMediaService.cs:189-230`, `:262-268`).
- **Variant media:** pointer via `MediaId`; variant media change republishes variant events
  (`ProductMediaService.cs:139-179`).
- **Fallback logic:** `Media[]` (SQL-owned, projected to Mongo) is preferred; legacy `Product.Images`
  (comma/JSON string) is the compatibility fallback (`ProductDocument.cs:248-258`).

---

## 6. Pricing Architecture

There is **no price-list, tier, wholesale, or promotion entity in the catalog**; pricing is columnar.
Runtime pricing authority:

| Price | Location | Authority | Used by |
|-------|----------|-----------|---------|
| Cost / PurchasePrice | `Product.PurchasePrice`, `ProductVariant.CostPrice`, `ProductUnit.PurchasePrice` | SQL (system of record) | margin/profit |
| POS SalePrice (fallback) | `Product.SalePrice`, `ProductVariant.Price` | SQL | POS only |
| Storefront simple-product price | `ProductUnit.SellingPrice` on the default `ProductUnit` | SQL | storefront/cart/search simple products |
| Storefront variant-product price | `ProductVariant.OnlinePrice ?? ProductVariant.Price` | SQL | storefront/cart/search variant products |
| Unit price | `ProductUnit.SellingPrice` + `ConversionFactor` | SQL | multi-unit POS and simple storefront price |
| Effective storefront price | `ProductStorefrontVisibilityAuthority`: default unit for simple products; active priced variant for variant products | Derived from SQL-owned projection | storefront/search/affiliate |

- The current runtime rule is **unit-authoritative for simple storefront pricing**: a simple
  product without `DefaultUnit` is projected as visible but non-purchasable (`StorefrontEffectivePrice == 0`). `OnlinePrice` remains the source for variant storefront pricing, with `Price` as POS
  fallback (`ProductVariantDocument.EffectivePrice`).
- Variant-product storefront price = active variant with `EffectivePrice>0`, ordered by
  `IsDefault` then `SortOrder` (`ProductStorefrontVisibilityAuthority.ResolveVisibleVariant`,
  `ProductStorefrontVisibilityAuthority.cs:22-27`).
- **Promotion interaction** is overlaid at projection time:
  `PromotionVisibilityProjectionSupport.SelectWinningProductPromotion` /
  `ApplyProductPromotionOverlay` applies the winning product-scoped promotion onto the Mongo
  document during the SQL-authority recomposition (`ProductSearchSqlAuthorityOverlay.cs:328-341`).
  Coupon/category discount interaction is governed elsewhere (coupon validation reads `CategoryId`;
  the empty-category guard exists precisely to stop category-scoped coupon bypass —
  `Product.cs:46-50`). **Discount/promotion/coupon math is not a catalog responsibility** — catalog
  only exposes base price + winning promotion overlay.
- **CommerceHub pricing** is a *separate resolver* over variant SQL truth:
  `CommerceHubProductDocument` carries `Price`, `CommerceHubPriceOverride`, `PriceSource` (default
  `"ProductVariantEffectiveOnlinePrice"`), and `OverridePresent`
  (`CommerceHubProductDocument.cs:35-43`). `CommerceHubPriceAuthority` accepts
  `ProductVariant.EffectiveOnlinePrice` as the base price and only applies `CommerceHubPrice` when
  `CommerceHubPriceOverrideRuntimePolicy` is enabled for the tenant. The default runtime policy is
  disabled, so stored override values are audited/preserved but not public price authority unless
  the rollout policy is explicitly enabled.
- **POS pricing** uses `SalePrice`/`Price` and `ProductUnit` conversion factors directly from SQL;
  POS does not read Mongo for price.

**Runtime pricing authority = PostgreSQL.** Mongo/ES carry a *projection* of the SQL price plus the
SQL-resolved promotion overlay; they cannot originate price.

---

## 7. Catalog Publishing

Publishing is **product-level** (`PublishStatus` enum: `Draft=0`, `Published=1`, `Archived=2` —
`PublishStatus.cs`). Visibility flags:

- `IsActive` — hard on/off (also gates POS + storefront).
- `PublishStatus == Published` **AND** `IsOnlineVisible == true` — required for storefront/ES
  visibility (`ProductSearchSqlAuthorityOverlay.cs:171-172`; Mongo upsert requires both
  `IsActive && IsOnlineVisible` `StorefrontProductProjectionMutationBoundary.cs:76-79`).
- `IsSearchable` — eligible for search indexing.
- `IsFeatured` — homepage hero section.
- `AllowBackorder` — purchasable when out of stock.

**Projection rebuild / cache invalidation / search indexing** on publish change:
- `UpdateProduct` emits `ProductPublishStatusChangedEvent` when status or `IsOnlineVisible` flips
  (`ProductsController.cs:723-736`).
- The SQL-authority writer **tombstones** the Mongo `ProductDocument` when SQL no longer proves
  visibility (`ProductSqlAuthorityProjectionWriter.cs:94-112`).
- `ProductStockPriceSearchIndexConsumer` **deletes the ES document** when SQL authority says not
  active/visible (`ProductStockPriceSearchIndexConsumer.cs:346-362`, `:488-498`).
- `ProductCacheInvalidationConsumer` invalidates list/detail/homepage caches on every catalog event
  (`ProductCacheInvalidationConsumer.cs:244-313`), using `ProjectionConsumerEventFence` to dedupe
  replays and resolving the *current SQL slug* for a correct invalidation key
  (`:208-221`).
- Archiving: `UpdateProduct` emits `ProductArchivedEvent` when transitioning into archived/inactive
  (`ProductsController.cs:747-755`).

**Soft delete:** `Product` is soft-deleted via `BaseEntity.IsDeleted` (`:13`). `DeleteProduct`
guards against deleting products with stock/transaction history (falls back to inactive) —
controller comment `ProductsController.cs:808-812`. `ProductDeletedEvent` drives Mongo tombstone +
ES delete + cache invalidation. Variant delete uses `ProductVariantDeletedEvent` + Mongo tombstone
array + `DeletedVariantTombstones` to prevent resurrection by delayed create events
(`ProductVariantDocument`/`ProductDocument` tombstone logic).

---

## 8. Search Architecture

**Search truth originates in PostgreSQL, projected through Mongo, then indexed into Elasticsearch,
and re-owned by SQL on every index operation.**

Pipeline (`ProductStockPriceSearchIndexConsumer.cs`):
1. Event (stock/price/variant-stock) arrives.
2. Consumer waits for Mongo variant/product projection to be fresh (throws `ProjectionNotReadyException`
   to retry if Mongo is older than the event — `:306-320`, `:451-468`).
3. It calls `ProductSearchSqlAuthorityOverlay.ApplyAsync` to **rebuild the document from SQL**
   (variants, stock, media, units, category, winning promotion) — `:322-326`, `:470-474`.
4. If SQL says product is gone or not visible → **delete ES doc** (`:328-344`, `:476-498`).
5. Else map via `ProductSearchDocumentMapper.MapFromProductDocument` and `IndexAsync`.

`ProductSearchDocumentMapper` computes `Price = product.StorefrontVisiblePrice`, `IsInStock`,
`StockQuantity` (from active variants or product-level), `HasVariants`, `Brand`, `IsFeatured`,
`ImageUrl` (primary), promotion fields, affiliate enablement
(`ProductSearchDocumentMapper.cs:7-71`). `StorefrontVisiblePrice` resolves to default-unit price for
simple products and active variant effective price for variant products, preventing search price
from becoming a second authority. Stale-update protection: ES write skips if existing doc is newer
(`ProductSearchIndexService.cs:54-67`).

**Elasticsearch is not the authority** for any field — it is a search acceleration index fed by the
SQL-recomposed Mongo document. Filtering/sorting/faceting (price, in-stock, category, brand,
featured) all read from that recomposed document. Mongo itself is the storefront read candidate,
but the storefront read service **also recomposes SQL authority before returning**
(`StorefrontProductService` reads Mongo then `ApplyCurrentSqlProductDetailAuthorityAsync` —
`StorefrontProductService.cs:93-122`).

---

## 9. Storefront

Storefront reads follow a strict layered order (`StorefrontProductService.cs:19-32`,
`ProductSearchService.cs:14-22`):

1. Redis cache (graceful miss/error).
2. MongoDB `ProductDocument` as read-model candidate.
3. **Recompose current SQL product/media/variant/promotion authority** (SQL overlay).
4. Populate Redis only with SQL-accepted DTOs.
5. Return mapped DTO.

- **Product detail pages:** `GetProductBySlugAsync` / `GetProductByIdAsync` resolve Mongo by slug or
  id, recompose SQL, map to `ProductReadDto` (`StorefrontProductService.cs:56-160`).
- **Category pages / collections:** `IProductReadRepository.GetProductListAsync` /
  `GetProductListByCategoryTreeAsync` query Mongo with `onlineVisibleOnly` + affiliate filters
  (`ProductReadRepository.cs:37-61`).
- **Homepage products:** `GetFeaturedProductsAsync` (`IsFeatured && IsActive && IsOnlineVisible`),
  `GetNewArrivalsAsync` (recent by `CreatedAtUtc`) — `ProductReadRepository.cs:90-120`.
- **Search:** `ProductSearchService` queries ES, then hydrates from Mongo candidates and recomposes
  SQL before mapping (`ProductSearchService.cs:46-90`). Mongo text search is the ES fallback
  (`ProductReadRepository.SearchProductsAsync`, `:63-77`). Search pricing uses
  `ProductStorefrontVisibilityAuthority`, so simple products use the default SQL unit price and
  variant products use active variant effective price.
- **Recommendations / related / featured:** `Product.RelatedProductIds` (JSON array on `Product`) is
  the only related-product mechanism in the catalog; "featured" = `IsFeatured` flag; there is **no
  collaborative-filtering or ML recommendation engine** in the catalog implementation.
- **Caching:** Redis JSON cache keyed by slug/id (affiliate-mode suffix to prevent cache poisoning,
  `StorefrontProductService.cs:65-67`, `:138-142`); invalidated broadly on every catalog event
  (`ProductCacheInvalidationConsumer`).

---

## 10. CommerceHub

CommerceHub **does not own the catalog** — it **projects** a marketplace listing from the tenant's
SQL product/variant truth.

- `CommerceHubProductDocument` (`PointOfSale.Infrastructure/MongoDB/Documents/CommerceHubProductDocument.cs`)
  is keyed by `TenantId + ProductId + VariantId` (one listing row per variant), holding
  `Price`, `CommerceHubPriceOverride`, `PriceSource`, `CategoryId/Name/Path`, `AvailableStock`,
  `InStock`, `AverageRating`, `ReviewCount`, `PopularityScore`, `IsPublished`, plus per-concern
  event-version stamps and a `SearchSyncStatus` retry state machine (`Pending/Synced/Failed/
  TerminalFailure`, max 5 retries — `:9-14`, `:151-164`).
- `CommerceHubProjectionConsumer` subscribes to the **full catalog event surface**
  (`CommerceHubListingCreatedEvent/UpdatedEvent/UnpublishedEvent`, `ProductVariantStockChangedEvent`,
  `ProductMediaChangedEvent`, `ProductUpdatedEvent`, `ProductVariant*`, `ProductDeleted/Archived/
  PublishStatusChanged`, `Promotion*`, `Tenant*`, `StoreConfigUpdatedEvent`, `ShippingMethod*`,
  `TenantCurrencyChangedEvent` — `CommerceHubProjectionConsumer.cs:22-44`). It uses
  `CommerceHubPriceAuthority` to resolve the effective listing price
  (`:57`, `:74-100`) and re-indexes CommerceHub ES via its own search service.
- **Authority:** CommerceHub reads SQL product/variant/stock/promotion truth through the same events
  and re-derives. It has an audited override field, but the default runtime rejects override use
  unless `CommerceHubPriceOverrideRuntimePolicy` is enabled for the tenant. CommerceHub is a
  **projection + gated resolver layer**, not a catalog owner. The `ProductReadModelConsumer` /
  `MarketplaceMongoReadModelPipeline` feed the marketplace read model similarly from SQL-derived
  events.

---

## 11. CQRS

**Write model (PostgreSQL):**
- Writes run inside `ProductsController` (`CreateProduct`/`UpdateProduct`/`DeleteProduct`) and
  `ProductVariantService`, inside an explicit `BeginTransactionAsync` with MassTransit outbox events
  published in the same transaction (`ProductsController.cs:379-468`, `:565-758`).
- Events: `ProductCreatedEvent`, `ProductUpdatedEvent`, `ProductDeletedEvent`,
  `ProductPriceChangedEvent`, `ProductStockChangedEvent`, `ProductPublishStatusChangedEvent`,
  `ProductArchivedEvent`, `ProductMediaChangedEvent`, and the variant event family
  (`ProductEvents.cs`, `ProductVariantEvents.cs`).

**Read model (MongoDB + Elasticsearch + Redis):**
- Mongo `ProductDocument` is written by `ProductSqlAuthorityProjectionWriter` →
  `StorefrontProductProjectionMutationBoundary` (upsert or visibility tombstone,
  `ProductSqlAuthorityProjectionWriter.cs`, `StorefrontProductProjectionMutationBoundary.cs`).
- Variants are embedded in `ProductDocument.Variants[]` via `ProductVariantProjectionConsumer`
  (create/update/delete/stock), guarded by `ProjectionConsumerEventFence` idempotency and
  delete-tombstones (`ProductVariantProjectionConsumer.cs`).
- ES is fed by `ProductStockPriceSearchIndexConsumer` after SQL recomposition.
- Redis is invalidated (never the source of truth) by `ProductCacheInvalidationConsumer`.

**Rebuild process:** The `ProductSqlAuthorityProjectionWriter` + `ProductSearchSqlAuthorityOverlay`
constitute an **on-write and on-read SQL recomposition** (a "rebuild" of the projection from SQL on
every mutation), not a background full-rebuild. Dedicated reindex tooling exists
(`CommerceHubReindexService`, `AdminSearchSyncController`, `SearchReindexController`,
`AF16E_ReindexAffiliateProductsWithStorefrontUrl`) for backfills, but the steady state is
event-driven SQL-recomposed projection.

**Cache invalidation:** best-effort, fenced, covers list + detail (id/slug) + homepage + promotion
keys (`ProductCacheInvalidationConsumer.cs:244-313`).

---

## 12. Runtime Authority Matrix

| Concern | Owner (system of record) | Projection / derivative | Enforcement |
|---------|--------------------------|--------------------------|-------------|
| Product identity, name, SEO, category, publication | **PostgreSQL `Products`** | Mongo/ES/Redis | SQL overlay on read; tombstone on divergence |
| Variant identity, price, stock, SKU, barcode | **PostgreSQL `ProductVariants`/`ProductVariantStocks`** | Mongo `Variants[]` | Unique filtered indexes; semantic-lifecycle filter; SQL variant authority in consumer |
| Media | **PostgreSQL `ProductMedia`** | Mongo `Media[]` | `ProductMediaChangedEvent`; SQL media authority in overlay |
| Pricing (base) | **PostgreSQL** | Mongo/ES (projection) | Default unit for simple storefront; variant effective online price for variant storefront; SQL recompute |
| Promotion overlay | **PostgreSQL `Promotions`** | Mongo/ES doc fields | `PromotionVisibilityProjectionSupport` at projection time |
| Inventory identity | **PostgreSQL `ProductVariantStocks`** | Mongo `TotalStock` | `MutationSource` tagging; `StockUpdatedAtUtc` monotonic gate |
| Visibility / publishing | **PostgreSQL `PublishStatus`+`IsOnlineVisible`+`IsActive`** | Mongo/ES/Redis | Writer tombstones; ES deletes; cache invalidates |
| Search index | **PostgreSQL (via SQL overlay)** | Elasticsearch | `ProductSearchSqlAuthorityOverlay` recompose before index |
| Storefront projection | **PostgreSQL** | MongoDB `ProductDocument` | `ProductSqlAuthorityProjectionWriter` |
| SEO / slug / canonical URL | **PostgreSQL `Product.Slug`** (+ name-derived category slug) | Redis-cached SEO DTO | `SeoService` reads Mongo then SQL; category slug computed from name |
| CommerceHub listing | **PostgreSQL (tenant catalog)** | Mongo `commercehubproducts` + gated override metadata | `CommerceHubProjectionConsumer` + `CommerceHubPriceAuthority` |
| Affiliate discovery | **PostgreSQL** | Mongo `publicaffiliateproducts` + ES | `ProductStorefrontVisibilityAuthority.TryResolveAffiliatePublicPricing` |

---

## 13. Runtime Authority Divergences (implementation-backed)

1. **Product vs Variant price authority.** For `HasVariants=true`, `Product.SalePrice/OnlinePrice`
   are forced to 0/null; the variant owns price. For simple products, the product and its single
   default variant are kept in sync via mirror logic both ways
   (`ProductsController.cs:328-344`, `ProductVariantService.cs:275-279`,
   `ProductVariantCloneImportAuthority.cs:255-266`). Risk: a direct edit to `Product.SalePrice`
   without variant resync would be silently ignored for variant products.

2. **Mongo vs PostgreSQL (projection lag).** Mongo/ES are always *behind* SQL by at least one event.
   The system closes the gap with monotonic version/timestamp gates (`LastEventVersion`,
   `LastEventOccurredAtUtc`, `StockUpdatedAtUtc`) and **SQL recomposition on read/index**, so lag
   never surfaces as wrong truth on the public path — but a raw Mongo read (bypassing the overlay)
   can show stale data. The overlay throws `ProjectionNotReadyException` to retry indexing until
   Mongo catches up (`ProductStockPriceSearchIndexConsumer.cs:306-320`).

3. **Search vs Storefront.** Both derive from the same SQL-recomposed `ProductDocument`, but ES maps
   a *flattened* view (`StorefrontVisiblePrice`, active-variant stock, promotion fields) while the
   storefront reads the fuller Mongo doc + SQL recompose. Divergence is possible only if the mapper
   and overlay drift; both are fed from the same overlay output.

4. **Price authority (storefront vs CommerceHub).** Storefront simple products use the default SQL
   `ProductUnit.SellingPrice`; storefront variant products use variant `OnlinePrice ?? Price`.
   CommerceHub always starts from variant effective online price and only applies a stored
   `CommerceHubPrice` when `CommerceHubPriceOverrideRuntimePolicy` is enabled for that tenant. With
   the default policy, overrides are retained for audit/governance but public price remains
   `ProductVariantEffectiveOnlinePrice`.

5. **Media authority.** SQL `ProductMedia` is authoritative; variant `MediaId` points into it. The
   Mongo `Media[]` is rebuilt from SQL on `ProductMediaChangedEvent`. Legacy `Product.Images` string
   is a fallback only and is **never written by the controllers** (`ProductsController.cs:342`,
   `:594` set `Images = null`).

6. **Visibility authority.** `IsOnlineVisible` alone is insufficient — the SQL overlay also requires
   `PublishStatus == Published` and `IsActive` (`ProductSearchSqlAuthorityOverlay.cs:171-172`). Mongo
   upsert further requires `IsActive && IsOnlineVisible`
   (`StorefrontProductProjectionMutationBoundary.cs:76-79`). Three independent gates must align.

7. **Slug authority.** Product slug is a stored SQL column; category "slug" is **computed from the
   name at read time** and has no DB column (`StorefrontCategoryService.cs:408-419`). Two different
   slug semantics in one subsystem.

---

## 14. Technical Debt (implementation-backed)

1. **Legacy `WarehouseStock` / `Product.Images` carry-over.** `WarehouseStock` is explicitly a
   "compatibility table only, not an inventory authority"
   (`ProductVariantStock.cs:13-16`); `Product.Images` is set to null on every write and only exists
   as a Mongo fallback (`ProductsController.cs:342`,`:594`). Dead-ish fields retained for backward
   compat.

2. **No `Brand`/`Attribute`/price-list master data.** Brand is free text; attributes are denormalized
   variant name/value pairs; no tier/wholesale pricing. Limits reporting, brand governance, and B2B
   pricing that docs may imply exist.

3. **Variant/product mirror coupling.** Simple-product identity and POS fallback prices rely on
   bidirectional mirroring between `Product` and its default variant; storefront display price is
   default-unit authoritative. The invariant "exactly one default current variant" is guarded by
   code + unique index but is easy to violate via direct SQL or partial updates
   (`ProductVariantService.cs:210-219`).

4. **`ProductUnit` is `BaseEntity` but NOT `ITenantOwned`** directly — tenant scope is inherited via
   the parent `Product.TenantId` (`ProductUnit.cs:6`) and the `Unit.TenantId` join. The current EF
   model and migration add active uniqueness for product/unit, default/base unit, and unit barcode,
   plus database triggers preventing live barcode collisions with products and variants.

5. **Projection retry storms on `ProjectionNotReadyException`.** ES indexing throws
   `ProjectionNotReadyException` to force MassTransit retry until Mongo catches up
   (`ProductStockPriceSearchIndexConsumer.cs:302-320`). Under heavy variant churn this can create
   retry pressure; bounded by fences but not rate-limited.

6. **ES document delete on hidden products is terminal-ish.** If SQL authority flips to hidden, the
   ES doc is deleted; a later visibility restore depends on a subsequent event re-triggering index
   — there is no periodic full reconciliation beyond the admin reindex endpoints.

7. **`Images` JSON vs `Media[]` dual model.** Two media representations coexist; primary-image
   resolution must check both (`ProductSearchDocumentMapper.GetPrimaryImageUrl`,
   `:73-112`). Opportunity for a single media model.

---

## 15. Certification

### Implementation Confidence
**HIGH.** Every section is grounded in directly read source files (entities, EF configs, events,
controllers, consumers, Mongo/ES documents, services). No documentation was trusted as authority.

### Verified Capabilities
- Two-tier Product/Variant aggregate with enforced 1-default-variant invariant.
- SQL-authoritative CQRS with Mongo/ES/Redis as SQL-recomposed projections.
- Simple-product storefront price authority is the SQL default `ProductUnit.SellingPrice`; search,
  affiliate, cart, and storefront DTOs converge through `ProductStorefrontVisibilityAuthority`.
- Variant-level inventory (`ProductVariantStock`), pricing, SKU/barcode uniqueness (SQL unique
  filtered indexes + semantic-lifecycle gating).
- Publishing/visibility tri-state (`Draft/Published/Archived` + `IsActive`/`IsOnlineVisible`) with
  tombstone/delete propagation to all derived stores.
- SQL-overlay search indexing with stale-event rejection and projection-ready retry.
- CommerceHub listing projection with audited, rollout-gated price override metadata (not catalog
  ownership).
- Category self-referencing hierarchy with live-lineage validation and name-derived slug.
- Media gallery (SQL `ProductMedia`) with variant `MediaId` pointer and legacy `Images` fallback.
- SEO/canonical/breadcrumb service reading Mongo then SQL; 30-min SEO cache, cache-first.
- Event-fenced, idempotent cache invalidation across list/detail/homepage/promotion keys.

### Missing Capabilities
- **No** `Brand` master, `Attribute`/`AttributeValue` taxonomy, or price-list/tier/wholesale entity.
- **No** ML/collaborative recommendations (only `RelatedProductIds` JSON + `IsFeatured`).
- **No** CDN/thumbnail-generation pipeline (URLs only, validated by extension/provider).
- **No** category sort-order column (ordering derived in read services).
- **No** hard product-level slug uniqueness constraint (product slug is stored but not uniquely
  indexed; category slug is name-derived and thus non-unique by construction).

### Architectural Observations
- The dominant pattern is **"SQL is always re-asked for truth on the public path."** This is a
  deliberate anti-staleness stance: rather than trusting eventually-consistent Mongo/ES, every
  storefront read and every ES index op recomputes the document from PostgreSQL. This trades read
  latency for correctness and makes PostgreSQL the uncontested authority.
- Variant is a **first-class aggregate** (own events, own stock, own SKU/barcode uniqueness, own
  semantic lifecycle for merge/split), embedded in the storefront Mongo doc but never subordinate to
  the product for commerce fields.
- CommerceHub and Affiliate are **projections**, not catalog owners. CommerceHub carries audited
  override metadata, but public override application is gated by `CommerceHubPriceOverrideRuntimePolicy`;
  both surfaces subscribe to catalog events and re-derive from PostgreSQL authority.

### Runtime Authority Verification
Verified by reading the write path (`ProductsController`, `ProductVariantService`), the projection
writers (`ProductSqlAuthorityProjectionWriter`, `ProductVariantProjectionConsumer`,
`StorefrontProductProjectionMutationBoundary`), the SQL overlay
(`ProductSearchSqlAuthorityOverlay`), the consumers (`ProductCacheInvalidationConsumer`,
`ProductStockPriceSearchIndexConsumer`, `CommerceHubProjectionConsumer`), and the read services
(`StorefrontProductService`, `ProductSearchService`, `SeoService`). In every case the read/index
path either (a) rebuilds from SQL or (b) tombstones/deletes when SQL no longer proves truth. No code
path was found where Mongo/ES/Redis originate catalog truth on the public surface.

### Technical Debt
See §14. Highest-impact items: retained legacy `WarehouseStock`/`Images` fields, absence of master
data for brand/attributes/price-tiers, and reliance on `ProjectionNotReadyException` retry pressure
under variant churn.
