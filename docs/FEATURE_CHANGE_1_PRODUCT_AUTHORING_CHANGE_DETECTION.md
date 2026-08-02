# FEATURE-CHANGE-1 Product Authoring Change Detection

## Certification

PASS

Product Authoring update now skips `PUT /products/{id}` when the current canonical Product Authoring payload has no effective semantic difference from the loaded baseline.

## 1. Baseline Architecture

- `ProductForm` remains the owner of Product Authoring save execution.
- One baseline snapshot is stored in `productAuthoringBaselineRef`.
- The baseline is created after product load completes, including default simple variant identity when applicable.
- The baseline is built through `buildProductAuthoringPayloadFromDraft()`, the same canonical Product Authoring payload builder used by save.
- After a successful Product Authoring save, the same baseline ref is advanced to the saved canonical payload.
- For existing variant products where the current save omits variants, omitted `variants: null` is treated as backend reconstruction equivalence to the baseline variants.

## 2. Semantic Comparison Algorithm

1. Product loads into ProductForm.
2. ProductForm builds the loaded Product Authoring payload with `buildProductAuthoringPayloadFromDraft()`.
3. ProductForm canonicalizes that payload with `createCanonicalProductAuthoringSnapshot()`.
4. On save, ProductForm performs existing validation first.
5. ProductForm builds the current Product Authoring payload.
6. ProductForm compares baseline to current with `hasSemanticProductAuthoringChanges()`.
7. If unchanged, ProductForm shows `No changes to save.` and returns before idempotency key creation, request options, cache invalidation, RowVersion mutation, or Product PUT.
8. If changed, the existing Product Authoring save pipeline continues unchanged.

## 3. Normalization Rules

- `null`, `undefined`, blank strings, empty objects, and empty arrays are canonical empty values.
- Strings are trimmed and internal whitespace is collapsed.
- Numbers are compared as finite numeric values, not formatted strings.
- Object keys are sorted before comparison.
- Arrays are canonicalized item-by-item and sorted by stable canonical JSON.
- Variant attributes are compared after existing Product Authoring variant normalization.
- Product Unit ordering is ignored.
- Variant ordering-only differences are ignored.
- Feature, FAQ, metadata, and other equivalent array/object ordering differences are ignored.
- Existing variant-product `variants: null` on update is equivalent to the loaded baseline variants because the backend reconstructs omitted variants.

## 4. Ignored Fields

- `rowVersion`
- `RowVersion`
- `expectedVersion`
- `idempotency`
- `idempotencyKey`
- `localId`
- `reactKey`
- `key`
- `sortOrder`
- UI state, loading state, toast state, focus state, expanded state, selection state, preview state, and generated client-only keys are not present in the compared Product Authoring payload.

## 5. Compared Fields

The comparer evaluates the canonical Product Authoring body produced by `ProductForm`, including:

- Product content: name, Arabic name, descriptions, brand, manufacturer, tags, video URL.
- Classification and type: category, product type, `hasVariants`.
- Tax and sales flags: tax rate, price-includes-tax, stock tracking, active flag.
- Publication and storefront fields: publish status, online visibility, searchable, featured, product template.
- SEO fields: slug, meta title, meta description, meta keywords.
- Physical fields: weight, dimensions, units, low stock threshold, backorder flag.
- Structured content: features, FAQs, AI generated marker, AI metadata.
- Simple identity: SKU and barcode for simple products.
- Product Units for simple products.
- Variant authoring payload when variants are included.

## 6. Files Modified

- `PointOfSale.Web/src/components/ProductForm.jsx`
- `PointOfSale.Web/src/components/ProductForm.test.tsx`
- `FEATURE_CHANGE_1_PRODUCT_AUTHORING_CHANGE_DETECTION.md`

No Product Authoring backend code was modified.

## 7. Regression Tests

Implemented in `PointOfSale.Web/src/components/ProductForm.test.tsx`:

- Case 1: Open product, immediately update, no PUT executed.
- Case 2: Change name, PUT executed.
- Case 3: Change variant price, PUT executed.
- Case 4: Change Product Units, PUT executed.
- Case 5: Whitespace-only differences, no PUT.
- Case 6: Null versus empty normalization, no PUT.
- Case 7: Equivalent ordering-only variant draft, no PUT.

## 8. Validation Evidence

- PASS: `npm test -- --run src/components/ProductForm.test.tsx`
- Result: 1 test file passed, 16 tests passed.
- PASS: `npm test -- --run src/components/ProductForm.test.tsx src/components/ProductVariantsTab.test.tsx src/pages/ProductWorkspacePage.test.tsx`
- Result: 3 test files passed, 30 tests passed.
- PASS: `npm run build`
- Result: Vite production build completed successfully.

Source evidence:

- Baseline creation: `PointOfSale.Web/src/components/ProductForm.jsx:747`.
- Single comparer: `PointOfSale.Web/src/components/ProductForm.jsx:398`.
- Single comparison point: `PointOfSale.Web/src/components/ProductForm.jsx:1002`.
- Idempotency key creation remains after the no-change return: `PointOfSale.Web/src/components/ProductForm.jsx:1024-1032`.
- Product PUT remains after the no-change return: `PointOfSale.Web/src/components/ProductForm.jsx:1038`.

## 9. Risk Assessment

- Low: Change detection is frontend-only and does not alter backend authoring, concurrency, DTOs, or persistence.
- Low: Existing validation still runs before semantic comparison.
- Low: Existing changed-save flow still creates idempotency keys, sends `If-Match`, updates RowVersion, invalidates cache, and calls `onSave()`.
- Observation: The worktree contains unrelated pre-existing modified/untracked files outside this feature scope. They were not reverted or edited for this change.
