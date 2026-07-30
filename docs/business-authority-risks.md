# Product Domain Business Authority Risks

Certification date: 2026-07-30

Final certification: BUSINESS AUTHORITY CERTIFICATION FAILED

## Critical

### R-01: Purchase flows mutate canonical ProductVariant cost outside Product Authoring

Severity: Critical

Evidence:

- `ProductVariant.CostPrice` is canonical Product Domain pricing data for variant products: `PointOfSale.Core/Entities/ProductVariant.cs:21-23`.
- `Product.PurchasePrice` is a legacy mirror; simple-product purchase authority is default `ProductUnit.PurchasePrice`, and variant cost authority is `ProductVariant.CostPrice`: `PointOfSale.Core/Entities/Product.cs:52-55`.
- `PurchasesController.CreatePurchase` writes `variant.CostPrice` directly: `PointOfSale.API/Controllers/PurchasesController.cs:219-223`.
- The same path mirrors simple-product purchase price outside Product Authoring: `PointOfSale.API/Controllers/PurchasesController.cs:224`, `PointOfSale.API/Controllers/PurchasesController.cs:784-798`.
- `PurchasesController.UpdatePurchase` repeats the direct cost mutation: `PointOfSale.API/Controllers/PurchasesController.cs:1131-1138`.
- `PurchasesController.DeletePurchase` mutates cost during reversal: `PointOfSale.API/Controllers/PurchasesController.cs:1460-1468`.
- `PurchaseReturnsController.CreatePurchaseReturn` writes both `variantEntity.CostPrice` and `defaultUnit.PurchasePrice`: `PointOfSale.API/Controllers/PurchaseReturnsController.cs:540-553`.

Impact:

Product Authoring cannot be certified as the only Product Domain business mutation authority while purchase and return flows remain a separate live authority for canonical cost state.

## High

### R-02: Inventory authorities mutate ProductVariantStock outside Product Authoring

Severity: High

Evidence:

- `ProductsController.AdjustStock` mutates default variant stock through `ProductVariantStockMutationSupport` and saves: `PointOfSale.API/Controllers/ProductsController.cs:828-884`.
- `ProductVariantsController.UpdateVariantStock` writes `stock.Quantity = request.Quantity` and saves: `PointOfSale.API/Controllers/ProductVariantsController.cs:1525-1538`.
- `WarehousesController.AdjustStock` calls `_inventoryService.AdjustStockAsync` and saves: `PointOfSale.API/Controllers/WarehousesController.cs:242-269`.
- `WarehousesController.TransferStock` calls `_inventoryService.TransferStockAsync` and saves: `PointOfSale.API/Controllers/WarehousesController.cs:349-392`.
- `InventoryService.SaveOpeningStockAsync` writes opening `existingStock.Quantity`: `PointOfSale.API/Services/InventoryService.cs:281-315`.
- Checkout reservation/release paths persist stock mutations through `ProductVariantStockRepository`: `PointOfSale.API/Services/Checkout/CheckoutService.cs:5147-5162`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:226-228`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:357-358`, `PointOfSale.Infrastructure/Data/Repositories/ProductVariantStockRepository.cs:456-457`.

Impact:

If ProductVariantStock is in the Product Domain business state, Product Authoring is not the only business mutation authority. If inventory is intentionally a separate domain authority, the architecture contract must say so explicitly; the strict certification question fails as written.

## High

### R-03: Default variant repair can create ProductVariant outside Product Authoring

Severity: High

Evidence:

- `ProductsController.AdjustStock` calls `_productVariantService.EnsureDefaultVariantAlignedAsync(product)`: `PointOfSale.API/Controllers/ProductsController.cs:828`.
- `ProductVariantService.EnsureDefaultVariantAlignedAsync` creates a default variant when no variants exist: `PointOfSale.API/Services/ProductVariantService.cs:246-252`.
- `CreateDefaultVariantEntityAsync` adds that variant with `_context.ProductVariants.Add(variant)`: `PointOfSale.API/Services/ProductVariantService.cs:417-440`.
- The service can save outside Product Authoring in `EnsureDefaultVariantAsync`: `PointOfSale.API/Services/ProductVariantService.cs:184-198`.

Impact:

This is likely intended as compatibility repair, but it is still a possible `ProductVariant` creation path outside Product Authoring when invoked by live non-authoring flows.

## Medium

### R-04: Product media mutation remains outside Product Authoring

Severity: Medium

Evidence:

- `ProductMediaService.RemoveMediaAsync` clears `variant.MediaId`, `variant.ImageUrl`, and `variant.UpdatedAt`: `PointOfSale.API/Services/ProductMediaService.cs:146-151`.
- It removes `ProductMedia`, touches product `UpdatedAt`, publishes events, and saves: `PointOfSale.API/Services/ProductMediaService.cs:153-181`.
- The projection touch is explicit: `TouchProductForMediaProjection` sets `product.UpdatedAt = changedAtUtc`: `PointOfSale.API/Services/ProductMediaService.cs:299-301`.

Impact:

If media assignment is part of Product Authoring, this is a bypass. If media is intentionally a separate Product Media Authority, it should be documented as such.

## Medium

### R-05: Architecture tests do not enforce Product Authoring as the only Product Domain mutation authority

Severity: Medium

Evidence:

- `ProductMutationAuthorityGovernanceTests` checks ProductController product/unit endpoints delegate to Product Authoring: `PointOfSale.Tests/Architecture/ProductMutationAuthorityGovernanceTests.cs:5-28`.
- The same test intentionally permits/classifies infrastructure-only bypasses for stock support and media projection touch: `PointOfSale.Tests/Architecture/ProductMutationAuthorityGovernanceTests.cs:71-82`.
- The test matrix requirement includes `ProductsController.AdjustStock`, `ProductVariantsController.UpdateVariantStock`, `ProductMediaService`, and `DevelopmentCommerceHubSeedService` as governed paths rather than forbidden paths: `PointOfSale.Tests/Architecture/ProductMutationAuthorityGovernanceTests.cs:84-119`.
- Pricing tests explicitly expect purchases and purchase returns to use `ProductPricingCompatibilityMirror`: `PointOfSale.Tests/Architecture/PricingAuthorityConsolidationCertificationTests.cs:69-95`.

Impact:

The tests prevent some future legacy resurrection, but they do not prevent purchase-cost or inventory mutations outside Product Authoring.

## Medium

### R-06: Unreachable legacy variant mutation code remains in production controllers

Severity: Medium

Evidence:

- `CreateVariant` returns `LegacyVariantMutationDeprecatedResult()` before persistence: `PointOfSale.API/Controllers/ProductVariantsController.cs:120-124`.
- Unreachable direct creation code remains after the guard: `_context.ProductVariants.Add(variant)` at `PointOfSale.API/Controllers/ProductVariantsController.cs:269`.
- `UpdateVariant` returns 410 before direct assignment code: `PointOfSale.API/Controllers/ProductVariantsController.cs:465-470`, with unreachable assignments at `PointOfSale.API/Controllers/ProductVariantsController.cs:635-645`.
- `DeleteVariant` returns 410 before `variant.IsDeleted = true`: `PointOfSale.API/Controllers/ProductVariantsController.cs:1058-1063`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1166-1170`.
- `SetDefaultVariant` returns 410 before default reassignment: `PointOfSale.API/Controllers/ProductVariantsController.cs:1226-1230`, `PointOfSale.API/Controllers/ProductVariantsController.cs:1292-1307`.

Impact:

Current runtime is fail-closed, but the dead code is a latent resurrection risk if the guard is removed or moved.

## Low

### R-07: Unreachable legacy multi-unit migration code remains

Severity: Low

Evidence:

- `MigrateToMultiUnit` returns `LegacyProductMigrationDeprecatedResult()` before direct migration logic: `PointOfSale.API/Controllers/ProductsController.cs:1664-1668`.
- Unreachable code still creates `ProductUnit` directly and updates `product.UpdatedAt`: `PointOfSale.API/Controllers/ProductsController.cs:1774-1790`.

Impact:

Currently blocked. Risk is legacy resurrection rather than live bypass.

## Informational

### R-08: Projection mutation governance is substantially stronger than business mutation governance

Severity: Informational

Evidence:

- `ProductProjectionGovernanceTests` allowlists approved projection mutation files and fails unclassified Mongo product projection writers: `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:5-67`.
- Business controllers/services are forbidden from directly mutating Product projection Mongo except infrastructure-only files: `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:69-102`.
- Product read repository writes must stay behind `StorefrontProductProjectionMutationBoundary`: `PointOfSale.Tests/Architecture/ProductProjectionGovernanceTests.cs:119-158`.

Impact:

Projection authority is guarded, but that does not certify business mutation authority because live SQL business mutations still exist outside Product Authoring.

## Certification Outcome

BUSINESS AUTHORITY CERTIFICATION FAILED

The decisive risks are R-01 and R-02. Either one is sufficient to answer the certification question "Can any business mutation still occur outside Product Authoring?" with yes.
