# Storefront Runtime Discovery Audit

**Audit date:** 2026-07-31  
**Repository:** `PointOfSale`  
**Base commit:** `89dd69bac7d592a92c10f26ea062931f9ab8ef41`  
**Audited state:** the current workspace, including its pre-existing uncommitted changes  
**Scope:** runtime discovery only; no implementation, refactoring, UI, CSS, API, PostgreSQL, MongoDB, or theme changes

## Certification summary

The source establishes the following runtime architecture:

1. Product authoring saves the authoritative product graph and an EF/MassTransit bus outbox in one PostgreSQL transaction.
2. After commit, RabbitMQ dispatches independent asynchronous consumers for the Mongo product projection, Elasticsearch product index, and Redis invalidation.
3. The normal Storefront `/products` request does **not** list products from MongoDB or Redis. It calls `/api/storefront/products/discover`, obtains candidates from Elasticsearch, and recomposes every candidate from PostgreSQL before returning it.
4. Elasticsearch create/update consumers require the Mongo projection to exist first. If the Mongo document is not ready, they throw and rely on message retry.
5. `/products` has two mutually exclusive visible UI paths:
   - with no configured `config.pages.product.sections`, it renders the canonical catalog controls and grid;
   - with one or more configured product sections, `SectionRenderer` replaces the canonical catalog with reusable section/shelf components. The canonical discovery hook still executes, but its result is not rendered.
6. Curated configured shelves read product IDs from MongoDB and then recompose them from PostgreSQL. `all` and `random` configured shelves use the Elasticsearch discovery endpoint.
7. The end-to-end propagation model is queue-based eventual consistency. The authoring response waits for the PostgreSQL transaction, not Mongo projection, Elasticsearch indexing, Redis invalidation, Elasticsearch refresh, or browser refetch.

These conclusions are source-backed below. No production data, queue state, database rows, or tenant configuration was assumed.

---

## 1. Product lifecycle

### 1.1 Complete write-to-browser path

```text
POS Admin ProductForm
    |
    | POST /api/products or PUT /api/products/{id}
    v
ProductsController
    v
ProductAuthoringOrchestrator
    v
EF Core SaveChanges + PostgreSQL transaction commit
    |-- Products / ProductUnits / ProductVariants / media and related rows
    `-- MassTransit EF bus-outbox rows
             |
             | asynchronous RabbitMQ dispatch
             +--------------------------+---------------------------+
             v                          v                           v
      Mongo projection consumer   Elasticsearch consumer    Redis invalidation consumer
             |                          |                           |
             v                          |                           v
      Mongo products document           |                    best-effort key deletion
             |                          |
             `--------------------------+
                 create/update indexer reads Mongo, then overlays PostgreSQL
                                        v
                              Elasticsearch products index
                                        v
Browser /products -> discovery hook -> GET /api/storefront/products/discover
                                        v
                            StorefrontProductsController
                                        v
                              ProductDiscoveryService
                                        |
                          Elasticsearch candidate query
                                        v
                    PostgreSQL authority overlay/recomposition
                                        v
                           DTO -> component-local React state
                                        v
                         ProductListPage -> ProductCard renderer
                                        v
                                  theme component -> DOM
```

The Mongo, Elasticsearch, and Redis branches are separate consumers. The drawing does not imply that the three complete serially. The Elasticsearch create/update consumer creates a practical dependency on Mongo by explicitly reading the projected product and throwing when it is absent.

### 1.2 Stage inventory

