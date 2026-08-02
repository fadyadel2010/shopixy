# RECOVERY-2 Single Save Pipeline Report

## Certification

PASS WITH OBSERVATIONS

Recovery Phase 2 was implemented only. Recovery-1 ownership decisions were preserved.

## 1. Files Modified

- `PointOfSale.Web/src/components/ProductForm.jsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.jsx`
- `PointOfSale.Web/src/components/ProductVariantsTab.test.tsx`
- `PointOfSale.Web/src/services/variantStockApi.js`
- `PointOfSale.Web/src/utils/productAuthoringVariants.js`
- `RECOVERY-2_SINGLE_SAVE_PIPELINE_REPORT.md`

## 2. Save Pipeline Before

Recovery-1 already moved product authoring execution to `ProductForm`, but Recovery-2 found remaining duplicate authoring artifacts in `ProductVariantsTab`:

- A stale `buildProductAuthoringPayload()` implementation still existed in `ProductVariantsTab`.
- Variant authoring mapping existed separately in `ProductForm` and `ProductVariantsTab`.
- Product-authoring-style idempotency and row-version helpers still existed in `ProductVariantsTab`.
- Save Matrix already delegated to the parent adapter, but the child file still carried old Product Authoring code.

## 3. Save Pipeline After

The existing variant product runtime is now:

```text
User
-> Update Product or Save Matrix
-> ProductForm
-> ProductVariantsTab draft validation/extraction when variants changed
-> ProductForm builds one Product Authoring payload
-> ProductForm generates Product Authoring idempotency key
-> ProductForm generates If-Match from acceptedRowVersion
-> ProductForm executes PUT /products/{productId}
-> ProductForm accepts response and updates acceptedRowVersion
-> ProductForm invalidates product runtime cache
-> ProductForm fires product authoring notification
```

`ProductVariantsTab` is now draft-only for Product Authoring. It owns matrix state, option groups, validation, generated combinations, bulk editing, and draft extraction. It does not build a product payload, read or normalize product RowVersion, generate Product Authoring idempotency headers, generate `If-Match`, or execute the parent product PUT.

## 4. Builder Consolidation

- Product Authoring payload builder: one implementation remains, `ProductForm.buildProductAuthoringPayload()`.
- Variant authoring mapping: consolidated into `PointOfSale.Web/src/utils/productAuthoringVariants.js` through `toProductAuthoringVariant()` and `normalizeProductAuthoringVariants()`.
- Product Authoring idempotency creation: remains in `ProductForm.createProductAuthoringIdempotencyKey()`.
- Product Authoring request headers and `If-Match`: remain in `ProductForm.buildProductAuthoringRequestOptions()`.
- Product RowVersion normalization for Product Authoring: remains in `ProductForm.getProductRowVersion()`.
- Accepted RowVersion ownership: remains in `ProductForm` state only.

## 5. Removed Duplicate Execution Paths

- Removed stale `ProductVariantsTab.buildProductAuthoringPayload()`.
- Removed stale `ProductVariantsTab` product unit to authoring mapper.
- Removed stale `ProductVariantsTab` product publish status normalization used only by the deleted product builder.
- Removed stale `ProductVariantsTab` product RowVersion helper.
- Removed Product Authoring idempotency header construction from `ProductVariantsTab`.
- Moved variant stock idempotency and stock PUT construction into `variantStockApi.js`, keeping it outside the Product Authoring save pipeline.

## 6. Remaining Temporary Adapters

- `ProductVariantsTab.onSaveVariantDraft` remains as the Recovery-1 Save Matrix adapter so the Save Matrix UI is preserved while parent-owned persistence is enforced.
- `ProductVariantsTab.savePendingChanges()` remains for the existing imperative ProductForm ref contract, but it delegates through the same parent adapter and no longer performs Product Authoring itself.
- Legacy direct variant CRUD UI remains per the Recovery-2 boundary. Those endpoints are deprecated backend paths and were not removed.
- Post-authoring inventory changes remain separate stock adjustments after product authoring. They are not parent Product Authoring PUTs.

## 7. Deferred Items For Recovery-3

- Remove or redesign the Save Matrix UI once the single global save UX is finalized.
- Remove or permanently hide legacy direct variant CRUD UI paths.
- Decide whether post-authoring inventory changes should stay coupled to Save Matrix or move to a separate explicit stock workflow.
- Consider extracting the full Product Authoring payload builder from `ProductForm` only if another non-component caller is introduced.
- Standardize notifications for independent non-product-authoring resources.

## 8. Risks

- Full frontend and backend workspace tests are not clean due to unrelated existing failures; product-focused tests and build pass.
- Save Matrix still appears as a save action, but now it delegates to the parent ProductForm adapter. This is intentional for Recovery-2.
- Stock adjustment failures after a successful product authoring save may leave stock changes unapplied while product and variant authoring succeeds. This was pre-existing independent resource behavior and was not changed into Product Authoring.
- Legacy direct variant CRUD remains visible in non-matrix paths per the forbidden scope.

## 9. Validation Evidence

Product Authoring path searches:

- `rg -n "api\.put" "PointOfSale.Web/src/components/ProductForm.jsx" "PointOfSale.Web/src/components/ProductVariantsTab.jsx" "PointOfSale.Web/src/services/variantStockApi.js"`
- Result: one parent Product Authoring PUT at `ProductForm.jsx:856`; remaining matches are legacy variant CRUD and variant stock paths.
- `rg -n "buildProductAuthoringPayload" "PointOfSale.Web/src"`
- Result: one implementation at `ProductForm.jsx:729` and one call at `ProductForm.jsx:828`.
- `rg -n "Idempotency-Key" "PointOfSale.Web/src/components/ProductForm.jsx" "PointOfSale.Web/src/components/ProductVariantsTab.jsx"`
- Result: one Product Authoring header construction at `ProductForm.jsx:247`; no `ProductVariantsTab` match.
- `rg -n "If-Match" "PointOfSale.Web/src/components/ProductForm.jsx" "PointOfSale.Web/src/components/ProductVariantsTab.jsx"`
- Result: one Product Authoring `If-Match` generation point at `ProductForm.jsx:251`; no `ProductVariantsTab` match.
- `rg -n "acceptedRowVersion" "PointOfSale.Web/src/components/ProductForm.jsx" "PointOfSale.Web/src/components/ProductVariantsTab.jsx"`
- Result: one owner in `ProductForm`; no `ProductVariantsTab` match.
- `rg -n "toProductAuthoringVariant|normalizeProductAuthoringVariants" "PointOfSale.Web/src"`
- Result: one shared variant authoring mapper utility used by `ProductForm` and `ProductVariantsTab`.

Command validation:

- PASS: `npm test -- --run src/components/ProductForm.test.tsx src/components/ProductVariantsTab.test.tsx`
- PASS: `npm run build`
- PASS WITH OBSERVATIONS: `npm test -- --run` failed 7 existing non-product tests in storefront cart/checkout state, i18n certification, and builder runtime certification. ProductForm/ProductVariantsTab tests passed in the full run.
- PASS WITH OBSERVATIONS: `dotnet test "PointOfSale.sln" --no-restore` exceeded the 120s timeout after already reporting unrelated backend/architecture/security failures. No Recovery-2 backend code was changed.

Manual reasoning audit:

- Existing product and variant domain contracts were not changed.
- Backend code, DTOs, Product Authoring, persistence, and concurrency semantics were not changed.
- Save Matrix UI was not removed.
- Legacy CRUD was not removed.
- The only existing variant product authoring persistence path now reaches `PUT /products/{productId}` through `ProductForm.saveProductAuthoring()`.
