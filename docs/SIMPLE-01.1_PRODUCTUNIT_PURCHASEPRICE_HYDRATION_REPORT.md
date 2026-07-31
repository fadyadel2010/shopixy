# SIMPLE-01.1 ProductUnit PurchasePrice Hydration Report

## Root Cause

`GET /api/products/{id}` is served by `ProductsController.GetProduct` at `PointOfSale.API/Controllers/ProductsController.cs:213`. The endpoint calls `LoadProductForResponseAsync(id)` at `ProductsController.cs:215`, then returns `Ok(product.ToResponseDto())` at `ProductsController.cs:222`.

The query path is `LoadProductForResponseAsync` at `ProductsController.cs:2119`. It uses EF Core directly through `ApplicationDbContext`, includes `ProductUnits` and `ProductUnits.Unit` at `ProductsController.cs:2124`, then materializes the `Product` entity. There is no separate GET query object, repository class, or read service for this endpoint.

The entity already has canonical unit purchase pricing: `ProductUnit.PurchasePrice` exists at `PointOfSale.Core/Entities/ProductUnit.cs:21`, and the entity comment states the default `ProductUnit` is the canonical simple-product price authority at `ProductUnit.cs:5`.

The exact bug was in the product details response contract and manual mapper:

- `ProductUnitResponseDto` did not expose `PurchasePrice`; before the fix it had `ConversionFactor` and `SellingPrice`, but no purchase price.
- `SafeResponseMapper.ToProductUnitResponseDto` mapped `SellingPrice = unit.SellingPrice` but did not map `PurchasePrice = unit.PurchasePrice`.

Standalone product-unit DTOs were not the failing contract. `ProductUnitDto` already includes `PurchasePrice` at `PointOfSale.API/DTOs/UnitDTOs.cs:37`, and product-unit endpoints already projected it at `ProductsController.cs:945`, `ProductsController.cs:1283`, and `ProductsController.cs:1333`.

## Response Pipeline

- Controller: `ProductsController.GetProduct(Guid id)` at `PointOfSale.API/Controllers/ProductsController.cs:213`.
- Query/repository: `ApplicationDbContext.Products` inside `LoadProductForResponseAsync` at `ProductsController.cs:2119`.
- Service: none for this GET endpoint. Create/update write paths use `ProductAuthoringOrchestrator`.
- Mapper: `SafeResponseMapper.ToResponseDto(Product product)` at `PointOfSale.API/Mappers/SafeResponseMapper.cs:53`.
- Unit mapper: `SafeResponseMapper.ToProductUnitResponseDto(ProductUnit unit)` at `SafeResponseMapper.cs:380`.
- DTO: `ProductResponseDto.Units` at `PointOfSale.API/DTOs/EntityExposureResponseDtos.cs:183`, containing `ProductUnitResponseDto` at `EntityExposureResponseDtos.cs:115`.

## Files Modified

- `PointOfSale.API/DTOs/EntityExposureResponseDtos.cs`
- `PointOfSale.API/Mappers/SafeResponseMapper.cs`
- `PointOfSale.Tests/Products/ProductResponseMapperTests.cs`
- `SIMPLE-01.1_PRODUCTUNIT_PURCHASEPRICE_HYDRATION_REPORT.md`

## Exact Code Changes

- Added `public decimal? PurchasePrice { get; set; }` to `ProductUnitResponseDto` at `PointOfSale.API/DTOs/EntityExposureResponseDtos.cs:122`.
- Added `PurchasePrice = unit.PurchasePrice` to `ToProductUnitResponseDto` at `PointOfSale.API/Mappers/SafeResponseMapper.cs:387`.
- Added regression tests covering:
  - Entity to DTO to camelCase JSON serialization.
  - Saved product reload through `ProductsController.GetProduct(id)` to `OkObjectResult` to camelCase runtime JSON.

## Runtime Evidence

`ProductResponseMapperTests.GetProduct_ReturnsSavedProductUnitPurchasePrice_InRuntimeJsonResponse` seeds a saved simple product with default `ProductUnit.PurchasePrice = 1000` and `ProductUnit.SellingPrice = 1200`, clears EF tracking, invokes `ProductsController.GetProduct(productId)`, and serializes the returned `ProductResponseDto` with `JsonSerializerDefaults.Web`.

The asserted runtime JSON shape is:

```json
{
  "purchasePrice": 1000,
  "salePrice": 1200,
  "units": [
    {
      "purchasePrice": 1000,
      "sellingPrice": 1200,
      "conversionFactor": 1,
      "isDefault": true,
      "isBaseUnit": true
    }
  ],
  "variants": []
}
```

Source evidence:

- Saved unit purchase price in test setup: `PointOfSale.Tests/Products/ProductResponseMapperTests.cs:145`.
- Runtime endpoint invocation: `ProductResponseMapperTests.cs:157`.
- Runtime JSON assertion for `units[0].purchasePrice`: `ProductResponseMapperTests.cs:170`.
- Runtime JSON assertion for `units[0].sellingPrice`: `ProductResponseMapperTests.cs:171`.
- Compatibility mirror assertions unchanged: `ProductResponseMapperTests.cs:172` and `ProductResponseMapperTests.cs:173`.

## Regression Evidence

Commands run:

- `dotnet build PointOfSale.API\PointOfSale.API.csproj --no-restore`
  - Result: passed, 0 warnings, 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter FullyQualifiedName~ProductResponseMapperTests --no-restore`
  - Result: passed, 2 tests.
- `npm run test -- ProductForm.test.tsx --run`
  - Result: passed, 1 file, 6 tests.

Regression scope:

- Selling price unchanged: mapper still maps `SellingPrice = unit.SellingPrice` at `SafeResponseMapper.cs:388`; tests assert `1200`.
- Variant pricing unchanged: no `ProductVariantReferenceDto` mapping was changed; runtime test asserts `variants` remains an array.
- Compatibility mirrors unchanged: top-level product `PurchasePrice` and `SalePrice` still resolve from the default unit at `SafeResponseMapper.cs:64` and `SafeResponseMapper.cs:65`; tests assert both.
- Storefront unchanged: no storefront files were modified by this fix.
- POS unchanged: no POS backend pricing flow was modified by this fix.
- Purchase flow unchanged: no purchase controller/service files were modified by this fix.
- Frontend edit hydration source remains canonical: `ProductForm` hydrates units from `product.units` at `PointOfSale.Web/src/components/ProductForm.jsx:440`, normalizes `unit.purchasePrice` at `ProductForm.jsx:174`, and submits `productUnits[].purchasePrice` at `ProductForm.jsx:554`.

## Observations

I did not perform a live authenticated browser session through the Product Edit screen. The frontend `ProductForm` test suite was run and passed, and source evidence shows edit hydration reads from `product.units`, not top-level product compatibility mirrors.

Pre-existing uncommitted frontend/report files were present before this fix and were not part of the backend DTO/mapper change.

## Final Certification

SIMPLE-01.1 PASSED WITH OBSERVATIONS
