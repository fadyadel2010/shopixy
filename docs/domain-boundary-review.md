# Product Domain Boundary Review

Independent architecture certification date: 2026-07-30

Final certification: DOMAIN BOUNDARY CERTIFICATION FAILED

Certification question: Does every business authority belong to exactly one bounded context?

Answer: No. Most Product-adjacent business state can be assigned to a bounded context, but cost/purchase-price authority is live in more than one mutation context, and several cross-domain writes use shared EF entities/`ApplicationDbContext` rather than an explicit anti-corruption layer. The decisive certification failure is source-code-backed and does not depend on compilation or test results.

## Scope

Reviewed Product-affecting mutations in production source under `PointOfSale.API`, `PointOfSale.Core`, and `PointOfSale.Infrastructure`, with prior authority artifacts used only as navigation aids. Evidence below cites source files and line numbers from the current workspace.

## Task-01 Domain Inventory

Nothing remains unidentified for the Product-affecting surface reviewed here.

| Bounded context | Product-affecting responsibility | Source evidence |
|---|---|---|
| Product Authoring / Catalog | Product aggregate authored fields, units, variants, lifecycle, publish status, compatibility mirrors | `ProductsController` delegates create/update/delete/deactivate/unit mutation to `_productAuthoringOrchestrator.AuthorAsync` at `PointOfSale.API/Controllers/ProductsController.cs:302`, `:345`, `:408`, `:454`, `:1446`, `:1562`, `:1641`; `ProductAuthoringOrchestrator` persists Product, ProductUnit, ProductVariant at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:219`, `:1126`, `:1340`, and saves at `:493`. |
| Product Media | Product media rows and media projection touch; clears variant media pointers when a media item is removed | `ProductMediaController` uses `IProductMediaService` at `PointOfSale.API/Controllers/ProductMediaController.cs:24`, add/remove/reorder entry points at `:86`, `:148`, `:199`; `ProductMediaService` adds/removes media at `PointOfSale.API/Services/ProductMediaService.cs:102`, `:153`, clears `variant.MediaId` and `variant.ImageUrl` at `:148-149`, touches product projection timestamp at `:299-301`. |
| Pricing / Cost | Catalog sell-price resolution, variant cost, simple-product purchase-price mirrors, CommerceHub channel override resolution | `CatalogPricingAuthority` declares simple/variant pricing resolvers at `PointOfSale.Core/Pricing/CatalogPricingAuthority.cs:10-22`; `ProductVariant` declares variant price fields canonical at `PointOfSale.Core/Entities/ProductVariant.cs:22-23`; `ProductPricingCompatibilityMirror` says it is the only live component allowed to maintain legacy Product price fields at `PointOfSale.Core/Pricing/ProductPricingCompatibilityMirror.cs:6`; `CommerceHubPriceAuthority` chooses `CommerceHubPriceSource.CommerceHubOverride` at `PointOfSale.Core/Pricing/CommerceHubPriceAuthority.cs:102-111`. |
| Purchasing | Purchase invoices, purchase returns, stock receipt/reversal, weighted-average variant cost mutation | Purchase receipt mutates `variant.CostPrice` and stock at `PointOfSale.API/Controllers/PurchasesController.cs:219-225`; purchase update repeats at `:1133-1139`; purchase delete/reversal mutates cost and stock at `:1461-1478`; purchase return mutates cost and stock at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:540-556`. |
| Inventory / Warehouse | Quantitative stock ledger: `ProductVariantStock.Quantity`, `ReservedQuantity`, `DamagedQuantity`, movements, warehouse transfer/opening stock | `ProductVariantStock` defines quantity/reservation fields at `PointOfSale.Core/Entities/ProductVariantStock.cs:38-58`; `InventoryService` mutates stock in adjust/transfer/opening-stock flows at `PointOfSale.API/Services/InventoryService.cs:70`, `:148-151`, `:314`; warehouse controller calls inventory service and saves at `PointOfSale.API/Controllers/WarehousesController.cs:242`, `:269`, `:349-392`. |
| Sales | Sales invoices and immutable transaction snapshots; stock decrease/increase for sale and sales returns | `SalesController` validates and decrements stock at `PointOfSale.API/Controllers/SalesController.cs:242`, `:268`, `:310`, publishes stock events at `:471`; sales returns lock stock and publish at `PointOfSale.API/Controllers/SalesReturnsController.cs:683`, `:759`, `:951`. |
| Checkout | Cart/order validation, checkout price/cost snapshots, inventory reservation/release/commit orchestration | Checkout writes item cost snapshot from SQL variant at `PointOfSale.API/Services/Checkout/CheckoutService.cs:3950`, writes order item price/cost snapshots at `:6799-6800`, releases/binds reservations through repository at `:604`, `:5261`, `:5850`. |
| CommerceHub Marketplace | Marketplace listing lifecycle, channel price override, marketplace read model/search projection, marketplace checkout inventory bridge | `CommerceHubListing` owns listing publish state and price override fields at `PointOfSale.Core/Entities/CommerceHubListing.cs:14-21`, mutates publish/override at `:82-130`, unpublishes at `:141-161`; handlers add/update/unpublish listings at `PointOfSale.API/CommerceHub/Handlers/CommerceHubCommandHandlers.cs:215`, `:237`, `:599-656`, `:771-800`; `InventoryConsistencyService` reserves/commits/returns inventory for CommerceHub at `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs:130`, `:901`, `:1081`. |
| Affiliate | Affiliate product policy, affiliate links, commission policy/projections; reads catalog, price, media, stock | Admin affiliate controller delegates writes to `IAffiliateService` at `PointOfSale.API/Controllers/AdminProductAffiliateController.cs:171`, `:241`, `:286`, `:326`; `AffiliateService` mutates `AffiliateProductPolicies` at `PointOfSale.Infrastructure/Services/Affiliate/AffiliateService.cs:199`, `:433`, `:470`, `:500`; promotion links read Product and create link rows at `PointOfSale.API/Controllers/AffiliatePromotionLinksController.cs:74-88`, `:111-115`. |
| Projection / Search | Derivative Mongo/Elasticsearch/read models from SQL authority and events | `ProductSqlAuthorityProjectionWriter` recomposes from SQL at `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:89` and writes through `StorefrontProductProjectionMutationBoundary` at `:97`, `:122`; the mutation boundary is explicit at `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs:5-43`; SQL overlay returns missing/applied authority at `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:29-60`; `ProductProjectionGovernanceTests` allowlist projection mutators at `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5-25`. |

