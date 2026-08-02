# RECOVERY-3 Legacy Runtime Elimination Report

## Certification

PASS WITH OBSERVATIONS

Recovery Phase 3 was implemented only. Recovery-1 and Recovery-2 ownership decisions were preserved. No backend, API, DTO, persistence, concurrency, Product domain, or Variant domain code was modified.

## 1. Reachability Analysis

Canonical product authoring entry remains `ProductWorkspacePage` rendering `ProductForm` with the workspace form id and save callback. Evidence: `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx:162-174`.

`ProductForm` is the only production caller of `ProductVariantsTab`. Evidence: `rg -n "ProductVariantsTab" PointOfSale.Web/src` returns the ProductForm import/render, the ProductVariantsTab component/test, and non-runtime text references only.

`ProductForm` renders `ProductVariantsTab` only under `hasVariants`, and passes `forceMatrixMode={hasVariants}`, `onVariantDraftChange`, and `onSaveVariantDraft`. Evidence: `PointOfSale.Web/src/components/ProductForm.jsx:1352-1370`.

`ProductForm.handleSubmit()` reaches the variant tab only through `hasPendingChanges()`, `prepareProductAuthoringDraft()`, and `getLastError()`. Evidence: `PointOfSale.Web/src/components/ProductForm.jsx:921-938`.

`ProductVariantsTab` now exposes only the imperative methods used by ProductForm: `hasPendingChanges()`, `getLastError()`, and `prepareProductAuthoringDraft()`. Evidence: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1299-1313`.

`ProductVariantsTab` loads variants and warehouses only; it does not load parent product state or RowVersion. Evidence: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:531-537`.