| Stage | Owner and entry point | Trigger | Direct dependencies | Source-backed failure conditions | Caching/state |
|---|---|---|---|---|---|
| Admin form | `ProductForm.saveProductAuthoring` | User submits create/update | Admin Axios client, category selection, form validation, idempotency-key builder, row-version/`If-Match` on update | Client validation failure; missing category; invalid public simple/variant readiness; network/API failure | Calls the POS runtime-cache invalidator after success. This is admin state, not Storefront product discovery. |
| Product API | `ProductsController.CreateProduct` / `UpdateProduct` | `POST /api/products` or `PUT /api/products/{id}` | Authorization, tenant context, request mapper, idempotency and optimistic-concurrency headers, authoring orchestrator | Authentication/role failure, invalid tenant/request, missing idempotency key, missing/stale version, policy/orchestrator exception | No Storefront response cache is applied here. |
| Authoring orchestration | `ProductAuthoringOrchestrator.AuthorAsync` | Controller command | EF `ApplicationDbContext`, product policy, category authority, unit/variant graph synchronization, event publisher | Category lineage unavailable; publication-readiness violation; concurrency/idempotency conflict; graph persistence failure | PostgreSQL change tracker only during request. |
| Atomic authoritative commit | `CommitProductAuthoringTransactionAsync` | Orchestrator has staged mutation and events | PostgreSQL transaction, EF `SaveChangesAsync`, audit record, EF bus outbox | Any save/transaction/commit error aborts the successful write response and prevents an authoritative committed result | Authoritative persistent SQL state plus outbox rows. |
| Bus outbox dispatch | MassTransit EF outbox configured in `Program.cs` | Committed outbox rows are polled | PostgreSQL outbox tables, RabbitMQ | Dispatcher/RabbitMQ unavailable; message remains pending/retries according to MassTransit infrastructure | Persistent outbox; `QueryDelay` is 1 second and duplicate-detection window is 5 minutes. |
| Mongo create/update projection | `ProductCreatedEventConsumer`, `ProductUpdatedEventConsumer` -> `ProductSqlAuthorityProjectionWriter.ApplyAsync` | RabbitMQ integration event | PostgreSQL authority overlay, projection admission policy, Mongo repository, event/freshness fence | Invalid tenant/product identifiers are logged and acknowledged; SQL product is not public; admission-rule failure; Mongo exception causes consumer failure/retry | Persistent Mongo `products` document; no product TTL. |
| Mongo visibility lifecycle | `ProductVisibilityLifecycleProjectionConsumer` | delete/archive/publish-status lifecycle event | PostgreSQL current state, Mongo mutation boundary | Current SQL state is not active, online, and Published, or product is deleted: tombstone rather than public upsert; persistence exception retries | Mongo freshness/event fence prevents older messages overwriting newer state. |
| Elasticsearch create index | `ProductSearchIndexConsumer` | product-created event | Mongo product document, PostgreSQL authority overlay, Elasticsearch client | Mongo document absent -> `ProjectionNotReadyException`; SQL overlay returns non-public -> delete/skip public index; index service returns false -> exception/retry | Persistent Elasticsearch index; no app TTL and no explicit refresh request. |
| Elasticsearch update index | `ProductUpdatedSearchIndexConsumer` | product-updated event | Same as create | Same Mongo readiness dependency and retry behavior; inactive/offline/unsearchable current state is deleted from the index | Persistent derived index. |
| Elasticsearch visibility/delete | `ProductDeletedSearchIndexConsumer` | delete/archive/publish-status event | PostgreSQL authority overlay, Elasticsearch | Current public/searchable product is re-indexed; otherwise deleted; failed ES operation throws/retries | Persistent derived index. |
| Redis invalidation | `ProductCacheInvalidationConsumer` | create/update/delete/stock/price/publish-status event | SQL slug lookup, Redis cache service, event fence | Invalidation exceptions are logged and swallowed, so invalidation is explicitly best effort | Deletes list/detail/promotion/home keys and patterns; it does not populate the canonical discovery response. |
| Canonical Storefront API | `StorefrontProductsController.Discover` | Browser GET `/api/storefront/products/discover` | tenant header, Mongo category repository when category slug/tree resolution is requested, `IProductDiscoveryService` | Invalid tenant/category input; search unavailable; discovery exception returns service-unavailable behavior | No Redis product-list read in this endpoint. |
| Discovery engine | `ProductDiscoveryService.DiscoverAsync` | Controller request | Elasticsearch, PostgreSQL authority overlay | Elasticsearch disabled/unavailable/invalid; candidate window above 10,000; SQL overlay removes stale/non-public candidates | No service-local result cache. |
| Browser discovery state | `useProductListDiscovery` -> `useProductDiscovery` | Route mount and URL/query/language changes; manual retry | Axios Storefront client, React local state | HTTP failure; response arriving after a newer request is ignored intentionally | Component-local state only; no React Query cache, polling, or focus revalidation. |
| Catalog rendering | `ProductListPage` -> `ProductCard` -> resolved theme card renderer | Hook state update | store config, theme context, commerce behavior | Configured-section branch can replace this rendering; theme resolver/component errors can suppress section output | Theme/module resolution maps cache component resolution, not product discovery data. |

### 1.3 Authoritative PostgreSQL graph and outbox

The product mutation covers `Products` and its unit, variant, variant-attribute, variant-stock, media, price/mirror, idempotency, and audit/event infrastructure as used by the orchestrator and `ApplicationDbContext`. The EF/MassTransit bus outbox is the event-publication boundary. The successful controller response is downstream-independent because the observer and queue consumers occur after the SQL commit.

The concrete product table names in the current EF model snapshot are `Products`, `ProductMedia`, `ProductUnits`, `ProductVariants`, `ProductVariantAttributes`, `ProductVariantStocks`, and `ProductRequestIdempotency`. MassTransit's EF transport tables are `OutboxMessage`, `OutboxState`, and `InboxState`. These transport tables are distinct from other domain-specific outbox entities in the database.

Evidence:

- [`PointOfSale.Web/src/components/ProductForm.jsx`](PointOfSale.Web/src/components/ProductForm.jsx), `saveProductAuthoring`, request construction, defaults, and public-readiness validation.
- [`PointOfSale.API/Controllers/ProductsController.cs`](PointOfSale.API/Controllers/ProductsController.cs), create/update routes and request preconditions.
- [`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`](PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs), `AuthorAsync`, graph synchronization, `StageAuthoritativeOutboxAsync`, and `CommitProductAuthoringTransactionAsync`.
- [`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs`](PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs), publication and category policies.
- [`PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`](PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs), tracked-DbContext publication.
- [`PointOfSale.API/Program.cs`](PointOfSale.API/Program.cs), consumer registration, EF outbox, RabbitMQ, retry intervals, and publisher registration.
- [`PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`](PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs), authoritative table mappings.

---

## 2. Exact `/products` product source

### 2.1 Route and branch selection

Both root and language-prefixed `/products` routes resolve to `ProductListPage`. The visible page is selected by this condition:

```text
config.pages.product.sections.length > 0
    yes -> enabled configured sections -> SectionRenderer
    no  -> canonical ProductListPage catalog UI
```

`useProductListDiscovery(...)` executes before that condition and therefore runs in both cases. In the configured-section case, its product/error/loading result is not the visible content.

