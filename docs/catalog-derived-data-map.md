# Product Catalog Derived Data Map

Certification date: 2026-07-30

Status: canonical derived-data, compatibility, projection, and transactional-snapshot contract.

Final certification:

CATALOG DATA CONTRACT CERTIFICATION PASSED WITH OBSERVATIONS

## Authority Flow

```text
SQL Authority
  -> Outbox/Event Trigger
  -> Projection
  -> Mongo
  -> Search
  -> Affiliate
  -> CommerceHub
```

No reverse authority is permitted. Affiliate and CommerceHub may own their own adjacent SQL fields (`AffiliateProductPolicy`, `CommerceHubListing`) but their public product documents/search indices remain projections.

## Derived Data Inventory

| Derived surface | Fields | Source authority | Projection owner | Allowed writers | Forbidden writers | Evidence |
|---|---|---|---|---|---|---|
| Product price compatibility mirrors | `Product.PurchasePrice`, `Product.SalePrice`, `Product.OnlinePrice` | Default `ProductUnit.PurchasePrice`, default `ProductUnit.SellingPrice`, `ProductVariant.Price`, `ProductVariant.OnlinePrice`, `ProductVariant.CostPrice` by product shape | Pricing Compatibility | `ProductPricingCompatibilityMirror`; Product Authoring only as mirror host | Purchasing/returns/controllers unless routed through Pricing / Cost Authority | Comments at `PointOfSale.Core/Entities/Product.cs:52`-`:68`; helper at `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:9`; mirror calls at `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:22`, `:24`, `:43`, `:47` |
| Legacy product unit/string compatibility | `Product.Unit` | `ProductUnit` + `Unit` default/base relationship | Unit Compatibility | Product Authoring/unit migration | Inventory/projection/frontend | `PointOfSale.Core/Entities/Product.cs:83`; `PointOfSale.Core/Entities/ProductUnit.cs:9`-`:43`; `PointOfSale.Core/Entities/Unit.cs:19` |
| Legacy product media compatibility | `Product.Images`, `ProductVariant.ImageUrl` | `ProductMedia.Url`, `ProductVariant.MediaId` | Product Media Compatibility | Product Media and Product Authoring assignment/cleanup | Projection/search/frontend as source writers | `PointOfSale.Core/Entities/Product.cs:93`; `PointOfSale.Core/Entities/ProductMedia.cs:7`-`:14`; `PointOfSale.Core/Entities/ProductVariant.cs:122`, `:128`; cleanup at `PointOfSale.API/Services/ProductMediaService.cs:148` |
| Structured text display projections | `ProductDocument.Features`, `ProductDocument.Faqs`, `ProductDocument.Tags`, `ProductDocument.AiMetadata` | `Product.FeaturesJson`, `Product.FaqJson`, `Product.TagsJson`, `Product.AiMetadataJson` | Projection / Search | SQL authority projection writer/overlay | Mongo/search/frontend | SQL fields at `PointOfSale.Core/Entities/Product.cs:37`, `:43`, `:256`, `:266`; projection fields at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:244`, `:249`, `:254`, `:265` |
| Mongo product content projection | `ProductDocument.TenantId`, `ProductId`, `Name`, `NameAr`, `SKU`, `Barcode`, `CategoryId`, `CategoryName`, `Description`, `DescriptionAr`, `MetaTitle`, `MetaDescription`, `Brand`, `Images`, `Media`, `Slug`, `ProductTemplate`, `Weight` | SQL `Product`, `Category`, `ProductMedia` | Projection / Search | `ProductSqlAuthorityProjectionWriter`, projection consumers, mutation boundary | Product Authoring/business controllers direct Mongo writes | `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:123`-`:307`, `:703`; writer at `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:89`; boundary at `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs:5` |
| Mongo lifecycle and publication projection | `ProductDocument.IsOnlineVisible`, `IsSearchable`, `IsActive`, `IsFeatured`, `TrackStock`, `AllowBackorder`, `HasVariants` | SQL `Product` | Projection / Search | Projection consumers/writer | Frontend/search/business controllers as source writers | Projection fields at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:318`-`:347`, `:581`; SQL fields at `PointOfSale.Core/Entities/Product.cs:95`, `:96`, `:113`, `:180`-`:205` |
| Mongo unit projection | `ProductDocument.DefaultUnit`, `ProductDocument.Units`, `ProductUnitDocument.UnitId`, `Name`, `Symbol`, `SalePrice`, `ConversionFactor`, `IsBaseUnit` | SQL `ProductUnit` + `Unit`; `SalePrice` from `ProductUnit.SellingPrice` | Projection / Search | SQL authority overlay/writer | Cart/checkout/frontend as authority | Projection fields at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:20`-`:50`, `:196`, `:202`; SQL authority overlay evidence in `pricing-authority-matrix.md` |
| Mongo product compatibility prices | `ProductDocument.Price`, `ProductDocument.OnlinePrice`, `ProductDocument.PurchasePrice`, `ProductDocument.EffectivePrice` | SQL Product compatibility mirrors, default unit, default variant depending overlay | Projection / Search | Projection writer/overlay; computed accessor for `EffectivePrice` | Cart/checkout/business decisions as authority | ProductDocument warning at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:78`; fields at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:156`, `:164`, `:168`, `:208` |
| Mongo storefront price helpers | `ProductDocument.StorefrontEffectivePrice`, `ProductDocument.StorefrontVisiblePrice`, affiliate pricing helper outputs | SQL default unit/variant effective price | Derived computed accessors | Computed accessors only | Any persisted writer | Fields are `[BsonIgnore]` at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:174`, `:180`; visibility authority references in `PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs:5` |
| Mongo variant projection | `ProductVariantDocument.VariantId`, `SKU`, `Barcode`, `Price`, `OnlinePrice`, `EffectivePrice`, `CostPrice`, `MediaId`, `ImageUrl`, `IsDefault`, `IsActive`, `TotalStock`, `InStock`, `SortOrder`, `Attributes`, `Weight` | SQL `ProductVariant`, `ProductVariantAttribute`, `ProductVariantStock`, `ProductMedia` | Projection / Search | Variant/media/stock projection consumers, SQL overlay | Frontend/cart/checkout/search as source writers | `PointOfSale.Infrastructure/MongoDB/Documents/ProductVariantDocument.cs:72`-`:157`, `:214`; `EffectivePrice` computed at `:101`; `InStock` computed at `:146` |
| Mongo variant event metadata | `ProductVariantDocument.SyncedAtUtc`, `StockUpdatedAtUtc`, `LastStockEventId`, `LastStockUpdatedAtUtc`, `LastStockEventVersion`, `LastEventOccurredAtUtc`, `LastEventVersion`, tombstone fields | SQL/source event ordering metadata | Projection / Search | Projection consumers and repair/reindex | Business domains/frontend | `PointOfSale.Infrastructure/MongoDB/Documents/ProductVariantDocument.cs:36`-`:45`, `:163`-`:205`; tombstone list on product at `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:432` |
| Mongo affiliate projection on product | `ProductDocument.IsAffiliateEnabled`, `AffiliateEnabledAtUtc` | SQL `AffiliateProductPolicy` | Affiliate Projection | Affiliate projection consumer | Product Authoring/frontend/search as source writers | `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:599`, `:605`; consumer at `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs:76` |
| Mongo promotion projection on product | `HasActivePromotion`, `PromotionBadge`, `ActivePromotionId`, `PromotionDiscountType`, `PromotionDiscountValue`, `PromotionMaxDiscountAmount`, `PromotionStartDate`, `PromotionEndDate`, `SearchBoostOverride`, `DiscountedPrice` | SQL Promotion authority and product visible price | Promotion Projection | Promotion projection consumer | Product Authoring/frontend/search as source writers | `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:615`-`:680` |
| Elasticsearch product search document | `ProductSearchDocument.*`, including `Price`, `SearchText`, `Brand`, `IsInStock`, promotion and affiliate flags | SQL-recomposed Mongo ProductDocument / SQL authority overlay | Search Projection | Search index consumers/reindex services | Product Authoring/frontend/business controllers | Fields at `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:46`-`:332`; search text built at `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:341`; price classified derivative in `pricing-authority-matrix.md` |
| Affiliate public product documents/search | Public affiliate product price/range/category/storefront URL/visibility fields | SQL Product, ProductVariant, ProductUnit, ProductMedia, AffiliateProductPolicy, Store profile/currency | Affiliate Projection | `AffiliateProductPolicyProjectionConsumer`, affiliate reindex scripts | Product Authoring/frontend/search as source writers | Projection consumer evidence at `PointOfSale.Infrastructure/Consumers/Affiliate/AffiliateProductPolicyProjectionConsumer.cs:1111`, `:1190`, `:1625`; source-backed matrix in `docs/product-projection-authority-matrix.md` |
| CommerceHub product documents/search | CommerceHub public listing, price, override, media, stock, category, search sync fields | SQL `CommerceHubListing`, Product, ProductVariant, ProductMedia, ProductVariantStock, Store/Currency | CommerceHub Projection | `CommerceHubProjectionConsumer`, CommerceHub reindex/auto-healing/search jobs | Product Authoring/frontend/search as source writers | Listing authority at `PointOfSale.Core/Entities/CommerceHubListing.cs:11`-`:34`; projection evidence in `docs/product-projection-authority-matrix.md`; reindex status writes at `PointOfSale.Infrastructure/Jobs/CommerceHubReindexService.cs:693` |
| Marketplace product read model | Marketplace product content/price/tags/category projection fields | SQL Product/ProductVariant/default unit | Marketplace Projection | Marketplace read-model projection services/repository | Product Authoring/frontend as direct read-model writers | `PointOfSale.Infrastructure/ReadModels/Marketplace/MarketplaceReadModelProjectionServices.cs:268`; price projection noted in `pricing-authority-matrix.md` |

