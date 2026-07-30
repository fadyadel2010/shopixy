# Pricing Authority Audit

Audit date: 2026-07-30

Mission: determine whether multiple pricing authorities exist after Product Authoring Recovery. This is an architecture audit only. No implementation, UI, entity, migration, or deletion changes were made.

## Final Certification

PRICING AUTHORITY CONSOLIDATION PASSED

The original audit found multiple live pricing authorities. The recovery completed on 2026-07-30 and supersedes the pre-recovery findings below.

## Recovery Certification Evidence

| Task | Result | Source-code evidence |
|---|---|---|
| Authority declaration | Passed | `CatalogPricingAuthority` declares simple default-unit and variant authorities; `Product` price fields are documented as compatibility mirrors; `ProductVariant` prices are documented as canonical for variant products. |
| Product authority removed | Passed | Product authoring initializes product price fields to mirror-safe values and calls `ApplyProductPricingCompatibilityMirror`; it no longer writes product prices from `command.Pricing` as business authority. |
| New reads removed | Passed | Manual orders use `CatalogPricingAuthority.ResolveSimpleSellingPrice(configuredUnit)` or `ResolveVariantOnlinePrice(defaultVariant)`; affiliate checks require default-unit or variant prices. |
| New writes removed | Passed | Legacy product-price request fields are mapped into missing default-unit prices by `ApplyLegacySimplePricingToDefaultUnit`, not written as product authority. |
| Compatibility layer centralized | Passed | `ProductPricingCompatibilityMirror` is the only live component allowed to mirror product price fields. |
| CommerceHub override isolated | Passed | `CommerceHubPriceAuthority` keeps accepted `CommerceHubListing.CommerceHubPrice` overrides channel-specific and reports `CommerceHubPriceSource.CommerceHubOverride`. |
| Architecture tests | Passed | `PricingAuthorityConsolidationCertificationTests` verifies authority declarations, reader migration, compatibility centralization, simple default-unit resolution, variant resolution, and CommerceHub override isolation. |

Verification:

- `dotnet build PointOfSale.sln --no-restore -p:BaseOutputPath=C:\tmp\pos-pricing-build\` passed with warnings only.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-restore --filter PricingAuthorityConsolidationCertificationTests -p:BaseOutputPath=.codex-temp\pos-pricing-test-3\` passed: 6/6 tests.

## Historical Audit

The sections below are retained to show the source-backed authority split that existed before recovery.

## Executive Conclusion

Conclusion: OPTION B. Pricing Authority split exists. Recommend Pricing Authority Consolidation.

Evidence summary:

| Finding | Evidence |
|---|---|
| Simple storefront pricing is default-unit based, not product-price based | `ProductDocument` says `DefaultUnit.SalePrice` is the only valid storefront selling price and `Price` is legacy (`PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:78-81`); `ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice` returns `DefaultUnit?.SalePrice ?? 0` (`PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs:5-6`) |
| POS unit pricing is `ProductUnit.SellingPrice` first, with variant fallback | POS cart uses `productUnit.SellingPrice ?? variant.Price * conversionFactor` (`PointOfSale.API/Services/Pos/PosCartRuntimeService.cs:551`); sales resolver does the same (`PointOfSale.API/Controllers/SalesController.cs:2617-2629`) |
| Storefront checkout pricing is variant based | Checkout loads SQL variants and sets `CurrentPrice` from `variant.OnlinePrice > 0 ? variant.OnlinePrice : variant.Price` (`PointOfSale.API/Services/Checkout/CheckoutService.cs:3948-3950`, `4020-4028`) |
| CommerceHub has a separate channel override authority | `CommerceHubPriceAuthority` resolves base variant price but accepts positive `CommerceHubListing.CommerceHubPrice` when runtime policy allows (`PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs:83-111`) |
| Product-level pricing remains live, not removable | Authoring writes `Product.PurchasePrice`, `SalePrice`, `OnlinePrice` for simple products (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:997-999`); simple product/variant mirror sync writes both directions (`PointOfSale.API/Services/ProductVariantService.cs:273-277`, `364-367`, `408-416`); manual order fallback still reads product prices (`PointOfSale.API/Controllers/OrdersController.cs:2049-2053`) |

