# Product Authoring Final Recertification

Audit date: 2026-07-31

Final Certification: **PRODUCT AUTHORING CERTIFIED WITH OBSERVATIONS**

## 1. Executive Summary

This recertification audited the current source code only. The historical reports listed in the mission were read as claims and then independently verified against the runtime source.

The current Product Authoring runtime has one reachable frontend Product Authoring save owner: `ProductForm`. It has one frontend Product PUT executor, one frontend Product POST executor, one frontend Product Authoring payload builder, one frontend Product Authoring `If-Match` generation point, and one frontend Product Authoring idempotency-key generation point. Backend product create/update enters `ProductsController` and delegates to `ProductAuthoringOrchestrator.AuthorAsync`, which owns the SQL aggregate mutation transaction, idempotency claim/complete, concurrency enforcement, events, and audit staging.

No reachable frontend direct variant create/update/delete runtime remains. Backend direct variant CRUD routes still exist, but return `410 Gone` before their old bodies execute. Those dead bodies and helper methods remain in source and are listed below as cleanup observations.

Change detection is present and active in `ProductForm`: a baseline Product Authoring snapshot is captured after load, current saves are semantically canonicalized, no-change updates return before key generation and before PUT, and the baseline is refreshed after successful POST/PUT.

Certification is not unconditional because the audit found the following observations:

- Dead legacy backend bodies remain below early `410` returns in `ProductVariantsController` and `ProductsController`.
- Submitted empty variant lists cannot currently express intentional deletion of all variants because `SyncAuthoredVariantsAsync` returns early when `authoredVariants.Count == 0`.
- Variant stock quantity edits are applied after Product Authoring through a separate stock endpoint, so stock changes can fail after a successful product/variant authoring save.
- `ProductForm` still contains modal-mode legacy actions, but current production routing renders it from `ProductWorkspacePage` in workspace mode.
- Product stock adjustment remains a separate product-adjacent mutation outside Product Authoring.

## 2. Architecture Overview

### Current Product Authoring Path

```text
ProductWorkspacePage
-> ProductForm
-> ProductForm.saveProductAuthoring()
-> api.post('/products') or api.put(`/products/${productId}`)
-> ProductsController.CreateProduct() or ProductsController.UpdateProduct()
-> ProductAuthoringCommandMapper
-> ProductAuthoringOrchestrator.AuthorAsync()
-> ExecuteCreateAsync() or ExecuteUpdateAsync()
-> CommitProductAuthoringTransactionAsync()
-> SaveChangesAsync() and transaction commit
-> response DTO with RowVersion
-> ProductForm updates acceptedRowVersion and baseline snapshot
```

### Source Evidence

| Requirement | Status | Evidence | Comments |
| --- | --- | --- | --- |
| Exactly one frontend Product Authoring pipeline | PASS | `PointOfSale.Web/src/pages/ProductWorkspacePage.jsx:143-174`, `PointOfSale.Web/src/components/ProductForm.jsx:979-1071`, `PointOfSale.Web/src/components/ProductForm.jsx:1096-1122` | Workspace submit reaches `ProductForm.handleSubmit()`, then `saveProductAuthoring()`. |
| Exactly one frontend Product PUT executor | PASS | `PointOfSale.Web/src/components/ProductForm.jsx:1037-1042`; grep found only `variantStockApi.js:9` as another `/products/...` PUT, and it is stock, not Product Authoring | Product Authoring PUT is only in `ProductForm`. |
| Exactly one frontend Product POST executor | PASS | `PointOfSale.Web/src/components/ProductForm.jsx:1050-1055`; grep found only barcode generation and stock adjustment as product-adjacent POSTs | Product Authoring POST is only in `ProductForm`. |
| Exactly one frontend Product payload builder | PASS | `PointOfSale.Web/src/components/ProductForm.jsx:272-307`, `PointOfSale.Web/src/components/ProductForm.jsx:910-919` | `buildProductAuthoringPayloadFromDraft()` is the single Product Authoring body builder. |
| Exactly one frontend Product save owner | PASS | `PointOfSale.Web/src/components/ProductForm.jsx:979-1071`, `PointOfSale.Web/src/components/ProductVariantsTab.jsx:1248-1281` | `ProductVariantsTab` hands drafts to `ProductForm`; it does not execute Product Authoring PUT. |
| Exactly one Product RowVersion owner | PASS | `PointOfSale.Core/Entities/Product.cs:213-217`, `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs:1716-1718`, `PointOfSale.API/Mappers/SafeResponseMapper.cs:100` | Backend owns `RowVersion` as PostgreSQL `xmin`; frontend stores accepted backend evidence only. |
| Exactly one frontend If-Match generation point | PASS | `PointOfSale.Web/src/components/ProductForm.jsx:245-255` | `If-Match` is emitted only by ProductForm request options for existing products with rowVersion. |
| Exactly one frontend Product Authoring Idempotency-Key generation point | PASS | `PointOfSale.Web/src/components/ProductForm.jsx:240-243`, `PointOfSale.Web/src/services/api.js:262-270` | `api.js` mirrors a supplied key to `Idempotency-Key` and `X-Request-Id`; it does not generate Product Authoring keys. |
| Backend Product Authoring entry | PASS | `PointOfSale.API/Controllers/ProductsController.cs:286-309`, `PointOfSale.API/Controllers/ProductsController.cs:316-375` | Product create/update delegate to `_productAuthoringOrchestrator.AuthorAsync()`. |
| Backend Product Authoring transaction owner | PASS | `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:68-96`, `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs:461-517` | The orchestrator dispatches create/update/archive/remove and owns transaction/idempotency/concurrency save. |