`ProductVariantsTab` converts matrix rows into draft authoring variants and sends that draft to ProductForm by callback/ref, not by product PUT. Evidence: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:710-729` and `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1248-1281`.

`VariantMatrixTable` now accepts matrix-editing props only. It no longer accepts legacy fallback edit/delete callbacks or a fallback action flag. Evidence: `PointOfSale.Web/src/components/VariantMatrixTable.jsx:36-45`.

`ProductUnitsModal` had no current import or render path and called obsolete unit mutation endpoints. Evidence before deletion: `rg -n "ProductUnitsModal" PointOfSale.Web/src` returned only its own export; after deletion the same search returns no matches.

## 2. Legacy Candidate Inventory

| Candidate | Current references before deletion | Replacement | Reason |
| --- | --- | --- | --- |
| Manual non-matrix variant CRUD UI branch | Rendered only by `ProductVariantsTab` when not in matrix mode; no production caller because ProductForm passes `forceMatrixMode={hasVariants}` | Matrix authoring rows plus ProductForm Product Authoring save | Current UI path for product authoring always enters matrix mode for variant products. |
| Direct variant create/update modal handlers | Internal `ProductVariantsTab` handlers that called `POST /products/{productId}/variants` and `PUT /products/{productId}/variants/{id}` | `toProductAuthoringVariant()` draft extraction and ProductForm `PUT /products/{id}` | Backend direct mutation endpoints are deprecated, and Product Authoring replaces create/update. |
| Direct variant delete fallback | Internal `ProductVariantsTab` confirmation path that called `DELETE /products/{productId}/variants/{id}` | Select rows, delete from the matrix draft, and save through Product Authoring omission | Product Authoring sync deletes omitted variants; direct delete endpoint is obsolete. |
| Variant fallback action props | `VariantMatrixTable` props `onEditFallback`, `onDeleteFallback`, and `showFallbackActions` | Inline matrix editing and bulk delete-selected draft edits | Legacy row cleanup still exists, but it no longer routes to direct variant endpoints. |
| Manual variant media picker state | Used only by the removed direct modal | Matrix table and bulk image picker | Variant media is edited as draft row `mediaId`/`imageUrl`. |
| `set-stock` bulk branch | Internal branch in `ProductVariantsTab.handleApplyBulkAction`; `VariantBulkActions` emits `set-inventory`, not `set-stock` | `set-inventory` draft value plus post-authoring stock adjustment through `variantStockApi` | No UI emitter or test invoked `set-stock`. |
| `getLastSavedProduct()` imperative method and saved product state | Exposed only by `ProductVariantsTab`; no ProductForm or test caller | ProductForm owns save result and `onSave()` | Recovery-1 made ProductForm the save owner. |
| `getAuthoringDraft()` imperative method | Exposed only by `ProductVariantsTab`; no ProductForm or test caller | `prepareProductAuthoringDraft()` and `onVariantDraftChange` | Current parent contract uses validation-aware draft extraction. |
| `savePendingChanges()` imperative method | Exposed only by `ProductVariantsTab`; no ProductForm or test caller after Recovery-2 | ProductForm global save pipeline and existing Save Matrix adapter | Child no longer owns product save execution. |
| `ProductUnitsModal.jsx` | No imports or render call sites; only its own export | ProductForm inline Product Units state and `productUnits` in Product Authoring payload | Obsolete direct unit mutation UI was unreachable. |

## 3. Deleted Runtime Inventory

| Deleted item | Evidence | Justification |
| --- | --- | --- |
| `ProductUnitsModal.jsx` file | `rg -n "ProductUnitsModal" PointOfSale.Web/src` now returns no matches | Unused component with obsolete `/products/{id}/units` mutation runtime. |
| ProductVariantsTab direct variant create/update/delete handlers | `rg -n "api\.(post|put|delete).*variants" PointOfSale.Web/src` now returns only `variantStockApi.js` stock update | Removed deprecated direct variant mutation execution paths. |
| Manual variant modal state and JSX | `rg -n "showModal|editingVariant|confirmDelete|handleSaveVariant|handleDeleteVariant|confirmDeleteVariant" PointOfSale.Web/src/components/ProductVariantsTab.jsx` returns no matches | Modal existed only for direct variant CRUD. |
| VariantMatrixTable fallback action props/buttons | `rg -n "onEditFallback|onDeleteFallback|showFallbackActions|advancedEdit" PointOfSale.Web/src` returns no matches | Removed legacy button chain into deprecated direct variant CRUD. |
| `set-stock` branch in ProductVariantsTab | `rg -n "set-stock" PointOfSale.Web/src` returns no matches | No emitter existed; `VariantBulkActions` emits `set-inventory`. |
| Unused imperative methods `getLastSavedProduct`, `getAuthoringDraft`, `savePendingChanges` | `rg -n "savePendingChanges|getAuthoringDraft|getLastSavedProduct" PointOfSale.Web/src` returns no matches | No parent/test caller remained; current ref contract is validation draft only. |
| ProductVariantsTab local success/applying-stock state | No remaining setters or renders after CRUD and `set-stock` deletion | State only supported removed legacy runtime. |

## 4. Remaining Legacy Inventory

Legacy row detection remains. Evidence: `showLegacyRows`, `legacyRows`, `canReuseLegacyDefaultVariant()`, and legacy row banners remain in `ProductVariantsTab`. Reason: existing persisted variants may not match current option groups, and cleanup must still be possible through Product Authoring omission.

Legacy variant `imageUrl` support remains. Reason: existing persisted data may have image URLs without media IDs; matrix authoring still maps gallery `mediaId` and legacy `imageUrl` safely.

Save Matrix UI remains as an observation. It no longer executes a product PUT in `ProductVariantsTab`; it calls `onSaveVariantDraft`, which ProductForm supplies. Evidence: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1248-1281`. This is not an alternative Product Authoring executor, but it is still an alternate UI entry into the same ProductForm-owned save path.

Post-authoring inventory adjustment remains separate from Product Authoring. Evidence: `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1213-1245` and `PointOfSale.Web/src/services/variantStockApi.js:9`. Reason: stock is an independent resource and Recovery-2 explicitly deferred the stock workflow decision.

Simple product default variant loading remains in ProductForm. Evidence: `PointOfSale.Web/src/components/ProductForm.jsx:584-617`. Reason: simple product SKU/barcode identity still depends on the default variant mirror.

Product unit reads remain in POS and Purchase Invoice pages. Evidence: `rg -n "api\.get\(`/products/.*units" PointOfSale.Web/src` returns read-only uses in `POS.jsx` and `PurchaseInvoice.jsx`. Reason: those are not Product Authoring mutation paths.