## Task 01: Pricing Inventory

### Domain Entities

| Entity | Pricing fields | Evidence | Classification |
|---|---|---|---|
| `Product` | `PurchasePrice`, `SalePrice`, `OnlinePrice` | `PointOfSale.Core/Entities/Product.cs:52-67` | Live simple-product compatibility/mirror and fallback; product comments conflict with newer unit/variant authority |
| `ProductVariant` | `Price`, `OnlinePrice`, `CostPrice`, `EffectiveOnlinePrice` | `PointOfSale.Core/Entities/ProductVariant.cs:58-72`, `187-193` | Canonical variant sale/online/cost authority |
| `ProductUnit` | `PurchasePrice`, `SellingPrice`, `IsDefault` | `PointOfSale.Core/Entities/ProductUnit.cs:17-25`, `33-36` | Canonical unit/default-unit pricing authority |
| `CommerceHubListing` | `CommerceHubPrice` | Source scan: `PointOfSale.Core/Entities/CommerceHubListing.cs:17`, `46-75`, `106-138` | Canonical CommerceHub channel override when policy permits |
| `OrderItem` | `UnitPrice`, `CostPrice`, computed subtotal | Source scan: `PointOfSale.Core/Entities/OrderItem.cs:91-116`, `144-147` | Immutable order snapshot |
| `SalesInvoiceItem` | `UnitPrice` | Source scan: `PointOfSale.Core/Entities/SalesInvoiceItem.cs:35-37` | Sales snapshot |
| `PurchaseInvoiceItem` | `UnitPrice` | Source scan: `PointOfSale.Core/Entities/PurchaseInvoiceItem.cs:35-37` | Purchase cost input/snapshot |
| `ReturnItem` | `UnitPrice` | Source scan: `PointOfSale.Core/Entities/ReturnItem.cs:32` | Return snapshot |
| `StockMovement` | `Price` | Source scan: `PointOfSale.Core/Entities/StockMovement.cs:87-89` | Inventory valuation snapshot |

### EF Configuration And Schema

| Location | Pricing fields | Evidence | Classification |
|---|---|---|---|
| Product EF config | `PurchasePrice`, `SalePrice` precision | `PointOfSale.Infrastructure/Data/Configurations/ProductConfiguration.cs:23-27` | Persisted product compatibility/simple fields |
| Variant EF config | `Price`, `OnlinePrice`, `CostPrice` | `PointOfSale.Infrastructure/Data/Configurations/ProductVariantConfiguration.cs:38-46` | Persisted canonical variant fields |
| Product units EF/model | default/base uniqueness, unit table | `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1216-1251` | Persisted canonical unit fields, constraints around unit identity |
| Current baseline migration | `Products.PurchasePrice`, `SalePrice`, `OnlinePrice` | `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:3588-3590` | Live schema |
| Current baseline migration | `ProductUnits.PurchasePrice`, `SellingPrice` | `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:5434-5435` | Live schema |
| Current baseline migration | `ProductVariants.Price`, `OnlinePrice`, `CostPrice` | `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:7146-7148` | Live schema |

### Commands, DTOs, API Contracts, View Models