Evidence: [`PointOfSale.Storefront/src/app/router.tsx`](PointOfSale.Storefront/src/app/router.tsx) and [`PointOfSale.Storefront/src/pages/ProductListPage.tsx`](PointOfSale.Storefront/src/pages/ProductListPage.tsx).

### 2.2 Canonical catalog runtime chain

```text
/products or /{language}/products
  -> ProductListPage
  -> useDiscoveryFilters (React Router URLSearchParams)
  -> useProductListDiscovery
  -> useProductDiscovery (custom useState/useEffect hook)
  -> fetchProductsDiscover
  -> Axios GET /api/storefront/products/discover
       headers: X-Tenant-Id, Accept-Language, session/customer/affiliate context
  -> StorefrontProductsController.Discover
       optional category slug/tree resolution through Mongo CategoryReadRepository
  -> ProductDiscoveryService.DiscoverAsync
  -> Elasticsearch query: up to 10,000 candidate hits
  -> ProductSearchSqlAuthorityOverlay for every candidate
  -> current PostgreSQL public/searchable filtering and recomposition
  -> current sorting, paging, facets, response DTO
  -> useProductDiscovery local state
  -> ProductListPage canonical controls/grid
  -> ProductCard
  -> product-card theme renderer
  -> browser DOM
```

There is no product repository between the canonical controller and Elasticsearch: `ProductDiscoveryService` owns the ES query, then calls the SQL authority overlay. Mongo product documents and Redis product-list cache are not read in this chain.

### 2.3 Requested source locator matrix

| Requested element | Exact implementation |
|---|---|
| API endpoint | `GET /api/storefront/products/discover` |
| Controller | `StorefrontProductsController.Discover` in [`PointOfSale.API/Controllers/StorefrontProductsController.cs`](PointOfSale.API/Controllers/StorefrontProductsController.cs) |
| Service | `ProductDiscoveryService.DiscoverAsync` in [`PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`](PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs) |
| Repository | No product repository in canonical discovery. Category slug/tree resolution uses `ICategoryReadRepository`; current product authority is recomposed with EF/PostgreSQL by `ProductSearchSqlAuthorityOverlay`. |
| Mongo query | No Mongo **product** query in canonical discovery. For category input, `CategoryReadRepository` resolves an active/online category and its tree. |
| Elasticsearch query | `ProductDiscoveryService` queries the configured products index, `From(0)`, `Size(10000)`, tracked total hits, bool filters, sorting, and aggregations. |
| Redis query | None in `/discover`. The legacy `GET /api/storefront/products` path is separately marked obsolete/deprecated and uses the legacy read/cache path. |
| React Query hook | None. `useProductDiscovery` is a custom `useState`/`useEffect` hook; the Storefront source has no TanStack React Query hook in this chain. |
| Page hook | `useProductListDiscovery` in [`PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts`](PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts) |
| Request hook | `useProductDiscovery` in [`PointOfSale.Storefront/src/hooks/useProductDiscovery.ts`](PointOfSale.Storefront/src/hooks/useProductDiscovery.ts) |
| Filter state | `useDiscoveryFilters` in [`PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts`](PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts), backed by URL search parameters |
| HTTP function | `fetchProductsDiscover` in [`PointOfSale.Storefront/src/services/api/storefrontApi.ts`](PointOfSale.Storefront/src/services/api/storefrontApi.ts) with endpoint constant in `endpoints.ts` |
| React component | `ProductListPage` |
| Product component | Shared `ProductCard`, then the configured theme product-card renderer |
| State management | URL state plus component-local React state. Store configuration is in Zustand; product results are not stored in Redux/Zustand. |
| Loading skeleton | Canonical initial request uses `SkeletonGrid` with `DEFAULT_PAGE_SIZE = 20`; the hook/API default page size is 12. Existing products remain visible during a refetch. |
| Pagination | URL `page`/`pageSize`; backend paginates **after** SQL recomposition; canonical `Pagination` renders controls. |
| Sorting | `relevance`, browse/newest, price, and discount behavior is implemented across URL hook and discovery service. `setSort` does not reset the current page. |
| Filtering | Category, brand, min/max price, promotion, stock, and backend `hasVariants`; SQL reapplies current filters after recomposition. |
| Search | URL `q` -> Elasticsearch multi-match; browse requests use match-all plus filters. |

### 2.4 Configured-section product sources

Configured `product-grid` and `featured-products` sections pass through `RuntimeSectionContainer` and `useHomepageSectionProducts`:

| Section source | Runtime chain | Data store |
|---|---|---|
| `all` | Section -> `useHomepageSectionProducts` -> `fetchProductsDiscover` -> `/discover` -> ES candidates -> SQL overlay | Elasticsearch + PostgreSQL |
| `random` | Same discovery call, with a larger bounded fetch and deterministic client subset | Elasticsearch + PostgreSQL, then browser selection |
| curated product IDs | Section -> `fetchHomepageProductsByIds` -> `/api/storefront/home/products/by-ids` -> `StorefrontHomeController` -> `StorefrontHomeService.GetProductsByIdsAsync` -> `ProductReadRepository.GetByProductIdsAsync` -> SQL card recomposition | MongoDB + PostgreSQL |

The curated branch preserves configured input order. It is the relevant Mongo product-query exception to the canonical `/products` source.

Evidence:

- [`PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`](PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx)
- [`PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts`](PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts)
- [`PointOfSale.API/Controllers/StorefrontHomeController.cs`](PointOfSale.API/Controllers/StorefrontHomeController.cs)
- [`PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`](PointOfSale.API/Services/Storefront/StorefrontHomeService.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs`](PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs)

---

## 3. Theme and component ownership

### 3.1 Ownership classification

| Runtime element | Used by | Ownership classification | Evidence/result |
|---|---|---|---|
| Canonical filter/search/count/breadcrumb/sidebar/grid/pagination block inside `ProductListPage` | `/products` only | **Products** | It is the normal catalog branch when `config.pages.product.sections` is empty. |
| `ProductCard`, card renderer resolver, commerce behavior | Product listing and other product surfaces | **Shared canonical** | The catalog uses the same card/rendering infrastructure as reusable sections. |
| `SectionRenderer`, `RuntimeSectionContainer`, theme section resolver | Home, products when configured, and other configurable pages | **Shared section framework** | `HomePage` and configured `ProductListPage` both use the same renderer. |
| `useHomepageSectionProducts` | Reusable product shelves | **Shared, home/shelf semantics** | Its name, source selection, by-ID home endpoint, and module cache show shelf ownership rather than catalog ownership. |
| `product-grid` / `featured-products` theme sections | Home and any configured page that admits them | **Shared shelf components** | Not Products-specific. |
| `config.pages.product.sections` | Product details default template and Products listing branch | **Shared/overloaded configuration** | `ProductTemplateResolver` reads the same array as the default product-details template; `ProductListPage` treats any non-empty array as listing content. |
| `GET /api/storefront/products` | Legacy clients | **Deprecated, not proven dead** | Controller source explicitly marks it deprecated/obsolete. It remains routable. |
| Theme `product-grid` implementations discovered through `import.meta.glob` | Runtime section resolver | **Live-capable, not dead** | Dynamic resolution means lack of a static import is not evidence of dead code. |

### 3.2 Reused sections and labels

The modern theme `product-grid` contains the literal presentation labels:

- `popular now`
- `fast browsing`
- `cart-ready picks`
- `View All`, linking to `/products`

These are not canonical Products-page controls. They belong to the reusable `product-grid` shelf implementation and can appear inside `/products` only because configured product sections replace the catalog branch.

Other theme/default product-grid or featured-products implementations also expose `View All` to `/products`. If one of those shelves is rendered on `/products`, the action is a self-link. Multiple configured shelves can create multiple such actions.

The builder availability rule permits the ordinary shared section types broadly, and permits product-context types (`product-hero`, `product-gallery`, `product-specs`, `product-video`, `related-products`) for the `product`/`product-template` keys. The Storefront listing branch does not distinguish a product-details template from a products-list template before passing the array to `SectionRenderer`.

Evidence:

- [`PointOfSale.Storefront/src/pages/HomePage.tsx`](PointOfSale.Storefront/src/pages/HomePage.tsx)
- [`PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx`](PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx)
- [`PointOfSale.Storefront/src/storefront/themes/ThemeSectionResolver.ts`](PointOfSale.Storefront/src/storefront/themes/ThemeSectionResolver.ts)
- [`PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx`](PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx)
- [`PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts`](PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts)
- [`PointOfSale.Web/src/modules/storefront/builder/pageSectionAvailability.js`](PointOfSale.Web/src/modules/storefront/builder/pageSectionAvailability.js)
- [`theme-authority/shopixy-theme-authority.json`](theme-authority/shopixy-theme-authority.json)

---

## 4. Product visibility rules

Visibility is layered. A product may be committed in PostgreSQL yet fail projection admission, indexing admission, discovery admission, current SQL recomposition, request filters, or UI branch selection.

### 4.1 Authoring/publication readiness

A product that remains Draft, inactive, or offline bypasses public-readiness enforcement and is not expected to be public. A product intended to be public must satisfy:

| Product type | Required public state |
|---|---|
| All | Valid tenant/product/category identity and lineage; `PublishStatus = Published`; `IsActive = true`; `IsOnlineVisible = true` |
| Simple | Valid units; valid base/default unit; positive default selling price; valid conversions; base conversion equal to 1 |
| Variant | At least one active, non-deleted semantic variant with a positive effective selling price |

The admin form defaults a new product to Published, active/online/searchable values in its model, but variant creation explicitly constructs Draft authoring payload behavior. The final payload/current SQL row is authoritative.

### 4.2 Mongo projection admission

`StorefrontProductProjectionAdmissionPolicy` requires:

- valid projection contract, identity, schema/version, and event metadata;
- active and online product state;
- simple-product default-unit/positive-price/conversion invariants; or
- variant identity validity and at least one active, positive-priced variant.

Lifecycle projection additionally upserts only a non-deleted SQL product that is active, online, and Published; otherwise it writes a tombstone. Freshness/event fencing prevents an older event from replacing newer projection state.

Evidence:

- [`PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs`](PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`](PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs`](PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs)

### 4.3 Elasticsearch admission

Create and update indexing requires:

- the Mongo product projection to exist;
- a currently active tenant;
- a current SQL product that is not deleted;
- valid, non-deleted category lineage;
- current active/online public state;
- `IsSearchable = true`;
- the same viable simple-unit or variant-price graph after SQL overlay.

If active/online/searchable current state is false, the index consumers delete the product from Elasticsearch rather than expose it.

`ProductSearchSqlAuthorityOverlay` makes online visibility equal to SQL `IsOnlineVisible` **and** `PublishStatus == Published`. It maps `IsSearchable` independently. The final discovery recomposition requires all three current booleans.

### 4.4 Tenant and category rules

- Tenant visibility requires a PostgreSQL tenant row with `IsActive = true`, active status, and no deletion timestamp.
- Product SQL overlay requires category lineage to exist, remain non-deleted, be tenant-correct, and be cycle-safe.
- That SQL lineage method does not test category `IsActive` or `IsOnlineVisible`.
- Mongo category route/tree resolution does require projected categories to be active and online and prunes invalid lineage. Consequently category enabled/online state affects what category navigation and category-filter resolution can select, but it is not an additional base-product boolean in an unfiltered discovery request.

Evidence:

- [`PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs`](PointOfSale.Infrastructure/Reliability/TenantLineageAuthority.cs)
- [`PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs`](PointOfSale.API/Services/Catalog/CategoryLineageAuthority.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/CategoryReadRepository.cs`](PointOfSale.Infrastructure/MongoDB/Services/CategoryReadRepository.cs)
- [`PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`](PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs)

