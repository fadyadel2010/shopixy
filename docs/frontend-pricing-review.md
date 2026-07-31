# Frontend Pricing Review

Review date: 2026-07-30

Certification question: Does every pricing screen expose only canonical pricing authority?

Answer: yes, with observations. Editable frontend catalog pricing now routes through ProductUnit, ProductVariant, or CommerceHub listing override authority. Compatibility Product price fields remain in backend DTO/read models and a few frontend read-only/display type names, but the audited frontend authoring paths do not expose editable `Product.SalePrice`, `Product.PurchasePrice`, or `Product.OnlinePrice`.

Final certification: FRONTEND PRICING CERTIFICATION PASSED WITH OBSERVATIONS

## Backend Contract Used

| Authority | Evidence |
|---|---|
| Simple product selling price | Default `ProductUnit.SellingPrice`; contract at `docs/pricing-authority-matrix.md:16`, field at `PointOfSale.Core/Entities/ProductUnit.cs:26`. |
| Simple product purchase/cost price | Default `ProductUnit.PurchasePrice`; contract at `docs/pricing-authority-matrix.md:17`, field at `PointOfSale.Core/Entities/ProductUnit.cs:21`. |
| Variant price / online / cost | `ProductVariant.Price`, `ProductVariant.OnlinePrice`, `ProductVariant.CostPrice`; contract at `docs/pricing-authority-matrix.md:18`-`docs/pricing-authority-matrix.md:20`, fields at `PointOfSale.Core/Entities/ProductVariant.cs:62`, `PointOfSale.Core/Entities/ProductVariant.cs:68`, `PointOfSale.Core/Entities/ProductVariant.cs:73`. |
| CommerceHub channel override | `CommerceHubListing.CommerceHubPrice`; contract at `docs/pricing-authority-matrix.md:21` and `docs/pricing-authority-matrix.md:61`; frontend edit state at `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:126`, submit payload at `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:340`. |
| Product price fields | Compatibility mirrors only; contract at `docs/pricing-authority-matrix.md:22` and `docs/catalog-canonical-data-contract.md:120`-`docs/catalog-canonical-data-contract.md:122`. |

## Screen Inventory

| Screen / surface | Pricing exposure | Certification |
|---|---|---|
| Create Product | Simple products expose SKU/barcode identity and Product Unit purchase/selling price rows; request submits `productUnits` and `variants`, not product price fields. Evidence: `PointOfSale.Web/src/components/ProductForm.jsx:549`-`PointOfSale.Web/src/components/ProductForm.jsx:562`, `PointOfSale.Web/src/components/ProductForm.jsx:718`-`PointOfSale.Web/src/components/ProductForm.jsx:719`, test at `PointOfSale.Web/src/components/ProductForm.test.tsx:203`. | Pass |
| Edit Product | Same `ProductForm`; existing units hydrate from `product.units`, then save as ProductUnit payload. Evidence: `PointOfSale.Web/src/components/ProductForm.jsx:440`, `PointOfSale.Web/src/components/ProductForm.jsx:549`-`PointOfSale.Web/src/components/ProductForm.jsx:562`, update test at `PointOfSale.Web/src/components/ProductForm.test.tsx:299`. | Pass |
| Duplicate Product | No separate duplicate pricing authoring surface found in frontend source. Product creation/editing is through `ProductForm` and `ProductWorkspacePage`. Evidence: search for `ProductForm` usage at `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx:169`, modal usage via product pages. | Pass |
| Quick Edit | No separate quick-edit Product price form found. POS quick access normalizes backend quick-access price into local `canonicalUnitPrice`; cart consumes default unit selling price or local canonical price. Evidence: `PointOfSale.Web/src/components/QuickProductGrid.jsx:89`, `PointOfSale.Web/src/hooks/usePOSCart.js:205`. | Pass |
| Bulk Edit | No catalog bulk price editor found. CommerceHub bulk action operates listing IDs and listing state, not product price fields. Evidence: `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:408`. | Pass |
| Import Wizard | No frontend product import wizard pricing UI found in audited frontend source. | Pass |
| Export Wizard | No frontend product export wizard pricing UI found in audited frontend source. | Pass |
| Product Variants | Variant matrix and modal edit expose `price`, `onlinePrice`, and `costPrice` on variants. Evidence: state and payload at `PointOfSale.Web/src/components/ProductVariantsTab.jsx:26`-`PointOfSale.Web/src/components/ProductVariantsTab.jsx:27`, `PointOfSale.Web/src/components/ProductVariantsTab.jsx:234`-`PointOfSale.Web/src/components/ProductVariantsTab.jsx:235`; matrix inputs at `PointOfSale.Web/src/components/VariantMatrixTable.jsx:259`-`PointOfSale.Web/src/components/VariantMatrixTable.jsx:269`. | Pass |
| Product Units | Unit UI exposes `purchasePrice` and `sellingPrice` on ProductUnit only. Evidence: `PointOfSale.Web/src/components/ProductForm.jsx:1056`-`PointOfSale.Web/src/components/ProductForm.jsx:1073`; standalone unit modal at `PointOfSale.Web/src/components/ProductUnitsModal.jsx:201`-`PointOfSale.Web/src/components/ProductUnitsModal.jsx:217`. | Pass |
| CommerceHub Listing | Exposes CommerceHub listing override `commerceHubPrice`, not Product price mirrors. Evidence: `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:126`, `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:340`, `PointOfSale.Web/src/pages/CommerceHubSettings.jsx:872`-`PointOfSale.Web/src/pages/CommerceHubSettings.jsx:873`. | Pass |
| Affiliate Product | Product form embeds affiliate settings separately from pricing; no price input found in affiliate settings card. Evidence: `PointOfSale.Web/src/components/ProductAffiliateSettingsCard.jsx:27`-`PointOfSale.Web/src/components/ProductAffiliateSettingsCard.jsx:42`. | Pass |
| Manual Order | No frontend manual-order catalog price editor found in audited frontend source; backend contract states manual order should resolve default unit or variant authority. | Pass |
| Purchase | Purchase invoice cost defaults now use `canonicalCostPrice` or default-unit `purchasePrice` only. Evidence: `PointOfSale.Web/src/pages/PurchaseInvoice.jsx:177`-`PointOfSale.Web/src/pages/PurchaseInvoice.jsx:178`, `PointOfSale.Web/src/pages/PurchaseInvoice.jsx:248`. | Pass |
| Purchase Return | Frontend return screen uses original invoice snapshots; no Product price editor found. | Pass |
| Sales / POS | POS cart consumes `defaultUnit.sellingPrice` or local `canonicalUnitPrice`, not `product.salePrice` in the cart hook. Evidence: `PointOfSale.Web/src/hooks/usePOSCart.js:205`, source normalization at `PointOfSale.Web/src/pages/POS.jsx:138`, `PointOfSale.Web/src/pages/POS.jsx:153`, `PointOfSale.Web/src/pages/POS.jsx:164`. | Pass |
| Checkout / Storefront | Storefront and CommerceHub checkout use backend-composed `effectivePrice`, pricing presentation, and expected pricing snapshots. Evidence: `PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx:211`, `PointOfSale.CommerceHub/src/pages/ProductDetailsPage.tsx:331`, `PointOfSale.CommerceHub/src/pages/CheckoutPage.tsx:463`, `PointOfSale.Storefront/src/store/checkoutStore.ts:1800`. | Pass |