| Contract | Pricing fields | Evidence | Classification |
|---|---|---|---|
| Product authoring command | `ProductAuthoringPricing.PurchasePrice/SalePrice/OnlinePrice`; unit `PurchasePrice/SellingPrice`; variant `Price/OnlinePrice/CostPrice` | Source scan: `PointOfSale.Core/Application/ProductAuthoring/ProductAuthoringCommand.cs:48-53`, `89-108` | Business mutation input for all three pricing models |
| Products controller create/update requests | product, unit, variant pricing fields | Source scan: `PointOfSale.API/Controllers/ProductsController.cs:2176-2303` | Admin authoring API contract |
| Product variants controller requests/responses | `Price`, `OnlinePrice`, `CostPrice` | Source scan: `PointOfSale.API/Controllers/ProductVariantsController.cs:2174-2227` | Variant authoring API contract |
| Unit DTOs | `PurchasePrice`, `SellingPrice` | `PointOfSale.API/DTOs/UnitDTOs.cs:37-38`, `51-52` | Unit API contract |
| Storefront DTOs | unit `SalePrice`; product/list `Price`, `DefaultUnit`, `EffectivePrice`; variant `Price`, `OnlinePrice`, `EffectivePrice`; presentation `CurrentPrice`, `CompareAtPrice`, `DiscountedPrice` | Source scan: `PointOfSale.API/DTOs/Storefront/StorefrontDtos.cs:30`, `80-91`, `156-171`, `431-444`, `606-616` | Storefront read API, derivative |
| Checkout DTOs | `CartPrice`, `CurrentPrice`, `CostPrice` | Source scan: `PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs:868-937` | Checkout snapshot/revalidation contract |
| Cart DTOs | `UnitPrice`, `DiscountedUnitPrice`, `LineTotal`, `Subtotal` | Source scan: `PointOfSale.API/DTOs/Cart/CartDtos.cs:49`, `394-500` | Cart snapshot contract |
| CommerceHub DTOs | `CommerceHubPrice`, `EffectivePrice`, listing/variant `Price`, `OnlinePrice`, `UnitPrice` | Source scan: `PointOfSale.API/DTOs/CommerceHub/*.cs` | CommerceHub admin/read/checkout contracts |
| Frontend TypeScript contracts | storefront product/unit/variant/cart/order price fields and CommerceHub checkout price snapshot | Source scan: `PointOfSale.Storefront/src/types/product.ts`, `cart.ts`, `order.ts`; `PointOfSale.CommerceHub/src/types/checkout.ts` | Consumers of backend contracts only |

### Mongo Projections

| Projection | Pricing fields | Evidence | Classification |
|---|---|---|---|
| `ProductUnitDocument` | `SalePrice` | `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:10-37` | Derived from SQL `ProductUnit.SellingPrice` |
| `ProductDocument` | `Price`, `OnlinePrice`, `EffectivePrice`, `StorefrontEffectivePrice`, `StorefrontVisiblePrice`, `DefaultUnit`, `Units`, `PurchasePrice` | `PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:78-81`, `151-208` | Mixed legacy compatibility plus canonical storefront helpers |
| `ProductVariantDocument` | `Price`, `OnlinePrice`, `EffectivePrice`, `CostPrice` | Source scan: `PointOfSale.Infrastructure/MongoDB/Documents/ProductVariantDocument.cs:88-109` | Derived from SQL variants |
| `CommerceHubProductDocument` | `Price`, `CommerceHubPriceOverride`, `PriceSource` | Source scan: `PointOfSale.Infrastructure/MongoDB/Documents/CommerceHubProductDocument.cs:49-54` | Derived from variant plus listing override |
| Affiliate documents | `Price`, `MinVariantPrice`, `MaxVariantPrice` | Source scan: `PointOfSale.Infrastructure/MongoDB/Documents/Affiliate/PublicAffiliateProductDocument.cs:49-65` | Derived public affiliate read model |
| Order/cart documents | `UnitPrice`, `Subtotal`, item totals | Source scan: `PointOfSale.Infrastructure/MongoDB/Documents/OrderDocument.cs:99-100`, `410-417`; `CommerceHubOrderDocument.cs:33`, `92`, `146` | Transaction snapshots |

### Search Projections

| Projection | Pricing fields | Evidence | Classification |
|---|---|---|---|
| Product search | `ProductSearchDocument.Price` | Source scan: `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs:135-139` | Derived from storefront visible price |
| Product search mapper | `Price = product.StorefrontVisiblePrice` | Source scan: `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocumentMapper.cs:30-46` | SQL/Mongo derivative |
| CommerceHub search | `Price`, `PriceSource`, override flags | Source scan: `PointOfSale.Infrastructure/Elasticsearch/Documents/CommerceHubProductSearchDocument.cs:37-39` | Derived CommerceHub read model |
| Affiliate search | `Price`, `MinVariantPrice`, `MaxVariantPrice` | Source scan: `PointOfSale.Infrastructure/Elasticsearch/Documents/PublicAffiliateProductSearchDocument.cs:48-62` | Derived affiliate read model |