## Search Fields

Search fields are all projection fields. Their owner is Projection / Search.

| Search field group | Source | Rule |
|---|---|---|
| Identity/search scope: `TenantId`, `ProductId`, `EntityType`, document id | SQL Product + tenant | Must be tenant-filtered; no reverse authority. |
| Text: `Name`, `NameAr`, `Description`, `CategoryName`, `SKU`, `Barcode`, `Brand`, `SearchText` | SQL Product, Category, search enrichment | Built at index time only. Search enrichment may expand terms but cannot mutate catalog text. |
| Price: `Price` | `ProductDocument.StorefrontVisiblePrice` from SQL default unit/variant | Used for filter/sort/display only; checkout/cart revalidate. |
| State: `IsActive`, `IsOnlineVisible`, `IsSearchable`, `HasVariants`, `IsInStock`, `StockQuantity` | SQL Product and Inventory projections | Search suppresses or ranks; it does not authorize purchase. |
| Promotion: `HasActivePromotion`, `PromotionDiscountValue`, `PromotionDiscountType`, `ActivePromotionId`, `PromotionStartDate`, `PromotionEndDate`, `SearchBoostOverride` | Promotion projection | Merchandising/search only; final price presentation is backend-composed. |
| Affiliate: `IsAffiliateEnabled`, `AffiliateEnabledAtUtc` | AffiliateProductPolicy projection | Filter/rank only; affiliate policy remains SQL authority. |

