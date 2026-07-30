# Product Catalog Canonical Data Contract

Certification date: 2026-07-30

Status: canonical architecture specification. This document defines ownership. It does not certify that implementation already complies.

Final certification:

CATALOG DATA CONTRACT CERTIFICATION PASSED WITH OBSERVATIONS

Reason: this contract assigns every inventoried Product Catalog field exactly one canonical owner. The observations are source-backed implementation conflicts where live code still writes fields outside the owner boundary.

## Certification Question

Does every Product Catalog field have exactly one canonical owner?

Answer: yes, by this contract. Every field is assigned to one owner in `catalog-field-ownership-matrix.md`.

Implementation observations:

- `ProductVariant.CostPrice` owner is Pricing / Cost Authority. Current code writes it from Product Authoring and Purchasing/Purchase Returns; those non-owner writes must be routed through the owner contract. Evidence: canonical field declaration at `PointOfSale.Core/Entities/ProductVariant.cs:22`, `PointOfSale.Core/Entities/ProductVariant.cs:73`; non-owner assignments at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1231`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1331`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1350`; purchasing assignments at `PointOfSale.API/Controllers/PurchasesController.cs:219`, `PointOfSale.API/Controllers/PurchasesController.cs:1133`, `PointOfSale.API/Controllers/PurchasesController.cs:1461`; return assignment at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:540`.
- `ProductUnit.PurchasePrice` owner is Pricing / Cost Authority for simple-product default-unit cost. Current code writes it from Product Authoring and purchase/return compatibility mirroring. Evidence: field at `PointOfSale.Core/Entities/ProductUnit.cs:21`; Product Authoring writes at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1090`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1132`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1144`; purchasing writes at `PointOfSale.API/Controllers/PurchasesController.cs:794` and `PointOfSale.API/Controllers/PurchaseReturnsController.cs:550`.
- `ProductVariant.MediaId` and `ProductVariant.ImageUrl` owner is Catalog Media Assignment. Product Media is allowed to clear those pointers only as delete cleanup. Evidence: fields at `PointOfSale.Core/Entities/ProductVariant.cs:122`, `PointOfSale.Core/Entities/ProductVariant.cs:128`; authoring maps media at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1234`; cleanup at `PointOfSale.API/Services/ProductMediaService.cs:148`.

## Catalog Entity Inventory

Canonical SQL catalog entities:

| Entity | Contract status | Evidence |
|---|---|---|
| `Product` | Catalog aggregate root for authored product content, lifecycle, publication, compatibility mirrors, SEO, shipping metadata, merchandising metadata, and product-level projection markers. | `PointOfSale.Core/Entities/Product.cs:9`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:52` |
| `Category` | Tenant POS product taxonomy. This is the repo's ProductCategory entity. | `PointOfSale.Core/Entities/Category.cs:7`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:51` |
| `Unit` | Tenant unit-of-measure catalog. | `PointOfSale.Core/Entities/Unit.cs:7`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:57` |
| `ProductUnit` | Product-to-unit binding and simple-product default-unit price authority. | `PointOfSale.Core/Entities/ProductUnit.cs:7`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:58` |
| `ProductVariant` | Sellable SKU and variant product price/identity/media/semantic lifecycle authority. | `PointOfSale.Core/Entities/ProductVariant.cs:27`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:54` |
| `ProductVariantAttribute` | Variant option key/value metadata. | `PointOfSale.Core/Entities/ProductVariantAttribute.cs:17`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:56` |
| `ProductVariantStock` | Inventory-owned product-adjacent stock ledger per variant and warehouse. | `PointOfSale.Core/Entities/ProductVariantStock.cs:17`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:55` |
| `ProductMedia` | Product media gallery. | `PointOfSale.Core/Entities/ProductMedia.cs:5`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:53` |

Catalog-adjacent authorities that reference products but do not own base catalog fields:

| Entity | Canonical owner | Evidence |
|---|---|---|
| `CommerceHubListing` | CommerceHub Marketplace listing/channel authority. | `PointOfSale.Core/Entities/CommerceHubListing.cs:8`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:59` |
| `CommerceHubCategory`, `CommerceHubCategoryAttribute` | CommerceHub global marketplace taxonomy. | `PointOfSale.Core/Entities/CommerceHubCategory.cs:6`; `PointOfSale.Core/Entities/CommerceHubCategoryAttribute.cs:15` |
| `AffiliateProductPolicy`, `AffiliateCategory` | Affiliate marketplace policy/taxonomy authority. | `PointOfSale.Core/Entities/Affiliate/AffiliateProductPolicy.cs:14`; `PointOfSale.Core/Entities/Affiliate/AffiliateCategory.cs:22` |
| `ProductPixelOverride` | Analytics/pixel value override authority. | `PointOfSale.Core/Analytics/ProductPixelOverride.cs:16`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:383` |
| `ProductRequestIdempotency` | Product mutation infrastructure authority. | `PointOfSale.Core/Entities/ProductRequestIdempotency.cs:5`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:108` |
| `ProductReview` | Marketplace/customer review authority, not catalog authoring. | `PointOfSale.Core/Entities/ProductReview.cs:5`; DbSet at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:81` |

Concept inventory:

- `ProductBrand` does not exist as a separate entity. Brand is `Product.Brand`; source `PointOfSale.Core/Entities/Product.cs:241`.
- `ProductTag` does not exist as a separate entity. Tags are `Product.Tags` and `Product.TagsJson`; source `PointOfSale.Core/Entities/Product.cs:251`, `PointOfSale.Core/Entities/Product.cs:256`.
- Product category is represented by `Product.CategoryId` plus the `Category` entity; source `PointOfSale.Core/Entities/Product.cs:50`, `PointOfSale.Core/Entities/Category.cs:7`.

## Owner Catalog

