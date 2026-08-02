# Recovery Post Change Detection Audit

## Certification

Product PUT Executed Without Effective Changes

No fixes were implemented. No additional legacy code was removed. This audit covers only the current ProductForm save decision and its immediate variant draft input.

## 1. ProductForm PUT Conditions

`ProductForm` executes Product Authoring save when its HTML form is submitted. The workspace Update Product button submits that form through `form={PRODUCT_WORKSPACE_FORM_ID}` in `ProductWorkspacePage`, and `ProductForm` handles the submit at `PointOfSale.Web/src/components/ProductForm.jsx:912`.

The exact current conditions are:

| Condition | Source | Effect |
| --- | --- | --- |
| Submit event fires | `ProductForm.jsx:912-914` | Starts `handleSubmit()` and sets loading. |
| If existing variant product and variant tab reports pending changes | `ProductForm.jsx:921` | Calls `prepareProductAuthoringDraft()` and sets `includeVariants = true` when draft is returned. |
| If variant draft validation fails | `ProductForm.jsx:922-930` | Returns without Product PUT. |
| Otherwise, always calls Product Authoring save | `ProductForm.jsx:935-938` | Calls `saveProductAuthoring()` even when no Product field changed and no variant draft changed. |
| Category missing | `ProductForm.jsx:811-817` | Blocks save before payload construction. |
| Product authoring validation fails | `ProductForm.jsx:819-825` | Blocks save before PUT. |
| `productId` exists | `ProductForm.jsx:855-856` | Executes `api.put(`/products/${productId}`, productData, requestOptions)`. |
| `productId` does not exist | `ProductForm.jsx:867-868` | Executes `api.post('/products', productData, requestOptions)`. |

The line that causes save execution without any Product dirty check is `PointOfSale.Web/src/components/ProductForm.jsx:935`. The network PUT line is `PointOfSale.Web/src/components/ProductForm.jsx:856`.

## 2. Original Draft Comparison

`ProductForm` does not keep an original loaded Product DTO or a canonical normalized baseline for comparison.

`fetchProduct()` immediately maps the loaded DTO into editable state at `ProductForm.jsx:536-582`. It stores `acceptedRowVersion`, `hasVariants`, `productUnits`, and `formData`, but there is no `originalProduct`, `baselinePayload`, `dirtyFields`, or equivalent Product Authoring comparison state.

The only fingerprint in `ProductForm` is `saveFingerprint` at `ProductForm.jsx:842-850`. It is used to reuse an idempotency key for retrying the same attempted save. It is not compared against the loaded product and does not suppress PUT.

## 3. Canonical Dirty-State Detection

There is no canonical ProductForm dirty-state detection for Product Authoring.

`ProductForm` exposes `hasPendingChanges()` through its imperative ref, but that only delegates to variant pending changes. Evidence: `ProductForm.jsx:414-416` and `ProductForm.jsx:431-434`.

Product field edits, Product Units edits, simple SKU/barcode edits, publication edits, SEO edits, marketing edits, and AI-applied edits are not compared to an original Product Authoring baseline before submit.

`ProductVariantsTab` has local matrix dirty detection at `ProductVariantsTab.jsx:702-708`, but that is scoped to variant matrix state and is not a ProductForm-level Product Authoring dirty contract.

## 4. No-Change Update Product Behavior

Yes. Clicking Update Product with no effective Product change can still execute `PUT /api/products/{id}`.

Path:

```text
User clicks Update Product
-> ProductWorkspacePage submit button submits ProductForm
-> ProductForm.handleSubmit()
-> no product dirty comparison
-> saveProductAuthoring()
-> productId truthy
-> api.put(`/products/${productId}`, productData, requestOptions)
```

Source evidence: `ProductForm.jsx:912-938` and `ProductForm.jsx:855-856`.

This means a stale `acceptedRowVersion` can produce a `409 Conflict` even if the user made no semantic change in the current UI session.

## 5. Variant Draft Generation And Dirty State