### Database Schema And Migration History

| Artifact | Evidence | Meaning |
|---|---|---|
| Current baseline product columns | `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:3588-3590` | Product-level price columns remain live schema |
| Current baseline unit columns | `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:5434-5435` | Unit price columns are live schema |
| Current baseline variant columns | `PointOfSale.Infrastructure/Migrations/20260723214642_Baseline_Recovery3.cs:7146-7148` | Variant price columns are live schema |
| Product-to-variant backfill | `DB/migrations/Phase11_1_ProductVariants.sql:160-201` | Historical migration copied product prices into variant prices |
| Product-to-unit backfill | `DB/sql/migrate-multi-unit-data.sql:29-39` | Historical migration copied product prices into default unit prices |

## Task 02: Read Path Inventory

| Area | Fields read | Reason | Authority assumed | Evidence |
|---|---|---|---|---|
| POS cart | `ProductUnit.SellingPrice`, fallback `ProductVariant.Price`, `ProductVariant.CostPrice` | Create cart item price/cost snapshot | SQL ProductUnit, fallback SQL ProductVariant | `PointOfSale.API/Services/Pos/PosCartRuntimeService.cs:500-580` |
| Sales invoices | `ProductUnit.SellingPrice`, fallback `ProductVariant.Price`; `ProductVariant.CostPrice` | Re-authoritize submitted sale lines | SQL ProductUnit, fallback SQL ProductVariant | `PointOfSale.API/Controllers/SalesController.cs:1269-1288`, `2617-2629` |
| Storefront checkout | `ProductVariant.OnlinePrice`, `Price`, `CostPrice` | Price-change detection and final order snapshots | SQL ProductVariant | `PointOfSale.API/Services/Checkout/CheckoutService.cs:3801-4040` |
| Manual order creation | `ProductUnit.SellingPrice`, `ProductVariant.EffectiveOnlinePrice`, fallback `Product.OnlinePrice/SalePrice` | Re-authoritize order line price | Split: unit, variant, product legacy fallback | `PointOfSale.API/Controllers/OrdersController.cs:2034-2053` |
| Storefront product detail/list | `DefaultUnit.SalePrice`, variants `Price/OnlinePrice` | Compose public price/purchasability | SQL-overlaid Mongo/default unit and variant docs | `PointOfSale.API/Services/Storefront/StorefrontProductService.cs:278-323`, source scan |
| Storefront cart | `ProductDocument.DefaultUnit.SalePrice`, variant effective price | Add-to-cart validation and snapshot | Mongo projection of SQL unit/variant | Source scan: `PointOfSale.API/Services/Cart/CartService.cs:362-631` |
| CommerceHub cart/checkout | `ProductVariant.Price/OnlinePrice`, `CommerceHubListing.CommerceHubPrice` | Resolve final item price and commission | SQL ProductVariant plus listing override | `PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs:451-459`, `571-604`; `PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs:83-111` |
| Affiliate eligibility | Variant online/price; product online/sale; unit selling price | Require positive storefront price before affiliate enablement | Split: variant, product legacy, unit fallback | `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:553-587` |
| Discount / coupon / promotion | Cart item `UnitPrice`, line total, subtotal | Calculate discounts against cart/order snapshot | Snapshot, not catalog authority | Source scan: `PointOfSale.API/Services/Promotions/PromotionEngine.cs:447-557`; `PointOfSale.Infrastructure/Discounts/CouponValidationService.cs:645-718` |
| Purchase / inventory | Purchase `UnitCost`, `ProductVariant.CostPrice` | Weighted average cost and stock movement valuation | Purchase snapshot plus SQL variant cost | `PointOfSale.API/Controllers/PurchasesController.cs:197-223` |
| Reports / analytics / exports | `SalesInvoiceItem.UnitPrice`, `OrderItem.UnitPrice`, `ProductVariant.CostPrice`, purchase/return snapshots | Financial reporting and external analytics | Transaction snapshots and SQL variant cost | Source scan: `ReportsController`, GA/TikTok dispatchers |
| Mongo product read repository | `ProductDocument.Price` and variant documents | Search/filter/display and fallback reads | Mongo derivative; some legacy compatibility | Source scan: `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs:398-406` |
| Elasticsearch product search | `ProductSearchDocument.Price` | Search filter/sort/facets | Elasticsearch derivative from `StorefrontVisiblePrice` | Source scan: `ProductDiscoveryService.cs:338-350`, `637-670`; mapper evidence above |
| Background services/projections | Existing Mongo/search prices plus SQL overlay | Reindex/convergence | SQL overlay derivative | Source scan: `ProductSearchIndexConsumer`, `ProductStockPriceSearchIndexConsumer`, `CommerceHubReindexService` |

