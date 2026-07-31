# FEATURE-V3 Intelligent Variant Authoring

## Existing UX

- POS Admin Product Authoring already separates simple and variant products.
- Variant products use `ProductVariantsTab` as the Variant Authoring Workspace.
- The workspace already has attribute presets, a matrix table, selected-row bulk actions, SKU generation, image selection, and canonical product authoring save through `PUT /products/{productId}`.
- V2 architecture is preserved: variant matrix saves through the Product Authoring payload, sends idempotency and `If-Match` headers, and does not call deprecated variant mutation APIs from matrix save.

## New UX

- Live counter shows `Variants to Generate` immediately from attribute/value edits.
- Variant explosion states are computed interactively:
  - `<= 100`: Safe.
  - `101-300`: Informational moderate matrix warning.
  - `301-1000`: Warning.
  - `> 1000`: Requires explicit confirmation before rendering generated rows.
- Workspace statistics are always visible: generated variants, active, inactive, missing purchase price, missing selling price, missing SKU, duplicate SKU, duplicate barcode, duplicate combinations.
- Frontend-only templates now create starter attributes for T-Shirt, Shoes, Electronics, Food, and Furniture.
- SKU Builder now supports prefix, separator, casing, optional pattern tokens such as `TSHIRT-{COLOR}-{SIZE}`, and live preview.
- Combination Rules can exclude selected attribute combinations before they appear in the matrix.
- Live validation now highlights duplicate attribute values, duplicate SKU/barcode cells, duplicate combinations, negative inventory, and negative weight.
- Unsaved matrix changes trigger refresh/browser navigation protection and workspace Back/Cancel confirmation.
- Bulk actions now support purchase price, selling price, inventory, weight, status, image assignment/clearing, SKU, barcode, and delete against selected rows only.
- Contextual captions now reflect current counts, including `Generate N Variants`, `Delete N Selected`, and `Enable N Selected`.

## Architecture Impact

- Domain model: unchanged.
- Variant aggregate: unchanged.
- Product units architecture: unchanged.
- Canonical pricing: unchanged.
- Database schema: unchanged.
- Public API contracts: unchanged.
- CommerceHub, Storefront, Checkout, and Cart: unchanged by implementation.
- Persistence remains the existing Product Authoring save path in `ProductVariantsTab`.
- New V3 template/rule/SKU intelligence is frontend workspace state only.

## Component Hierarchy

- `ProductWorkspacePage`
  - guards workspace Back through `ProductForm.confirmDiscardPendingChanges`.
- `ProductForm`
  - exposes pending variant changes from `ProductVariantsTab`.
- `ProductVariantsTab`
  - owns V3 authoring intelligence, live stats, template/rule state, SKU builder state, matrix save.
  - renders `VariantOptionsBuilder`, `VariantMatrixTable`, `VariantBulkActions`.
- `VariantOptionsBuilder`
  - renders attributes, values, duplicate value warnings, live variant counter, explosion status.
- `VariantMatrixTable`
  - renders spreadsheet-like rows and invalid cell highlighting.
- `VariantBulkActions`
  - applies selected-row bulk edits and opens `VariantMediaPicker` for bulk image assignment.

## State Flow

1. `optionGroups` and `excludedRules` produce the live combination count.
2. Option validation blocks generation when attributes are empty, duplicate, or unnamed.
3. Large-count confirmation gates rendering above 1000 generated rows.
4. Valid generated combinations plus persisted variants produce `matrixRows`.
5. `rowDrafts`, `selectedKeys`, `deletedKeys`, and `defaultKey` update the matrix interactively.
6. `rowValidationSummary` derives live statistics and row issue markers from `matrixRows`.
7. Save validates option state and row state, then builds the canonical Product Authoring payload.

## Files Modified

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx`
  - V3 counters, thresholds, templates, exclusion rules, SKU pattern preview, live stats, validation, unsaved protection.
- `PointOfSale.Web/src/components/VariantOptionsBuilder.jsx`
  - duplicate value visibility, live counter, explosion status.
- `PointOfSale.Web/src/components/VariantMatrixTable.jsx`
  - invalid cell highlighting for duplicate SKU/barcode and negative inventory/weight.
- `PointOfSale.Web/src/components/VariantBulkActions.jsx`
  - count-aware captions and bulk image editing.
- `PointOfSale.Web/src/components/ProductForm.jsx`
  - exposes variant pending-change confirmation to parent/Cancel.
- `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx`
  - workspace Back uses pending-change confirmation.
- `PointOfSale.Web/src/components/ProductVariantsTab.test.tsx`
  - V3 regression coverage.
- `docs/FEATURE_V3_INTELLIGENT_VARIANT_AUTHORING.md`
  - this certification report.

## Validation Changes

- Generation is blocked by duplicate attribute values, empty named attributes, unnamed valued groups, and duplicate attribute names.
- Save is blocked by duplicate variant combinations, duplicate SKU, duplicate barcode, missing purchase price, missing selling price, negative inventory, and negative weight.
- Large matrix rendering above 1000 combinations requires confirmation.
- Validation happens before persistence and before inventory synchronization.

## Regression Tests

Passed:

- `cd PointOfSale.Web && npm test -- ProductVariantsTab.test.tsx`
  - 11 tests passed.
  - Covers live counter, large warning band, template loading, duplicate value detection, SKU preview/application, combination exclusion, statistics, smart captions, negative inventory/weight, unsaved refresh warning, canonical save, duplicate SKU blocking, and no deprecated matrix variant APIs.
- `cd PointOfSale.Web && npm test -- ProductForm.test.tsx ProductWorkspacePage.test.tsx ProductVariantsTab.test.tsx`
  - 3 files passed, 19 tests passed.
- `cd PointOfSale.Storefront && npm test -- src/app/router.matching.test.tsx src/commerceAuthority.certification.test.ts src/components/products/productCardRendererResolver.test.ts src/store/cartStore.persistence.test.ts src/storefront/products/ProductTemplateResolver.test.ts`
  - 5 files passed, 27 tests passed.

Backend test observation:

- `dotnet test PointOfSale.sln`
  - Timed out after 10 minutes before returning results.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~Product --no-restore`
  - Timed out after 5 minutes before returning results.
- `dotnet test PointOfSale.Tests/PointOfSale.Tests.csproj --filter FullyQualifiedName~Product --no-build --no-restore --logger "console;verbosity=normal"`
  - Timed out after 3 minutes before returning results.

## Manual Validation

Build/runtime checks passed:

- `cd PointOfSale.Web && npm run build`
  - Vite production build succeeded.
- `dotnet build PointOfSale.sln --no-restore`
  - Build succeeded with 0 warnings and 0 errors.
- `cd PointOfSale.Storefront && npm run build`
  - TypeScript and Vite production build succeeded.

Runtime behavior was validated through rendered React/Vitest DOM interactions for the Variant Authoring Workspace and through production builds for POS Admin and Storefront.

## Final Certification

FEATURE V3 PASSED WITH OBSERVATIONS

Supported by source code and regression/runtime evidence listed above. Observation: backend tests did not complete in this environment despite a clean .NET solution build, so backend test completion is not certified as passed.