Variant draft generation by itself does not mark ProductForm dirty because ProductForm has no Product dirty state.

`ProductVariantsTab` recomputes `authoringVariants` from `matrixRows` and calls `onVariantDraftChange` at `ProductVariantsTab.jsx:710-729`. ProductForm stores that as `variantAuthoringSummary` at `ProductForm.jsx:750-752`, mainly for publication readiness and payload building when variants are included.

Global ProductForm submit only includes variants when `variantsTabRef.current.hasPendingChanges()` is true. Evidence: `ProductForm.jsx:921-938`.

However, `ProductVariantsTab` dirty detection can mark the variant matrix dirty without a semantic Product change because:

| Source | Evidence | False-positive possibility |
| --- | --- | --- |
| Any row draft key counts dirty | `ProductVariantsTab.jsx:702-704` | Entering a value equal to the original still creates a row draft. There is no row-vs-original semantic comparison. |
| Option group signature is string/order based | `ProductVariantsTab.jsx:93-103` and `ProductVariantsTab.jsx:705` | Reordering groups or values can mark dirty even if the semantic attribute set is the same. |
| Exclusion rules are string/order based | `ProductVariantsTab.jsx:102-111` and `ProductVariantsTab.jsx:706` | Equivalent rule sets in different order can mark dirty. |
| Default comparison is key based | `ProductVariantsTab.jsx:707` | Equivalent default identity should compare correctly when keys are stable, but key derivation depends on normalized attributes. |

Clicking Generate Variants does not itself appear in `hasUnsavedChanges`; it sets `confirmedGenerationSignature`, while dirty detection checks row drafts, deleted keys, option group signatures, exclusion signatures, and default key. Evidence: `ProductVariantsTab.jsx:570-585`, `ProductVariantsTab.jsx:878-899`, and `ProductVariantsTab.jsx:702-708`.

## 6. Normalization, Ordering, And Formatting Risks

ProductForm payload construction performs normalization but does not use normalization for change detection.

Potential false-difference sources:

| Area | Source | Notes |
| --- | --- | --- |
| Product fields | `ProductForm.jsx:547-582` then `ProductForm.jsx:729-748` | Null strings become empty strings; numeric DTO values become string state then numbers; missing booleans get defaults. These can differ textually from the DTO without a semantic change. |
| Product Units | `ProductForm.jsx:169-187` and `ProductForm.jsx:661-671` | Unit values are normalized into draft objects and then payload objects. Ordering follows loaded array/user order; no baseline comparison exists. |
| Variant attributes | `PointOfSale.Web/src/utils/productAuthoringVariants.js:5-23` | Attribute arrays become objects. Object key order can differ from input order; attribute order should be ignored unless backend treats object order semantically. |
| Variant rows | `ProductVariantsTab.jsx:604-661` and `ProductVariantsTab.jsx:710-715` | Rows are generated from option groups and unmatched existing variants. Row ordering affects `sortOrder`, which may be semantically relevant for display but can also change through order-only edits. |
| Product Authoring idempotency fingerprint | `ProductForm.jsx:842-850` | `JSON.stringify` is used only to reuse an idempotency key. It can be affected by object/array order, but it does not decide whether to save. |

Generated payload can differ only by formatting/null/default values. Examples include `nameAr: null -> ''`, `slug: null -> ''`, `weight: null -> null` via `''`, `productTemplate: null -> 'default'` when visible, `images: null` being added, and simple numeric values being parsed from string state back to numbers. These differences do not prevent PUT because no semantic diff gate exists.

## 7. Field-By-Field Loaded DTO To Payload Comparison

This comparison describes the no-user-change path and highlights only semantic changes. Formatting, ordering, default normalization, and null-vs-omitted equivalence are ignored as requested.

