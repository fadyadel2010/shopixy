# BUGFIX VARIANT PRODUCT UPDATE ROOT CAUSE ANALYSIS

## Symptoms

- Editing an existing Variant Product and clicking Update Product can fail during `saveMatrixChanges()`.
- Runtime evidence confirms `HTTP 409 Conflict`, not validation failure.
- Backend response:
  - `code = "CONFLICT"`
  - `message = "Product authoring concurrency conflict."`
  - `data.status = "ConcurrencyConflict"`
  - `data.errors[0].field = "expectedVersion"`
  - `data.errors[0].message = "Product has been changed by another transaction."`
- No success notification is shown because `ProductWorkspacePage.handleSave()` is only called after the product form reports a completed save.
- The stale error was not clearly visible because the frontend did not read the nested `data.errors[]` conflict envelope.

## Investigation

- Frontend trace:
  - `ProductForm.handleSubmit()` calls `variantsTabRef.current.savePendingChanges()` before its own parent-save branch when `productId && hasVariants` (`PointOfSale.Web/src/components/ProductForm.jsx:837`).
  - `ProductVariantsTab.savePendingChanges()` calls `saveMatrixChanges()` only when matrix mode has unsaved changes (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1629`).
  - `saveMatrixChanges()` sends a canonical `PUT /products/{id}` with `variants` populated (`PointOfSale.Web/src/components/ProductVariantsTab.jsx:1529`).
  - After the canonical product PUT, `saveMatrixChanges()` may also issue stock PUTs for inventory cells edited in the matrix.
  - On any failure inside `saveMatrixChanges()`, it catches the error, sets local tab error text, returns `false`, and `ProductForm.handleSubmit()` exits without its own save.

- Backend trace:
  - `ProductsController.UpdateProduct()` requires `Idempotency-Key` and a valid `If-Match`/`Expected-Version` (`PointOfSale.API/Controllers/ProductsController.cs:319`).
  - `ProductsController.UpdateProduct()` maps `UpdateProductRequest` to `ProductAuthoringCommand` (`PointOfSale.API/Controllers/ProductsController.cs:337`).
  - `ProductAuthoringOrchestrator.AuthorAsync()` reconstructs omitted units/variants for update admission.
  - `ProductAuthoringCommandMapper.ReadExpectedVersion()` reads the submitted expected version from `If-Match` first, then `Expected-Version` (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringCommandMapper.cs:442`).
  - `ProductAuthoringOrchestrator.IsStale()` compares the command expected version against the currently loaded `Product.RowVersion` before mutation (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1517`).
  - `ProductAuthoringOrchestrator.ApplyExpectedProductRowVersion()` applies the expected version as EF's original value for optimistic concurrency (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:1521`).
  - The observed message `"Product has been changed by another transaction."` is returned from the `DbUpdateConcurrencyException` catch in `CommitProductAuthoringTransactionAsync()` (`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:497`).
  - `ProductsController.ToProductAuthoringActionResult()` converts `ProductAuthoringStatus.ConcurrencyConflict` into `409 Conflict` (`PointOfSale.API/Controllers/ProductsController.cs:971`).

- Canonical payload trace:
  - Existing Variant Product matrix save sends the current Product Authoring form snapshot merged over the loaded product response, then supplies generated `variants`.
- Existing Variant Product parent save, when no matrix save occurred, sends `hasVariants: true`, simple product `sku: null`, simple product `barcode: null`, `productUnits: null`, and `variants: null` so the backend reconstructs existing variants.
  - Regression coverage asserts the matrix payload includes current parent fields, `productUnits: null`, generated variant attributes, prices, SKU/barcode, active/default flags, and the canonical `PUT /products/{id}` endpoint.

## Execution Timeline

Before the concurrency fix, one click on Update Product with dirty matrix data could produce two sequential canonical product updates:

1. Request #1, from `ProductVariantsTab.saveMatrixChanges()`
   - Endpoint: `PUT /products/{id}`
   - Expected version source: `parentProduct.rowVersion` from the tab's loaded product snapshot, merged with the current parent form snapshot.
   - Payload: full Product Authoring payload with `variants` populated.
   - Response: saved `ProductResponseDto`, including the new `RowVersion`.

