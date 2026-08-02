# Storefront Runtime Recovery Master Plan

**Date:** 2026-07-31  
**Repository:** `PointOfSale`  
**Mode:** source-code-backed runtime recovery plan only  
**Canonical reference:** `STOREFRONT_RUNTIME_DISCOVERY_AUDIT.md`

No runtime code was changed for this document.

---

## Executive Summary

The current Storefront runtime does **not** match the intended architecture for `/products`.

Source validation shows that `/products` is routed to `ProductListPage`, and `ProductListPage` contains the canonical catalog implementation: search, filters, sorting, pagination, category navigation, product count, loading state, empty state, and grid. However, the same page currently branches on `config.pages.product.sections.length > 0`; when that array is non-empty, reusable configured sections are rendered through `SectionRenderer` and the canonical catalog UI is not rendered.

That branch explains the observed `Popular Now`, `Fast Browsing`, `Cart Ready Picks`, and `View All` behavior. Those labels come from the modern themed `product-grid` reusable section, not from the canonical catalog.

Product visibility is a separate but related problem. Product authoring commits PostgreSQL and MassTransit outbox rows in one transaction, then Mongo, Elasticsearch, and Redis consumers run asynchronously. Canonical `/products` calls `/api/storefront/products/discover`, which requires Elasticsearch candidates and then recomposes each result from PostgreSQL. A newly saved product can be absent until the outbox, RabbitMQ, Mongo projection, Elasticsearch indexing, Elasticsearch refresh, and browser refetch all converge.

Recovery should restore this ownership chain:

```text
POS Admin
  -> Product Authoring
  -> Projection Pipeline
  -> Canonical Storefront Catalog
  -> Theme rendering
```

The recovery should **not** preserve the current behavior where reusable Home/product-grid sections replace the `/products` catalog.

---

## Architecture Validation

### A. Products Page Ownership

`/products` should render `ProductListPage` as the canonical catalog owner.

Source:

- `PointOfSale.Storefront/src/app/router.tsx` routes both non-prefixed `/products` and language-prefixed `products` to `ProductListPage`.
- `PointOfSale.Storefront/src/app/router.tsx` routes `/products/:slug` to `ProductDetailsPage`, proving detail ownership is separate.
- `PointOfSale.Storefront/src/pages/ProductListPage.tsx` imports and renders canonical catalog controls: `SearchInput`, `SortSelector`, `CategoryNav`, `ProductFiltersSidebar`, `MobileDrawer`, `ActiveFiltersBar`, `Pagination`, and `ProductCard`.

Current runtime is architecturally wrong because `ProductListPage` has this replacement branch:

- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`: reads `const productSections = config.pages?.product?.sections || []`.
- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`: when `productSections.length > 0`, renders only `SectionRenderer` for enabled configured sections.
- The canonical discovery hook still executes before the branch, but its result is invisible in the configured-section branch.

Conclusion: `/products` should render `ProductListPage` canonical catalog. `SectionRenderer` may be used by Home, Category configured content, and Product Details templates, but it should not replace the canonical Products listing.

### B. Configuration Ownership

`config.pages.product.sections` is currently ambiguous in source and is being consumed by both listing and detail-related code.

Source:

- `PointOfSale.Storefront/src/types/storeConfig.ts` defines `pages.product` as a generic `StorefrontPageConfig`.
- `PointOfSale.API/DTOs/Storefront/StorefrontStoreConfigDtos.cs` exposes `StorefrontPagesDto.Product` and separately exposes `ProductTemplates`.
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx` supports both `page=product` and `page=product-template`.
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx` stores `product-template:{templateId}` separately from ordinary page keys.
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs` detects the `product-template:` prefix and persists that content under `product_templates`; normal `product` persists under `pages`.
- `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs` maps `pages.product` and `product_templates` separately.
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts` only accepts `config.pages.product.sections` as a default product-detail template if the sections include product-detail section types: `product-hero`, `product-gallery`, `product-specs`, or `product-video`.

