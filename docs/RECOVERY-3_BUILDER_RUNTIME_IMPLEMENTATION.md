# Recovery 3 — Builder Runtime Implementation

## Certification Result

**PASS WITH OBSERVATIONS**

Builder Recovery is complete. Every Builder/Runtime violation V1 through V10 documented by `BUILDER_RUNTIME_CERTIFICATION.md` has been recovered. Builder is now an authoring system: it declares supplemental structure and presentation; Runtime injects and executes mandatory Product Details behavior; Theme presents the runtime result.

This recovery moved ownership only. It did not redesign Storefront, Runtime, Themes, Builder UX, Product Authoring, Projection, Catalog, Search, or Theme Runtime. StoreConfig APIs, Builder APIs, DTOs, Theme contracts, and Runtime contracts remain compatible.

Cache Cleanup and Final Storefront Certification were not started.

## Canonical Baseline

`BUILDER_RUNTIME_CERTIFICATION.md` was read completely before Builder code changes and was treated as the canonical source of truth. Its findings were recovered without reopening the audit or redesigning the certified architecture.

The completed Product Authoring, Projection, Catalog Ownership, Runtime Ownership, and Theme Runtime recoveries remain frozen.

## Recovered Violations

| Violation | Architecture before | Recovery | Result |
| --- | --- | --- | --- |
| V1 — Product Hero ownership | Builder could omit, disable, duplicate, remove, or replace Product Hero. | Backend admission requires exactly one enabled, first-position Product Hero. Builder state excludes it and renders a locked Runtime card. The Product Template Resolver removes all authored heroes and injects exactly one enabled Runtime hero at order zero. | Recovered |
| V2 — Customer review truth | Builder authored customer reviews, ratings, and review-shaped content. | Removed `customer-reviews` from the authority and renderer set. Builder exposes testimonials/marketing quotes without rating truth. Legacy drafts are read through a one-way compatibility mapper that discards ratings and produces testimonials. | Recovered |
| V3 — Weak semantic admission | Admission primarily checked shallow field types and allowed malformed nested lists, URLs, media values, and inconsistent objects. | Core, Web, and Storefront validators now enforce exact fields, nested object shape, list size, GUID validity/uniqueness, string lengths, enums, numeric ranges, colors, and safe image/video/URL semantics. | Recovered |
| V4 — Product Details capability suppression | Builder settings could decide gallery, SKU, purchase, variant, stock, media, and video behavior. | Product Details contracts retain placement/presentation fields only. Runtime always enables Product Hero capabilities and owns product-video playback. Builder cannot persist capability suppression. | Recovered |
| V5 — Dormant Product/Category Builder modes | Product and Category modes, dashboard/sidebar entries, routing, demo paths, and preview surfaces existed without a live Runtime consumer. | Removed Product/Category modes, navigation entries, routes, publish/preview branches, demo cases, and tests. Only Home and Product Template authoring remain. | Recovered |
| V6 — Contract/runtime drift | Fields had duplicate names or no effective consumer, including `maxItems`/`limit`, ignored visibility flags, and obsolete settings. | The canonical authority now has 15 unique section types with one closed field set per type. Runtime consumers use the canonical meanings; obsolete aliases and visibility flags are compatibility-input only and never Builder output. Architecture tests maintain an exact authority-to-consumer map. | Recovered |
| V7 — Broad native preview override | Native preview could install an entire mutable StoreConfig/theme override from Builder. | Removed `ThemeNativeCanvas`, its feature flag, exports, and certification path. Builder preview now uses the scoped Builder canvas/runtime boundary. | Recovered |
| V8 — Template deletion lifecycle bypass | Product-template deletion mutated published configuration through an undifferentiated update path. | Deletion now runs under the publish lock, snapshots the transition, persists a `Published` action, emits the normal events/cache invalidation, and records the publication audit while preserving the existing DELETE API and UX. | Recovered |
| V9 — Invalid native preview targeting | Non-home preview targeting could bind Product/Category modes to unrelated page configuration. | Removed native preview and dead modes. Product Template preview is keyed only by the selected template ID; Home remains keyed to Home. | Recovered |
| V10 — Preview context leakage | Preview provider cleanup did not restore the previous StoreConfig and tenant context. | Builder preview captures and restores both prior StoreConfig and prior tenant, clearing either value when no prior context existed. | Recovered |

## Architecture Before

| Concern | Previous path | Ownership defect |
| --- | --- | --- |
| Product Details | Builder sections/settings → Product Template Resolver → Theme renderer | Builder could change whether Runtime capabilities existed. |
| Product Hero | Mutable Builder section | A mandatory Runtime container was optional and replaceable. |
| Reviews | Builder-authored review objects → StoreConfig → storefront section | Builder owned business truth and ratings. |
| Contracts | Multiple permissive registries and runtime fallbacks | Settings could be ignored, duplicated, or interpreted differently. |
| Product/Category modes | Builder UI/routing/preview/publish branches → no live Runtime consumer | Dormant authoring surfaces accepted work that Runtime never executed. |
| Preview | Builder → native whole-config/theme override | Preview could replace broader Runtime context and leak it after cleanup. |
| Template deletion | DELETE → direct config mutation | A published Runtime change bypassed explicit publication semantics. |