Evidence: `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:46`-`:332`; `BuildSearchText()` at `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:341`.

## Compatibility Fields

| Field | Mirror source | Mirror destination | Owner | Removal plan |
|---|---|---|---|---|
| `Product.PurchasePrice` | Default unit purchase cost or variant cost compatibility source | Product compatibility column and ProductDocument compatibility field | Pricing Compatibility | Remove only after all DTO/API/manual-order/report/projection fallbacks stop reading it and data migration is complete. |
| `Product.SalePrice` | Default unit selling price or variant price compatibility source | Product compatibility column and ProductDocument compatibility field | Pricing Compatibility | Remove after ProductUnit/Variant-only readers are enforced and legacy DTOs retire. |
| `Product.OnlinePrice` | Variant online price or null/simple fallback policy | Product compatibility column and ProductDocument compatibility field | Pricing Compatibility | Remove after product-level online price is absent from authoring DTOs, projections, and fallbacks. |
| `Product.Unit` | Default/base `Unit.Name` | Product string field | Unit Compatibility | Remove after all surfaces read ProductUnit/Unit relation. |
| `Product.Images` | `ProductMedia.Url` ordered gallery or legacy upload string | Product string field and projection fallback image | Product Media Compatibility | Remove after every media consumer reads `ProductMedia`. |
| `Product.Tags` | `Product.TagsJson` structured tag list | Product string field and legacy DTOs | Product Content Compatibility | Remove after all consumers read structured tags. |
| `ProductVariant.ImageUrl` | `ProductVariant.MediaId` -> `ProductMedia.Url` | Variant legacy URL field and projection image fallback | Product Media Compatibility | Remove after all variant image consumers use `MediaId`/media gallery. |
| `ProductDocument.Price`, `ProductDocument.OnlinePrice`, `ProductDocument.PurchasePrice` | SQL compatibility mirrors / overlay | Mongo projection compatibility fields | Projection Compatibility | Keep as display/fallback only; never cart/checkout authority. |

