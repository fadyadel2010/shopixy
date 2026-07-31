# FEATURE-V2 Variant Authoring Experience

## Current UX

Before V2, variant authoring forced the admin user through a fragmented workflow: create the parent product, move into variant management, create options and values, generate combinations, then open variants one by one to enter SKU, purchase price, selling price, stock, barcode, media, and status. The legacy mutation endpoints for direct variant create/update/delete are still intentionally deprecated and return `410 Gone` (`PointOfSale.API/Controllers/ProductVariantsController.cs:123`, `:469`, `:1061`, `:1229`, `:1582`), so a replacement UX cannot depend on those APIs.

## New UX

The POS Admin variant tab now behaves as a guided Variant Authoring Workspace:

- Attribute presets are available for common dimensions: Color, Size, Material, Capacity, and Flavor (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1199`).
- Each selected attribute is configured independently through the existing option/value builder.
- The matrix generates variant combinations in memory and filters locally deleted rows before save (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:559`).
- The grid exposes spreadsheet-style editing for Variant Name, Attributes, SKU, Barcode, Purchase Price, Selling Price, Inventory, Weight, Default, and Active (`PointOfSale.Web/src/components/VariantMatrixTable.jsx:127`).
- Bulk actions now cover purchase price, selling price, percentage price increase, inventory by warehouse, weight, SKU generation, barcode generation, enable, disable, and delete selected rows (`PointOfSale.Web/src/components/VariantBulkActions.jsx:43`, `:112`, `:146`, `:172`, `:210`).
- The SKU builder supports prefix, separator, casing, and row-level manual overrides (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1234`).
- Variant media remains available through `VariantMediaPicker`, with per-variant `mediaId` / `imageUrl` captured in the matrix draft (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1664`).

## Architecture Impact

No product, variant, product unit, inventory, storefront, checkout, cart, or database schema redesign was introduced.

The save path uses the existing canonical Product Authoring API:

- The workspace builds a full product authoring payload from the parent product and edited variant rows (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:337`).
- Matrix save calls `PUT /products/{productId}` with an idempotency key and `If-Match` row version where available (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1036`, `:1069`, `:1073`).
- `productUnits` is sent as `null` from this workspace so variant authoring does not attempt to redesign or resync canonical product units directly from the client.
- Inventory changes are applied after canonical variant persistence through the existing warehouse stock endpoint (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1085`, `:1107`).
- Parent product saves delegate pending variant-grid changes to the variant tab first, then avoid submitting stale variant snapshots (`PointOfSale.Web/src/components/ProductForm.jsx:732`, `:756`).

Application-layer DTO mapping was extended only to preserve already-existing variant surface fields used by the new authoring grid:

- `ProductAuthoringVariant` now carries optional `Weight`, `MediaId`, and `ImageUrl` (`PointOfSale.Core/Application/ProductAuthoring/ProductAuthoringCommand.cs:99`).
- Product authoring requests accept those fields (`PointOfSale.API/Controllers/ProductsController.cs:2302`).
- Mapper, reconstruction, and orchestrator paths preserve those fields during canonical create/update (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs:387`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringReconstructionService.cs:116`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1262`, `:1335`, `:1357`).
- Variant read DTOs expose weight for the grid (`PointOfSale.API/Controllers/ProductVariantsController.cs:1590`, `:1601`, `:2186`).

## Component Hierarchy

- `ProductForm.jsx`
  - Owns parent product submission.
  - Delegates variant workspace save through `variantsTabRef.current.savePendingChanges()`.
- `ProductVariantsTab.jsx`
  - Owns guided authoring state, attribute presets, SKU builder, generated matrix rows, validation, canonical save, and stock sync.
- `VariantOptionsBuilder.jsx`
  - Continues to manage option names and option values.
- `VariantMatrixTable.jsx`
  - Provides spreadsheet-style row editing.
- `VariantBulkActions.jsx`
  - Applies selected-row operations.
- `VariantMediaPicker.jsx`
  - Provides per-variant image/media assignment.