## 3. Ownership Matrix

| Runtime Area | Owner | Evidence | Certification |
| --- | --- | --- | --- |
| Product workspace shell | `ProductWorkspacePage` | `ProductWorkspacePage.jsx:32-40`, `ProductWorkspacePage.jsx:143-174` | PASS. Owns route identity, layout, global submit button, back, preview, AI modal wiring. |
| Product save execution | `ProductForm` | `ProductForm.jsx:979-1071`, `ProductForm.jsx:1096-1122` | PASS. Owns save validation, payload build, change detection, POST/PUT, rowVersion acceptance, baseline refresh, toast, cache invalidation. |
| Product content fields | `ProductForm` | `ProductForm.jsx:476-511`, `ProductForm.jsx:1128-1134` | PASS. Owns form state and field changes. |
| Product type | `ProductForm` | `ProductForm.jsx:467`, `ProductForm.jsx:825-849` | PASS. Owns `hasVariants` and mode switching. |
| Simple Product SKU/barcode | `ProductForm` | `ProductForm.jsx:472-475`, `ProductForm.jsx:763-811`, `ProductForm.jsx:1128-1134` | PASS. Loads default variant mirror and owns simple identity state. |
| Product Units | `ProductForm` for workspace Product Authoring | `ProductForm.jsx:257-270`, `ProductForm.jsx:469`, `ProductForm.jsx:851-902`, `ProductForm.jsx:302` | PASS. Inline Product Units are included in Product Authoring payload for simple products. Backend compatibility unit endpoints delegate to Product Authoring. |
| Variant draft authoring | `ProductVariantsTab` | `ProductVariantsTab.jsx:478-484`, `ProductVariantsTab.jsx:1183-1211`, `ProductVariantsTab.jsx:1299-1313` | PASS. Owns option groups, matrix rows, validation, draft extraction. Does not own Product PUT. |
| Variant matrix row UI | `VariantMatrixTable` | `VariantMatrixTable.jsx:36-45`, `VariantMatrixTable.jsx:225-299` | PASS. Owns row controls and callbacks only. |
| Variant media selection in matrix | `VariantMatrixTable` with callback to `ProductVariantsTab` | `VariantMatrixTable.jsx:54-81`, `VariantMatrixTable.jsx:87-104`, `VariantMatrixTable.jsx:309-315` | PASS. Reads gallery media and emits row `mediaId` changes. |
| Product media gallery | `ProductMediaManager` | `ProductForm.jsx:1520-1533`, `ProductMediaManager.jsx:185-186`, `ProductMediaManager.jsx:241-245`, `ProductMediaManager.jsx:282-283`, `ProductMediaManager.jsx:303-305`, `ProductMediaManager.jsx:347-351` | PASS. Separate resource, intentionally outside Product Authoring body. |
| Inventory notice | `ProductForm` | `ProductForm.jsx:1568-1587` | PASS. ProductForm only displays guidance. |
| Variant stock adjustment | `ProductVariantsTab` plus `variantStockApi` | `ProductVariantsTab.jsx:1213-1245`, `variantStockApi.js:8-11`, `ProductVariantsController.cs:1398-1578` | OBSERVATION. Separate stock resource after Product Authoring. Potential partial failure after product save. |
| Product stock adjustment | `ProductStockModal` | `ProductStockModal.jsx:36-52`, `Products.jsx:180-185` | OBSERVATION. Product-adjacent stock mutation remains outside Product Authoring. |
| Publication | `ProductForm` | `ProductForm.jsx:1589-1692`, `ProductForm.jsx:990-996` | PASS. ProductForm owns publication fields and readiness validation. |
| SEO | `ProductForm` | `ProductForm.jsx:1746-1799` | PASS. ProductForm owns SEO fields in Product Authoring payload. |
| Marketing | `ProductForm` | `ProductForm.jsx:1695-1744` | PASS. ProductForm owns brand/manufacturer/tags/video fields. |
| Pixel override | `ProductPixelOverrideTab` | `ProductForm.jsx:1814-1823`, `ProductPixelOverrideTab.jsx:155-164` | PASS WITH OBSERVATION. Separate resource. Stale inline comment remains at `ProductPixelOverrideTab.jsx:253-270`. |
| Affiliate | `ProductAffiliateSettingsCard` | `ProductForm.jsx:1825-1831`, `ProductAffiliateSettingsCard.jsx:17-27`, `adminProductAffiliateApi.js:61-104` | PASS. Separate affiliate policy resource, role-gated by ProductForm. |