## Transactional Snapshot Review

These fields should never belong to Catalog:

| Transactional field group | Owner | Source at creation | Rule | Evidence |
|---|---|---|---|---|
| Cart item `UnitPrice`, `DiscountedUnitPrice`; cart `Subtotal` | Cart | Backend-visible price and promotion calculation | Mutable only in cart context; checkout revalidates. | `PointOfSale.API/Services/Cart/CartService.cs:619`, `:631`, `:3001` |
| Checkout `CartPrice`, `CurrentPrice`, `CostPrice`, recalculated subtotal/totals | Checkout | Cart snapshot plus SQL variant revalidation | Snapshot only; never writes catalog. | `PointOfSale.API/Services/Checkout/CheckoutService.cs:3530`-`:3532`, `:3949`-`:3950`, `:4366` |
| Order item `UnitPrice`, `CostPrice`, `Subtotal`; order totals | Orders / Checkout | SQL product/variant/unit price and cost at checkout/order creation | Immutable order record except governed corrections/refunds. | `PointOfSale.Core/Entities/OrderItem.cs:95`, `:116`, `:147`; `PointOfSale.API/Services/Checkout/CheckoutService.cs:6799`-`:6800` |
| Sales invoice item `UnitPrice` | Sales | Catalog-authorized unit/variant price at sale time | Sales snapshot; never catalog authority. | `PointOfSale.Core/Entities/SalesInvoiceItem.cs:37`; authoritative sales resolver at `PointOfSale.API/Controllers/SalesController.cs:1269`-`:1288` |
| Purchase invoice item `UnitPrice` | Purchasing | Purchase receipt input | Purchase snapshot; may feed cost authority only through explicit Pricing / Cost boundary. | `PointOfSale.Core/Entities/PurchaseInvoiceItem.cs:37`; purchasing write at `PointOfSale.API/Controllers/PurchasesController.cs:210` |
| Return item `UnitPrice` | Returns | Original sale/purchase line | Return snapshot; never catalog authority. | `PointOfSale.Core/Entities/ReturnItem.cs:32`; return line at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:519` |
| Stock movement `Price`, quantities before/after | Inventory | Transaction stock movement valuation | Inventory audit/valuation snapshot; never product price authority. | `PointOfSale.Core/Entities/StockMovement.cs:87`; stock movement write at `PointOfSale.API/Controllers/PurchasesController.cs:228` |
| `InventoryReservation` and reservation state | Inventory / Checkout | Checkout/CommerceHub reservation operation | Reservation controls stock availability but is not catalog content. | `PointOfSale.Core/Entities/InventoryReservation.cs:6`, `:15`; checkout reservation references at `PointOfSale.API/Services/Checkout/CheckoutService.cs:5059` |

## Non-Owner Mutation Observations

These are implementation findings, not ownership definitions:

| Field | Contract owner | Current non-owner writer | Required remediation |
|---|---|---|---|
| `ProductVariant.CostPrice` | Pricing / Cost Authority | Product Authoring writes initial/update values; Purchasing and Purchase Returns assign recalculated cost directly. | Route every cost mutation through a named Pricing / Cost Authority service. Product Authoring may submit intent; Purchasing may submit receipt/return facts. |
| `ProductUnit.PurchasePrice` | Pricing / Cost Authority | Product Authoring and purchase/return compatibility mirroring assign it directly. | Route simple default-unit purchase cost writes through Pricing / Cost Authority; keep `Product.PurchasePrice` as mirror only. |
| `ProductVariant.MediaId`, `ProductVariant.ImageUrl` | Catalog Media Assignment | Product Media clears pointers on delete. | Keep allowed cleanup explicit; forbid Product Media from assigning new variant media. |
| Product projection timestamps/version fields | Infrastructure | Product Authoring, media, affiliate, and repair paths touch projection freshness. | Keep as infrastructure-only and guard with source-scanning tests. |

Evidence for these observations is consolidated in `business-authority-review.md`, `domain-boundary-review.md`, `domain-authority-matrix.md`, and `pricing-authority-matrix.md`.
