# Product Domain Business Authority Review

Independent architecture certification date: 2026-07-30

Certification question: Can any business mutation still occur outside Product Authoring?

Final answer: Yes.

Final certification: BUSINESS AUTHORITY CERTIFICATION FAILED

This review attempted to disprove Product Authoring as the only Product Domain business mutation authority. It found live Product Domain business mutations outside Product Authoring. The failure is source-code-backed; compilation and test status were not used as certification evidence.

## Scope And Method

Searched production source under `PointOfSale.API`, `PointOfSale.Core`, and `PointOfSale.Infrastructure` for:

- Direct EF mutations: `_context.Products.*`, `_context.ProductVariants.*`, `_context.ProductUnits.*`, `_dbContext.Products.*`, `_dbContext.ProductVariants.*`, `_dbContext.ProductUnits.*`.
- Product-domain `SaveChanges` sites in controllers, services, background services, consumers, jobs, repair services, and projection services.
- Assignments to `Product`, `ProductVariant`, `ProductUnit`, `ProductMedia`, and `ProductVariantStock` fields.
- Product projection Mongo and Elasticsearch writers.
- Product authoring and architecture governance tests.

## Certification Result

Product Authoring is not the only Product Domain business mutation authority.

Source evidence:

- Product Authoring owns main product create/update/lifecycle/unit authoring entry points in `ProductsController`: create delegates to `_productAuthoringOrchestrator.AuthorAsync` at `PointOfSale.API/Controllers/ProductsController.cs:297-302`, update at `PointOfSale.API/Controllers/ProductsController.cs:340-345`, delete at `PointOfSale.API/Controllers/ProductsController.cs:408-410`, deactivate at `PointOfSale.API/Controllers/ProductsController.cs:454-456`, and unit mutations at `PointOfSale.API/Controllers/ProductsController.cs:1446-1448`, `PointOfSale.API/Controllers/ProductsController.cs:1562-1564`, and `PointOfSale.API/Controllers/ProductsController.cs:1641-1643`.
- Product Authoring persists authoritative product state in `ProductAuthoringOrchestrator`: create adds `Product` at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:213-219`, updates apply product fields at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:992-1036`, units are added/updated at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1126-1144`, variants are added/updated at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1340-1353`, and the transaction saves once at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:493`.
- But purchases mutate canonical variant cost outside Product Authoring: `PurchasesController.CreatePurchase` sets `variant.CostPrice = VariantCostAuthority.ApplyPurchaseReceipt(...)` at `PointOfSale.API/Controllers/PurchasesController.cs:219-223`, mirrors simple-product purchase price through `MirrorSimpleProductPurchasePrice(product, variant)` at `PointOfSale.API/Controllers/PurchasesController.cs:224`, then adjusts stock at `PointOfSale.API/Controllers/PurchasesController.cs:225-226`.
- `PurchasesController.UpdatePurchase` repeats the same direct cost mutation at `PointOfSale.API/Controllers/PurchasesController.cs:1131-1138`.
- `PurchasesController.DeletePurchase` mutates variant cost on reversal at `PointOfSale.API/Controllers/PurchasesController.cs:1460-1468`.
- `PurchaseReturnsController.CreatePurchaseReturn` mutates `variantEntity.CostPrice` at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:540-544`, mirrors simple-product `defaultUnit.PurchasePrice` at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:545-553`, and saves at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:671`.
- The source declares these fields canonical Product Domain pricing state: `ProductVariant.Price, OnlinePrice, CostPrice are canonical for variant products` at `PointOfSale.Core/Entities/ProductVariant.cs:21-23`; `Product.PurchasePrice` is only a legacy mirror while simple purchase authority is `ProductUnit.PurchasePrice` and variant cost authority is `ProductVariant.CostPrice` at `PointOfSale.Core/Entities/Product.cs:52-55`.
- Inventory mutation authorities also remain outside Product Authoring: product stock adjustment calls `ProductVariantStockMutationSupport.LockStockForMutationAsync` and changes `variantStock.Quantity` at `PointOfSale.API/Controllers/ProductsController.cs:828-851`, then saves at `PointOfSale.API/Controllers/ProductsController.cs:884`; variant stock endpoint writes `stock.Quantity = request.Quantity` at `PointOfSale.API/Controllers/ProductVariantsController.cs:1525-1538`; warehouse stock adjustment and transfer call `_inventoryService.AdjustStockAsync` / `_inventoryService.TransferStockAsync` and save at `PointOfSale.API/Controllers/WarehousesController.cs:242-269` and `PointOfSale.API/Controllers/WarehousesController.cs:349-392`; checkout reserves/releases `ProductVariantStock` through `ProductVariantStockRepository.SaveChangesAsync` at `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:226`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:357`, and `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:456`.

## Task-01: Business Mutation Inventory

Live Product Authoring paths:

- `POST /api/products`: Product create through `ProductsController.CreateProduct` to `ProductAuthoringOrchestrator.AuthorAsync`; source `PointOfSale.API/Controllers/ProductsController.cs:286-309`.
- `PUT /api/products/{id}`: Product update through Product Authoring; source `PointOfSale.API/Controllers/ProductsController.cs:316-358`.
- `DELETE /api/products/{id}`: Product removal through Product Authoring; source `PointOfSale.API/Controllers/ProductsController.cs:367-418`.
- `PUT /api/products/{id}/deactivate`: Product archive/deactivate through Product Authoring; source `PointOfSale.API/Controllers/ProductsController.cs:425-465`.
- `POST/PUT/DELETE /api/products/{id}/units`: ProductUnit authoring through Product Authoring; source `PointOfSale.API/Controllers/ProductsController.cs:1353-1460`, `PointOfSale.API/Controllers/ProductsController.cs:1472-1570`, `PointOfSale.API/Controllers/ProductsController.cs:1585-1649`.

Live business mutation paths outside Product Authoring:

- Purchase receipt cost mutation: `PurchasesController.CreatePurchase`; source `PointOfSale.API/Controllers/PurchasesController.cs:219-225`.
- Purchase update cost mutation: `PurchasesController.UpdatePurchase`; source `PointOfSale.API/Controllers/PurchasesController.cs:1131-1139`.
- Purchase delete/reversal cost mutation: `PurchasesController.DeletePurchase`; source `PointOfSale.API/Controllers/PurchasesController.cs:1460-1468`.
- Purchase return cost mutation: `PurchaseReturnsController.CreatePurchaseReturn`; source `PointOfSale.API/Controllers/PurchaseReturnsController.cs:540-553`.
- Product stock adjustment for simple products: `ProductsController.AdjustStock`; source `PointOfSale.API/Controllers/ProductsController.cs:771-909`.
- Variant stock adjustment: `ProductVariantsController.UpdateVariantStock`; source `PointOfSale.API/Controllers/ProductVariantsController.cs:1398-1556`.
- Warehouse stock adjustment, transfer, and opening stock: `WarehousesController` and `InventoryService`; source `PointOfSale.API/Controllers/WarehousesController.cs:188-280`, `PointOfSale.API/Controllers/WarehousesController.cs:349-392`, `PointOfSale.API/Controllers/WarehousesController.cs:489-496`, `PointOfSale.API/Services/InventoryService.cs:281-338`.
- Checkout inventory reservation and release: `CheckoutService` via `IProductVariantStockRepository`; source `PointOfSale.API/Services/Checkout/CheckoutService.cs:5147-5162`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:5256-5261`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:226-228`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:357-358`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:456-457`.
- Product media deletion clears variant media and touches product projection timestamp: `ProductMediaService.RemoveMediaAsync`; source `PointOfSale.API/Services/ProductMediaService.cs:146-181` and `PointOfSale.API/Services/ProductMediaService.cs:299-301`.
- Default variant compatibility repair can create or realign a `ProductVariant` outside Product Authoring when product stock adjustment calls `EnsureDefaultVariantAlignedAsync`; source `PointOfSale.API/Controllers/ProductsController.cs:828`, `PointOfSale.API/Services/ProductVariantService.cs:204-304`, `PointOfSale.API/Services/ProductVariantService.cs:417-440`.

Deprecated or blocked legacy paths:

- `ProductVariantsController.CreateVariant`, `UpdateVariant`, `DeleteVariant`, and `SetDefaultVariant` return `LegacyVariantMutationDeprecatedResult()` before the direct mutation code; source `PointOfSale.API/Controllers/ProductVariantsController.cs:120-124`, `PointOfSale.API/Controllers/ProductVariantsController.cs:465-470`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1058-1063`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1226-1230`.
- `ProductsController.MigrateToMultiUnit` returns `LegacyProductMigrationDeprecatedResult()` before direct ProductUnit persistence; source `PointOfSale.API/Controllers/ProductsController.cs:1664-1668`.

Projection, repair, and indexing paths:

- Product projection writer recomposes from SQL authority before mutating Mongo; source `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs:89`.
- Mongo Product projection persistence is behind `StorefrontProductProjectionMutationBoundary`; source `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs:30-36`.
- `ProductReadRepository` writes ProductDocument projection state with `ReplaceOneAsync` / `UpdateOneAsync`; source `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs:194`, `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs:551`, `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs:572`, `PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs:607`.
- Admin sync and search reindex are projection/rebuild paths, not catalog business authors; source `PointOfSale.API/Controllers/AdminSyncController.cs:56-58`, `PointOfSale.API/Controllers/AdminSyncController.cs:161-163`, `PointOfSale.API/Controllers/AdminSearchSyncController.cs:63-67`, `PointOfSale.API/Controllers/SearchReindexController.cs:119-120`.

## Task-02: Authority Classification

No mutation path is left unclassified. See `business-authority-matrix.md`.

Classification summary:

- Canonical Business Authority: Product Authoring orchestrator and Product endpoints that delegate to it.
- Inventory Authority: product/variant/warehouse/checkout stock mutations.
- Pricing/Cost Authority Outside Product Authoring: purchase and purchase-return cost mutations through `VariantCostAuthority`. This is a certification-failing business authority outside Product Authoring.
- Compatibility Only: simple-product mirrors and default variant alignment where explicitly used for legacy compatibility.
- Projection Only: Mongo/Elasticsearch product document writers, projection consumers, search sync/reindex.
- Repair Only: projection rebuild/repair, deprecated migration code after 410 guard, development seed.
- Legacy: unreachable legacy variant CRUD code behind 410 guards.
- Forbidden: live Product Domain business mutation outside Product Authoring if certification requires Product Authoring to be the only business authority. Purchase cost and inventory stock mutation paths meet this condition.

## Task-03: Direct Database Mutation Audit

Direct production occurrences found by the requested patterns:

- `_context.Products.Add`: Product Authoring only at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:219`.
- `_context.Products.Update`: no production occurrence found in `PointOfSale.API`, `PointOfSale.Core`, or `PointOfSale.Infrastructure`.
- `_context.ProductVariants.Add`: unreachable legacy `ProductVariantsController.CreateVariant` at `PointOfSale.API/Controllers/ProductVariantsController.cs:269`; compatibility/default variant service at `PointOfSale.API/Services/ProductVariantService.cs:440`; Product Authoring at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1340`.
- `_context.ProductVariants.Update`: no production occurrence found.
- `_context.ProductUnits.Add`: unreachable legacy migration at `PointOfSale.API/Controllers/ProductsController.cs:1785`; Product Authoring at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1126`.
- `_context.ProductUnits.Update`: no production occurrence found.
- `_context.SaveChangesAsync` in Product Authoring: transaction commit at `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:493`.
- `_context.SaveChangesAsync` outside Product Authoring but affecting Product Domain: product stock adjustment at `PointOfSale.API/Controllers/ProductsController.cs:884` and `PointOfSale.API/Controllers/ProductsController.cs:908`; product stock sync outbox/idempotency at `PointOfSale.API/Controllers/ProductsController.cs:2094`; unreachable legacy multi-unit migration at `PointOfSale.API/Controllers/ProductsController.cs:1716`, `PointOfSale.API/Controllers/ProductsController.cs:1766`, `PointOfSale.API/Controllers/ProductsController.cs:1790`, `PointOfSale.API/Controllers/ProductsController.cs:1970`; unreachable legacy variant CRUD/default code at `PointOfSale.API/Controllers/ProductVariantsController.cs:292`, `PointOfSale.API/Controllers/ProductVariantsController.cs:403`, `PointOfSale.API/Controllers/ProductVariantsController.cs:873`, `PointOfSale.API/Controllers/ProductVariantsController.cs:989`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1170`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1307`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1330`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1346`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1799`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1824`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1990`; live variant stock mutation at `PointOfSale.API/Controllers/ProductVariantsController.cs:1538`; media mutation at `PointOfSale.API/Services/ProductMediaService.cs:181`; default variant repair at `PointOfSale.API/Services/ProductVariantService.cs:197`; purchase return commit at `PointOfSale.API/Controllers/PurchaseReturnsController.cs:671`.

Determination:

- Product Authoring direct mutations are canonical.
- Unreachable legacy direct mutations are Legacy/Compatibility Only, guarded by 410 before persistence.
- Product stock, variant stock, warehouse stock, checkout stock, and purchase cost changes are live business mutations outside Product Authoring.
- Projection Mongo writes are Projection Only or Repair Only when recomposed from SQL authority.

## Task-04: Service Audit

- `ProductVariantService`: can create/realign default variants (`ProductVariantService.cs:204-304`, `ProductVariantService.cs:417-440`) and mirror simple product fields (`ProductVariantService.cs:308-380`). It is Compatibility Only when invoked by Product Authoring; it is outside Product Authoring when invoked by `ProductsController.AdjustStock` (`ProductsController.cs:828`).
- `InventoryService`: mutates opening stock and stock movements (`InventoryService.cs:281-338`). Classification: Inventory Authority, not Product Authoring.
- `CheckoutService` + `ProductVariantStockRepository`: reserve/release ProductVariantStock (`CheckoutService.cs:5147-5162`, `CheckoutService.cs:5256-5261`, `ProductVariantStockRepository.cs:226-228`, `ProductVariantStockRepository.cs:357-358`, `ProductVariantStockRepository.cs:456-457`). Classification: Inventory Authority.
- `PurchasesController` and `PurchaseReturnsController` service logic: mutates `ProductVariant.CostPrice` and simple-product `ProductUnit.PurchasePrice`; classification: Pricing/Cost Authority outside Product Authoring and certification violation.
- `ProductMediaService`: deletes ProductMedia and clears `ProductVariant.MediaId/ImageUrl`; classification: Product Media Authority / Compatibility projection touch, outside Product Authoring.
- Storefront, POS cart, cart, and checkout read services use SQL authority overlays or stock repository for reads/reservations; no catalog authoring mutation found in storefront product read controllers.
- Projection services (`ProductSqlAuthorityProjectionWriter`, `ProductReadRepository`, consumers, reindex services) mutate derivative read models only.

## Task-05: Controller Audit

- `ProductsController`: canonical for product create/update/delete/deactivate/unit authoring through Product Authoring. Also has live `AdjustStock` outside Product Authoring (`ProductsController.cs:771-909`) and projection sync (`ProductsController.cs:1997-2094`). Result: mixed; certification failure because stock mutation remains.
- `ProductVariantsController`: legacy create/update/delete/default endpoints are fail-closed with 410 before persistence; stock endpoint remains live (`ProductVariantsController.cs:1398-1556`). Result: not a live catalog CRUD bypass, but still live inventory mutation.
- `ProductMediaController`/`ProductMediaService`: mutates product media and variant media pointers outside Product Authoring. Result: Product Media Authority outside Product Authoring.
- `PurchasesController`: live purchase flows mutate canonical cost fields outside Product Authoring. Result: certification failure.
- `PurchaseReturnsController`: live return flow mutates canonical cost fields outside Product Authoring. Result: certification failure.
- `WarehousesController`: live stock adjustment, transfer, opening stock. Result: Inventory Authority outside Product Authoring.
- `SalesController`, `SalesReturnsController`, `OrdersController`: audited as product readers/stock consumers from searched mutation patterns. They snapshot product/variant prices and cost, create orders/sales/returns, and use inventory mutation support; no direct catalog authoring assignments found in the reviewed assignment search except stock/inventory effects.
- `StorefrontProductsController`, `StorefrontSearchController`, storefront cart controllers/services: read, resolve variants, cart, checkout, reserve inventory; no direct Product/ProductVariant catalog authoring mutation found.
- `AdminSyncController`, `AdminSearchSyncController`, `SearchReindexController`: projection/reindex/repair only.
- `AdminProductAffiliateController`, `AffiliatePromotionLinksController`, affiliate services/consumers: affiliate policy/projection changes; `AffiliateService` touches product projection version for affiliate projection refresh, not Product Authoring catalog fields.

## Task-06: Background Process Audit

- Product projection consumers and Mongo writers mutate derivative ProductDocument/read models. Classification: Projection Only. Governance test allowlist is in `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5-25`.
- CommerceHub reindex/auto-healing/search analytics mutate CommerceHub product projections and indices. Classification: Repair/Reindex/Projection.
- `ProductVariantStockRepository` and stock outbox paths mutate ProductVariantStock and stock sync outbox. Classification: Inventory Authority.
- CommerceHub inventory consistency updates `variant.StockUpdatedAtUtc` for stock sync at `PointOfSale.API/Services/CommerceHub/InventoryConsistencyService.cs:1956-1962`. Classification: Inventory projection/infrastructure marker, outside Product Authoring.
- `ProjectionFailureCompensation` mutates variant/product projection timestamps in compensation paths. Classification: Repair/Infrastructure.

## Task-07: Bypass Analysis

Bypass attempts and outcomes:

- Variant CRUD bypass through `ProductVariantsController.CreateVariant/UpdateVariant/DeleteVariant/SetDefaultVariant`: blocked by immediate 410 response before persistence (`ProductVariantsController.cs:120-124`, `ProductVariantsController.cs:465-470`, `ProductVariantsController.cs:1058-1063`, `ProductVariantsController.cs:1226-1230`). Not a live bypass.
- Product unit bypass through `ProductsController.Add/Update/DeleteProductUnit`: blocked as a bypass because those methods reconstruct unit state and call Product Authoring (`ProductsController.cs:1405-1448`, `ProductsController.cs:1534-1564`, `ProductsController.cs:1634-1643`). Not a bypass.
- Purchase bypass: successful. Purchase flows directly mutate `ProductVariant.CostPrice` and simple-product default unit purchase price outside Product Authoring (`PurchasesController.cs:219-224`, `PurchasesController.cs:1131-1138`, `PurchasesController.cs:1460-1468`).
- Purchase return bypass: successful. Return flow directly mutates `ProductVariant.CostPrice` and `ProductUnit.PurchasePrice` outside Product Authoring (`PurchaseReturnsController.cs:540-553`).
- Inventory bypass: successful if ProductVariantStock is considered Product Domain business state. Stock adjustment, warehouse, and checkout mutation paths remain outside Product Authoring.
- Projection bypass: not found as business authoring; product projections are derivative and governed by SQL-authority recomposition/boundaries.

## Task-08: Architecture Test Review

Relevant tests:

- `ProductMutationAuthorityGovernanceTests.ProductBusinessMutationEndpoints_MustDelegateToProductAuthoring` checks ProductController product/unit methods delegate to Product Authoring and do not directly save (`PointOfSale.Tests/Architecture/ProductMutationAuthorityGovernanceTests.cs:5-28`).
- `LegacyVariantBusinessMutationEndpoints_MustBeDeprecatedBeforePersistence` checks variant CRUD/default endpoints hit legacy deprecation before persistence (`ProductMutationAuthorityGovernanceTests.cs:31-55`).
- `ProductInfrastructureOnlyBypasses_MustBeExplicitlyClassified` explicitly acknowledges stock mutation support and media projection touch (`ProductMutationAuthorityGovernanceTests.cs:71-82`).
- `ProductProjectionGovernanceTests` allowlists product projection mutation files and rejects unclassified projection writers (`PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5-67`).
- `PricingAuthorityConsolidationCertificationTests` declares pricing authorities and explicitly expects purchase/return compatibility mirroring outside Product Authoring (`PointOfSale.Tests/Architecture/PricingAuthorityConsolidationCertificationTests.cs:10-24`, `PointOfSale.Tests/Architecture/PricingAuthorityConsolidationCertificationTests.cs:69-95`).

Coverage gaps:

- No architecture test forbids `PurchasesController` or `PurchaseReturnsController` from changing `ProductVariant.CostPrice` or `ProductUnit.PurchasePrice` outside Product Authoring.
- No architecture test asserts Product Authoring is the only Product Domain business mutation authority across inventory, purchase cost, media, and stock flows.
- Existing tests intentionally classify some non-Product-Authoring paths as infrastructure/compatibility, which conflicts with a strict "only business mutation authority" certification.

## Task-09: Legacy Detection

Legacy/deprecated code found:

- Deprecated variant mutation endpoints contain unreachable direct mutation code after 410 response and `#pragma warning disable CS0162`; source examples at `ProductVariantsController.cs:120-125`, `ProductVariantsController.cs:248-292`, `ProductVariantsController.cs:465-471`, `ProductVariantsController.cs:633-647`, `ProductVariantsController.cs:1058-1063`, `ProductVariantsController.cs:1166-1170`, `ProductVariantsController.cs:1226-1231`, `ProductVariantsController.cs:1292-1307`.
- Deprecated multi-unit migration endpoint contains unreachable direct ProductUnit creation after 410 response; source `ProductsController.cs:1664-1668`, `ProductsController.cs:1774-1790`.
- Legacy compatibility price mirrors are still present by design: `Product.PurchasePrice` comment at `Product.cs:52-55`; `ProductPricingCompatibilityMirror` used by Product Authoring, purchases, returns, ProductVariantService, and development seed.

## Task-10: Risk Assessment

See `business-authority-risks.md`.

## Final Certification

BUSINESS AUTHORITY CERTIFICATION FAILED

Reason: live Product Domain business mutations remain outside Product Authoring. The decisive source-backed examples are purchase/purchase-return cost mutation of `ProductVariant.CostPrice` and `ProductUnit.PurchasePrice`, and inventory mutation of `ProductVariantStock` through stock, warehouse, and checkout paths.