## Task 03: Write Path Inventory

| Writer | Fields written | Trigger/source | Authority classification | Evidence |
|---|---|---|---|---|
| Product authoring orchestrator | `Product.PurchasePrice`, `SalePrice`, `OnlinePrice` | Product create/update command | Live simple-product product mirror; variant products zero/null product prices | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:997-999` |
| Product authoring orchestrator | `ProductUnit.PurchasePrice`, `SellingPrice` | Product authoring units | SQL ProductUnit authority | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1082-1090`, `1124-1143` |
| Product authoring orchestrator | `ProductVariant.Price`, `OnlinePrice`, `CostPrice` | Product authoring variants | SQL ProductVariant authority | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1219-1228`, `1289-1317` |
| Product authoring outbox | product `SalePrice`, `OnlinePrice`, `PurchasePrice`, unit data | Product created/updated events | Event payload from SQL/product/default unit | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:607-644`, `662-719` |
| Product variant service | default variant from product prices, product mirror from default variant | Simple-product sync | Bidirectional compatibility mirror | `PointOfSale.API/Services/ProductVariantService.cs:273-277`, `364-367`, `408-416` |
| Product variants controller | variant `Price`, `OnlinePrice`, `CostPrice` | Direct variant API | SQL ProductVariant authority outside unified product authoring | Source scan: `PointOfSale.API/Controllers/ProductVariantsController.cs:247-258`, `632-637` |
| Purchases controller | `PurchaseInvoiceItem.UnitPrice`, `ProductVariant.CostPrice`, `Product.PurchasePrice` mirror, `StockMovement.Price` | Purchase receipt | Purchase cost authority updates SQL variant cost | `PointOfSale.API/Controllers/PurchasesController.cs:197-223`, `775-780` |
| Purchase returns controller | `ProductVariant.CostPrice`, simple `Product.PurchasePrice` mirror | Purchase return | SQL variant cost recalculation | Source scan: `PointOfSale.API/Controllers/PurchaseReturnsController.cs:538-545` |
| POS cart runtime | `PosCartItem.UnitPriceSnapshot`, `CostPriceSnapshot`, totals | POS cart add/update | Snapshot from SQL unit/variant | `PointOfSale.API/Services/Pos/PosCartRuntimeService.cs:551-580` |
| Sales controller | `SalesInvoiceItem.UnitPrice`, cost snapshots | Sale creation/update | Snapshot from SQL unit/variant | `PointOfSale.API/Controllers/SalesController.cs:1269-1288`, `2617-2629` |
| Storefront cart service | Redis cart `UnitPrice`, promotion prices | Add-to-cart/update | Snapshot from Mongo storefront visibility | Source scan: `PointOfSale.API/Services/Cart/CartService.cs:611-631`, `2925-2971` |
| Checkout service | checkout `CurrentPrice`, `CostPrice`; order `UnitPrice`, `CostPrice` | Checkout revalidation/order creation | SQL variant to immutable order snapshot | `PointOfSale.API/Services/Checkout/CheckoutService.cs:3948-3950`, `6798-6800` |
| CommerceHub command handlers/listing entity | `CommerceHubListing.CommerceHubPrice` | Admin publish/update/remove override | CommerceHub channel override | Source scan: `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs:197-267`, `603-637`; `PointOfSale.Core/Entities/CommerceHubListing.cs:46-75`, `106-138` |
| Product created consumer | Mongo product/unit/product price fields | Product created event | Event-derived projection, not SQL recomposed in that consumer | Source scan: `PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs:175-213` |
| Product SQL authority projection writer / overlay | Mongo `DefaultUnit`, `Units`, `Price`, `OnlinePrice`, variants, purchase price | Product update/repair/rebuild/search overlay | SQL recomposition | `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:172-222`, `238-250`, `316-329` |
| Product search mapper/indexer | Elasticsearch `Price` | Product index/reindex | Derived from `StorefrontVisiblePrice` | Source scan: `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocumentMapper.cs:30-46` |
| CommerceHub projection/search overlay | CommerceHub Mongo/search `Price`, override fields | Listing/price/promotion/reindex events | SQL variant plus listing override | Source scan: `CommerceHubProjectionConsumer`, `CommerceHubSearchSqlAuthorityOverlay` |
| Seeders/migrations/repair | Product/variant/unit price backfills | Development seed and historical migration | Historical compatibility/backfill | `DB/migrations/Phase11_1_ProductVariants.sql:160-201`, `DB/sql/migrate-multi-unit-data.sql:29-39` |

