# Frontend Pricing Certification

Review date: 2026-07-30

Final certification:

FRONTEND PRICING CERTIFICATION PASSED WITH OBSERVATIONS

## Certification Answer

Every editable pricing screen audited now exposes only canonical pricing authority:

- Simple products: ProductUnit purchase/selling price only.
- Variant products: ProductVariant price, online price, and cost price only.
- CommerceHub: CommerceHub listing override price only.
- POS, purchase, sales, returns, storefront, and checkout: transaction snapshots or backend-composed read models, not catalog authority editors.

## Source-Code Evidence

| Requirement | Evidence | Result |
|---|---|---|
| Product-level pricing UI must not exist for simple products. | `ProductForm` simple section is identity only at `PointOfSale.Web/src/components/ProductForm.jsx:920`; unit price inputs are under Product Units at `PointOfSale.Web/src/components/ProductForm.jsx:1056`-`PointOfSale.Web/src/components/ProductForm.jsx:1073`. | Pass |
| No Product price fields submitted. | Product payload submits `productUnits` and `variants` at `PointOfSale.Web/src/components/ProductForm.jsx:718`-`PointOfSale.Web/src/components/ProductForm.jsx:719`; tests assert no `purchasePrice`, `salePrice`, or `onlinePrice` at `PointOfSale.Web/src/components/ProductForm.test.tsx:202`-`PointOfSale.Web/src/components/ProductForm.test.tsx:204`, `PointOfSale.Web/src/components/ProductForm.test.tsx:268`-`PointOfSale.Web/src/components/ProductForm.test.tsx:270`, and `PointOfSale.Web/src/components/ProductForm.test.tsx:298`-`PointOfSale.Web/src/components/ProductForm.test.tsx:300`. | Pass |
| Simple product validation uses unit authority. | Base/default unit and default-unit selling price checks at `PointOfSale.Web/src/components/ProductForm.jsx:661`-`PointOfSale.Web/src/components/ProductForm.jsx:678`. | Pass |
| Variant product validation uses variant authority. | Active priced variant check at `PointOfSale.Web/src/components/ProductForm.jsx:649`-`PointOfSale.Web/src/components/ProductForm.jsx:657`; variant payload at `PointOfSale.Web/src/components/ProductForm.jsx:562`-`PointOfSale.Web/src/components/ProductForm.jsx:579`. | Pass |
| Purchase write path does not fall back to product price mirrors. | Purchase item cost uses `canonicalCostPrice` or ProductUnit purchase price at `PointOfSale.Web/src/pages/PurchaseInvoice.jsx:248`; variant selector emits `canonicalCostPrice` at `PointOfSale.Web/src/components/VariantSelectModal.jsx:55`. | Pass |
| POS cart does not use Product price mirrors as authority. | Cart line price uses `defaultUnit.sellingPrice` or local `canonicalUnitPrice` at `PointOfSale.Web/src/hooks/usePOSCart.js:205`; quick access/POS supply canonical local prices at `PointOfSale.Web/src/components/QuickProductGrid.jsx:89`, `PointOfSale.Web/src/pages/POS.jsx:138`, `PointOfSale.Web/src/pages/POS.jsx:153`, and `PointOfSale.Web/src/pages/POS.jsx:164`. | Pass |
| CommerceHub listing exposes only CommerceHub override. | Edit state and submit payload use `commerceHubPrice` at `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:126`, `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:340`, and `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:872`-`PointOfSale.Web/src/pages/CommerceHubSettings.jsx:873`. | Pass |

## Observations

1. Backend `CreateProductRequest` and `UpdateProductRequest` still include compatibility product price inputs. Frontend no longer submits them. Evidence: `PointOfSale.API/Controllers/ProductsController.cs:2192`-`PointOfSale.API/Controllers/ProductsController.cs:2194`, `PointOfSale.API/Controllers/ProductsController.cs:2246`-`PointOfSale.API/Controllers/ProductsController.cs:2248`.
2. ProductUnit has no separate `OnlinePrice`. Current backend contract makes simple storefront price the default-unit selling price. Evidence: `PointOfSale.Core/Entities/ProductUnit.cs:21`, `PointOfSale.Core/Entities/ProductUnit.cs:26`, `docs/pricing-authority-matrix.md:16`.
3. Some frontend read-only DTO/display names still include `salePrice`, but these are not editable authority. POS search backend maps this value from `CatalogPricingAuthority.ResolveSimpleSellingPrice` at `PointOfSale.API/Controllers/ProductsController.cs:534`.

## Verification

```powershell
npm run test -- ProductForm.test.tsx posCartStorage.test.ts usePOSCart --run
```

Result: passed. 2 test files, 9 tests.