### 4.5 Request-time discovery rules

The Elasticsearch candidate query applies tenant, active, online, searchable-compatibility, affiliate, query, category, brand, price, promotion, stock, and variant filters as requested. PostgreSQL recomposition then reapplies current visibility and request filters before sorting and pagination.

`ReadModelEvolutionQueryFilters` can temporarily admit an older ES document whose searchable field is missing during schema convergence, but the final SQL recomposition still requires current `IsSearchable = true`. The public response therefore always requires current SQL searchable state.

### 4.6 Conditions that do not hide a base product by themselves

- **Out of stock:** retained in an unfiltered listing; it changes `InStock`/purchase behavior and is excluded only when the stock filter requests it.
- **Images/media:** missing product media does not hide the card; frontend image resolution falls back to a placeholder SVG.
- **`IsFeatured = false`:** not a canonical discovery requirement; it affects featured-shelf semantics.
- **Redis list cache state:** not consulted by canonical `/discover`.
- **`ShowOutOfStockProducts` configuration:** mapped in Storefront configuration, but `ProductListPage` does not automatically translate it into the discovery `inStock` URL filter.

Evidence: [`PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs`](PointOfSale.Infrastructure/MongoDB/Documents/ProductStorefrontVisibilityAuthority.cs), [`PointOfSale.Storefront/src/utils/resolveProductImage.ts`](PointOfSale.Storefront/src/utils/resolveProductImage.ts), and the canonical discovery/page sources above.

---

## 5. Products-page theme audit

### 5.1 Feature matrix

| Capability | Canonical branch: no configured sections | Configured-section branch |
|---|---|---|
| Search | Present | Not rendered by the catalog page |
| Filters | Category, brand, price, stock, promotion present | Not rendered by the catalog page |
| Variant filter | Backend and URL model support exist, but `useProductListDiscovery` omits `hasVariants`; no visible canonical control | Section-dependent; no catalog control |
| Sorting | Present | No catalog sorting control |
| Pagination | Present | No catalog pagination; each shelf has its own bounded item count |
| Product count | Present | No catalog result count |
| Breadcrumbs | Present | No catalog breadcrumbs |
| Category navigation | Horizontal navigation present | Not rendered by catalog page |
| Sidebar | Desktop sidebar plus mobile filter drawer present | Not rendered by catalog page |
| Loading skeleton | `SkeletonGrid`; initial count 20 while discovery default page size is 12 | Shelf-specific skeleton/count |
| View All | No catalog-level self-link | Theme shelf may link `/products` while already on `/products`; multiple shelves can duplicate it |
| Home/reusable widgets | None in canonical branch | Any enabled reusable section can appear; modern grid hardcodes Popular Now, Fast Browsing, and Cart-ready Picks |
| Canonical request | Visible | Still executes, but its data/error/loading state is not rendered |

### 5.2 Layout and behavior inconsistencies proven by code

1. Initial canonical skeleton cardinality is 20 while the hook and API default page size is 12.
2. Changing sort does not reset the page, so a high current page remains selected under the new ordering.
3. `hasVariants` exists in URL/backend discovery capability but is dropped when `useProductListDiscovery` creates the actual request and public filter actions.
4. Any non-empty product section array removes all canonical catalog navigation and controls from the visible page.
5. Shelf `View All` actions can be self-links when rendered on `/products` and can be duplicated across multiple sections.
6. Product-detail-oriented sections can be admitted under the same `product` configuration key; the listing page does not supply a product-details model to those sections, so their own component guards determine whether they render content.
7. Theme resolution or section runtime failure is contained by the section resolver/error boundary and can yield no section output without restoring the canonical catalog branch.

---

## 6. Product pipeline timeline

### 6.1 Actual consistency model

```text
T0  Admin request begins
T1  PostgreSQL product graph + EF bus outbox commit
    -> API may return success here
T2  MassTransit outbox poll/dispatch (configured QueryDelay: 1 second)
T3  Independent RabbitMQ consumers run
    -> Mongo projection
    -> Elasticsearch indexing
    -> Redis invalidation
T4  Elasticsearch create/update consumer can fail if Mongo is not ready
    -> configured global retry intervals: 1s, 5s, 15s, 30s
T5  Successful Elasticsearch index request
    -> no explicit refresh/wait-for-refresh option in ProductSearchIndexService
T6  A browser discovery request sees the refreshed index candidate
T7  PostgreSQL recomposition confirms current visibility and returns the product
T8  React local state renders it
```