## State Flow

1. Load parent product, existing variants, and warehouses together.
2. Seed selected warehouse and SKU prefix from product context.
3. User selects attributes and configures values.
4. Matrix combinations are generated locally.
5. Deleted rows are tracked in memory and excluded from the save payload.
6. Row edits are stored as drafts keyed by variant combination or existing variant id.
7. Before save, validation blocks duplicate combinations, duplicate SKU, duplicate barcode, missing purchase/selling prices, negative inventory, and negative weight (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:839`).
8. Canonical product authoring payload is saved.
9. Persisted variants are reloaded.
10. Inventory drafts are applied to the selected warehouse through the existing stock endpoint.

## Files Modified

V2 implementation files:

- `PointOfSale.Web/src/components/ProductVariantsTab.jsx`
- `PointOfSale.Web/src/components/VariantBulkActions.jsx`
- `PointOfSale.Web/src/components/VariantMatrixTable.jsx`
- `PointOfSale.Web/src/components/ProductForm.jsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.test.tsx`
- `PointOfSale.API/Controllers/ProductsController.cs`
- `PointOfSale.API/Controllers/ProductVariantsController.cs`
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs`
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringReconstructionService.cs`
- `PointOfSale.Core/Application/ProductAuthoring/ProductAuthoringCommand.cs`

The working tree already contained unrelated modified and untracked files before this V2 pass; those were not reverted.

## Regression Tests

Runtime validation performed:

- `npm run test -- ProductVariantsTab.test.tsx ProductForm.test.tsx ProductWorkspacePage.test.tsx --run`
  - Passed: 3 files, 10 tests.
  - Covers canonical variant authoring save, duplicate SKU prevention, parent product save behavior, and product workspace routing coverage.
- `dotnet build PointOfSale.sln`
  - Passed with existing warnings and 0 errors.
- `npm run build` in `PointOfSale.Web`
  - Passed. Vite reported existing large chunk warnings.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --no-build --filter "FullyQualifiedName~ProductAuthoringCommandMapperCharacterizationTests|FullyQualifiedName~ProductAuthoringReconstructionCharacterizationTests|FullyQualifiedName~ProductVariantTests"`
  - Passed: 15 tests.
- `npm run test -- cartStore.persistence.test.ts ProductTemplateResolver.test.ts purchaseExperience.test.ts --run` in `PointOfSale.Storefront`
  - Passed: 3 files, 11 tests.

Specific new V2 tests:

- `saves generated variants through canonical product authoring` verifies generated variants are saved through `PUT /products/{id}` with variant payload, idempotency key, row version, and no deprecated variant mutation calls (`PointOfSale.Web/src/components/ProductVariantsTab.test.tsx:107`).
- `blocks duplicate SKU values before save` verifies duplicate SKU validation prevents API submission (`PointOfSale.Web/src/components/ProductVariantsTab.test.tsx:147`).

## Manual Validation

Source-level validation confirms:

- Direct variant mutation endpoints remain deprecated and are not used by the V2 matrix save path.
- Variants are not persisted until the user confirms Save Matrix.
- SKU, barcode, purchase price, selling price, inventory, weight, active status, deletion, default selection, and media references are carried in the matrix state.
- Duplicate SKU, duplicate barcode, missing purchase price, missing selling price, negative inventory, negative weight, duplicate combinations, and empty variant saves are blocked before persistence.
- Parent product save no longer overwrites canonical variants with a stale form snapshot.

Observed limits:

- Variant-level tax category bulk editing was not implemented because there is no variant-level tax category field in the current Product Authoring / Product Variant contract, and adding one would require domain or schema changes that are explicitly out of scope.
- Storefront-specific `useVariantSelector` test execution was attempted, but no matching test file exists. Existing storefront product resolver, purchase experience, and cart persistence tests passed instead.
- Browser-click manual testing was not performed in this pass; validation is from source inspection, unit/component tests, solution build, and frontend build.

## Final Certification

FEATURE V2 PASSED WITH OBSERVATIONS
