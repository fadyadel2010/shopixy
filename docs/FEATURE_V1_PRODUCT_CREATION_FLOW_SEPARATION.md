# FEATURE-V1 Product Creation Flow Separation

## Existing Workflow

- POS Admin used one create form for both simple and variant products.
- Product type was controlled by the checkbox text `This product has multiple options (size, color, etc.)`.
- Product Units, Purchase Price, and Selling Price remained visible while creating a variant product.
- Create validation evaluated variant products as publication-ready products and could surface `products.activePricedVariantRequired` before the parent product existed.

## New Workflow

- POS Admin creation now starts with a Product Type radio selector:
  - Simple Product
  - Variant Product
- Simple Product creation keeps Product Units, Purchase Price, Selling Price, Inventory, Base Unit, and Default Unit visible and submits canonical Product Unit pricing.
- Variant Product creation hides Product Units, Purchase Price, Selling Price, the create-time Variants placeholder, and the create-time Inventory section.
- Variant Product creation shows the guidance: pricing, inventory, SKU, and stock are configured after variants are created.
- Variant parent products are submitted as Draft in create mode and then redirected to `/products/{id}/edit?focus=variants`.
- The update workspace reads `focus=variants`, scrolls to `#product-variants`, and focuses that section.

## UX Screenshots

- Not captured. The UI state was validated with React Testing Library DOM assertions instead:
  - Variant Product selected.
  - Product Units absent.
  - Purchase Price absent.
  - Selling Price absent.
  - Variant guidance visible.

## Files Modified

- `PointOfSale.Web/src/components/ProductForm.jsx`
  - Product Type radio selector.
  - Variant create-mode guidance.
  - Create-only Product Units/Inventory hiding for variant products.
  - Create-only variant parent Draft payload.
  - Variants deep-link scroll/focus target.
- `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx`
  - Reads `focus` query parameter.
  - Redirects newly-created variant parents to `?focus=variants`.
- `PointOfSale.Web/src/components/ProductForm.test.tsx`
  - Added regression coverage for variant create flow.
  - Existing simple product canonical unit pricing coverage remains.
- `PointOfSale.Web/src/pages/ProductWorkspacePage.test.tsx`
  - Added redirect regression coverage.
- `PointOfSale.Web/src/i18n/locales/en.json`
  - Added/updated Product Type and variant create guidance copy.
- `PointOfSale.Web/src/i18n/locales/ar.json`
  - Added/updated matching Product Type and variant create guidance copy.

## Validation Changes

- Simple products still use the existing Product Unit validation path:
  - Base unit required for public products.
  - Default unit required for public products.
  - Default unit selling price must be positive for public products.
- Variant product create mode no longer evaluates active priced variant readiness before the parent exists.
- Variant product create mode submits:
  - `hasVariants: true`
  - `sku: null`
  - `barcode: null`
  - `publishStatus: 0`
  - `productUnits: null`
  - `variants: []`
- Backend publication policy remains unchanged. Published variant products without an active priced variant are still rejected by `PRODUCT_ACTIVE_PRICED_VARIANT_REQUIRED`, so the V1 admin workflow creates the parent as Draft first.

## Source Evidence

- Product Type selector and guidance: `PointOfSale.Web/src/components/ProductForm.jsx:860`, `PointOfSale.Web/src/components/ProductForm.jsx:904`.
- Variant create validation/payload separation: `PointOfSale.Web/src/components/ProductForm.jsx:660`, `PointOfSale.Web/src/components/ProductForm.jsx:745`, `PointOfSale.Web/src/components/ProductForm.jsx:747`.
- Variant create section hiding: `PointOfSale.Web/src/components/ProductForm.jsx:1519`, `PointOfSale.Web/src/components/ProductForm.jsx:1521`, `PointOfSale.Web/src/components/ProductForm.jsx:1522`.
- Variant focus target: `PointOfSale.Web/src/components/ProductForm.jsx:1207`.
- Create redirect deep-link: `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx:42`, `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx:63`, `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx:181`.
- Backend publication policy unchanged: `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs:208`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs:278`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs:293`.

## Regression Tests

- `npm run test -- ProductForm.test.tsx ProductWorkspacePage.test.tsx --run`
  - Passed: 2 files, 8 tests.
- `npm run build`
  - Passed: Vite production build completed.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~ProductAuthoringProducerCompletenessIntegrationTests.PublicVariantCreate_WithoutActivePricedVariant_IsRejected"`
  - Passed: confirms unchanged backend publication rejection for public variant products without active priced variants.

## Observations

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter ProductAuthoringProducerCompletenessIntegrationTests` has one existing failure in `PublicUpdate_OmittingProductUnits_ValidatesReconstructedAggregateAndPreservesUnits`. This V1 change did not modify backend code, API contracts, database schema, domain models, CommerceHub, Storefront, Checkout, or Cart.
- Variant parent creation is intentionally Draft-first in V1 because backend publication readiness still requires active priced variants for public variant products.

## Final Certification

FEATURE V1 PASSED WITH OBSERVATIONS