## Task-02 Business State Ownership

The detailed field matrix is in `domain-authority-matrix.md`. Summary:

- Product authored content, units, variants, lifecycle, and publication are Product Authoring/Catalog.
- Product media rows are Product Media; variant media pointers are authored by Product Authoring but cleared by Product Media on media deletion.
- Stock quantity, reserved quantity, damaged quantity, reservation rows, and stock movements are Inventory/Warehouse.
- CommerceHub listing publish state and `CommerceHubPrice` are CommerceHub Marketplace.
- Affiliate enablement/category/template/rate/caps are Affiliate.
- Mongo/Elasticsearch product documents are Projection/Search only.
- `ProductVariant.CostPrice` and simple `ProductUnit.PurchasePrice` are not uniquely owned in live mutation code. Product Authoring writes them, and Purchasing/Purchase Returns also mutate them.

## Task-03 Boundary Validation

Passes:

- Product create/update/lifecycle/unit mutation paths enter Product Authoring rather than direct controller persistence: `ProductsController.cs:302`, `:345`, `:408`, `:454`, `:1446`, `:1562`, `:1641`.
- Legacy variant CRUD/default endpoints are guarded as deprecated before persistence by architecture tests and source markers: `PointOfSale.Tests/Architecture/ProductMutationAuthorityGovernanceTests.cs:31-55`; direct legacy cost/media writes still exist behind those guards at `ProductVariantsController.cs:640-645`.
- Projection writes are behind SQL overlay and mutation boundary: `ProductSqlAuthorityProjectionWriter.cs:89-122`; `StorefrontProductProjectionMutationBoundary.cs:5-43`.
- CommerceHub channel price override does not rewrite catalog variant price; it resolves final marketplace price with `CommerceHubPriceAuthority` and persists override on `CommerceHubListing`: `CommerceHubPriceAuthority.cs:102-111`; `CommerceHubListing.cs:17-21`, `:239-243`.

Failures:

- `ProductVariant.CostPrice` is assigned by Product Authoring at `ProductAuthoringOrchestrator.cs:1350`, but purchase receipt/update/delete and purchase returns also assign it at `PurchasesController.cs:219`, `:1133`, `:1461`, and `PurchaseReturnsController.cs:540`. That is more than one live mutation authority for the same business field.
- `ProductUnit.PurchasePrice` is assigned by Product Authoring at `ProductAuthoringOrchestrator.cs:1090`, `:1132`, `:1144`, and by purchase/return compatibility mirroring at `PurchasesController.cs:794` and `PurchaseReturnsController.cs:550`. The mirror helper centralizes product-level compatibility, but the field still has multiple live business mutators.
- Inventory mutations are intentionally outside Product Authoring, but many business controllers directly call shared stock mutation support and mutate `ProductVariantStock` rows: `SalesController.cs:310`, `PurchasesController.cs:225`, `PurchaseReturnsController.cs:556`, `ProductVariantsController.cs:1525`, `ProductsController.cs:850`, `InventoryService.cs:70`. The domain owner is identifiable as Inventory, but the boundary is a shared helper/DbContext pattern rather than a sealed application contract.

## Task-04 Cross-Domain Dependencies

See `cross-domain-dependencies.md` for the dependency list. No dependency remains undocumented.

## Task-05 Authority Collisions

| Field or concept | Collision result | Evidence |
|---|---|---|
| `CostPrice` | Critical collision: Product Authoring and Purchasing/Purchase Returns both mutate live canonical variant cost. | `ProductAuthoringOrchestrator.cs:1350`; `PurchasesController.cs:219`, `:1133`, `:1461`; `PurchaseReturnsController.cs:540`; canonical declaration at `ProductVariant.cs:22-23`, `:73`. |
| `ProductUnit.PurchasePrice` | High collision: Product Authoring owns units, while purchase/return flows mirror purchase cost into default unit. | `ProductAuthoringOrchestrator.cs:1090`, `:1132`, `:1144`; `PurchasesController.cs:794`; `PurchaseReturnsController.cs:550`; `ProductUnit.cs:21`. |
| Stock and reservation | No business-owner ambiguity found; owner is Inventory. Boundary implementation is broad/shared. | `ProductVariantStock.cs:38-58`; stock repository reserve/release/commit at `ProductVariantStockRepository.cs:165`, `:312`, `:605-606`, `:718-719`; controller direct stock writes listed in Task-03. |
| Media | Medium ambiguity: Product Media owns `ProductMedia`, but Product Authoring owns variant media assignment and Product Media clears variant media pointers. | `ProductMediaService.cs:102`, `:148-153`; `ProductAuthoringOrchestrator.cs:1234`; legacy variant code behind 410 assigns at `ProductVariantsController.cs:642-643`. |
| Visibility/lifecycle | Split but identifiable: Product Authoring owns Product publish/lifecycle; CommerceHub owns marketplace listing publication; Affiliate owns affiliate eligibility/policy. | Product lifecycle at `ProductAuthoringOrchestrator.cs:422-439`; CommerceHub listing publish/unpublish at `CommerceHubListing.cs:82-161`; affiliate policy writes at `AffiliateService.cs:433`, `:470`, `:500`. |
| Projection freshness/version/timestamps | Informational ambiguity: `UpdatedAt`/projection versions are touched by several contexts as projection triggers, not pure business state. | Product authoring `ProductAuthoringOrchestrator.cs:1036`, `:1362`; media touch `ProductMediaService.cs:299-301`; affiliate projection touch `AffiliateService.cs:529`; stock timestamp on variant `ProductVariant.cs:160`. |

## Task-06 Anti-Corruption Layer Review

Explicit boundaries found:

- Product Authoring: `IProductAuthoringOrchestrator` and supporting policy/validator/reconstruction interfaces in `PointOfSale.Core/Interfaces/ProductAuthoring/IProductAuthoringOrchestrator.cs:5-8`, `IProductAuthoringPolicy.cs:5-23`, `IProductAuthoringReconstructionService.cs:5-8`, `IProductAuthoringValidator.cs:5-8`; DI registration at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringServiceCollectionExtensions.cs:11-17`.
- Projection: `IStorefrontProductProjectionMutationBoundary` at `StorefrontProductProjectionMutationBoundary.cs:5-7`; SQL overlay at `ProductSearchSqlAuthorityOverlay.cs:29-60`.
- CommerceHub pricing: `ICommerceHubPriceAuthority` and `CommerceHubPriceAuthority` at `CommerceHubPriceAuthority.cs:6-30`.
- Inventory reservation repository: `IProductVariantStockRepository` at `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:14`.
- Product media service: `IProductMediaService` at `PointOfSale.API/Services/ProductMediaService.cs:10-15`.

Violations / observations:

- Purchasing mutates ProductVariant/ProductUnit cost fields directly through shared EF tracked entities, not through a Product/Pricing ACL: `PurchasesController.cs:219-225`, `:1133-1139`, `:1461-1478`; `PurchaseReturnsController.cs:540-556`.
- Inventory ownership is real, but admission is split between repository and helper calls from several controllers/services. There is no single inventory mutation application service that all stock-affecting business workflows must enter: examples at `SalesController.cs:242-310`, `PurchasesController.cs:182-225`, `ProductVariantsController.cs:1475-1525`, `ProductsController.cs:830-850`, `InventoryService.cs:51-70`.
- `ApplicationDbContext` exposes Product, media, variant, stock, CommerceHub, and affiliate policy DbSets in one shared context at `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:52-59`, `:455`. Shared context is not automatically wrong, but it weakens bounded-context enforcement unless guarded by architecture tests or ACLs.

## Task-07 Domain Leakage

Proven leakage:

- Purchasing writes catalog pricing/cost state (`ProductVariant.CostPrice`, `ProductUnit.PurchasePrice`) directly. This is not just a transaction snapshot; the assignments change canonical/live product cost fields. Evidence: `PurchasesController.cs:219`, `:794`, `:1133`, `:1461`; `PurchaseReturnsController.cs:540`, `:550`.

Not proven as business leakage:

- Inventory writes ProductVariantStock, which is Product-adjacent but belongs to Inventory by field semantics and repository naming. Evidence: `ProductVariantStock.cs:38-58`; `ProductVariantStockRepository.cs:14`, `:165`, `:312`, `:605-606`.
- Checkout writes order/cart snapshots and calls inventory reservation/release/commit services; it does not own catalog price or lifecycle authority. Evidence: `CheckoutService.cs:3950`, `:6799-6800`, `:5261`, `:5850`.
- CommerceHub writes `CommerceHubListing` and read models; it reads Product/Variant authority and owns its channel override only. Evidence: `CommerceHubListing.cs:14-21`, `:82-161`; `CommerceHubCommandHandlers.cs:215`, `:599-656`, `:771-800`; `CommerceHubSearchSqlAuthorityOverlay.cs:78-100`, `:195-216`.
- Affiliate writes affiliate policy/link/read-model state and reads Product eligibility; no direct catalog field mutation was found in the reviewed sources. Evidence: `AdminProductAffiliateController.cs:171`, `:241`, `:286`; `AffiliateService.cs:433`, `:470`, `:500`; `AffiliatePromotionLinksController.cs:74-88`, `:111-115`.

## Task-08 Architecture Contracts

Explicit contracts:

- Product Authoring: explicit.
- Projection/Search: explicit for storefront product projections.
- CommerceHub listing/pricing: explicit entity methods and price authority.
- Affiliate product policy: service boundary exists.
- Product Media: service boundary exists.
- Inventory reservation repository and mutation helper exist.

Implicit or incomplete contracts:

- Purchasing-to-pricing/cost authority is implicit; direct EF assignment is the live integration mechanism.
- Inventory-to-product boundary is partially implicit; stock mutation support is a helper, not a sealed domain service used by every writer.
- `UpdatedAt` and `ProjectionMutationVersion` ownership is implicit across projection-triggering domains.

## Task-09 Risk Assessment

| Risk | Severity | Reason |
|---|---|---|
| Multiple live writers for `ProductVariant.CostPrice` | Critical | Same canonical cost field is mutated by Product Authoring and Purchasing/Purchase Returns. |
| Multiple live writers for `ProductUnit.PurchasePrice` | High | Product Authoring and purchase/return mirroring both change the same unit cost field. |
| Direct EF cross-domain cost mutation without ACL | High | Purchasing updates Product/ProductUnit/ProductVariant tracked entities directly. |
| Broad stock mutation through shared helper/DbContext | Medium | Owner is identifiable as Inventory, but enforcement is distributed across controllers/services. |
| Product Media clears variant media pointer outside Product Authoring | Medium | Likely intentional compatibility behavior, but ownership of `ProductVariant.MediaId` is split. |
| Projection/version timestamp ownership is implicit | Low | Mostly infrastructure sequencing, but it is cross-domain and should be documented. |
| Projection/search read-model writes | Informational | Classified and guarded by source-level mutation boundary and architecture tests. |

## Final Certification

DOMAIN BOUNDARY CERTIFICATION FAILED

Reason: every important Product-affecting domain was identifiable, and many boundaries are intentionally separated; however, not every business authority belongs to exactly one bounded context. `ProductVariant.CostPrice` and `ProductUnit.PurchasePrice` have multiple live mutation authorities, and the Purchasing-to-Pricing/Product integration happens through direct shared EF entity mutation rather than an explicit anti-corruption layer.