2. Request #2, from `ProductForm.handleSubmit()`
   - Endpoint: `PUT /products/{id}`
   - Expected version source before this fix: the parent form path still existed after Request #1. It attempted to derive `effectiveRowVersion` from the matrix result and could fall back to the initially loaded `acceptedRowVersion`.
   - Payload: parent Product Authoring payload with `variants: null` and reconstructed backend variants.
   - Failure: runtime confirmed this request can submit stale expected version evidence and receive `409 Conflict`.

Request #2 was not required. Request #1 already persisted the current ProductForm snapshot and the Variant Workspace matrix through the canonical Product Authoring endpoint. The minimal fix is to stop the second canonical Product update when Request #1 succeeds and returns the saved product.

## Root Cause

The confirmed root cause is a duplicate canonical Product Authoring mutation in the dirty Variant Workspace path:

1. `ProductVariantsTab.saveMatrixChanges()` builds its canonical product update payload from the tab-owned `parentProduct` snapshot loaded at mount time, not from the current `ProductForm` state. When the user edits product-level fields and variant fields before clicking Update Product, the matrix save can submit stale product-level data.

2. A successful matrix save advances the backend Product RowVersion, but `ProductForm` still entered the parent product PUT path after the child save. Runtime evidence proves stale expected version evidence could still reach that second request.

3. `ProductForm` treats `savePendingChanges() === false` as an internal child failure and exits without surfacing the child validation message in the parent form error region or workspace toast path.

4. Frontend API error extraction ignores canonical validation `errors[]` details, so backend validation messages can collapse into the generic `Product authoring validation failed.` or `Failed to save variant matrix`.

5. For existing Variant Products, `ProductForm` still included legacy `productUnits` in the parent update payload. That conflicts with the V1/V2/V3 separation where Variant Products should persist sellable identity/pricing through variants rather than ProductUnit authoring.

The backend 409 is correct. Optimistic concurrency must reject stale expected version evidence. The frontend defect was submitting an unnecessary second canonical mutation after the Variant Workspace had already completed the canonical Product Authoring save.

## Why Variant Products Are Affected

- Simple Product updates do not invoke `ProductVariantsTab.savePendingChanges()`.
- Variant Product updates with no unsaved matrix changes use only the parent ProductForm update path.
- Before this fix, Variant Product updates with unsaved matrix changes executed two product authoring mutations in sequence unless the matrix save failed first.

## Why Empty Variant Products Can Save

- If no matrix rows/drafts are included, `savePendingChanges()` either does not execute or does not send a populated variants payload.
- The parent ProductForm update can then submit normally using its own accepted RowVersion.

## Why Populated Variant Products Fail

- Populated/edited matrix data activates `savePendingChanges()` and `saveMatrixChanges()`.
- The dirty matrix save path produced a first Product Authoring update, then the parent ProductForm produced a second Product Authoring update. The second update was exposed to a stale `If-Match`/`Expected-Version` conflict because the first update had already advanced the product row version.

## Why User Data Can Be Lost Or Appear Lost

- On failed matrix save, the parent ProductForm stops without a top-level error.
- If the matrix save partially succeeds before a later sub-request fails, frontend state and backend state can diverge.
- The child tab error is easy to miss in the full workspace, while the parent save button returns to its initial state.
- A 409 conflict does not clear React state directly; the perceived reset comes from the failed save leaving the user without clear top-level conflict feedback and with the workspace/list refresh path not reached.

## Validation Evidence Before Fix

- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~ProductAuthoringPolicyCharacterizationTests|FullyQualifiedName~ProductAuthoringCommandMapperCharacterizationTests|FullyQualifiedName~ProductAuthoringReconstructionCharacterizationTests" --no-restore`
  - Passed: 13.
- `npm run test -- ProductVariantsTab.test.tsx ProductForm.test.tsx --run`
  - Passed before the final regression additions: 18.

## Files Modified

- `PointOfSale.Web/src/components/ProductForm.jsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx`
- `PointOfSale.Web/src/components/ProductForm.test.tsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.test.tsx`
- `BUGFIX_VARIANT_PRODUCT_UPDATE_ROOT_CAUSE_ANALYSIS.md`

## Exact Fix

