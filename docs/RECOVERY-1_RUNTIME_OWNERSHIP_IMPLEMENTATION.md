# Phase 1 Runtime Ownership Recovery Implementation

**Repository:** PointOfSale  
**Date:** 2026-08-02  
**Canonical baseline:** `RUNTIME_OWNERSHIP_CERTIFICATION.md`  
**Scope:** Runtime ownership recovery only

## Executive Summary

Phase 1 recovered the executable ownership conflicts identified by the Runtime Ownership Certification without redesigning Storefront, Themes, Builder, Product Authoring, Projection, Search, or Catalog.

The recovery makes the following runtime boundaries exclusive:

| Runtime | Exclusive owner after recovery |
|---|---|
| Catalog page, filters, sorting, pagination | `ProductListPage` and Catalog Runtime |
| Product Details execution | Product Details Runtime through `ProductTemplateResolver`, canonical sections, and `RuntimeSectionContainer` |
| Public category identity and descendants | `StorefrontCategoryService` using SQL `CategoryLineageAuthority` |
| Category page | `CategoryPage` |
| Search entry, shortcut, modal state, and enablement | `SearchRuntimeProvider` |
| Product Card wishlist, stock state, and quick-add | `ProductCardRuntime` |
| Product discovery variant/attribute filters | Catalog Runtime end to end |
| Storefront search-index mutation | `IUnifiedSearchIndexService` |

Themes retain presentation ownership: visual shells, classes, typography, spacing, colors, labels, and responsive placement. Builders retain layout/configuration ownership on their intended surfaces. Neither can replace the recovered runtime behavior.

The certification's cache-invalidation split is intentionally deferred because the Phase 1 mandate explicitly prohibits Cache Cleanup. No cache implementation or invalidation topology was redesigned.

## Ownership Conflicts Recovered

### 1. Canonical catalog page boundary

**Ownership before:** `/products` executed the canonical `ProductListPage` catalog and Product Builder sections above and below it. `pages.product.sections` therefore participated in catalog-page runtime ownership.

**Recovery:** Removed Builder section resolution/execution from `ProductListPage` and deleted the catalog-section splitting helper.

**Ownership after:** `ProductListPage` exclusively owns `/products`, including catalog request state, filters, sorting, pagination, loading, errors, and results. Builder product sections no longer execute on the route.

**Reasoning:** Removing the secondary execution path is smaller and safer than changing the Catalog Runtime or interpreting Builder sections differently.

### 2. Product Details composition and section execution

**Ownership before:** Product Details composition could come from saved product templates, `pages.product.sections`, built-in templates, and theme-specific executable section replacements.

**Recovery:**

- Removed `pages.product.sections` as a Product Details fallback.
- Kept saved Product templates and built-in templates as configuration inputs resolved by one owner: `ProductTemplateResolver`.
- Ensured the canonical Product Details hero runtime is present even when a saved template omits it.
- Prevented themes from replacing executable Product Details section implementations for product hero, gallery, specifications, video, and related products.
- Made variant selection, add-to-cart, and stock status mandatory runtime decisions rather than section-prop decisions.

**Ownership after:** `ProductTemplateResolver` exclusively resolves Product Details composition, and the Product Details Runtime exclusively executes its behavior. Themes style the canonical components but do not replace their behavior. Builder compatibility remains through saved Product template composition, without using the generic Product-page configuration as a second runtime owner.

**Reasoning:** The correction removes only the two duplicate execution paths and preserves existing template identifiers, saved template data, built-in fallbacks, and theme presentation primitives.

### 3. Category identity authority

**Ownership before:** Public category browsing used PostgreSQL, while filtered catalog routes resolved slugs and descendants through MongoDB. A Mongo miss could remove the requested category constraint and return an unfiltered catalog.

**Recovery:**

- Added `ResolveCategoryTreeIdsAsync` to `IStorefrontCategoryService`.
- Resolved GUIDs, generated slugs, live visibility, and descendants from SQL `CategoryLineageAuthority`.
- Removed `ICategoryReadRepository` from the Storefront category service and catalog controller ownership paths.
- Removed dead Mongo category tree/mapping helpers.
- Made an unknown category fail closed: legacy list returns an empty result and discovery retains an impossible category constraint instead of dropping the filter.

**Ownership after:** SQL category lineage through `StorefrontCategoryService` exclusively owns public category identity, visibility, slugs, and descendant expansion for both category browsing and catalog filtering.

**Reasoning:** Reusing the existing public SQL category service aligns both routes without changing endpoint contracts or the Catalog Runtime.

### 4. Category page duplicate ownership

