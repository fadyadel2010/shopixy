# RECOVERY-1 Runtime Ownership Report

## Certification

PASS WITH OBSERVATIONS

Recovery Phase 1 was implemented only. Recovery-2 cleanup was not implemented.

## Ownership Changes

- `ProductWorkspacePage` no longer owns product save success notifications or AI-apply notifications. It remains responsible for the page shell, global submit button, navigation, and workspace layout.
- `ProductForm` now owns the product authoring save pipeline for global submit and variant draft saves. It owns product payload construction, RowVersion use, idempotency key creation, product authoring request execution, success handling, failure handling, POS runtime cache invalidation, and global toast notifications.
- `ProductForm` no longer stores a variant snapshot from the product load response. It receives a read-only authoring summary from `ProductVariantsTab` for publication readiness and pixel price derivation.
- `ProductVariantsTab` now loads only variants and warehouses. It no longer loads the parent product or selects a product RowVersion.
- `ProductVariantsTab` now exposes variant draft extraction through `getAuthoringDraft()` and validated extraction through `prepareProductAuthoringDraft()`.
- `ProductVariantsTab` keeps Save Matrix, but Save Matrix now validates and hands the draft to the parent-owned `onSaveVariantDraft` adapter instead of issuing the parent product PUT itself.
- Legacy variant CRUD paths and buttons remain present. No APIs, DTOs, backend code, persistence behavior, or Product Authoring backend behavior were changed.

## Files Modified

- `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx`
- `PointOfSale.Web/src/components/ProductForm.jsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx`
- `PointOfSale.Web/src/components/ProductForm.test.tsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.test.tsx`
- `RECOVERY-1_RUNTIME_OWNERSHIP_REPORT.md`

## Why Each Change Was Required

- `ProductWorkspacePage.jsx`: removed global save notification ownership so the workspace page only owns shell, navigation, global submit placement, and layout.
- `ProductForm.jsx`: centralized product authoring request execution and notifications in the form, which is the canonical owner of product state, loading, RowVersion, payload, authoring request, success, and failure.
- `ProductForm.jsx`: added a temporary `onSaveVariantDraft` adapter for Save Matrix so existing matrix persistence behavior can be retained while product save ownership moves to the form.
- `ProductForm.jsx`: replaced product-loaded variant snapshot state with a variant-tab-supplied authoring summary to reduce duplicate variant ownership while preserving readiness and pixel price calculations.
- `ProductVariantsTab.jsx`: removed parent product loading and RowVersion use from the variant workspace, leaving it with variant matrix state, option groups, validation, generated combinations, bulk editing, and draft extraction.
- `ProductVariantsTab.jsx`: retained Save Matrix but changed it to validate/confirm/extract the draft, then call the ProductForm adapter for the product authoring save.
- `ProductForm.test.tsx`: updated characterization tests to assert ProductForm owns the product PUT when variant drafts are pending.
- `ProductVariantsTab.test.tsx`: updated characterization tests to assert ProductVariantsTab hands variant drafts to a parent save adapter and does not issue the parent product PUT.

## Deferred Items For Recovery-2

- Remove or convert the user-visible Save Matrix persistence action after the single global save model is finalized.
- Remove or permanently hide legacy direct variant CRUD UI paths that call deprecated endpoints.
- Delete or consolidate now-unused ProductVariantsTab product-authoring helper code after adapters are no longer needed.
- Consolidate duplicated frontend mappers and validation helpers.
- Further reduce ProductForm's derived variant summary usage if a richer read-only contract is introduced.
- Standardize independent section notifications for media, pixel override, and affiliate settings outside product authoring.

## Risk Assessment

- Low to medium: Save Matrix now depends on a parent adapter. In the canonical ProductForm runtime the adapter is supplied; standalone use would surface a clear unavailable-save error instead of performing product authoring itself.
- Low: ProductVariantsTab no longer loads the parent product for SKU prefix. ProductForm supplies a product name prefix; SKU builder behavior remains available.
- Medium: Full frontend test suite still has unrelated failing storefront/i18n/builder tests. Focused product runtime tests pass, but the full suite is not clean before/after this recovery work.
- Low: No backend, DTO, persistence, concurrency, Product Authoring, API, Save Matrix button, or legacy CRUD removal was performed.

## Validation

- PASS: `npm test -- --run src/components/ProductForm.test.tsx src/components/ProductVariantsTab.test.tsx src/pages/ProductWorkspacePage.test.tsx`
- PASS: `npm run build` in `PointOfSale.Web`
- PASS WITH WARNINGS: `dotnet build PointOfSale.sln` completed with 0 errors and existing analyzer warnings.
- OBSERVATION: `npm test -- --run` in `PointOfSale.Web` failed 7 unrelated tests in storefront checkout/cart state, i18n certification, and builder runtime certification areas. These failures are outside the modified Recovery-1 product runtime files.

## Recovery Boundary

Recovery-2 was not implemented. Save Matrix, legacy CRUD, APIs, DTOs, backend code, persistence behavior, and Product Authoring backend behavior were preserved.