| Loaded Product DTO field | ProductForm state mapping | Final Product Authoring payload | Semantic change on no edit? | Notes |
| --- | --- | --- | --- | --- |
| `id` | Not placed in form state | Not in body; id is route/path | No | Endpoint path carries identity. |
| `rowVersion` / `RowVersion` | `acceptedRowVersion` | `If-Match` header | No | Concurrency token only. |
| `name` | `product.name` | `name` | No | Unless user edits. |
| `nameAr` | `product.nameAr || ''` | `nameAr` | No | Null-to-empty ignored. |
| `categoryId` | `product.categoryId || ''` | `categoryId` | No | Missing category blocks save. |
| `description` | `product.description || ''` | `description` | No | Null-to-empty ignored. |
| `descriptionAr` | `product.descriptionAr || ''` | `descriptionAr` | No | Null-to-empty ignored. |
| `taxRate` | `product.taxRate.toString()` | `parseFloat(formData.taxRate) || 0` | No | Numeric formatting ignored. |
| `priceIncludesTax` | `product.priceIncludesTax` | `priceIncludesTax` | No | Direct boolean. |
| `trackStock` | `product.trackStock` | `trackStock` | No | Direct boolean. |
| `isActive` | `product.isActive` | `isActive` | No | Direct boolean. |
| `slug` | `product.slug || ''` | `slug` | No | Null-to-empty ignored. |
| `metaTitle` | `product.metaTitle || ''` | `metaTitle` | No | Null-to-empty ignored. |
| `metaDescription` | `product.metaDescription || ''` | `metaDescription` | No | Null-to-empty ignored. |
| `metaKeywords` | `product.metaKeywords || ''` | `metaKeywords` | No | Null-to-empty ignored. |
| `weight` | `product.weight?.toString() || ''` | number or `null` | No | Numeric/null normalization ignored. |
| `length` | `product.length?.toString() || ''` | number or `null` | No | Numeric/null normalization ignored. |
| `width` | `product.width?.toString() || ''` | number or `null` | No | Numeric/null normalization ignored. |
| `height` | `product.height?.toString() || ''` | number or `null` | No | Numeric/null normalization ignored. |
| `weightUnit` | `product.weightUnit || 'kg'` | `weightUnit` | No unless backend stored distinct blank/null semantics | Default normalization ignored. |
| `dimensionUnit` | `product.dimensionUnit || 'cm'` | `dimensionUnit` | No unless backend stored distinct blank/null semantics | Default normalization ignored. |
| `publishStatus` | `normalizePublishStatus(product.publishStatus)` | normalized number | No | Draft/published/archive coercion ignored. |
| `isOnlineVisible` | `product.isOnlineVisible ?? true` | `isOnlineVisible` | No unless backend stored null as semantic false/unknown | Default normalization ignored. |
| `isSearchable` | `product.isSearchable ?? true` | `isSearchable` | No unless backend stored null as semantic false/unknown | Default normalization ignored. |
| `isFeatured` | `product.isFeatured ?? false` | `isFeatured` | No | Default normalization ignored. |
| `lowStockThreshold` | `product.lowStockThreshold?.toString() || ''` | integer or `null` | No | Numeric/null normalization ignored. |
| `allowBackorder` | `product.allowBackorder ?? false` | `allowBackorder` | No | Default normalization ignored. |
| `brand` | `product.brand || ''` | `brand` | No | Null-to-empty ignored. |
| `manufacturer` | `product.manufacturer || ''` | `manufacturer` | No | Null-to-empty ignored. |
| `tags` | `product.tags || ''` | `tags` | No | Null-to-empty ignored. |
| `videoUrl` | `product.videoUrl || ''` | `videoUrl` | No | Null-to-empty ignored. |
| `productTemplate` | `product.productTemplate || 'default'` | `default`/selected value if online visible, otherwise `null` | No unless template defaulting is semantically different | Default normalization ignored. |
| `featuresJson` / `features` | parsed list | `features` array | No if parsed list equals source semantics | Formatting/order ignored where equivalent. |
| `faqJson` / `faqs` | parsed FAQ list | `faqs` array | No if parsed list equals source semantics | Formatting/order ignored where equivalent. |
| `aiGenerated` | `Boolean(product.aiGenerated)` | `aiGenerated` | No | Boolean normalization ignored. |
| `aiMetadataJson` / `aiMetadata` | parsed object or `null` | `aiMetadata` | No if parsed object equals source semantics | Object formatting/order ignored. |
| `hasVariants` / `variants.length` | `!!product.hasVariants || variantCount > 1` | `hasVariants` | No for stable existing product type | Derivation can differ from raw DTO if DTO flag is false but multiple variants exist; that is semantic product type correction. |
| Product DTO `sku`/`barcode` | Not directly used for simple product; simple identity loaded from default variant | `sku`/`barcode` from `simpleProductData` for simple products; `null` for variant products | No if default variant identity equals canonical simple identity | Variant product nulls are canonical. |
| `units` | `normalizeProductUnitDrafts(product.units)` | `productUnits` for simple products; `null` for variant products | No if units unchanged | Direct unit mutation no longer used. |
| `variants` from Product DTO | Not stored as ProductForm source of truth | Existing variant product sends `variants: null` unless variant tab pending; create variant product sends normalized draft/empty list | No when no variant draft pending | Backend reconstructs omitted variants on update. |
| Product media fields/images | Not loaded into ProductForm authoring state | `images: null` | No under current independent media resource model | Media is outside Product Authoring runtime. |