## 4. Runtime Graph

### Existing Product: User Clicks Update Product Until Backend Success

```text
1. User clicks the top-bar Update Product button.
   Source: ProductWorkspacePage.jsx:143-157.

2. The button submits the form with id product-workspace-form.
   Source: ProductWorkspacePage.jsx:145-146, ProductWorkspacePage.jsx:164-168, ProductForm.jsx:1947-1957.

3. ProductForm.handleSubmit() receives the submit event, prevents default, sets loading, clears error.
   Source: ProductForm.jsx:1096-1101.

4. If this is an existing variant product and ProductVariantsTab reports pending matrix changes, ProductForm asks the tab for a validated draft.
   Source: ProductForm.jsx:1102-1117, ProductVariantsTab.jsx:1299-1313.

5. ProductVariantsTab.prepareProductAuthoringDraft() validates rows, confirms deletion of stocked variants when applicable, and returns the variant draft snapshot.
   Source: ProductVariantsTab.jsx:1183-1211.

6. ProductForm.saveProductAuthoring() validates category and Product Authoring readiness.
   Source: ProductForm.jsx:979-996.

7. ProductForm builds the canonical Product Authoring payload.
   Source: ProductForm.jsx:999, ProductForm.jsx:910-919, ProductForm.jsx:272-307.

8. For existing products, ProductForm compares current payload to the loaded baseline. If unchanged, it returns a no-change result before idempotency key creation and before PUT.
   Source: ProductForm.jsx:1000-1011.

9. If changed, ProductForm creates or reuses a pending-save idempotency key based on fingerprint.
   Source: ProductForm.jsx:1024-1032.

10. ProductForm builds request headers with Idempotency-Key and If-Match from acceptedRowVersion.
    Source: ProductForm.jsx:1035, ProductForm.jsx:245-255.

11. ProductForm executes one Product Authoring PUT.
    Source: ProductForm.jsx:1037-1039.

12. api.js mirrors the idempotency key to Idempotency-Key and X-Request-Id and adds cross-cutting headers.
    Source: api.js:262-274.

13. ProductsController.UpdateProduct() requires idempotency and expected version.
    Source: ProductsController.cs:316-331, ProductsController.cs:1008-1047, ProductsController.cs:1158-1192.

14. ProductsController maps the request through ProductAuthoringCommandMapper.
    Source: ProductsController.cs:348-352.

15. ProductAuthoringCommandMapper maps request fields, units, variants, Idempotency-Key, If-Match, and submission metadata into ProductAuthoringCommand.
    Source: ProductAuthoringCommandMapper.cs:266-413, ProductAuthoringCommandMapper.cs:419-470.

16. ProductsController calls ProductAuthoringOrchestrator.AuthorAsync().
    Source: ProductsController.cs:362.

17. ProductAuthoringOrchestrator requires idempotency, reconstructs omitted units/variants, admits the command, and dispatches update.
    Source: ProductAuthoringOrchestrator.cs:68-96, ProductAuthoringReconstructionService.cs:23-71.

18. ExecuteUpdateAsync loads the product, resolves idempotency, checks stale RowVersion, validates identity and units, then enters the commit function.
    Source: ProductAuthoringOrchestrator.cs:243-335.

19. The commit function starts a transaction, claims idempotency, applies mutation, applies expected RowVersion to EF OriginalValue, stages events/audit, saves, commits, and returns accepted result.
    Source: ProductAuthoringOrchestrator.cs:461-517, ProductAuthoringOrchestrator.cs:1568-1577.

20. ProductsController reloads product for response and returns DTO with RowVersion, units, and variants.
    Source: ProductsController.cs:368-375, SafeResponseMapper.cs:100-109.

21. ProductForm accepts returned RowVersion, refreshes baseline snapshot, invalidates POS runtime cache, notifies success, calls onSave, clears pending save identity.
    Source: ProductForm.jsx:1040-1071.
```