## Search Findings

The source search covered `salePrice`, `purchasePrice`, `onlinePrice`, `costPrice`, `Product.salePrice`, `Product.purchasePrice`, `Product.onlinePrice`, legacy state, validation, payload mapping, API adapters, and forms in `PointOfSale.Web/src`, `PointOfSale.Storefront/src`, and `PointOfSale.CommerceHub/src`.

Remaining hits are classified as:

- Canonical ProductUnit or ProductVariant fields: `PointOfSale.Web/src/components/ProductForm.jsx:554`, `PointOfSale.Web/src/components/ProductForm.jsx:572`-`PointOfSale.Web/src/components/ProductForm.jsx:573`, `PointOfSale.Web/src/components/ProductVariantsTab.jsx:234`-`PointOfSale.Web/src/components/ProductVariantsTab.jsx:235`.
- Transaction snapshots or sanitized persisted cart unit data: `PointOfSale.Web/src/utils/posCartStorage.js:87`, `PointOfSale.Web/src/pages/PurchaseInvoice.jsx:533`.
- Read-only compatibility DTO/type names backed by backend canonical resolvers: POS search response maps `SalePrice` from `CatalogPricingAuthority.ResolveSimpleSellingPrice` at `PointOfSale.API/Controllers/ProductsController.cs:534`; frontend display still references `product.salePrice` in product search/table views at `PointOfSale.Web/src/components/ProductSearch.jsx:250` and `PointOfSale.Web/src/pages/Products.jsx:342`.
- Storefront type compatibility comments, not authoring UI: `PointOfSale.Storefront/src/types/product.ts:134` and `PointOfSale.Storefront/src/types/product.ts:183`.

## Observation

The mission text asks for simple product `Default Product Unit -> Purchase Price, Selling Price, Online Price`. The current backend ProductUnit contract has `PurchasePrice` and `SellingPrice`, but no `OnlinePrice`. Evidence: `PointOfSale.Core/Entities/ProductUnit.cs:21` and `PointOfSale.Core/Entities/ProductUnit.cs:26`; product-unit request DTO at `PointOfSale.API/Controllers/ProductsController.cs:2292`-`PointOfSale.API/Controllers/ProductsController.cs:2300`. Under the current backend contract, simple storefront online price is the default unit selling price, not a separate editable field.