This is **background, queue-based eventual consistency**, not immediate consistency and not a synchronous projection. The source provides no hard end-to-end propagation bound because it does not bound:

- outbox/RabbitMQ availability;
- consumer retry/exhaustion and error-transport handling;
- Mongo/Elasticsearch infrastructure latency;
- Elasticsearch refresh visibility;
- when a browser makes its next request.

The normal path is automatic. Manual synchronization/repair controllers exist, but product save does not call them. They are operational recovery surfaces, not part of the normal propagation timeline.

### 6.2 Browser refresh implications

- Canonical discovery fetches on mount and when its query/category/language inputs change, plus explicit retry/refetch.
- It has no polling and no refetch-on-window-focus mechanism.
- If the request completes before indexing becomes visible, that mounted state stays unchanged until one of those triggers occurs.
- Configured shelf results have a module-level cache with no TTL; a successful empty/stale response can remain for the lifetime of that loaded module unless the hook's manual refetch deletes the key or the module reloads.

---

## 7. Cache inventory

| Cache/state layer | Owner | Data/path affected | Invalidation | TTL | Refresh mechanism |
|---|---|---|---|---|---|
| Browser HTTP cache | Browser | HTTP responses in general | No product-discovery-specific invalidator exists in inspected source | No `/discover` cache TTL/header configured in inspected source | Normal browser/request behavior; Axios does not add a product cache policy |
| Service worker/CDN | None found in Storefront runtime/config inspected | No proven product-discovery layer | None found | None found | None found |
| React Query | Not present in this chain | None | N/A | N/A | N/A |
| Canonical React state | `useProductDiscovery` | Current discovery response/loading/error | Replaced on a new hook request | Component lifetime; not a TTL cache | Mount, dependency change, or manual refetch |
| URL state | `useDiscoveryFilters` / React Router | query, categories, brands, price, promotion, stock, variant flag, sort, page, page size | User navigation/action | URL lifetime | Navigation/history |
| Zustand store configuration | `storeConfigStore` + bootstrap | tenant live theme/pages/features | Bootstrap assignment; no periodic invalidation in source | No TTL/persistence configured | Storefront bootstrap/reload |
| Redux/Zustand products | None | Product discovery result is not stored there | N/A | N/A | N/A |
| Configured-shelf product Map | `useHomepageSectionProducts` module `productCache` | `all`, `random`, and curated shelf responses by tenant/source/size/seed/IDs | Manual hook refetch removes its key; exported global clear has no runtime caller found | No TTL | Manual refetch or JS module reload |
| Configured-shelf in-flight Map | same hook | Request promise deduplication | Removed after completion | Request lifetime | Next uncached request |
| Theme-section resolver Map | `ThemeSectionResolver` | Resolved React section modules | None in source | Module lifetime | Module reload/new uncached key |
| Product-template resolver Map | `ProductTemplateResolver` | Resolved template selection keyed by config update/template ID | Key changes with configuration identity | Module lifetime | Config key change/module reload |
| Backend in-memory discovery cache | None in `ProductDiscoveryService` | None | N/A | N/A | N/A |
| Redis JSON cache | Redis cache services | Product detail, deprecated list, promotion/home and other legacy/storefront reads; **not canonical `/discover`** | `ProductCacheInvalidationConsumer` deletes product list/detail/slug/promotion/home keys/patterns | Defaults in app settings: product 10 minutes; product list 5 minutes; call sites may provide their own values | Cache miss/factory refresh; event invalidation is best effort |
| Mongo product read model | `ProductReadRepository` | Projected product documents and curated by-ID shelves | Event-driven upsert/tombstone with freshness fence | No TTL index for `products` | Projection consumers or manual repair |
| Mongo category read model | `CategoryReadRepository` | category navigation/slug/tree resolution | Category projection lifecycle | No product-pipeline TTL shown | Category events/repair |
| Elasticsearch product index | `ProductSearchIndexService` | Discovery candidate documents | Product event consumers index/delete | No application TTL | Event indexing; ES refresh is not explicitly awaited |
| PostgreSQL | Product authoring and SQL authority overlay | Source of truth | Transactional writes | Persistent, not a cache | Authoring transaction |

Redis cache service operations are fail-open/best-effort by design in the inspected implementation. That can affect Redis-backed detail/legacy/home reads, but it cannot directly explain a missing result from the canonical Elasticsearch `/discover` chain because that chain does not read Redis.

---

## 8. Failure-point inventory

Each item below corresponds to a guard, branch, exception, filter, or refresh behavior in the cited runtime source.

### 8.1 Save and authoritative SQL

- Admin validation prevents submission because category/public unit/variant readiness is invalid.
- Variant create mode is authored as Draft and therefore is not public until a later publish transition.
- Wrong/missing tenant, role, idempotency key, expected version, or `If-Match` rejects the API request.
- Category lineage does not exist, is deleted, tenant-invalid, or cyclic.
- Authoring policy rejects public simple or variant readiness.
- Optimistic concurrency or idempotency claim fails.
- EF graph save, audit/outbox staging, or transaction commit fails.

### 8.2 Outbox and transport