## Task 04: Pricing Authority Matrix

See `pricing-authority-matrix.md`. No pricing field from this audit remains unclassified there.

## Task 05: Simple Product Analysis

Question: Is `Product.SalePrice` authoritative, or is default `ProductUnit` authoritative?

Answer: both remain live, but for different surfaces. That is an authority split.

Evidence for default unit authority:

| Evidence | Meaning |
|---|---|
| `ProductDocument` critical rule says `DefaultUnit.SalePrice` is the only valid storefront selling price and `Price` is legacy (`PointOfSale.Infrastructure/MongoDB/Documents/ProductDocument.cs:78-81`) | Simple storefront pricing authority is default-unit pricing |
| `ProductStorefrontVisibilityAuthority.ResolveSimpleProductPrice` returns `product.DefaultUnit?.SalePrice ?? 0` (`PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs:5-6`) | Runtime simple storefront price ignores product-level `SalePrice` |
| SQL overlay maps `ProductUnit.SellingPrice` to `DefaultUnit.SalePrice` (`PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:184-222`) | Default unit derives from current SQL units, not stale Mongo product price |

Evidence for `Product.SalePrice` remaining live:

| Evidence | Meaning |
|---|---|
| Product authoring writes `Product.SalePrice` for non-variant products (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:997-999`) | Product sale price is still part of business mutation path |
| Default variant sync copies `Product.SalePrice` into `ProductVariant.Price` for simple products (`PointOfSale.API/Services/ProductVariantService.cs:273-277`, `408-416`) | Product sale price can originate variant price during simple sync/create |
| Default variant sync copies `ProductVariant.Price` back into `Product.SalePrice` (`PointOfSale.API/Services/ProductVariantService.cs:364-367`) | Product sale price is maintained as a live mirror |
| Manual order price fallback uses `Product.OnlinePrice` or `Product.SalePrice` when unit/default variant are absent (`PointOfSale.API/Controllers/OrdersController.cs:2049-2053`) | Product sale price remains a runtime fallback |
| Affiliate eligibility checks product `OnlinePrice/SalePrice` before checking units (`PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:572-587`) | Product price still affects business eligibility |

Conclusion: Default product unit is authoritative for simple storefront purchasability/visible price. `Product.SalePrice` is still live as a simple-product compatibility/mirror/fallback and cannot be classified as dead legacy.

## Task 06: Variant Product Analysis

Question: Where does variant pricing originate?

Answer: SQL `ProductVariant` is the primary variant pricing origin. There is no `VariantUnit` type found in the audited source. `ProductUnit` can affect POS unit-price resolution but not variant online checkout authority.

| Variant pricing surface | Origin | Evidence |
|---|---|---|
| Variant POS/base selling price | `ProductVariant.Price` | Entity says variant `Price` is POS sale price (`PointOfSale.Core/Entities/ProductVariant.cs:58-61`); authoring writes it (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1219-1228`, `1315`) |
| Variant online price | `ProductVariant.OnlinePrice` with fallback to `Price` | Entity and computed property define fallback (`PointOfSale.Core/Entities/ProductVariant.cs:63-67`, `187-193`); checkout uses the same SQL expression (`PointOfSale.API/Services/Checkout/CheckoutService.cs:4020-4028`) |
| Variant cost/purchase price | `ProductVariant.CostPrice` | Entity defines cost (`PointOfSale.Core/Entities/ProductVariant.cs:69-72`); purchases update it (`PointOfSale.API/Controllers/PurchasesController.cs:218-223`) |
| POS unit-adjusted variant sale price | `ProductUnit.SellingPrice`, else `ProductVariant.Price * conversionFactor` | POS and sales resolvers (`PointOfSale.API/Services/Pos/PosCartRuntimeService.cs:551`, `PointOfSale.API/Controllers/SalesController.cs:2617-2629`) |
| CommerceHub variant item price | `ProductVariant.OnlinePrice` if positive else `Price`, optionally `CommerceHubListing.CommerceHubPrice` override | `CommerceHubPriceAuthority` (`PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs:83-111`) |