Result: There is exactly one reachable Product Authoring execution path for the global update button. Save Matrix is an alternate UI entry, but it delegates to `ProductForm.handleSaveVariantDraft()` and then to the same `saveProductAuthoring()` path.

## 5. Certification Matrix

| Category | Status | Evidence | Comments |
| --- | --- | --- | --- |
| Product Authoring Pipeline | PASS | `ProductForm.jsx:979-1071`, `ProductsController.cs:286-375`, `ProductAuthoringOrchestrator.cs:68-96` | One frontend Product Authoring save path; backend create/update both enter orchestrator. |
| Product PUT Executor | PASS | `ProductForm.jsx:1038`; grep found no other frontend Product Authoring PUT | Variant stock PUT is separate at `variantStockApi.js:9`. |
| Product POST Executor | PASS | `ProductForm.jsx:1051`; grep found no other frontend Product Authoring POST | Barcode generation and stock adjustment are separate resources. |
| Payload Builder | PASS | `ProductForm.jsx:272-307`; backend mapper at `ProductAuthoringCommandMapper.cs:266-413` | One frontend body builder; one backend command mapper. |
| Runtime Ownership | PASS WITH OBSERVATIONS | Ownership matrix above | Modal-mode actions and Save Matrix UI remain, but not separate Product Authoring executors. |
| Legacy Runtime | PASS WITH OBSERVATIONS | `ProductVariantsController.cs:118-123`, `463-469`, `1056-1061`, `1224-1229`, `ProductsController.cs:1702-1707` | Legacy routes return `410`; dead bodies remain below returns. |
| Change Detection | PASS | `ProductForm.jsx:309-405`, `747-755`, `1002-1011`, `1042`, `1055` | No-change updates skip PUT. |
| Variant Runtime | PASS WITH OBSERVATIONS | `ProductVariantsTab.jsx:1183-1281`, `ProductAuthoringOrchestrator.cs:1285-1381` | Variant edits persist through ProductForm. Empty submitted variant list is no-op. |
| Simple Product Runtime | PASS | `ProductForm.jsx:257-270`, `ProductForm.jsx:302`, `ProductAuthoringOrchestrator.cs:1081-1155`, `1189-1201` | Simple units and default variant mirror are persisted through Product Authoring. |
| Storefront Compatibility | PASS | `ProductAuthoringOrchestrator.cs:611-725`, `ProductEvents.cs:97-126`, `ProductEvents.cs:180-204`, `ProductSearchSqlAuthorityOverlay.cs:183-304`, `StorefrontProductService.cs:270-353` | Events and SQL authority overlay carry units, variants, HasVariants, pricing, media. |
| Concurrency | PASS WITH OBSERVATIONS | `Product.cs:213-217`, `ApplicationDbContext.cs:1716-1718`, `ProductsController.cs:1158-1192`, `ProductAuthoringOrchestrator.cs:1521-1577` | Stale updates map to HTTP 409 Conflict, not 412 Precondition Failed. |
| Idempotency | PASS WITH OBSERVATIONS | `ProductForm.jsx:240-255`, `ProductsController.cs:1008-1047`, `ProductAuthoringOrchestrator.cs:126-139`, `461-517`, `1598-1606` | Product Authoring key is required. Update fingerprint includes reconstructed command state. |