| Owner | Owns | Allowed writers |
|---|---|---|
| Product Authoring / Catalog | Product identity/content/category/SEO/shipping/publication/base product metadata; Unit and Category catalog rows; ProductUnit identity and sell-price membership except cost value; ProductVariant identity/SKU/barcode/attributes/default flag/lifecycle/sort/weight. | `IProductAuthoringOrchestrator` via `ProductsController`, governed admin taxonomy/unit services. Evidence: controller delegation at `PointOfSale.API/Controllers/ProductsController.cs:302`, `PointOfSale.API/Controllers/ProductsController.cs:345`; orchestrator persistence at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:219`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1126`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1340`. |
| Pricing / Cost Authority | Simple default-unit purchase cost, simple default-unit selling price as price authority, variant sale/online/cost prices, CommerceHub base price resolution. | Named pricing authority APIs, with Product Authoring as command host only after delegating to pricing rules. Evidence: `PointOfSale.Core/Pricing/CatalogPricingAuthority.cs:8`; CommerceHub resolver at `PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs:30`. |
| Product Media | `ProductMedia` rows and media ordering; may clear variant media pointers only when deleting referenced media. | `ProductMediaService` through `ProductMediaController`. Evidence: `PointOfSale.API/Services/ProductMediaService.cs:10`, `PointOfSale.API/Services/ProductMediaService.cs:102`, `PointOfSale.API/Services/ProductMediaService.cs:153`, `PointOfSale.API/Services/ProductMediaService.cs:223`. |
| Inventory / Warehouse | `ProductVariantStock` quantities/reservations/damaged stock and stock projection freshness markers. | Inventory services, stock repository, stock mutation support, CommerceHub inventory consistency. Evidence: `PointOfSale.Core/Entities/ProductVariantStock.cs:38`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:165`, `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs:110`. |
| Projection / Search | Mongo/Elasticsearch/read-model documents, event ordering metadata, tombstones, denormalized fields. | Projection consumers, SQL authority overlay, mutation boundary, repair/reindex jobs. Evidence: `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:89`; `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs:5`; `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:29`; allowlist at `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5`. |
| CommerceHub Marketplace | Listing publication, CommerceHub category assignment, channel price override, listing attribute snapshots. | CommerceHub command handlers and listing methods. Evidence: `PointOfSale.Core/Entities/CommerceHubListing.cs:14`, `PointOfSale.Core/Entities/CommerceHubListing.cs:17`, `PointOfSale.Core/Entities/CommerceHubListing.cs:82`, `PointOfSale.Core/Entities/CommerceHubListing.cs:106`. |
| Affiliate | Affiliate enablement, commission policy, affiliate category/template, public affiliate projections. | `AffiliateService`, affiliate projection consumers. Evidence: `PointOfSale.Core/Entities/Affiliate/AffiliateProductPolicy.cs:27`, `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:433`, `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs:76`. |
| Analytics / Pixel | Per-product pixel conversion value override. | Admin pixel tracking controller and product pixel override service. Evidence: `PointOfSale.Core/Analytics/ProductPixelOverride.cs:16`; `PointOfSale.Infrastructure/Analytics/ProductPixelOverrideService.cs:94`. |
| Transaction Domains | Orders, checkout, cart, sales, purchases, returns, stock movements, reservations. | Their own bounded contexts only; they may snapshot catalog fields but may not become catalog authority. Evidence: checkout price/cost snapshots at `PointOfSale.API/Services/Checkout/CheckoutService.cs:3949`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:3950`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:6799`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:6800`. |
| Infrastructure | Idempotency rows, `RowVersion`, projection mutation versions, event metadata. | Persistence pipeline, idempotency service, projection infrastructure. Evidence: common audit fields at `PointOfSale.Core/Entities/BaseEntity.cs:8`; product projection version at `PointOfSale.Core/Entities/Product.cs:211`; variant row version at `PointOfSale.Core/Entities/ProductVariant.cs:171`. |

## Mutation Contract

| Classification | Create | Update | Delete | Mirror | Project |
|---|---|---|---|---|---|
| Canonical | Owner only. | Owner only. | Owner only, including soft delete/lifecycle when applicable. | Not applicable unless the owner explicitly produces a compatibility mirror. | Projection can read after canonical commit. |
| Derived | Projection owner creates from canonical source. | Projection owner recomposes from canonical source. | Projection owner tombstones/deletes when canonical source disappears. | No reverse mirror. | Yes, within projection boundary only. |
| Compatibility | Owner or approved compatibility helper creates/maintains mirror. | Owner or approved compatibility helper only. | Removed only by owner-approved migration/removal plan. | Yes, from named source to named destination. | May be projected as compatibility, never as authority. |
| Projection | Projection owner only. | Projection owner only. | Projection owner only. | No. | Yes. |
| Transactional Snapshot | Transaction owner creates at transaction time after catalog validation. | Immutable except transaction correction/refund workflows in the transaction context. | By transaction lifecycle only. | No. | May feed analytics, not catalog. |
| Infrastructure | Persistence/infrastructure owner only. | Persistence/infrastructure owner only. | Infrastructure retention policy only. | No. | May drive projection ordering. |

Forbidden rule: a non-owner must not create, update, delete, mirror, or project a field unless its row in `catalog-field-ownership-matrix.md` explicitly grants that capability.

## Projection Contract

Canonical flow:

```text
SQL Authority
  -> Outbox/Event Trigger
  -> Projection Pipeline
  -> Mongo Read Models
  -> Elasticsearch/Search
  -> Affiliate Public Read Models
  -> CommerceHub Public/Search Read Models