Intended ownership inferred from current source:

- Product Details ownership: `ProductDetailsPage` + `resolveProductTemplate` + `config.productTemplates` + built-in product templates.
- Product Listing ownership: `ProductListPage` + `useProductListDiscovery` + `/discover`.
- `config.pages.product.sections` must not own the canonical listing replacement path. If it remains in the schema for compatibility, it should either be ignored by `/products` or limited to non-replacing supplemental slots after the canonical catalog.

### C. Theme Ownership

Theme components are intended to style specific surfaces, not own catalog discovery.

Source:

- Home: `PointOfSale.Storefront/src/pages/HomePage.tsx` reads `config.pages.home.sections` and renders `SectionRenderer`.
- Products: `PointOfSale.Storefront/src/pages/ProductListPage.tsx` owns catalog controls and grid.
- Product Details: `PointOfSale.Storefront/src/pages/ProductDetailsPage.tsx` fetches one product and renders resolved template sections with `sectionProps={{ product }}`.
- Shared reusable sections: `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx` normalizes sections and delegates to `RuntimeSectionContainer`.
- Theme section resolution: `PointOfSale.Storefront/src/storefront/themes/ThemeSectionResolver.ts` loads themed section modules from `storefront/themes/*/sections/*.tsx` and defaults from `components/sections/defaults/*.tsx`.
- Shared product cards: `ProductListPage` and section containers both render `ProductCard`; this sharing is correct.

Incorrectly shared/reused components:

- `SectionRenderer` is incorrectly used by `ProductListPage` as a replacement for the canonical catalog.
- Theme `product-grid` sections are incorrectly allowed to become the full `/products` page surface.
- `useHomepageSectionProducts` is incorrectly used for `/products` when the configured-section branch is active.
- Theme section headers that always link to `/products` are incorrect inside `/products`, because `View All` is a reusable shelf affordance, not a catalog control.
- `PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx` hardcodes `popular now`, `fast browsing`, and `cart-ready picks`; those labels are appropriate to a merchandising shelf and incorrect as the canonical Products page header.

### D. Canonical Catalog Ownership

The canonical catalog owner is `ProductListPage`.

Source-backed ownership:

- Search: `SearchInput` in `PointOfSale.Storefront/src/pages/ProductListPage.tsx`; URL state in `PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts`; request through `useProductListDiscovery`.
- Filters: `ProductFiltersSidebar`, `MobileDrawer`, `ActiveFiltersBar` in `ProductListPage`; URL state in `useDiscoveryFilters`.
- Sorting: `SortSelector` in `ProductListPage`; `setSort` in `useDiscoveryFilters`; backend sort in `ProductDiscoveryService`.
- Pagination: `Pagination` in `ProductListPage`; `page` and `pageSize` in URL; backend result in `ProductDiscoveryService`.
- Category navigation: `CategoryNav` in `ProductListPage`; category tree from `useCategories`; backend category slug/tree resolution in `StorefrontProductsController.DiscoverProducts`.
- Breadcrumbs: inline breadcrumb in `ProductListPage`.
- Product count: `totalCount` from `useProductListDiscovery`, shown through `CommercePageHeader` and `Pagination`.
- Grid: `ProductCard` grid in `renderContent` inside `ProductListPage`.
- Loading state: `SkeletonGrid` in `ProductListPage`.
- Empty state: `CommerceEmptyState` in `ProductListPage`.

Why they disappeared:

- `ProductListPage` renders all of the above only in the `productSections.length === 0` branch.
- Any non-empty `config.pages.product.sections` suppresses the canonical branch and renders `SectionRenderer` instead.

---

## Source Findings

### Route and Page Findings