## 6. Legacy Verification

### Frontend Searches

| Search Area | Result | Evidence |
| --- | --- | --- |
| Direct Product Authoring PUT bypass | PASS | Only `ProductForm.jsx:1038` for Product Authoring; `variantStockApi.js:9` is stock. |
| Direct Product Authoring POST bypass | PASS | Only `ProductForm.jsx:1051`; product-adjacent matches are barcode generation and stock adjustment. |
| Product payload builder duplicates | PASS | Product builder functions found only in `ProductForm.jsx:272-307` and call wrapper at `ProductForm.jsx:910-919`. |
| Product RowVersion/If-Match/idempotency helpers in ProductVariantsTab | PASS | No matches in `ProductVariantsTab.jsx`; helpers exist in `ProductForm.jsx:236-255`. |
| Direct frontend variant CRUD | PASS | No `api.post/put/delete` variant CRUD remains; remaining variant writes are stock only at `variantStockApi.js:9`. |
| Obsolete Product callbacks | PASS | No `onProductSave`, `onVariantSave`, `savePendingChanges`, `getAuthoringDraft`, `getLastSavedProduct`, fallback edit/delete props, or `set-stock` matches in `PointOfSale.Web/src`. |
| ProductUnitsModal | PASS | No runtime references remain. Current worktree has `ProductUnitsModal.jsx` deleted. |

### Backend Legacy And Dead Code

| Location | Classification | Evidence | Comment |
| --- | --- | --- | --- |
| `ProductVariantsController.CreateVariant` | Dead legacy body after reachable `410` | `ProductVariantsController.cs:118-126` | Route exists, immediately returns deprecated result, old body continues under unreachable-code pragma. |
| `ProductVariantsController.UpdateVariant` | Dead legacy body after reachable `410` | `ProductVariantsController.cs:463-472` | Same pattern. |
| `ProductVariantsController.DeleteVariant` | Dead legacy body after reachable `410` | `ProductVariantsController.cs:1056-1064` | Same pattern. |
| `ProductVariantsController.SetDefaultVariant` | Dead legacy body after reachable `410` | `ProductVariantsController.cs:1224-1232` | Same pattern. |
| `ProductVariantsController.LegacyVariantMutationDeprecatedResult` | Reachable deprecation response | `ProductVariantsController.cs:1582-1588` | Confirms direct variant mutation routes are not active authoring paths. |
| `ProductVariantsController.UpdateVariantStock` | Reachable non-authoring stock mutation | `ProductVariantsController.cs:1398-1578` | Separate stock resource. Not a variant CRUD authoring path. |
| `ProductsController.MigrateToMultiUnit` | Dead legacy body after reachable `410` | `ProductsController.cs:1702-1710`, `ProductsController.cs:2151-2157` | Route exists, immediately returns deprecation result, legacy migration body remains. |
| `ProductsController.CompleteProductMutationIdempotencyAsync` and snapshot helpers | Obsolete product save helper code | `ProductsController.cs:1054-1156` | Current product create/update use Product Authoring idempotency in orchestrator; these helpers are not on the canonical create/update path. |
| `ProductVariantService.CreateDefaultVariantAsync` | Obsolete helper retained | `ProductVariantService.cs:38-45`, `ProductVariantService.cs:115-129` | Marked `[Obsolete]`; Product Authoring uses `EnsureDefaultVariantAlignedAsync` at `ProductAuthoringOrchestrator.cs:223` and `353`. |

Conclusion: no reachable legacy Product Authoring or Variant CRUD execution path remains in the audited UI. Backend legacy route bodies remain in source but are not reachable past early `410` returns.

## 7. Change Detection Verification

