# Frontend Pricing Remediation

Review date: 2026-07-30

## Completed Changes

| Remediation | Files | Evidence |
|---|---|---|
| Removed simple-product product-level pricing UI from ProductForm. | `PointOfSale.Web/src/components/ProductForm.jsx` | Simple section is now identity only at `PointOfSale.Web/src/components/ProductForm.jsx:920`; unit pricing inputs are at `PointOfSale.Web/src/components/ProductForm.jsx:1056`-`PointOfSale.Web/src/components/ProductForm.jsx:1073`. |
| Stopped ProductForm from submitting product-level `purchasePrice`, `salePrice`, or `onlinePrice`. | `PointOfSale.Web/src/components/ProductForm.jsx`, `PointOfSale.Web/src/components/ProductForm.test.tsx` | Request now submits `productUnits` and `variants` at `PointOfSale.Web/src/components/ProductForm.jsx:718`-`PointOfSale.Web/src/components/ProductForm.jsx:719`; tests assert absent legacy fields at `PointOfSale.Web/src/components/ProductForm.test.tsx:202`-`PointOfSale.Web/src/components/ProductForm.test.tsx:204`. |
| Removed simple-product product price validation. | `PointOfSale.Web/src/components/ProductForm.jsx` | Validation requires base unit, default unit, and default-unit selling price at `PointOfSale.Web/src/components/ProductForm.jsx:661`-`PointOfSale.Web/src/components/ProductForm.jsx:678`; variant validation requires active priced variants at `PointOfSale.Web/src/components/ProductForm.jsx:649`-`PointOfSale.Web/src/components/ProductForm.jsx:657`. |
| Renamed POS cart local pricing input to canonical names. | `PointOfSale.Web/src/components/QuickProductGrid.jsx`, `PointOfSale.Web/src/pages/POS.jsx`, `PointOfSale.Web/src/hooks/usePOSCart.js` | Quick access supplies `canonicalUnitPrice` at `PointOfSale.Web/src/components/QuickProductGrid.jsx:89`; POS variant selection supplies canonical local price at `PointOfSale.Web/src/pages/POS.jsx:138` and `PointOfSale.Web/src/pages/POS.jsx:153`; cart hook consumes it at `PointOfSale.Web/src/hooks/usePOSCart.js:205`. |
| Removed purchase invoice fallback to product-level price fields. | `PointOfSale.Web/src/pages/PurchaseInvoice.jsx`, `PointOfSale.Web/src/components/VariantSelectModal.jsx` | Variant selector returns `canonicalCostPrice` at `PointOfSale.Web/src/components/VariantSelectModal.jsx:55`; purchase line cost uses variant cost or default-unit purchase cost at `PointOfSale.Web/src/pages/PurchaseInvoice.jsx:248`. |
| Removed dead English locale keys for simple product legacy pricing hints. | `PointOfSale.Web/src/i18n/locales/en.json` | `skuPricing`, `posPriceHint`, `samePosPrice`, `costPriceHint`, and `priceRequired` no longer appear in English locale search. |

## Verification

Command:

```powershell
npm run test -- ProductForm.test.tsx posCartStorage.test.ts usePOSCart --run
```

Result: 2 test files passed, 9 tests passed.

An initial Vitest invocation with `--runInBand` failed because this project's Vitest CLI does not support that Jest option; the corrected `--run` invocation passed.

## Remaining Observations

| Observation | Impact | Evidence |
|---|---|---|
| Backend create/update DTOs still include legacy product price parameters. | Frontend no longer submits them, but backend compatibility contract remains visible in source. | `PointOfSale.API/Controllers/ProductsController.cs:2192`-`PointOfSale.API/Controllers/ProductsController.cs:2194`, `PointOfSale.API/Controllers/ProductsController.cs:2246`-`PointOfSale.API/Controllers/ProductsController.cs:2248`. |
| ProductUnit has no distinct `OnlinePrice` field. | The requested simple-product "Online Price" cannot be implemented as `ProductUnit.OnlinePrice` without backend contract work. Current backend treats default-unit selling price as simple storefront price. | `PointOfSale.Core/Entities/ProductUnit.cs:21`, `PointOfSale.Core/Entities/ProductUnit.cs:26`, `docs/pricing-authority-matrix.md:16`, `docs/pricing-authority-matrix.md:50`. |
| Some read-only frontend fields are still named `salePrice` due legacy DTO shape. | These are display/adapter names, not editable business authority. POS search backend maps `SalePrice` from the canonical default ProductUnit. | `PointOfSale.API/Controllers/ProductsController.cs:534`, display references at `PointOfSale.Web/src/components/ProductSearch.jsx:250` and `PointOfSale.Web/src/pages/Products.jsx:342`. |
| Arabic locale still contains a dead `costPriceHint` key. | No source references remain; not runtime pricing authority. It was not patched because the file encoding did not match patch context safely. | Search hit at `PointOfSale.Web/src/i18n/locales/ar.json:2712`; no source reference outside locale. |

## Recommended Next Steps

1. Retire backend product-level price request parameters once all clients are confirmed off the compatibility DTO.
2. Decide whether simple products need a distinct ProductUnit online price. If yes, add it to backend ProductUnit contract first; if no, document that default-unit selling price is the online price.
3. Rename read-only POS/product DTO fields from `SalePrice` to a canonical presentation name in a versioned API response.