## Architecture After

Product Details now follows one ownership direction:

`Runtime Product Template Resolver → exactly one mandatory Product Hero → Builder supplemental declarations → Runtime section containers → Theme presentation`

- Runtime injects Product Hero at order zero on every resolved Product Template.
- Resolver output contains exactly one enabled Product Hero even when saved input is missing, disabled, or duplicated.
- Builder holds only supplemental sections. Its Product Hero card is a locked Runtime indicator with no remove, disable, duplicate, replace, reorder, or edit actions.
- Backend admission rejects missing, disabled, duplicate, or non-leading Product Hero definitions.
- Runtime owns gallery existence, SKU, pricing, purchase execution, variant selection, stock, availability, media truth, and product-video behavior.
- Builder may declare placement, ordering, layout variant, visual skin, spacing, headings, and supplemental editorial/marketing content.
- Theme continues to receive Runtime-prepared Product Details models and presents them without taking Runtime ownership.

## Ownership Changes

| Concern | Builder owns | Runtime owns | Theme owns |
| --- | --- | --- | --- |
| Product Hero | No lifecycle or capability ownership; a locked status representation only | Mandatory injection, uniqueness, enabled state, order, product/media/price/stock/variant/purchase behavior | Product Hero markup, layout, styling, responsive presentation |
| Supplemental Product sections | Placement, ordering, layout/skin, spacing, titles | Data/media resolution and executable behavior | Presentation |
| Testimonials | Marketing names, avatars, and quote text | Admission and safe materialization | Presentation |
| Customer reviews/ratings | Nothing | Future review truth/runtime, if introduced | Presentation only through a Runtime contract |
| Section contracts | Values for admitted canonical fields | Closed contract validation and one consumer per field | Visual effect of presentation fields |
| Preview | Scoped authoring state | Tenant/StoreConfig context and cleanup semantics | The selected presentation implementation |
| Product-template deletion | User intent through the preserved API | Serialized publication lifecycle, snapshot, event/cache, audit | Nothing |

## Canonical Builder Contracts

The authority contains 15 section contracts and 15 unique section type IDs. Product Details authoring is restricted to:

| Section | Builder fields |
| --- | --- |
| `product-hero` | `layoutVariant` |
| `product-gallery` | `layoutVariant`, `title` |
| `product-specs` | `layoutVariant`, `title` |
| `product-video` | `title` |
| `related-products` | `layoutVariant`, `title`, `limit` |

`customer-reviews`, ratings, verified-review fields, purchase history, review statistics, and Product Details capability flags are not admitted Builder contracts.

## Files Modified by Builder Recovery

The worktree already contained the frozen Runtime and Theme recoveries plus unrelated changes. This inventory is the Builder Recovery delta only.

### Canonical authority and admission

- `theme-authority/shopixy-theme-authority.json`
- `PointOfSale.Core/Constants/StorefrontSectionContracts.cs`
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs`

### Web Builder authoring boundary

- `PointOfSale.Web/src/bootstrap/index.ts`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/pages/StorefrontDashboard.jsx`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/store/builderStore.js`
- `PointOfSale.Web/src/modules/storefront/builder/pageSectionAvailability.js`
- `PointOfSale.Web/src/modules/storefront/builder/sectionContractRegistry.js`
- `PointOfSale.Web/src/modules/storefront/builder/sectionRegistry.js`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderCanvas.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderThemeProvider.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/index.js`
- `PointOfSale.Web/src/modules/storefront/components/builder/normalizeStoreConfig.js`
- `PointOfSale.Web/src/modules/storefront/config/demoSections.js`
- `PointOfSale.Web/src/storefront/products/ProductTemplateRegistry.ts`
- Deleted `PointOfSale.Web/src/modules/storefront/components/builder/ThemeNativeCanvas.jsx`.
- Deleted `PointOfSale.Web/src/modules/storefront/components/builder/themeNativeCanvasFlag.js`.
- Deleted `PointOfSale.Web/src/modules/storefront/components/builder/themeNativeCanvas.certification.test.ts`.
- Deleted the unused local preview renderer directory `PointOfSale.Web/src/modules/storefront/components/sections/`.

### Storefront Runtime and presentation consumers

- `PointOfSale.Storefront/src/storefront/products/ProductTemplateRegistry.ts`
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts`
- `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`
- `PointOfSale.Storefront/src/storefront/runtime/sectionContract.ts`
- `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/featured-products.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/image-block.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/related-products.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/subcategory-grid.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/testimonials.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/text-block.tsx`
- `PointOfSale.Storefront/src/components/sections/defaults/video-block.tsx`
- Deleted `PointOfSale.Storefront/src/components/sections/defaults/customer-reviews.tsx`.

### Regression protection

- Added `PointOfSale.Tests/Architecture/BuilderRuntimeRecoveryTests.cs`.
- `PointOfSale.Tests/Architecture/StoreBuilderRuntimeContractCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StoreBuilderLayoutAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StoreBuilderSectionContentAuthorityCertificationTests.cs`
- `PointOfSale.Tests/Architecture/StoreBuilderEndToEndAuthorityCertificationTests.cs`
- Added `PointOfSale.Web/src/modules/storefront/store/builderRuntimeOwnership.test.ts`.
- `PointOfSale.Web/src/modules/storefront/builder/builderRuntimeContract.certification.test.ts`
- `PointOfSale.Web/src/modules/storefront/builderWorkspaceAuthority.test.ts`
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.test.ts`
- `PointOfSale.Storefront/src/storefront/runtime/sectionContract.certification.test.tsx`
- `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md`