| Requirement | Status | Evidence | Comments |
| --- | --- | --- | --- |
| Baseline snapshot | PASS | `ProductForm.jsx:747-755` | Baseline built after product load through same payload builder. |
| Semantic comparison | PASS | `ProductForm.jsx:398-405` | Compares stable canonical snapshots. |
| Normalization | PASS | `ProductForm.jsx:321-386` | Trims and collapses strings, normalizes numbers/booleans, sorts arrays and object keys, removes empty canonical values. |
| Ignored fields | PASS | `ProductForm.jsx:309-319` | Ignores RowVersion, expected/idempotency/local UI keys, `sortOrder`. |
| Compared fields | PASS | `ProductForm.jsx:272-307` plus canonicalization at `344-386` | Compares Product Authoring body: content, type, tax/flags, publication, SEO, dimensions, product template, units, variants when included. |
| Existing variant omitted variants equivalence | PASS | `ProductForm.jsx:388-395` | `variants: null` uses previous baseline variants for existing variant products. |
| Skip PUT | PASS | `ProductForm.jsx:1002-1011` | No-change existing product returns before idempotency key creation and PUT. |
| No-op save behavior | PASS | `ProductForm.jsx:1002-1010` | Shows success message and returns result with `__productAuthoringNoChanges`. |
| Baseline refresh after successful PUT | PASS | `ProductForm.jsx:1040-1042` | Baseline updates after accepted update. |
| Baseline refresh after successful POST | PASS | `ProductForm.jsx:1053-1055` | Baseline initializes after accepted create. |
| Product PUT bypassing change detection | PASS | Grep found Product Authoring PUT only at `ProductForm.jsx:1038`; change gate is at `ProductForm.jsx:1002-1011` | No bypass found. |

## 8. Runtime Certification Scenarios

| Scenario | Expected Runtime | Certification | Evidence |
| --- | --- | --- | --- |
| A. Simple Product create, update, save, refresh | Create uses POST with product units and `variants: []`; backend creates product, units, default variant alignment; update uses PUT only when semantic change exists; refresh reloads product and default variant mirror. | PASS | `ProductForm.jsx:287-306`, `ProductForm.jsx:1050-1055`, `ProductAuthoringOrchestrator.cs:211-240`, `ProductForm.jsx:687-755`, `ProductForm.jsx:763-811`. |
| B. Variant Product create, generate variants, edit matrix, update, refresh | Initial variant create is draft-safe and navigates to edit workspace. Matrix edits are validated in `ProductVariantsTab`, handed to `ProductForm`, persisted through one Product PUT with variants included, then variants are refetched. | PASS WITH OBSERVATION | `ProductWorkspacePage.jsx:67-72`, `ProductForm.jsx:300-305`, `ProductVariantsTab.jsx:1183-1281`, `ProductForm.jsx:1081-1094`, `ProductAuthoringOrchestrator.cs:349-353`, `ProductVariantsTab.jsx:1280`. Empty variant-list clearing is not supported. |
| C. Existing Product open, immediately Update | No Product PUT. | PASS | Baseline at `ProductForm.jsx:747-755`; skip at `ProductForm.jsx:1002-1011`. |
| D. Whitespace-only changes | No Product PUT after canonical text normalization. | PASS | `ProductForm.jsx:321-323`, `ProductForm.jsx:349-351`, `ProductForm.jsx:398-405`, `ProductForm.jsx:1002-1011`. |
| E. Variant price change | One Product PUT with variants included. | PASS | Row price edit callbacks at `VariantMatrixTable.jsx:252-258`; draft handoff at `ProductVariantsTab.jsx:1248-1281`; Product PUT at `ProductForm.jsx:1038`. |
| F. Product Unit change | One Product PUT for simple product. | PASS | Unit draft changes at `ProductForm.jsx:869-902`; units payload at `ProductForm.jsx:257-270` and `302`; Product PUT at `ProductForm.jsx:1038`. |
| G. Storefront compatibility | Product Authoring emits product/unit/variant state and SQL-authority projection maps units, variants, media, HasVariants, and storefront DTO fields. | PASS | `ProductAuthoringOrchestrator.cs:611-725`, `ProductSearchSqlAuthorityOverlay.cs:183-304`, `StorefrontProductService.cs:270-353`. |

## 9. Storefront Compatibility

Storefront compatibility is certified by source evidence.