- Committed outbox is not dispatched because its dispatcher or RabbitMQ is unavailable.
- Queue delivery is delayed.
- Consumer exhausts the configured retry intervals and is moved/handled by transport error behavior.
- Event contains an empty tenant or product identifier; relevant projection/index consumers log and acknowledge it without applying the product.

### 8.3 Mongo projection

- SQL authority lookup returns no current product.
- Tenant is inactive/deleted.
- Product is deleted, inactive, offline, or not Published at lifecycle projection time, producing a tombstone.
- Projection schema/version/contract/event metadata fails admission.
- Simple product lacks a valid default/base unit, positive price, or valid conversions.
- Variant product lacks an active positive-priced variant.
- Mongo write/query fails.
- Freshness fence identifies the event as stale and prevents it from replacing newer state.

### 8.4 Elasticsearch indexing

- Create/update index consumer runs before Mongo projection exists; it throws `ProjectionNotReadyException`.
- Mongo projection remains absent through all retries.
- PostgreSQL overlay removes the product because tenant, product, category lineage, public state, unit/variant readiness, or current data is invalid.
- `IsSearchable` is false, causing deletion/non-inclusion.
- Elasticsearch is disabled: `ProductSearchIndexService` treats indexing as a no-op success, while discovery itself fails closed when disabled.
- Elasticsearch request fails; index service returns false and consumer throws.
- Product is indexed but not yet visible to search because the index operation does not request or await refresh.
- A stale event is rejected by the index fence.
- Lifecycle event deletes a product that is archived/unpublished/offline in current SQL.

### 8.5 Canonical discovery API

- Browser sends the wrong or missing `X-Tenant-Id`.
- Store domain/bootstrap resolves the wrong tenant context.
- Affiliate mode is active and product affiliate eligibility does not match.
- Elasticsearch is disabled, unavailable, has a missing/wrong configured index, or returns an invalid response; the endpoint has no Mongo fallback.
- Candidate query exceeds the explicit 10,000-result safety window.
- Product is absent from the ES candidate set even though SQL is current; SQL overlay cannot add a product that ES did not return.
- SQL recomposition removes a stale indexed product based on current visibility/searchable/category/unit/variant state.
- Search text, category, brand, price, promotion, in-stock, variant, current page, or page size excludes it.
- Sorting is changed while a high page number is retained, making the current page empty.
- Category slug/tree resolution fails because the Mongo category projection is absent, inactive, offline, or has invalid lineage.
- Requested category navigation cannot expose an inactive/offline projected category even though the unfiltered product base rule does not test those category booleans.

### 8.6 Canonical browser/UI

- The first request occurs before Elasticsearch refresh and the hook does not poll or refetch on focus afterward.
- Network/API failure leaves hook error state until retry/dependency change/remount.
- A newer request supersedes an older response; the older response is intentionally ignored.
- URL filters remain active and hide the product.
- `hasVariants` state/backend capability is not forwarded by the page hook, so expected variant-only UI behavior cannot be reached through the canonical page wiring.
- Store configuration fetch failure, missing required currency, maintenance mode, or disabled-store state blocks the Storefront before the product page.

### 8.7 Configured theme-section branch

- Any non-empty `config.pages.product.sections` replaces the canonical catalog UI.
- Canonical discovery succeeds but is invisible because section content is the selected render branch.
- Curated shelf omits the newly created product ID by configuration.
- Curated ID exists but Mongo projection is absent/stale, so the Mongo by-ID lookup cannot supply it.
- `all` shelf displays only its bounded page size.
- `random` shelf intentionally selects a subset and can omit a valid product.
- Module cache retains a successful pre-index empty/stale result with no TTL.
- Section is disabled or rejected during strict config normalization.
- Theme section cannot resolve or throws and is suppressed/contained by runtime section error handling.
- Product-detail-oriented section receives no listing product model and its own guard returns no content.
- Multiple shelf sections create duplicate product groups and `View All` links.
- Modern product-grid presentation makes the Products page look like Home through its hardcoded Popular Now, Fast Browsing, and Cart-ready Picks content.

### 8.8 Redis-specific failures, with scope

- Event invalidation fails and is swallowed, leaving Redis-backed product-detail/deprecated-list/home responses stale until TTL or a later refresh.
- Publish/archive invalidation coverage differs by event contract; archived behavior is carried by the lifecycle/status flows rather than a direct archived consumer registration in the invalidator.
- Redis unavailability is handled fail-open by cache services.
- These Redis cases do **not** remove a product from canonical `/api/storefront/products/discover`; they apply only to endpoints that read those keys.

### 8.9 Explicitly ruled out as universal hide rules

- Zero inventory does not universally hide a product; it hides only under the in-stock filter and affects purchase readiness.
- Missing media does not universally hide a card; image resolution has a placeholder.
- `IsFeatured` is not a canonical discovery visibility requirement.
- Redis staleness is not a canonical `/discover` product-source failure.

---

## 9. Runtime certification

### 9.1 Certified source of truth by stage