## Regression Protection

The architecture gates fail if a future change:

- permits a Product Template without exactly one enabled, leading Product Hero;
- exposes Product Hero add/remove/disable/duplicate/reorder/update operations in Builder;
- lets saved Product Hero sections replace the Runtime-injected section;
- adds customer reviews, ratings, or other business-truth fields to Builder authority;
- maps Product Details capability flags into Builder contracts;
- restores Product or Category Builder modes, routes, sidebar/dashboard entries, publish branches, or preview branches;
- restores native whole-config/theme preview replacement;
- fails to restore preview StoreConfig and tenant context;
- bypasses the product-template publication lifecycle on deletion;
- introduces an unknown/dead field, duplicate contract meaning, or a field without its declared Runtime consumer;
- weakens nested-list, GUID, URL, image, video, color, enum, number, or string admission.

The Runtime resolver tests independently prove missing, disabled, and duplicate authored heroes all resolve to one enabled Runtime Product Hero.

## Validation

| Validation | Result |
| --- | --- |
| Storefront production build: `npm run build` | PASS — TypeScript and Vite completed. |
| Web production build: `npm run build` | PASS — 5,065 modules transformed. |
| .NET solution build: `dotnet build PointOfSale.sln --no-restore --nologo --verbosity:minimal -m:1` | PASS — 0 errors; 199 existing solution-wide analyzer/obsolescence warnings. |
| Relevant .NET Builder architecture tests: `FullyQualifiedName~StoreBuilder\|FullyQualifiedName~BuilderRuntimeRecoveryTests` | PASS — 44/44. |
| Full Storefront tests: `npm test -- --run --reporter=dot` | PASS — 24 files, 223 tests. |
| Web Storefront/Builder tests: `npm test -- --run src/modules/storefront --reporter=dot` | PASS — 10 files, 52 tests. |
| Diff whitespace validation: `git diff --check` | PASS. |

The frontend tools reported non-blocking existing warnings: Vite/Browserslist maintenance notices, bundle-size notices, React Router future flags, and one existing React test `act(...)` warning. No build or test failed.

## Repository Search Proof

Machine-readable authority inspection produced:

```text
contractCount=15
uniqueTypeCount=15
productHeroFields=layoutVariant
forbiddenBusinessTypes=0
```

Product Details contract inspection produced:

```text
product-hero=layoutVariant
product-gallery=layoutVariant,title
product-specs=layoutVariant,title
product-video=title
related-products=layoutVariant,title,limit
```

Source searches and the closed-map architecture tests confirm:

- Product Template Resolver filters every saved `product-hero` and injects one enabled Runtime-owned hero at order zero.
- Runtime hard-codes Product Hero description, gallery, variant selector, purchase, SKU, and stock capabilities as available; Builder settings cannot suppress them.
- Builder's only valid page keys are `home` and `product-template`.
- no Product Builder or Category Builder route, dashboard/sidebar entry, publish branch, or preview branch remains;
- no native preview canvas/flag/export remains;
- no active Builder authority contains customer-review or rating truth;
- the 15 canonical contract IDs are unique;
- every canonical field is represented once in the architecture consumer map, with no unknown, duplicate, or unmapped Builder property;
- compatibility occurrences of `customer-reviews` and obsolete capability names exist only in the one-way draft migration mapper, defensive Runtime filtering, and regression assertions.

## Observations

1. Legacy persisted drafts remain readable. The compatibility mapper converts legacy customer reviews to testimonials, removes rating values, strips obsolete Product Details capability fields, and canonicalizes legacy aliases. It does not re-admit those fields for new Builder writes.
2. Product/Category properties in the public StoreConfig DTO shape were retained to honor the explicit API/DTO compatibility requirement. They have no Builder mode, route, preview, publish path, or Product Details Runtime consumer.
3. The existing DELETE endpoint remains available. Its internal state transition is now an explicit serialized publication operation so compatibility is preserved without retaining the lifecycle violation.
4. Solution and frontend maintenance warnings listed under Validation are outside Builder ownership and do not affect certification.

## Final Certification

All source-backed Builder/Runtime violations V1 through V10 are recovered.

Builder declares supplemental content, placement, ordering, layout, spacing, and visual settings. Runtime injects exactly one mandatory Product Hero and owns every Product Details capability and all business truth. Theme presents the Runtime result. Dead Builder modes and native preview replacement paths are removed, contracts are closed and semantically admitted, and CI-facing architecture tests prevent the documented violations from returning.

**PASS WITH OBSERVATIONS**