**Ownership before:** `CategoryPage` rendered its default category runtime unless Category Builder sections existed, in which case Builder replaced the route runtime.

**Recovery:** Removed the Builder replacement branch from `CategoryPage`.

**Ownership after:** `CategoryPage` always owns `/category`. Builder category layouts remain available on their intended Builder surfaces but cannot replace Category Runtime.

**Reasoning:** Removing the conditional replacement preserves the existing category behavior and requires no Builder redesign.

### 5. Theme/Search boundary

**Ownership before:** The default header owned modal/shortcut behavior, Cosmetics linked directly to `/search`, and other custom theme headers exposed no Search Runtime entry point. Theme choice therefore changed Search availability and mechanics.

**Recovery:**

- Added one `SearchRuntimeProvider` for enablement, modal state, keyboard shortcut, and open/close behavior.
- Added one shared `SearchRuntimeTrigger` for header placement.
- Wrapped the Storefront layout in the provider.
- Replaced local search implementations and direct `/search` links with the shared trigger across the default and custom theme headers.
- Preserved `behavior.enableSearch`, now interpreted by Search Runtime rather than by an individual theme header.

**Ownership after:** Search Runtime exclusively owns availability and mechanics. Themes only choose the trigger's placement and surrounding visual appearance.

**Reasoning:** A provider plus a presentation-neutral trigger preserves every theme header while removing theme-specific behavior forks.

### 6. Theme/Product Card boundary

**Ownership before:** Theme and product/store configuration selected card implementations, and individual theme renderers decided whether wishlist, stock status, and quick-add controls existed.

**Recovery:**

- Made renderer selection depend only on the registered visual theme.
- Stopped product authoring fields and storefront product templates from selecting Product Card behavior.
- Moved wishlist, stock label, and quick-add into `ProductCardInteractionLayer` in `ProductCardRuntime`.
- Removed functional control rendering from every theme card renderer.
- Removed the Electronics direct-renderer bypass from runtime product shelves so all shelves use canonical `ProductCard` behavior.
- Retained theme-specific card shells and presentation class maps.

**Ownership after:** `ProductCardRuntime` exclusively owns Product Card interaction behavior. Theme renderers own visual presentation only.

**Reasoning:** A shared interaction layer preserves the established theme renderers and data model while eliminating renderer-specific behavior decisions.

### 7. Filter ownership gap

**Ownership before:** `hasVariants` existed in URL state and backend discovery but the live Product List adapter omitted it. Attribute filtering had no implemented Catalog Runtime owner.

**Recovery:**

- Passed `hasVariants` from URL discovery state through the live adapter.
- Added repeated `attribute=name=value` URL/API query support.
- Added request validation for attribute filters.
- Added canonical variant-attribute values to the Elasticsearch Product Search document.
- Applied attribute filters to Elasticsearch candidates and reapplied them during SQL-authoritative result recomposition.

**Ownership after:** Catalog Runtime owns `hasVariants` and variant attributes end to end: URL state → adapter → API client → endpoint → discovery query → Elasticsearch candidate filtering → SQL-authoritative final filtering.

**Reasoning:** Extending the existing discovery contract is the smallest correction and preserves all existing filters and Storefront APIs.

### 8. Projection refresh ownership split

**Ownership before:** Product event consumers mutated the product search index through `IProductSearchIndexService`, while unified administrative Category/CMS search operations used `IUnifiedSearchIndexService`. Product search projection therefore had a separate mutation owner.

**Recovery:** Routed product create/update/delete, price/stock, media, promotion, variant, reindex, and repair mutations through `IUnifiedSearchIndexService` using `IndexProductAsync` and `DeleteProductAsync`.

**Ownership after:** `IUnifiedSearchIndexService` is the exclusive Storefront search-index mutation boundary for products, categories, and CMS documents. Events and administrative repair endpoints are triggers/clients of that owner, not alternate index owners. `IProductSearchIndexService` remains an internal product-index implementation/query dependency and is not injected into mutation consumers, reindex controllers, or repair services.

**Reasoning:** Converging callers on the already-existing unified boundary avoids redesigning events, admin repair flows, Elasticsearch indices, or search reads.

### 9. Cache invalidation split — intentionally deferred

**Ownership before:** Backend event invalidation does not directly invalidate frontend module/Zustand caches.

**Phase 1 action:** None. The task explicitly says not to begin Cache Cleanup. Changing cross-layer cache ownership or invalidation would violate that boundary.

**Ownership after:** Unchanged for this one cache-specific finding. It is recorded for the later Cache Cleanup phase and is not concealed by the Phase 1 certification.