## Task 07: Projection Analysis

Trace: SQL -> Outbox -> Consumers -> Mongo -> Search.

| Stage | Pricing projected | Evidence | Authority result |
|---|---|---|---|
| SQL product authoring | Product simple prices, units, variants | Product authoring writes product/unit/variant prices (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:997-999`, `1082-1143`, `1219-1317`) | SQL holds multiple live catalog price shapes |
| Outbox/events | Product created/updated events include sale/online/purchase and default unit data; variant events include price/online/cost | `ProductAuthoringOrchestrator.cs:607-644`, `662-719`, `1188-1199` | Event payload carries both product-level and unit/variant price data |
| Mongo product create | Maps event unit sale price, product sale/online/purchase | Source scan: `ProductCreatedEventConsumer.cs:175-213` | Event-derived projection on create |
| Mongo product update/rebuild | SQL overlay recomposes units/default unit, variants, and product compatibility fields | `ProductSearchSqlAuthorityOverlay.cs:172-222`, `238-250`, `316-329` | SQL recomposition converges projection |
| Mongo storefront runtime | Simple uses `DefaultUnit.SalePrice`; variants use variant effective price | `ProductStorefrontVisibilityAuthority.cs:5-44`, `70-89` | Mongo read model is derivative, but runtime helpers are canonical read path |
| Elasticsearch product search | Maps `ProductDocument.StorefrontVisiblePrice` into `ProductSearchDocument.Price` | Source scan: `ProductSearchDocumentMapper.cs:30-46` | Search price is derivative |
| CommerceHub projection | Final price from variant effective price plus listing override | `CommerceHubPriceAuthority.cs:83-111`; source scan: `CommerceHubProjectionConsumer`, `CommerceHubSearchSqlAuthorityOverlay` | Separate CommerceHub channel authority exists |

Projected fields:

| Projection | Field | Source |
|---|---|---|
| Product Mongo `DefaultUnit.SalePrice` | SQL `ProductUnit.SellingPrice` | `ProductSearchSqlAuthorityOverlay.cs:211-222` |
| Product Mongo `Units[].SalePrice` | SQL `ProductUnit.SellingPrice` | `ProductSearchSqlAuthorityOverlay.cs:199-208` |
| Product Mongo `Variants[].Price/OnlinePrice/CostPrice` | SQL `ProductVariant` fields | `ProductSearchSqlAuthorityOverlay.cs:238-250` |
| Product Mongo `Price/OnlinePrice` | Default active SQL variant when present; else SQL product fields | `ProductSearchSqlAuthorityOverlay.cs:316-329` |
| Elasticsearch product `Price` | `ProductDocument.StorefrontVisiblePrice` | Source scan: `ProductSearchDocumentMapper.cs:30-46` |
| CommerceHub Mongo/search `Price` | `CommerceHubPriceAuthority.FinalPrice` | `CommerceHubPriceAuthority.cs:102-120` |

## Task 08: Legacy Detection

| Field | Live business field or legacy compatibility? | Evidence-backed conclusion |
|---|---|---|
| `Product.SalePrice` | Legacy compatibility, but still live | Live because authoring writes it for simple products (`ProductAuthoringOrchestrator.cs:997-999`), simple variant sync reads/writes it (`ProductVariantService.cs:273-277`, `364-367`, `408-416`), orders still fallback to it (`OrdersController.cs:2049-2053`), and affiliate eligibility reads it (`AffiliateService.cs:572-587`). Legacy because `ProductVariant` comments explicitly say product prices remain for backward compatibility (`PointOfSale.Core/Entities/ProductVariant.cs:21-24`) and Mongo document says product `Price` is legacy for cart/checkout (`ProductDocument.cs:78-81`). |
| `Product.OnlinePrice` | Legacy compatibility, but still live | Live because authoring writes/nulls it (`ProductAuthoringOrchestrator.cs:997-999`), simple variant sync mirrors it (`ProductVariantService.cs:273-277`, `364-367`, `408-416`), manual order fallback reads it (`OrdersController.cs:2049-2053`), and affiliate eligibility reads it (`AffiliateService.cs:572-587`). Not canonical for simple storefront price because default unit is used (`ProductStorefrontVisibilityAuthority.cs:5-6`). |
| `Product.PurchasePrice` | Legacy/simple-product cost mirror, but still live | Live because authoring writes it (`ProductAuthoringOrchestrator.cs:997`), purchase flow mirrors variant cost into it for simple products (`PurchasesController.cs:218-223`, `775-780`), and default variant sync maps it to/from `ProductVariant.CostPrice` (`ProductVariantService.cs:273-277`, `364-367`, `408-416`). Canonical cost authority is `ProductVariant.CostPrice` for variant costs and reports. |
| `Product.CostPrice` | No entity field found | Product has `PurchasePrice`; variant has `CostPrice` (`Product.cs:52-55`, `ProductVariant.cs:69-72`). API DTOs may expose `CostPrice` for variants/inventory, not product entity authority. |

Important constraint: Do not remove product pricing fields based on this audit. Source evidence proves they remain live in mutation, mirror, fallback, and eligibility paths.

## Task 09: Architectural Recommendation

OPTION B: Pricing Authority split exists. Recommend Pricing Authority Consolidation.

Recommended consolidation direction, without implementation in this audit:

| Area | Recommendation |
|---|---|
| Name the catalog authorities explicitly | Define whether simple-product commercial price is `ProductUnit.SellingPrice` only, default `ProductVariant.Price/OnlinePrice` only, or a governed mirror between them. Current code uses both. |
| Treat product-level prices as compatibility only if proven and guarded | Product fields cannot be removed or ignored yet because they are live in authoring, mirror sync, fallback, affiliate eligibility, and migration history. |
| Separate channel overrides from base catalog price | CommerceHub `CommerceHubPrice` is a distinct channel override authority. Keep it explicit if business-approved, but do not confuse it with product catalog price. |
| Remove undocumented fallbacks only after source-backed migration | Manual order and affiliate fallback paths still read product-level prices. Consolidation must first eliminate or formalize those paths. |
| Maintain projection derivatives | Mongo and Elasticsearch should remain read-model projections from the selected SQL authority, not pricing authorities. |

## Quality Gate Certification

No implementation changes were made. No UI changes were made. No entity modifications were made. No migrations were created. No code was deleted.