| Area | Evidence | Result |
| --- | --- | --- |
| Product Authoring create/update events carry storefront data | `ProductAuthoringOrchestrator.cs:611-650`, `ProductAuthoringOrchestrator.cs:668-725` | PASS. Events include units/default unit, `HasVariants`, visibility, SEO, template, pricing mirror fields, and content. |
| Event contracts support units and variants behavior | `ProductEvents.cs:97-126`, `ProductEvents.cs:180-204` | PASS. `DefaultUnit`, `Units`, `TrackStock`, `AllowBackorder`, `HasVariants` are part of the contracts. |
| SQL authority overlay recomposes storefront read model | `ProductSearchSqlAuthorityOverlay.cs:183-222`, `ProductSearchSqlAuthorityOverlay.cs:238-304` | PASS. Units, default unit, media, variants, stock, and variant active-price guard are recomposed from SQL. |
| Product update consumer uses SQL authority projection writer | `ProductUpdatedEventConsumer.cs:75-93` | PASS. Update projection is recomposed from SQL authority rather than trusting stale event-only fields. |
| Storefront DTO maps units and variants | `StorefrontProductService.cs:270-353` | PASS. DTO includes default unit, pricing, availability, variants, defaultVariantId, and stored `HasVariants`. |

## 10. Remaining Risks

### Confirmed Issues

| Issue | Evidence | Impact |
| --- | --- | --- |
| Dead legacy variant CRUD bodies remain after `410` returns | `ProductVariantsController.cs:118-126`, `463-472`, `1056-1064`, `1224-1232` | Not reachable, but increases maintenance risk and audit burden. |
| Dead legacy multi-unit migration body remains after `410` return | `ProductsController.cs:1702-1710` | Not reachable, but obsolete persistence logic remains in source. |
| Empty submitted variant list is treated as no-op | `ProductAuthoringOrchestrator.cs:1285-1293` | Cannot intentionally delete all variants through an empty authored list. |
| Post-authoring inventory changes are separate after product save | `ProductVariantsTab.jsx:1213-1245`, `variantStockApi.js:8-11` | Product/variant authoring can succeed while stock updates fail. |
| Stale product concurrency returns HTTP 409, not 412 | `ProductsController.cs:988-997` | Correct conflict signal, but less conventional for failed `If-Match`. |

### Potential Risks

| Risk | Evidence | Impact |
| --- | --- | --- |
| Save Matrix remains as a second UI entry | `ProductVariantsTab.jsx:1248-1281` | It delegates to ProductForm, so architecture is safe, but UX can imply a separate save owner. |
| ProductForm modal mode still exists | `ProductForm.jsx:443-445`, `ProductForm.jsx:1839-1857` | No production import found outside ProductWorkspacePage, but legacy mode increases component complexity. |
| Product Authoring update idempotency fingerprint is reconstruction-sensitive | `ProductAuthoringOrchestrator.cs:80`, `ProductAuthoringOrchestrator.cs:260-268`, `ProductAuthoringReconstructionService.cs:47-69` | Concurrent changes to omitted units/variants can affect replay/conflict fingerprint semantics. |
| Product stock adjustment is product-adjacent but outside Product Authoring | `ProductStockModal.jsx:36-52`, `Products.jsx:180-185` | Separate stock ownership is probably correct, but it is still a product-scoped mutation surface. |
| Stale comment in Pixel Override | `ProductPixelOverrideTab.jsx:253-270` | Comment references old uncertainty about ProductForm and ProductVariantsTab behavior. |

### Future Improvements

| Improvement | Reason |
| --- | --- |
| Delete unreachable legacy bodies after `410` returns | Reduces audit surface and prevents future accidental resurrection. |
| Add explicit semantics for clearing all variants | Avoids ambiguity between omitted variants, empty variant submission, and intentional full deletion. |
| Decide whether Save Matrix should be renamed or removed | Makes ProductForm's single save ownership clearer to users. |
| Consider surfacing post-authoring stock as a separate explicit stock workflow | Avoids partial-success ambiguity when matrix save includes inventory edits. |
| Remove or split ProductForm modal-mode legacy code if no production caller remains | Simplifies ownership and lowers regression surface. |
| Standardize stale concurrency response semantics if desired | Consider 412 for failed `If-Match` while preserving existing client handling. |

## 11. Recommended Next Steps

1. Remove dead legacy backend bodies after the `410` returns in `ProductVariantsController` and `ProductsController`.
2. Define and implement explicit empty-variant-list semantics if Product Authoring must support clearing all variants.
3. Decide UX ownership for Save Matrix: keep as a draft handoff button, rename it, or remove it in favor of only Update Product.
4. Decide whether variant inventory edits should remain post-authoring or move to a dedicated stock workflow.
5. Remove stale comments and modal-mode code if no production runtime still needs them.

## Final Certification

**PRODUCT AUTHORING CERTIFIED WITH OBSERVATIONS**