```

No reverse authority is permitted.

Rules:

- SQL Product/Catalog/Inventory/CommerceHub/Affiliate owners are the only source authorities for their fields.
- Events trigger projection but are not independent source truth when SQL disagrees.
- Mongo, Elasticsearch, Redis, frontend DTOs, cart totals, checkout totals, affiliate public documents, and CommerceHub search documents are derivative unless their entity is explicitly assigned as a separate adjacent authority.
- Projection repair/reindex may rebuild from SQL. It may not originate product content, price, stock, publication, category, media, affiliate, or CommerceHub listing decisions.

Evidence:

- Projection writer recomposes from SQL before persistence: `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:89`.
- Product search SQL overlay is the runtime convergence layer: `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:29`.
- Mutation boundary controls Mongo Product projection writes: `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs:5`.
- Product document declares PostgreSQL as source of truth and default-unit storefront price rule: `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:78`.
- Search document uses product search fields as read-model data: `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:35`.

## Compatibility Contract

| Compatibility field | Owner | Mirror source | Mirror destination | Removal plan |
|---|---|---|---|---|
| `Product.PurchasePrice` | Pricing Compatibility | Default `ProductUnit.PurchasePrice` for simple products; `ProductVariant.CostPrice` for variant cost compatibility where required. | `Product.PurchasePrice` | Keep until all DTO/API/report/manual-order/legacy consumers stop reading product-level purchase price. Guard with architecture tests. |
| `Product.SalePrice` | Pricing Compatibility | Default `ProductUnit.SellingPrice` for simple products; `ProductVariant.Price` for legacy default variant compatibility. | `Product.SalePrice` | Keep until legacy product-level price consumers are removed and migrations prove no fallback dependency. |
| `Product.OnlinePrice` | Pricing Compatibility | Variant `OnlinePrice` or null/simple storefront default-unit semantics depending product shape. | `Product.OnlinePrice` | Keep until product-level online price is removed from DTOs/projections/fallbacks. |
| `Product.Images` | Product Media Compatibility | `ProductMedia.Url` ordered gallery, with legacy images as fallback. | `Product.Images` and projection `ImageUrl` compatibility fields. | Keep until all storefront/admin/API surfaces consume `ProductMedia` only. |
| `Product.Unit` | Unit Compatibility | Default/base `Unit.Name` through `ProductUnit`. | `Product.Unit` | Keep until product unit relations fully replace product string unit. |
| `Product.Tags` | Content Compatibility | `Product.TagsJson` structured tags. | `Product.Tags` comma-separated string. | Keep until all consumers use structured tags. |
| `ProductDocument.Price`, `ProductDocument.OnlinePrice`, `ProductDocument.PurchasePrice` | Projection / Compatibility | SQL product compatibility fields or unit/variant effective fields via overlay. | Mongo Product projection. | Keep as display/fallback compatibility only; never cart/checkout authority. |
| `ProductVariant.ImageUrl` | Media Compatibility | `ProductVariant.MediaId` and `ProductMedia.Url`. | Legacy URL field. | Keep until variant media selection uses `MediaId` everywhere. |

Evidence: compatibility helper at `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:9`; mirror calls at `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:22`, `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:24`, `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:43`, `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:47`; product document legacy price comments at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:78`.

## Transactional Data Exclusion

The following are not Product Catalog ownership fields and must never become catalog authority:

- Cart item `UnitPrice`, `DiscountedUnitPrice`, cart `Subtotal`.
- Checkout `CartPrice`, `CurrentPrice`, `CostPrice`, recalculated subtotals/totals.
- Order item `UnitPrice`, `CostPrice`, `Subtotal`.
- Sales invoice item `UnitPrice`.
- Purchase invoice item `UnitPrice`.
- Return item `UnitPrice`.
- Stock movement `Price`.
- Checkout/order/reservation state, including `InventoryReservation` and reserved quantities as transaction controls.

Evidence: cart snapshot write at `PointOfSale.API/Services/Cart/CartService.cs:619`; checkout revalidation at `PointOfSale.API/Services/Checkout/CheckoutService.cs:3949`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:3950`; order snapshot write at `PointOfSale.API/Services/Checkout/CheckoutService.cs:6799`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:6800`; order item snapshot fields at `PointOfSale.Core/Entities/OrderItem.cs:95`, `PointOfSale.Core/Entities/OrderItem.cs:116`; sales and purchase item snapshot fields at `PointOfSale.Core/Entities/SalesInvoiceItem.cs:37`, `PointOfSale.Core/Entities/PurchaseInvoiceItem.cs:37`.

## Architecture Contract

1. Product Catalog fields have exactly one canonical owner.
2. Implementation may have forbidden writers today; those writers do not define ownership.
3. Compatibility fields are owned mirrors with named source and destination.
4. Projection fields are disposable read models. They may be rebuilt from SQL and must not write back.
5. Transaction snapshots are immutable business records outside Catalog.
6. New fields must be added to `catalog-field-ownership-matrix.md` in the same change that introduces them.
7. New non-owner mutations must either be rejected or explicitly classified before merge.