- Added frontend API error extraction that reads canonical `errors[]`, nested conflict `data.errors[]`, model-state `errors`, `message`, and `error` before falling back to a generic string (`ProductForm.jsx:197`, `ProductVariantsTab.jsx:27`).
- Added `ProductForm.buildProductAuthoringPayload()` so the parent form and variant workspace use the same current Product Authoring payload source (`ProductForm.jsx:751`).
- Passed `getProductAuthoringSnapshot` into `ProductVariantsTab` and merged it into the matrix save product payload before generating variants (`ProductForm.jsx:1379`, `ProductVariantsTab.jsx:1518`).
- Changed existing Variant Product parent updates to send `productUnits: null` instead of legacy product unit data (`ProductForm.jsx:751`).
- Changed `saveMatrixChanges()` to return the saved product object on success, retain row state on failure, store the last error, and expose `getLastError()`/`getLastSavedProduct()` through the tab ref (`ProductVariantsTab.jsx:1488`, `ProductVariantsTab.jsx:1623`).
- Updated `ProductForm.handleSubmit()` to surface child save errors and validate against the post-matrix saved variant snapshot (`ProductForm.jsx:837`).
- Removed the redundant parent `PUT /products/{id}` after a successful dirty matrix save. `ProductForm` now calls `onSave()` with the saved product returned by `saveMatrixChanges()` (`ProductForm.jsx:873`).
- Added dev-only concurrency timeline logging before product PUTs and after successful product PUT/POST responses (`ProductForm.jsx:857`, `ProductForm.jsx:875`, `ProductForm.jsx:896`, `ProductForm.jsx:924`, `ProductVariantsTab.jsx:1542`, `ProductVariantsTab.jsx:1553`).
- Added translation fallback handling for the active-priced variant validation message so missing i18n keys do not display as raw key names (`ProductForm.jsx:192`).

## Runtime Verification

- Automated runtime-path verification was performed with component/API-contract tests covering the failing save sequence:
  - Existing Variant Product matrix save uses the current parent authoring snapshot.
  - Backend canonical validation `errors[]` and nested 409 conflict `data.errors[]` are surfaced to the user.
  - Matrix rows remain visible after failed validation.
  - Parent form surfaces post-matrix validation and stops the second save when the just-saved variant data is not publishable.
  - Parent form does not issue a second product update after the matrix save returns the saved product.
  - Parent form still uses `If-Match` for normal existing-product updates when no matrix save already completed the canonical mutation.
- Full authenticated browser verification against a live tenant was not executed in this session. The fix is therefore certified with observations rather than unconditional certification.

## Regression Tests

- Added `ProductVariantsTab` regression: `uses the current product authoring snapshot and preserves matrix edits when backend validation fails` (`PointOfSale.Web/src/components/ProductVariantsTab.test.tsx:153`).
- Added `ProductVariantsTab` regression: `surfaces nested product authoring concurrency conflict details without clearing matrix rows` (`PointOfSale.Web/src/components/ProductVariantsTab.test.tsx:205`).
- Added `ProductForm` regression: `surfaces nested product authoring concurrency conflict details` (`PointOfSale.Web/src/components/ProductForm.test.tsx:363`).
- Added `ProductForm` regression: `surfaces post-matrix variant validation at the parent form and prevents the parent update` (`PointOfSale.Web/src/components/ProductForm.test.tsx:395`).
- Added `ProductForm` regression: `does not issue a second product update after the matrix save returns the saved product` (`PointOfSale.Web/src/components/ProductForm.test.tsx:455`).

Validation executed after fix:

- `npm run test -- ProductVariantsTab.test.tsx ProductForm.test.tsx --run`
  - Passed: 23 tests across 2 files.
- `npm run build` from `PointOfSale.Web`
  - Passed.
- `dotnet build PointOfSale.sln --no-restore`
  - Passed: 0 warnings, 0 errors.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~ProductAuthoringPolicyCharacterizationTests|FullyQualifiedName~ProductAuthoringCommandMapperCharacterizationTests|FullyQualifiedName~ProductAuthoringReconstructionCharacterizationTests|FullyQualifiedName~ProductVariantTests|FullyQualifiedName~ProductVariantLifecycleAuthorityTests|FullyQualifiedName~ProductVariantCloneImportAuthorityTests" --no-restore`
  - Passed: 37 tests.
- `dotnet test PointOfSale.Tests\PointOfSale.Tests.csproj --filter "FullyQualifiedName~StorefrontProductBoundaryCertificationTests|FullyQualifiedName~StorefrontRuntimePurchasabilityHarmonizationTests" --no-restore`
  - Passed: 5 tests.

Observation:

- A broader filtered backend run for `ProductAuthoring|ProductVariant` was attempted but exceeded the command timeout before producing a final pass/fail result.

## Final Certification

- BUG FIX VERIFIED WITH OBSERVATIONS