## Files Modified

### Backend production

- `PointOfSale.API/Controllers/SearchReindexController.cs`
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`
- `PointOfSale.API/Services/ReadModels/DiscountReadModelRepairService.cs`
- `PointOfSale.API/Services/Storefront/IStorefrontCategoryService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs`
- `PointOfSale.Core/DTOs/ProductDiscoveryDtos.cs`
- `PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductMediaChangedEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductPromotionSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductStockPriceSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductVariantProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/PromotionProjectionConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocument.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Documents/ProductSearchDocumentMapper.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`

### Storefront production

- `PointOfSale.Storefront/src/components/common/ProductCard.tsx`
- `PointOfSale.Storefront/src/components/layout/Header.tsx`
- `PointOfSale.Storefront/src/components/products/ProductCardRenderers.tsx`
- `PointOfSale.Storefront/src/components/products/ProductCardRuntime.tsx`
- `PointOfSale.Storefront/src/components/products/index.ts`
- `PointOfSale.Storefront/src/components/products/productCardRendererResolver.ts`
- `PointOfSale.Storefront/src/components/search/SearchRuntime.tsx` (new)
- `PointOfSale.Storefront/src/components/search/index.ts`
- `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx`
- `PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts`
- `PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts`
- `PointOfSale.Storefront/src/pages/CategoryPage.tsx`
- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`
- `PointOfSale.Storefront/src/pages/productCatalogSections.ts` (deleted)
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`
- `PointOfSale.Storefront/src/storefront/layout/StorefrontLayout.tsx`
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts`
- `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`
- `PointOfSale.Storefront/src/storefront/themes/alizon/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/cosmetics/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/fashion/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/hype/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/minimal/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/pearl/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/phoenix/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/seven/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/themes/sharp/layout/header.tsx`
- `PointOfSale.Storefront/src/storefront/utils/resolveProductCardLayout.ts`
- `PointOfSale.Storefront/src/types/discovery.ts`

### Frontend tests