## 5. Runtime Graph After Recovery

```text
User
-> ProductWorkspacePage
-> ProductForm
-> ProductVariantsTab
-> variant draft state
-> ProductForm.prepare/save pipeline
-> ProductForm.buildProductAuthoringPayload()
-> ProductForm.buildProductAuthoringRequestOptions()
-> PUT /api/products/{id} with If-Match and Idempotency-Key
-> Backend Product Authoring
```

No frontend direct variant create/update/delete runtime remains. No ProductVariantsTab Product Authoring PUT runtime remains. ProductForm remains the sole frontend owner of product authoring POST/PUT execution.

## 6. Search Evidence

| Search | Result |
| --- | --- |
| `rg -n "ProductUnitsModal" PointOfSale.Web/src` | No matches after deletion. |
| `rg -n "api\.(post|put|delete).*variants" PointOfSale.Web/src` | One match remains: `variantStockApi.js:9` stock update only. No direct create/update/delete variant CRUD. |
| `rg -n "api\.put\(`/products/\$\{productId\}`" PointOfSale.Web/src` | One product authoring PUT: `ProductForm.jsx:856`. |
| `rg -n "api\.post\('/products'" PointOfSale.Web/src` | One product authoring POST: `ProductForm.jsx:868`. |
| `rg -n "buildProductAuthoringPayload" PointOfSale.Web/src` | One implementation and one call, both in `ProductForm.jsx`. |
| `rg -n "rowVersion|RowVersion|getProductRowVersion|If-Match|Expected-Version|Idempotency-Key" PointOfSale.Web/src/components --glob "*ProductVariantsTab.jsx"` | No matches in `ProductVariantsTab.jsx`. |
| `rg -n "savePendingChanges|getAuthoringDraft|getLastSavedProduct|onEditFallback|onDeleteFallback|showFallbackActions|advancedEdit|legacy workflow|fallback modal|set-stock" PointOfSale.Web/src` | No matches. |
| `rg -n "api\.(post|put|delete).*\/products/.*\/units" PointOfSale.Web/src` | No frontend direct product unit mutation matches. |
| `rg -n "normalizeProductAuthoringVariants|toProductAuthoringVariant" PointOfSale.Web/src` | Shared variant mapper remains in `utils/productAuthoringVariants.js`, used by ProductForm and ProductVariantsTab only. |
| `rg -n "ProductVariantsTab" PointOfSale.Web/src` | Production caller is ProductForm; tests and text references are non-runtime. |

## 7. Risks

Low: Save Matrix is still visible as a UI entry, but it delegates to ProductForm and does not execute Product Authoring from ProductVariantsTab.

Low: Legacy row cleanup now relies on matrix row selection/deletion plus Product Authoring save. This is the canonical replacement for the removed direct delete button.

Medium: Post-authoring inventory changes can still fail after a successful product authoring save. This was known in Recovery-2 and remains deferred because stock is an independent resource.

Low: The worktree contains pre-existing unrelated modified and untracked files from earlier recovery/theme/backend work. They were not reverted or edited as part of Recovery-3, except for the frontend files listed in this report.

## 8. Validation

PASS: `npm test -- --run src/components/ProductForm.test.tsx src/components/ProductVariantsTab.test.tsx src/pages/ProductWorkspacePage.test.tsx`

Result: 3 test files passed, 24 tests passed.

PASS: `npm run build`

Result: Vite production build completed successfully.

Manual runtime reasoning: ProductForm remains the only frontend component that builds and executes Product Authoring POST/PUT requests. ProductVariantsTab now supplies a validated draft and optional post-authoring inventory adjustments only. VariantMatrixTable no longer exposes direct legacy CRUD fallback actions.

## 9. Deferred Recovery Items

Decide whether Save Matrix should remain as an alternate UI entry into ProductForm-owned save or be removed/renamed in a UX-specific recovery.

Decide whether post-authoring inventory changes should remain coupled to variant authoring or move to a separate stock workflow.

Keep legacy row detection until persisted legacy variants are normalized or an explicit migration removes the need for compatibility cleanup.

Independent media, pixel override, affiliate, POS product-unit reads, and stock flows remain outside Product Authoring runtime and were not modified in Recovery-3.