| Concern | Certified owner |
|---|---|
| Product authoring truth | PostgreSQL product graph committed by `ProductAuthoringOrchestrator` |
| Event handoff | MassTransit EF bus outbox in the same PostgreSQL transaction |
| Product document projection | MongoDB `products` via projection consumers and admission/mutation boundary |
| Search candidate projection | Elasticsearch via product search consumers |
| Current public truth in discovery | PostgreSQL recomposition by `ProductSearchSqlAuthorityOverlay` |
| Canonical Products listing | `/api/storefront/products/discover` -> ES candidates -> SQL overlay |
| Configured curated shelf | home by-IDs endpoint -> Mongo product IDs/documents -> SQL recomposition |
| Catalog client state | URL parameters plus local React hook state |
| Configured-shelf client cache | module-level `Map`, no TTL |
| Theme/page ownership decision | `config.pages.product.sections.length` in `ProductListPage` |

### 9.2 Certification statements

- The expected propagation is not immediate. It is asynchronous and eventually consistent.
- Mongo is required for the normal **indexing** create/update path, but is not the product-list source for canonical `/products` discovery.
- PostgreSQL remains authoritative at write time and again at request time.
- Elasticsearch is necessary for a product to become a canonical discovery candidate.
- Redis is not in the canonical discovery read chain.
- React Query is not used for the catalog; the current hook is custom local state.
- A configured Products-page section array changes both component ownership and product-source behavior.
- The source contains no guaranteed end-to-end time bound and no browser polling that closes the eventual-consistency window automatically.

### 9.3 Investigation boundary

This audit certifies the architecture encoded in the current repository workspace. It does not assert the live health of PostgreSQL, MongoDB, RabbitMQ, Elasticsearch, Redis, any error queue, or a specific tenant's stored `StoreConfig`, because no live environment/tenant was part of the requested source-code discovery. Those facts require a separate runtime-state inspection.

No runtime, source, UI, CSS, API, PostgreSQL, MongoDB, or theme implementation was changed to produce this report.

---

## Primary source index

### Authoring and event handoff

- [`PointOfSale.Web/src/components/ProductForm.jsx`](PointOfSale.Web/src/components/ProductForm.jsx)
- [`PointOfSale.API/Controllers/ProductsController.cs`](PointOfSale.API/Controllers/ProductsController.cs)
- [`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs`](PointOfSale.API/Services/ProductAuthoring/ProductAuthoringOrchestrator.cs)
- [`PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs`](PointOfSale.API/Services/ProductAuthoring/ProductAuthoringInfrastructurePolicies.cs)
- [`PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs`](PointOfSale.Infrastructure/EventBus/MassTransitEventPublisher.cs)
- [`PointOfSale.API/Program.cs`](PointOfSale.API/Program.cs)
- [`PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`](PointOfSale.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs)

### Projection, search, and invalidation

- [`PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductCreatedEventConsumer.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductUpdatedEventConsumer.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductVisibilityLifecycleProjectionConsumer.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductSearchIndexConsumer.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductUpdatedSearchIndexConsumer.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductDeletedSearchIndexConsumer.cs)
- [`PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs`](PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs`](PointOfSale.Infrastructure/MongoDB/Services/ProductSqlAuthorityProjectionWriter.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs`](PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionAdmissionPolicy.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs`](PointOfSale.Infrastructure/MongoDB/Services/StorefrontProductProjectionMutationBoundary.cs)
- [`PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs`](PointOfSale.Infrastructure/MongoDB/Services/ProductReadRepository.cs)
- [`PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`](PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs)
- [`PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs`](PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchIndexService.cs)

### Storefront API and browser

- [`PointOfSale.API/Controllers/StorefrontProductsController.cs`](PointOfSale.API/Controllers/StorefrontProductsController.cs)
- [`PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs`](PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs)
- [`PointOfSale.Infrastructure/Elasticsearch/Services/ReadModelEvolutionQueryFilters.cs`](PointOfSale.Infrastructure/Elasticsearch/Services/ReadModelEvolutionQueryFilters.cs)
- [`PointOfSale.Storefront/src/pages/ProductListPage.tsx`](PointOfSale.Storefront/src/pages/ProductListPage.tsx)
- [`PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts`](PointOfSale.Storefront/src/hooks/useDiscoveryFilters.ts)
- [`PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts`](PointOfSale.Storefront/src/hooks/useProductListDiscovery.ts)
- [`PointOfSale.Storefront/src/hooks/useProductDiscovery.ts`](PointOfSale.Storefront/src/hooks/useProductDiscovery.ts)
- [`PointOfSale.Storefront/src/services/api/storefrontApi.ts`](PointOfSale.Storefront/src/services/api/storefrontApi.ts)

### Theme/configured-section branch

- [`PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx`](PointOfSale.Storefront/src/components/sections/SectionRenderer.tsx)
- [`PointOfSale.Storefront/src/storefront/themes/ThemeSectionResolver.ts`](PointOfSale.Storefront/src/storefront/themes/ThemeSectionResolver.ts)
- [`PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx`](PointOfSale.Storefront/src/storefront/runtime/containers/RuntimeSectionContainer.tsx)
- [`PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts`](PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts)
- [`PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts`](PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts)
- [`PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx`](PointOfSale.Storefront/src/storefront/themes/modern/sections/product-grid.tsx)
- [`PointOfSale.API/Controllers/StorefrontHomeController.cs`](PointOfSale.API/Controllers/StorefrontHomeController.cs)
- [`PointOfSale.API/Services/Storefront/StorefrontHomeService.cs`](PointOfSale.API/Services/Storefront/StorefrontHomeService.cs)
- [`PointOfSale.API/Controllers/StorefrontStoreConfigController.cs`](PointOfSale.API/Controllers/StorefrontStoreConfigController.cs)