- `PointOfSale.Storefront/src/components/products/productCardRendererResolver.test.ts`
- `PointOfSale.Storefront/src/components/theme-components/themeComponentSystem.test.ts`
- `PointOfSale.Storefront/src/pages/ProductListPage.catalogOwnership.test.ts`
- `PointOfSale.Storefront/src/pages/productCatalogSections.test.ts` (deleted with removed duplicate runtime)
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.test.ts`
- `PointOfSale.Storefront/src/storefront/runtime/runtimeOwnershipRecovery.test.ts` (new)
- `PointOfSale.Storefront/src/storefront/themes/ThemeRegistry.test.ts`

### Backend tests

- `PointOfSale.Tests/Architecture/CrossSystemCriticalRiskHardeningTests.cs`
- `PointOfSale.Tests/Architecture/DataAuthorityViolationHardeningTests.cs`
- `PointOfSale.Tests/Architecture/GlobalReliabilityInfrastructureExtractionTests.cs`
- `PointOfSale.Tests/Architecture/RuntimeOwnershipRecoveryTests.cs` (new)
- `PointOfSale.Tests/Architecture/StorefrontRecoveryRebuildCertificationTests.cs`
- `PointOfSale.Tests/Integration/ProductVariantDistributedReliabilityIntegrationTests.cs`
- `PointOfSale.Tests/Integration/ProductVariantPriceDistributedReliabilityIntegrationTests.cs`
- `PointOfSale.Tests/Integration/PurchaseReturnDistributedReliabilityIntegrationTests.cs`
- `PointOfSale.Tests/Integration/SalesInvoiceConcurrencyIntegrationTests.cs`
- `PointOfSale.Tests/Storefront/StorefrontSearchReliabilityTests.cs`

### Documentation

- `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md` (this implementation record)

The canonical `RUNTIME_OWNERSHIP_CERTIFICATION.md` baseline was read and left unchanged.

## Architectural Reasoning

The recovery follows four constraints:

1. Remove duplicate execution paths instead of introducing new abstraction layers.
2. Reuse existing canonical owners (`ProductListPage`, `StorefrontCategoryService`, `RuntimeSectionContainer`, and `IUnifiedSearchIndexService`).
3. Treat StoreConfig, templates, themes, and events as inputs or presentation/trigger clients rather than alternate runtime owners.
4. Preserve public endpoints, query parameter compatibility, saved Product templates, theme identifiers, Builder configuration data, and Storefront DTO behavior.

No Storefront redesign, Theme Cleanup, Builder Cleanup, Product Authoring redesign, Search redesign, Catalog redesign, Projection redesign, or Cache Cleanup was performed.

## Regression Tests

### Added/updated frontend coverage

- Catalog and category routes cannot execute Builder runtime sections.
- Search Runtime owns shortcut/modal behavior and every custom theme header uses its shared trigger.
- Product Details configuration cannot fall back to the generic Product-page Builder and cannot lose its canonical hero runtime.
- Product Details behavior cannot be replaced by theme section resolution.
- Product Card renderer choice is presentation-only and card controls remain runtime-owned.
- `hasVariants` and attributes are carried across the live Catalog adapter/API boundary.

### Added/updated backend coverage

- Catalog category resolution uses SQL `StorefrontCategoryService`, not Mongo category repositories.
- Unknown category constraints fail closed.
- Variant and attribute filters are owned end to end by discovery.
- All Storefront product index mutation callers use `IUnifiedSearchIndexService`.
- Existing reliability and integration test mocks were updated to the canonical search mutation interface.

## Validation

| Validation | Result |
|---|---|
| Full Storefront frontend suite: `npm test -- --run` | **PASS** — 23 files, 214 tests |
| Storefront production build: `npm run build` | **PASS** — 517 modules transformed |
| Web production build: `npm run build` | **PASS** — 5,082 modules transformed |
| Solution build: `dotnet build PointOfSale.sln --no-restore --verbosity:minimal` | **PASS** — 0 errors, 0 warnings in final incremental build |
| Backend runtime/category/search reliability filter | **PASS** — 8 tests |
| Touched legacy projection/search architecture checks | **PASS** — 5 tests |
| `git diff --check` | **PASS** |

The focused backend command covered `RuntimeOwnershipRecoveryTests`, `StorefrontSearchReliabilityTests`, and `CategoryLineageAuthorityTests`. Five directly touched legacy architecture checks also passed independently.

A broader class-name filter selected 227 unrelated architecture tests and reported 173 passing / 54 failing. Those failures are existing repository drift outside this recovery, including missing historical migration fixture files and unrelated payment/publication assertions. One touched legacy test method still fails on its unrelated `new ProductPublishStatusChangedEvent` assertion; the search-index assertions changed by this recovery pass when selected independently. These baseline failures were not modified because doing so would exceed Runtime Ownership scope.

### Duplicate ownership and override searches

Source searches produced these results:

- No runtime source reference remains to `renderConfiguredProductSections`, `splitProductCatalogSections`, or `productCatalogSections`; only negative regression assertions mention the old names.
- `ProductListPage`, `CategoryPage`, and `ProductTemplateResolver` contain no Builder section execution path for the recovered routes.
- Theme/default headers contain no `SearchModal`, `useSearchShortcut`, direct `/search` link, or `focus=search` behavior; header implementations render `SearchRuntimeTrigger`.
- `ProductCardRenderers.tsx` contains no wishlist, quick-add, stock-control, cart mutation, or availability behavior.
- `RuntimeSectionContainer` contains no Electronics card-renderer bypass and does not source required Product Details controls from section props.
- Storefront product mutation consumers, reindex controllers, and repair services contain no injected `IProductSearchIndexService` and no direct product `IndexAsync`/`DeleteAsync` calls. Remaining matching calls belong to the separate CommerceHub search index runtime.
- `StorefrontProductsController` contains no `ICategoryReadRepository`, slug lookup, or Mongo category tree call.
- `StorefrontCategoryService` and its interface contain no Mongo category owner/reference.

## Intentionally Deferred

- **Cache invalidation split:** deferred to Cache Cleanup by explicit task instruction.
- No Theme Cleanup, Builder Cleanup, or Cache Cleanup was started.
- Unrelated baseline architecture-test drift and missing historical migration test fixtures were not repaired.

## Certification

### Phase 1 Runtime Ownership Recovery: PASS within the authorized recovery scope

All executable Runtime Ownership conflicts from the canonical certification were recovered with one owner per affected runtime boundary:

- Catalog and Category routes are no longer replaceable by Builder runtime.
- Product Details behavior is no longer supplied by Product-page fallback or Theme runtime replacement.
- Category identity is no longer split between SQL and MongoDB.
- Search behavior no longer varies by Theme implementation.
- Product Card behavior no longer varies by Theme renderer or product authoring configuration.
- Catalog variant/attribute filters have a complete owner.
- Storefront search-index mutations have one unified owner.

The broader repository cannot be certified as having unified cross-layer cache invalidation until the explicitly separate Cache Cleanup phase. That mandated defer does not introduce a new duplicate owner and is the only canonical finding left unchanged.