Actual semantic changes are only user edits or variant matrix dirty changes that alter product fields, units, variant rows, default variant identity, product type, publication fields, or authored variant values. ProductForm does not compute this semantic set before PUT.

## 8. Answers To Requested Questions

1. ProductForm executes Product PUT when the form is submitted, validation passes, and `productId` is truthy. It does not require Product dirty state.

2. ProductForm does not compare current draft against the original loaded Product.

3. There is no canonical Product Authoring dirty-state detection in ProductForm.

4. Yes. Clicking Update Product with no effective Product change can still execute PUT.

5. Variant draft generation does not mark ProductForm dirty because ProductForm has no dirty state. Variant matrix dirty detection can still false-positive through row drafts or order-sensitive signatures.

6. Yes. Option group/exclusion signatures and variant attribute/object normalization can produce order-sensitive differences. ProductForm's idempotency fingerprint is also order-sensitive but does not control save execution.

7. Yes. Payload can differ only by formatting/null/default normalization, and Product PUT still executes.

8. Field-by-field comparison is provided in section 7.

9. Exact source lines responsible: `ProductForm.jsx:935-938` unconditionally invokes `saveProductAuthoring()` after validation, and `ProductForm.jsx:855-856` executes PUT whenever `productId` is present.

## 9. Audit Searches

| Search | Result |
| --- | --- |
| `rg -n "dirty|hasUnsavedChanges|hasPendingChanges|original|snapshot|JSON.stringify|fingerprint|saveFingerprint|prepareProductAuthoringDraft|saveProductAuthoring|handleSubmit" PointOfSale.Web/src/components` | ProductForm has submit/save/idempotency fingerprint and variant pending check only; ProductVariantsTab has matrix dirty detection only. |
| `rg -n "ProductForm.jsx"` through direct reads | `fetchProduct`, `buildProductAuthoringPayload`, `saveProductAuthoring`, and `handleSubmit` confirm no loaded DTO baseline comparison. |
| `rg -n "rowVersion|RowVersion|getProductRowVersion|If-Match|Expected-Version|Idempotency-Key" PointOfSale.Web/src/components --glob "*ProductVariantsTab.jsx"` | No ProductVariantsTab Product Authoring concurrency/idempotency ownership remains. |

## 10. Final Finding

The current runtime has a single Product Authoring pipeline, but the pipeline is submit-driven, not change-driven. ProductForm can send a Product PUT with only normalized/no-op payload differences, or no effective differences at all. Therefore a 409 can occur on a no-op Update Product click if `acceptedRowVersion` is stale.