- `PointOfSale.Storefront/src/app/router.tsx`: `/products` and language-prefixed `products` map to `ProductListPage`; `/products/:slug` maps to `ProductDetailsPage`.
- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`: calls `useProductListDiscovery({ routeCategory })` before deciding whether to render configured product sections or canonical catalog.
- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`: `productSections.length > 0` renders `SectionRenderer`; otherwise it renders the canonical catalog.
- `PointOfSale.Storefront/src/pages/ProductDetailsPage.tsx`: fetches by slug with `fetchProductBySlug(slug)` and renders resolved product-template sections with product context.
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts`: guards `config.pages.product.sections` so only product-detail section types can be used as a PDP default template.
- `docs/SIMPLE-02_STOREFRONT_PRODUCT_ROUTING_REPORT.md`: records the prior drift where listing-style `config.pages.product.sections` incorrectly replaced product-detail rendering, and documents the product-detail guard.

### Store Configuration Findings

- `PointOfSale.Storefront/src/types/storeConfig.ts`: Storefront config has `pages` and `productTemplates` as separate runtime concepts.
- `PointOfSale.API/DTOs/Storefront/StorefrontStoreConfigDtos.cs`: backend DTO has separate `StorefrontPagesDto.Product` and `ProductTemplates`.
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`: builder recognizes `home`, `product`, `category`, and `product-template`.
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`: `product-template:{templateId}` is the storage key for product templates; `product` is a normal page key.
- `PointOfSale.API/Services/Admin/AdminStoreConfigService.cs`: `TryGetProductTemplateKey` routes `product-template:` updates to `product_templates`; other page IDs update `pages`.
- `PointOfSale.API/Services/Storefront/StorefrontDraftConfigMapper.cs`: `MapPages` maps `home`, `product`, and `category`; `MapProductTemplates` maps `product_templates`.

### Section Runtime Findings

- `PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx`: renders normalized sections through `RuntimeSectionContainer`.
- `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`: treats `product-grid` and `featured-products` as product shelf sections.
- `PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts`: product shelves use a module-level `productCache` with no TTL; curated shelves call `fetchHomepageProductsByIds`; `all` and `random` call `fetchProductsDiscover`.
- `PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx`: renders reusable shelf header copy and `View All` link to `/products`.
- `PointOfSale.Storefront/src/components/sections/defaults/product-grid.tsx`: default reusable product grid also renders a `View All` link to `/products`.

### Discovery Findings

- `PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts`: combines URL filters with `useProductDiscovery`; maps discovery DTOs to `ProductListItemDto`.
- `PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts`: URL is the filter source of truth; supports `q`, `category`, `brand`, price range, promotion, stock, variants, sort, page, and page size.
- `PointOfSale.Storefront/src/hooks/useProductDiscovery.ts`: uses local React state, fetches on mount/dependency change, has manual `refetch`, and has no polling or focus revalidation.
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`: `fetchProductsDiscover` calls `ENDPOINTS.PRODUCTS_DISCOVER`.
- `PointOfSale.Storefront/src/services/api/endpoints.ts`: `PRODUCTS_DISCOVER` is `/api/storefront/products/discover`; `PRODUCTS` is marked legacy.
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`: `DiscoverProducts` validates tenant, resolves category slugs/tree IDs, builds `ProductDiscoveryQuery`, and calls `_discoveryService.DiscoverAsync`.
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`: fails closed when Elasticsearch is disabled; queries Elasticsearch up to a 10,000 candidate window; recomposes candidates through SQL authority; paginates after recomposition.
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`: recomposes current product visibility, category, units, variants, stock, media, pricing, and promotion from PostgreSQL.

### Authoring and Propagation Findings

- `PointOfSale.Web/src/components/ProductForm.jsx`: `saveProductAuthoring` validates category/readiness and posts or puts `/products`.
- `PointOfSale.API/Controllers/ProductsController.cs`: create/update require mutation preconditions and call `_productAuthoringOrchestrator.AuthorAsync`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`: create/update mutations call `CommitProductAuthoringTransactionAsync`; events are staged by `StageAuthoritativeOutboxAsync`.
- `PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`: commit executes `SaveChangesAsync`, then `CommitAsync`, then `AfterCommitAsync`.
- `PointOfSale.API/Program.cs`: MassTransit registers product Mongo projection, search index, lifecycle, and cache invalidation consumers; EF bus outbox uses PostgreSQL and a 1-second query delay; RabbitMQ retry intervals are 1s, 5s, 15s, 30s.
- `PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs`: writes Mongo projection through `ProductSqlAuthorityProjectionWriter`.
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs`: recomposes Mongo projection from SQL authority.
- `PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`: applies SQL overlay and tombstones non-visible products.
- `PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs`: rejects invalid public projections, including missing default unit for simple products or no active positive-priced variant for variant products.
- `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs`: create indexing reads Mongo first; if absent, throws `ProjectionNotReadyException`.
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs`: update indexing also reads Mongo first and throws when absent.
- `PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs`: lifecycle events recompose directly from SQL and index or delete based on current visibility.
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs`: `IndexAsync` indexes documents without an explicit refresh/wait-for-refresh request.
- `PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs`: Redis invalidation is best-effort and swallows failures; it does not feed canonical `/discover`.

---

## Incorrect Runtime Behaviors

1. `/products` is allowed to render reusable configured sections instead of the canonical catalog.
   - Source: `PointOfSale.Storefront/src/pages/ProductListPage.tsx`.

2. Reusable product shelf text appears on the catalog page.
   - Source: `PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx`.

3. `View All` appears while already inside `/products`.
   - Source: `PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx` and `PointOfSale.Storefront/src/components/sections/defaults/product-grid.tsx`.

4. Canonical controls disappear whenever `config.pages.product.sections` is non-empty.
   - Source: `PointOfSale.Storefront/src/pages/ProductListPage.tsx`.

5. Product shelf data paths can replace canonical listing semantics.
   - Source: `PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx` and `PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts`.

6. Builder/page availability allows generic section types on `product`, which feeds the replacement branch.
   - Source: `PointOfSale.Web/src/modules/storefront/builder/pageSectionAvailability.js`.

---

## Theme Ownership

Correct ownership:

- Home sections: `HomePage` + `config.pages.home.sections` + `SectionRenderer`.
- Product listing: `ProductListPage` + `useProductListDiscovery` + `/discover` + `ProductCard`.
- Product details: `ProductDetailsPage` + `resolveProductTemplate` + `productTemplates` + detail sections.
- Shared sections: `SectionRenderer`, `RuntimeSectionContainer`, theme `sections/*`.
- Shared cards: `ProductCard` and theme product-card renderers.

Incorrect sharing to recover:

- `ProductListPage` must stop using `SectionRenderer` as a page replacement.
- `product-grid` and `featured-products` must remain reusable shelf sections, not `/products` page owners.
- `useHomepageSectionProducts` must not become the data source for canonical `/products`.
- Theme section CTA links to `/products` should not be rendered inside the canonical `/products` page unless the section is explicitly supplemental and context-aware.

---

## Catalog Ownership

Canonical `/products` must keep one owner for each responsibility:

| Responsibility | Canonical owner | Current suppressor |
|---|---|---|
| Search | `ProductListPage` + `SearchInput` + `useDiscoveryFilters` | `productSections.length > 0` branch |
| Filters | `ProductFiltersSidebar`, `MobileDrawer`, `ActiveFiltersBar` | `productSections.length > 0` branch |
| Sorting | `SortSelector` + `useDiscoveryFilters.setSort` + backend sort | `productSections.length > 0` branch |
| Pagination | `Pagination` + discovery page/pageSize | `productSections.length > 0` branch |
| Category navigation | `CategoryNav` + `useCategories` + backend category tree resolution | `productSections.length > 0` branch |
| Breadcrumbs | `ProductListPage` breadcrumb | `productSections.length > 0` branch |
| Product count | `totalCount` in `CommercePageHeader` and `Pagination` | `productSections.length > 0` branch |
| Grid | `ProductCard` grid in `renderContent` | `productSections.length > 0` branch |
| Loading state | `SkeletonGrid` | `productSections.length > 0` branch |
| Empty state | `CommerceEmptyState` | `productSections.length > 0` branch |

Additional source gap:

- `useDiscoveryFilters` parses `hasVariants`, and `fetchProductsDiscover` supports it, but `useProductListDiscovery` does not forward `hasVariants` to `useProductDiscovery` or expose `setHasVariants` in its result. This does not explain all missing controls, but it is a catalog wiring defect.

---

## Propagation Analysis

### Verified Product Visibility Pipeline

```text
POS Admin ProductForm
  -> ProductsController
  -> ProductAuthoringOrchestrator
  -> PostgreSQL transaction + EF/MassTransit outbox
  -> RabbitMQ dispatch
  -> Mongo product projection
  -> Elasticsearch index consumer
  -> Elasticsearch products index
  -> GET /api/storefront/products/discover
  -> ProductDiscoveryService
  -> SQL authority recomposition
  -> React useProductDiscovery local state
  -> ProductListPage canonical grid
  -> ProductCard/theme renderer
  -> DOM
```

### Disappearance Points

POS Admin:

- `ProductForm` can reject save if category is missing or public readiness fails.
- Variant create/update can save as a non-public state depending on authoring state.

PostgreSQL:

- `ProductsController` can reject missing idempotency/concurrency preconditions.
- `ProductAuthoringOrchestrator` can reject category authority, publication readiness, idempotency, or stale row version.
- SQL transaction failure prevents authoritative commit and outbox persistence.

Outbox/RabbitMQ:

- MassTransit outbox may lag or fail to dispatch.
- RabbitMQ may be unavailable.
- Consumer retries can exhaust after configured intervals.

Mongo:

- Projection consumers reject missing tenant/product identity.
- SQL overlay tombstones products that are not active, online, published, category-valid, unit-valid, or variant-valid.
- Admission policy rejects invalid public simple/variant projection.
- Mongo write failure causes retry.
- Freshness fences can skip stale events.

Elasticsearch:

- Create/update index consumers require Mongo projection first; absent Mongo throws `ProjectionNotReadyException`.
- SQL overlay may remove the product before indexing.
- Hidden/unsearchable products are deleted from ES.
- Index service returns false on ES failure; consumers throw.
- No explicit ES refresh means a successful index may not be immediately searchable.

`/discover`:

- Missing/wrong `X-Tenant-Id` blocks request.
- Category slug/tree resolution depends on Mongo category projection.
- ES disabled/unavailable/misconfigured fails closed.
- Product absent from ES cannot be added by SQL overlay.
- SQL recomposition can remove stale candidates.
- URL filters/search/sort/page can exclude the product.

React/DOM:

- `useProductDiscovery` fetches on mount and dependency changes only; no polling/focus refetch.
- A request that completes before ES visibility leaves local state stale.
- Configured section branch hides canonical discovery results entirely.
- `useHomepageSectionProducts` has a module-level cache with no TTL for section shelves.

### Runtime Instrumentation Needed

Instrumentation should be added only at stage boundaries already present in source:

- `ProductAuthoringOrchestrator.CommitProductAuthoringTransactionAsync`: log product id, tenant id, event version, publish status, online/searchable flags, and whether outbox staging happened.
- `ProductCreatedEventConsumer` and `ProductUpdatedEventConsumer`: log projection disposition and tombstone reason from `ProductSqlAuthorityProjectionWriter`.
- `ProductSearchIndexConsumer` and `ProductUpdatedSearchIndexConsumer`: log Mongo-not-ready retry count, SQL overlay result, search visibility flags, and index result.
- `ProductSearchIndexService.IndexAsync`: log index name, document id, response result, and whether refresh was requested if recovery adds it.
- `StorefrontProductsController.DiscoverProducts`: log tenant, normalized categories, query, filters, page, page size, and result counts.
- `ProductDiscoveryService.RecomposeDiscoveryItemsFromSqlAuthorityAsync`: count candidates removed by SQL authority and by each filter class.
- `useProductDiscovery`: keep request id logging but gate noisy console logs to development; add optional timing and response count telemetry.
- `ProductListPage`: in development, log when `config.pages.product.sections` would have replaced the catalog so regressions are visible during recovery.

---

## Eventual Consistency Review

Should the user have to wait after saving a product?

- No for SQL authoring completion. The admin user should receive a successful save after the PostgreSQL transaction commits.
- Yes for public storefront discoverability under the current architecture, because Mongo, ES, ES refresh, and browser refetch are asynchronous.
- UX should not pretend the product is immediately discoverable; it should show a propagation state when relevant.

Should Product Authoring return before indexing?

- Yes. Source shows product authoring is transaction + outbox based. Blocking authoring on Mongo/ES would violate the current outbox architecture and couple writes to search infrastructure.

Should the browser automatically refresh?

- The Storefront Products page should refetch on focus/visibility and may perform bounded short polling after an empty initial result or manual retry. Current source has only mount/dependency/manual refetch.

Should polling exist?

- Source-compatible recommendation: yes, bounded polling only for catalog freshness windows, not indefinite polling. For example, refetch `/discover` a small number of times after initial load when result count is zero or after focus regain.

Should Server-Sent Events exist?

- Not as the recovery path. No current source establishes SSE infrastructure for product projection convergence.

Should WebSockets exist?

- Not as the recovery path. No current source establishes WebSocket product visibility propagation.

Should React refetch after save?

- The POS Admin product save should not directly mutate Storefront React state because it is a different app/runtime. Admin can poll a backend readiness endpoint or show "saved, publishing to storefront" status. Storefront `/products` should refetch on focus and bounded freshness conditions.

Should admin redirect wait for propagation?

- No hard wait before returning from save or normal navigation. If redirecting to a storefront preview/detail link, the admin should either wait on a bounded readiness probe or display that Storefront indexing is still pending.

Source-compatible improvements:

- Preserve outbox-based authoring.
- Add a read-only product visibility readiness endpoint or diagnostic action that checks SQL, Mongo projection, ES document/searchability, and `/discover` candidate visibility for one tenant/product.
- Add bounded Storefront refetch-on-focus and optional bounded post-load polling in `useProductDiscovery`.
- Add ES indexing refresh behavior only if validated for performance; otherwise expose readiness status instead of pretending immediate search visibility.
- Clear `useHomepageSectionProducts` module cache on store config update or remove its use from `/products` replacement path.

---

## Recovery Roadmap

1. Recover `/products` ownership.
2. Fence section runtime so reusable Home sections cannot replace canonical catalog.
3. Normalize configuration ownership: Product Details templates under `productTemplates`, canonical listing under `ProductListPage`.
4. Add focused propagation observability/readiness.
5. Add source-compatible catalog freshness refetch.
6. Remove duplicate product-list runtime paths from the visible `/products` page.

---

## Implementation Phases

### Phase 1: Products Page Runtime Recovery

Purpose:

- Make `/products` always render the canonical `ProductListPage` catalog controls and grid.

Files affected:

- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`
- `PointOfSale.Storefront/src/pages/ProductListPage.test.tsx` or new focused test
- `PointOfSale.Storefront/src/app/router.matching.test.tsx`

Plan:

- Remove or neutralize the branch where `productSections.length > 0` replaces the catalog.
- If product page sections must remain supported, render them only as clearly supplemental content after the canonical catalog and never as the primary product source.
- Prefer ignoring `config.pages.product.sections` for `/products` until a verified supplemental-slot contract exists.

Risk:

- Tenants relying on the mistaken product-section page replacement will see the canonical catalog instead.

Regression risk:

- Low for catalog correctness; medium for tenants that configured `pages.product.sections` expecting shelves.

Validation:

- Unit/render test proves `/products` renders search, filters, sort, category nav, count, grid, and pagination even when `config.pages.product.sections` contains `product-grid`.
- Browser/manual validation on `/products` and `/category/:slug`.

Rollback strategy:

- Revert the `ProductListPage` branch change only. The router and backend discovery path remain unchanged.

### Phase 2: Builder Configuration Guard

Purpose:

- Stop new invalid `pages.product.sections` compositions from being created as catalog replacements.

Files affected:

- `PointOfSale.Web/src/modules/storefront/builder/pageSectionAvailability.js`
- `PointOfSale.Web/src/modules/storefront/pages/StorefrontBuilderPage.jsx`
- `PointOfSale.Web/src/modules/storefront/components/builder/BuilderTopBar.jsx`
- Builder tests under `PointOfSale.Web/src/modules/storefront`

Plan:

- Clarify or remove `page=product` from builder workflows if it has no canonical runtime slot.
- Keep `page=product-template` for Product Details templates.
- Ensure product-detail sections stay available only for `product-template`.
- Ensure home/listing shelf sections remain available for Home and Category, not as `/products` replacement.

Risk:

- Admin UI workflows may need label/copy adjustments so users understand Product Template vs Products Catalog.

Regression risk:

- Medium in builder UI; low in Storefront runtime.

Validation:

- Builder tests prove product-template storage key remains `product-template:{templateId}`.
- Builder tests prove product-grid cannot be saved as a `/products` replacement.

Rollback strategy:

- Re-enable page availability while keeping Storefront runtime protected by Phase 1.

### Phase 3: Product Detail Template Ownership Cleanup

Purpose:

- Keep Product Details ownership under `productTemplates` and built-in product templates.

Files affected:

- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts`
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.test.ts`
- `PointOfSale.Web/src/storefront/products/ProductTemplateRegistry.ts`
- `PointOfSale.Web/src/admin/help/helpArticles/product-templates.md`

Plan:

- Preserve the current guard that only accepts detail section types as default PDP sections.
- Add regression coverage that `product-grid` in `pages.product.sections` never replaces Product Details or Product Listing.
- Prefer migrating valid detail sections from `pages.product.sections` to `productTemplates.default` in a later data migration if live data requires it.

Risk:

- Low; current source already has the correct PDP guard.

Regression risk:

- Low.

Validation:

- Existing/simple route tests plus template resolver tests.

Rollback strategy:

- Revert only test additions if they are too strict; do not remove the PDP guard.

### Phase 4: Canonical Catalog Wiring Completion

Purpose:

- Ensure all implemented catalog controls actually reach `/discover`.

Files affected:

- `PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts`
- `PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts`
- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`
- `PointOfSale.Storefront/src/components/filters/ProductFiltersSidebar.tsx`

Plan:

- Forward `hasVariants` from `useDiscoveryFilters` into `useProductDiscovery`.
- Expose `setHasVariants` if UI has or should have variant filtering.
- Reset page on sort change unless product requirements intentionally preserve page.
- Confirm `ProductFiltersSidebar` exposes all facet/filter controls expected by the catalog.

Risk:

- Low to medium; URL query behavior can affect shared links.

Regression risk:

- Medium around filter URL compatibility and pagination.

Validation:

- Hook tests for URL params to discovery query.
- Manual `/products?hasVariants=true`, price, brand, category, promotion, stock, and sort validation.

Rollback strategy:

- Revert hook forwarding/control changes.

### Phase 5: Product Visibility Readiness Diagnostics

Purpose:

- Make "saved but not visible" diagnosable without guessing.

Files affected:

- `PointOfSale.API/Controllers/ProductsController.cs` or a new admin diagnostics controller
- `PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`

Plan:

- Add structured logs at the source-listed stage boundaries.
- Add a read-only admin visibility probe for one product that reports SQL public state, Mongo projection state, ES index state, and `/discover` candidacy.
- Do not make product save depend on this probe.

Risk:

- Observability code can leak data if endpoint authorization/tenant scoping is weak.

Regression risk:

- Low if read-only and admin-scoped.

Validation:

- Integration test or manual probe for a known visible product, draft product, unpublished product, and missing ES document.

Rollback strategy:

- Disable endpoint route or remove diagnostics registration; logs can remain if non-invasive.

### Phase 6: Source-Compatible Freshness Improvements

Purpose:

- Reduce the window where a mounted catalog remains stale after ES convergence.

Files affected:

- `PointOfSale.Storefront/src/hooks/useProductDiscovery.ts`
- `PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts` if shelves remain supplemental
- `PointOfSale.Storefront/src/services/api/storefrontApi.ts`

Plan:

- Add refetch-on-window-focus/visibility for canonical discovery.
- Add bounded retry/polling after an empty or failed catalog response.
- Keep polling bounded and cancel on unmount/request supersession.
- Consider cache busting only for configured shelf caches; canonical discovery currently has no module cache.

Risk:

- Extra requests during navigation/focus.

Regression risk:

- Medium if polling is too aggressive.

Validation:

- Hook tests for cancellation and bounded retry.
- Browser validation with delayed ES availability.

Rollback strategy:

- Revert freshness hook changes; catalog still works via manual reload/refetch.

### Phase 7: Cleanup Duplicate Runtime Paths

Purpose:

- Remove product-listing ambiguity from the live runtime.

Files affected:

- `PointOfSale.Storefront/src/pages/ProductListPage.tsx`
- `PointOfSale.Storefront/src/hooks/useProducts.ts`
- `PointOfSale.API/Controllers/StorefrontProductsController.cs`
- Documentation/tests referencing legacy list endpoint

Plan:

- Keep legacy `GET /api/storefront/products` for compatibility only if active callers still need it.
- Ensure canonical `/products` never uses `useProducts` or Mongo list paths.
- Document `/discover` as the only Storefront catalog list path.

Risk:

- Unknown external clients may still call the legacy endpoint.

Regression risk:

- Low for Storefront if source search confirms no canonical usage.

Validation:

- Source search for `ENDPOINTS.PRODUCTS` usage.
- API contract tests for `/discover`.

Rollback strategy:

- Restore legacy endpoint usage only for external compatibility, not for `/products`.

---

## Risk Matrix

| Risk | Likelihood | Impact | Mitigation |
|---|---:|---:|---|
| Existing tenant has live `pages.product.sections` and expects shelves | High | Medium | Restore canonical catalog and optionally render sections as supplemental after catalog only after a formal slot decision |
| Product still delayed after catalog recovery | High | Medium | Add readiness diagnostics and bounded refetch; preserve eventual consistency |
| ES refresh delay persists | Medium | Medium | Measure with readiness probe before adding wait-for-refresh |
| Builder users confuse Product page and Product Template | High | Medium | Remove/rename invalid `product` builder surface; keep `product-template` explicit |
| Extra catalog refetch load | Medium | Low | Bounded polling, focus-only refetch, cancellation |
| Breaking product detail pages | Low | High | Preserve `ProductTemplateResolver` guard and route tests |
| Breaking Home sections | Low | Medium | Keep `HomePage` + `SectionRenderer` unchanged |
| Redis invalidation mistaken as catalog fix | Medium | Low | Document Redis is not in canonical `/discover` chain |

---

## Final Certification

Source validation certifies the current runtime is architecturally wrong for `/products`.

The canonical Products page is present in source but currently suppressed by `ProductListPage` when `config.pages.product.sections` is non-empty. Reusable Home/product shelf sections should not replace the canonical catalog. Product Details template ownership is already partially recovered by `ProductTemplateResolver`, but Products Listing still needs the same ownership recovery.

Product visibility after POS Admin save is eventually consistent by design. Product authoring should continue to return after PostgreSQL commit and outbox staging, not after Mongo/Elasticsearch convergence. The recovery should add diagnostics and bounded client freshness behavior, not redesign the platform around synchronous indexing, SSE, or WebSockets.

Recommended recovery: restore `/products` to always render the canonical catalog, fence builder/configuration ownership so new section layouts cannot replace it, preserve product-detail templates under `productTemplates`, and add targeted propagation observability for SQL -> Mongo -> Elasticsearch -> `/discover` convergence.
