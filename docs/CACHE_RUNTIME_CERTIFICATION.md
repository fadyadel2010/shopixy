# Cache Runtime Certification

- **Repository:** PointOfSale
- **Phase:** Storefront Architecture Cleanup — Cache Runtime Certification
- **Audit date:** 2026-08-03
- **Mode:** Source audit only; no runtime or application code was changed

**Certification:** **FAIL**

## Executive Summary

The Storefront cache architecture is **not certifiable as canonical**.

The backend has strong, source-proven authority boundaries: PostgreSQL remains business truth; MongoDB and Elasticsearch are event-driven read models; current product, category, navigation, footer, CMS, StoreConfig, media, variant, price, stock, and promotion values are frequently reauthorized or recomposed from SQL before public return. The completed Runtime, Theme, and Builder recoveries remain intact and frozen.

The cache layer does not preserve that same canonical ownership end to end:

1. Product event invalidation reaches Redis but not the customer Storefront's module Maps, Zustand stores, React component state, session storage, or possible HTTP caches. Successful browser snapshots for categories, homepage products, StoreConfig, navigation, footer, and smart offers can therefore remain stale for the life of the module, tab, or session.
2. Redis invalidation is explicitly best-effort. Cache services swallow deletion failures, while projection/invalidation event fences can still be completed. A duplicate delivery is then collapsed and cannot retry that deletion; expiration is the recovery path (`PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs:189-228`, `PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs:276-311`, `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs:284-359`).
3. The common Redis projection-lag envelope is not an active freshness guarantee. It bypasses only when a caller supplies a maximum lag and both projection and source-event timestamps; Storefront product/home callers do not supply that complete tuple (`PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs:199-270`, `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs:365-393`, `PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs:12-35`).
4. Multiple registered cache families have no production reader, or their reader deliberately ignores hits: category Redis entries, StoreConfig Redis, direct `ProductDocument` Redis, promotion keys, promotion-ranking Redis, footer Redis, CMS scope Redis, SEO metadata/breadcrumb/sitemap entries, online-customer Redis, and `StorefrontUrlResolver` memory entries. These are duplicate apparent owners without runtime value.
5. The legacy homepage Redis keys omit affiliate mode although their loaders filter by it. An affiliate request can populate the shared candidate list and make the ordinary response incomplete until invalidation or expiry (`PointOfSale.API/Services/Storefront/StorefrontHomeService.cs:126-152`, `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs:188-214`, `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs:250-285`).
6. Public Storefront GET endpoints do not have one explicit HTTP cache policy. StoreConfig, shipping, payment, preview, and selected stock-sensitive routes use `no-store`; product discovery/detail, navigation, footer, published CMS, search, smart offers, cart, and wishlist do not establish an equivalent policy. No response/output-cache middleware, service worker, or Caddy cache policy closes that boundary (`PointOfSale.API/Controllers/StorefrontStoreConfigController.cs:61-91`, `PointOfSale.API/Controllers/StorefrontProductsController.cs:224-230`, `PointOfSale.API/Controllers/StorefrontProductsController.cs:649-760`, `deploy/staging/Caddyfile:53-71`).
7. Redis cart data is authoritative ephemeral state, not a cache. `CartService` owns it, but `CheckoutService` also reads and directly rewrites the same document's idempotency fields, so mutation authority is split (`PointOfSale.API/Services/Cart/CartService.cs:32-58`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:3423-3470`, `PointOfSale.API/Services/Checkout/CheckoutService.cs:3552-3598`).

These failures are sufficient for a **FAIL**. No cache recovery is started by this document.

## Canonical References and Frozen Boundaries

The following mandatory references were read in full before the audit:

1. `docs/STOREFRONT_RUNTIME_DISCOVERY_AUDIT.md`
2. `docs/STOREFRONT_RUNTIME_RECOVERY_MASTER_PLAN.md`
3. `RUNTIME_OWNERSHIP_CERTIFICATION.md`
4. `RECOVERY-1_RUNTIME_OWNERSHIP_IMPLEMENTATION.md`
5. `THEME_RUNTIME_CERTIFICATION.md`
6. `RECOVERY-2_THEME_RUNTIME_IMPLEMENTATION.md`
7. `BUILDER_RUNTIME_CERTIFICATION.md`
8. `RECOVERY-3_BUILDER_RUNTIME_IMPLEMENTATION.md`

The following completed recoveries are frozen and were not reopened:

- **Recovery 1 — Runtime ownership:** Product authoring remains SQL/outbox owned; MongoDB and Elasticsearch are projections; Storefront discovery remains Elasticsearch candidates with current SQL recomposition; unified Elasticsearch mutation remains behind `IUnifiedSearchIndexService`.
- **Recovery 2 — Theme runtime:** Themes remain presentation-only. `LayoutRuntime` owns navigation/footer loading and layout slots.
- **Recovery 3 — Builder runtime:** Builder output remains supplemental authoring. Product Hero remains runtime-injected and current product truth is not builder-owned.

This certification audits caches around those boundaries. It does not redesign them.

## Audit Scope and Definitions

### Included

- Customer Storefront (`PointOfSale.Storefront`).
- Storefront API request paths and the caches/state stores they read or write.
- Product, category, StoreConfig, CMS, navigation, footer, smart-offer, cart, wishlist, SEO, shipping, theme, builder-section, MongoDB, and Elasticsearch boundaries that feed Storefront presentation.
- Authoring events, outbox delivery, projection consumers, invalidation consumers, and manual repair/reindex paths that can refresh those boundaries.
- Browser module state, Zustand state, component-local snapshots, local/session storage, dynamic-import resolution caches, and the HTTP boundary.

### Classification

- **Authoritative store:** state that cannot be reconstructed from another cache and is the declared source of truth. PostgreSQL is business truth. Redis cart is an authoritative ephemeral store.
- **Projection:** MongoDB or Elasticsearch state asynchronously derived from SQL/outbox events. It behaves like a cache boundary for freshness analysis but is not allowed to become business truth.
- **Cache:** a replaceable derivative snapshot with a hit/miss, retained result, or resolution memo.
- **Dormant artifact:** registered or written cache state with no active production return reader.
- **Presentation state:** retained browser state. It is a cache when it avoids a later source fetch; otherwise it is listed as operational state.

### Excluded applications

Repository-wide searches also found caches in the POS Admin, Marketplace, CommerceHub, WhatsApp, analytics, financial-reporting, and Super Admin runtimes. They are listed under **Adjacent Non-Storefront Caches** so they are not mistaken for hidden Storefront owners, but they are outside this customer Storefront contract unless a path feeds Storefront directly.

## Cache Inventory

### Server and Projection Inventory

| Cache or retained boundary | State | Lifetime / scope | Active consumer | Source of truth / fallback | Failure and concurrency behavior |
|---|---|---|---|---|---|
| MongoDB `products` projection | Live projection | Persistent; tenant/product | Product detail legacy reads, curated shelves, wishlist hydration, SEO candidate reads | PostgreSQL product/media/variant/category/promotion authority; no projection TTL | Event-fenced/freshness-guarded writes. SQL suppresses stale visible values, but a missing/new candidate can remain absent without a finite bound. |
| Elasticsearch product/unified search indexes | Live projection | Persistent; tenant/entity | `/products/discover`, unified search, suggestions, best sellers | PostgreSQL via event consumers and `IUnifiedSearchIndexService`; manual reindex/repair | Discovery fails closed when Elasticsearch is disabled/unavailable. SQL reauthorizes returned candidates, but cannot add an entity absent from the index (`ProductDiscoveryService.cs:101-175`, `:547-617`). |
| Redis product detail by id/slug | Live read-through candidate cache | Default 10 minutes; tenant/product; affiliate split | `StorefrontProductService` PDP reads | Miss: Mongo candidate, then current SQL overlay. Redis failure: source path. No negative caching | Common best-effort refresh lock; after lock failure/wait, duplicate factories may run. Every hit is rebuilt from current SQL by product id (`StorefrontProductService.cs:55-121`, `:132-196`, `:642-689`). |
| Redis legacy product-list cache | Live, deprecated route | Default 5 minutes; tenant/query/affiliate | Deprecated `GET /api/storefront/products` | Miss: Mongo list, then SQL overlay. Canonical `/discover` bypasses it | Hit membership is fixed by cached ids; SQL can remove/update but cannot add an absent product (`StorefrontProductService.cs:561-639`, `StorefrontProductsController.cs:262-288`). |
| Redis legacy home featured/new/best-seller caches | Live exposed API; not used by current SPA homepage | 30 / 15 / 15 minutes; tenant only | `StorefrontHomeService` endpoints | Mongo; best sellers Elasticsearch then Mongo degraded candidates; current SQL card recomposition | Shared key omits `affiliateModeOnly`. Best-effort lock/invalidation. Candidate membership is TTL/projection bound. |
| Redis legacy home-category cache | Live exposed API; not used by `useCategories` | 60 minutes; tenant | `StorefrontHomeService.GetHomepageCategoriesAsync` | Mongo category projection, then current SQL category filtering | No category mutation event invalidator; product invalidation may delete it only because the broad home pattern is used. |
| Redis shipping-method cache | Live | 30 minutes; tenant; warehouse-filtered requests bypass | `ShippingMethodService` | Mongo projection; SQL is upstream business truth; no SQL response fallback | Projection consumer deletes after a fresh Mongo write; failure is swallowed and fence completes. A request can also write after invalidation because population is fire-and-forget (`ShippingMethodService.cs:88-185`, `ShippingMethodProjectionConsumer.cs:111-179`, `:300-324`). |
| Redis SEO slug-resolution cache | Live | 60 minutes; tenant/slug | `SeoService.ResolveSlugAsync` | Current SQL authorization for product/category/CMS id/slug/publication | Visibility and canonical slug are checked on every hit, but cached names/response fields can remain stale for 60 minutes (`SeoService.cs:500-545`). |
| Redis SEO robots cache | Live | 12 hours; tenant | `SeoService.GetRobotsTxtAsync` | Current SQL tenant URL resolution on miss | No SEO invalidator. A changed storefront base URL can remain in `Sitemap:` for 12 hours (`SeoService.cs:775-835`). |
| Redis SEO metadata/breadcrumb/sitemap entries | Non-authoritative artifacts | 30 minutes / 60 minutes / 6 hours | Reads occur for metadata/breadcrumbs but hits are ignored; sitemap never reads | Current SQL recomposition every return | They incur writes and expiry but do not accelerate authoritative returns (`SeoService.cs:139-193`, `:288-343`, `:641-770`). |
| Redis category tree/by-slug entries | Write-only artifact | 30 minutes; tenant/tree or slug | No active reader; private read helpers have no call sites | Public category APIs read SQL every time | Fire-and-forget writes; only manual admin sync deletes tree/home keys; slug keys expire naturally (`StorefrontCategoryService.cs:69-92`, `:188-215`, `:470-509`). |
| Redis direct `ProductDocument` cache (`ProductKey`) | Dormant artifact | Default 10 minutes; tenant/product | No production Get/Set caller found | Would be Mongo `ProductDocument` | `ProductCacheService` is used as an invalidator, not as the Storefront product reader (`ProductCacheService.cs:121-167`, `:170-260`). |
| Redis StoreConfig cache | Dormant artifact | 30 minutes; tenant | No production Get/Set caller found | PostgreSQL StoreConfig | Several code paths invalidate it and the projection consumer deletes it, but public StoreConfig bypasses it (`StorefrontConfigProjectionConsumer.cs:373-390`, `StorefrontStoreConfigController.cs:21-26`, `:61-144`). |
| Redis promotion active/product/category key families | Dormant artifact | Comments declare 90 seconds; tenant/scope | No return reader found | PostgreSQL promotion authority and product SQL overlay | Promotion consumers delete these keys and product patterns, but there is no cache population owner (`PromotionCacheService.cs:57-171`). |
| Redis promotion-ranking config | Write-only artifact | 5 minutes; tenant | `PromotionRankingService` writes, never reads it | PostgreSQL `StoreConfig.PromotionRanking` | Service reads SQL on every call and fails closed to disabled, then writes Redis (`PromotionRankingCacheService.cs:41-140`, `PromotionRankingService.cs:41-75`). |
| Redis CMS published-scope cache | Dormant artifact | Default 15 minutes; tenant | No public CMS return reader; private method only writes | Public CMS page/list reads current SQL | CMS publish/delete invalidates it and footer Redis. Public CMS responses bypass it (`CmsPageReadService.cs:74-150`, `RedisInvalidationConsumer.cs:306-315`). |
| Redis Storefront footer cache | Dormant artifact | Default 15 minutes; tenant | `StorefrontFooterReadService` does not use it | Footer API reads current SQL StoreConfig/CMS/social data | Footer and CMS events invalidate it, but it has no production read-through owner (`StorefrontFooterCacheService.cs:35-65`, `StorefrontFooterReadService.cs:35-139`). |
| Redis homepage published-section scope | Registered read-through service, unreachable from a production controller found in this repo | Default 5 minutes; global or tenant | `HomepageSectionReadService` only | SQL homepage-section management records | Super Admin mutations and publish events invalidate; discovery references are SQL-sanitized after the cache (`HomepageSectionReadService.cs:109-125`, `:250-310`). This is not the StoreConfig-driven SPA homepage. |
| `StorefrontUrlResolver` `IMemoryCache` | Write-only artifact | 5 minutes; process/tenant | No read hit path | Current SQL Tenant every call | `ResolveByTenantIdAsync` always queries SQL then calls `_cache.Set`; `EvictCache` has no caller (`StorefrontUrlResolver.cs:93-135`). |
| Redis online-customer profile | Write-through artifact | 15 minutes; tenant/customer | Cache value is read only for logging and ignored | PostgreSQL online customer | SQL is queried on every profile call; projection consumer deletes the entry (`StorefrontOnlineCustomerService.cs:36-56`, `:81-150`). |
| Redis wishlist list/count | Live read-through cache | 10 minutes; tenant/customer | `StorefrontWishlistService` | Mongo wishlist projection; SQL fallback only when Mongo document is absent | Mutation service proactively deletes; projection consumer deletes again after projection. Redis/Mongo errors have different fallbacks; cache failures are swallowed (`StorefrontWishlistService.cs:163-200`, `:320-340`, `:484-623`, `:956-980`). |
| Redis cart document | **Authoritative ephemeral store, not a cache** | 24 hours sliding on writes; tenant/owner | Cart and Checkout | Redis itself; no reconstructive fallback | Redis failure is a 503. Owner-scoped distributed locks protect Cart mutations; `CheckoutService` directly reads/rewrites idempotency fields. Expiry means cart loss, not cache refresh (`CartService.cs:32-58`, `:2534-2566`, `:2871-2991`). |
| `IDistributedCache` memory fallback | Not a Storefront cache fallback | Process lifetime | WhatsApp safety components only | N/A | When Redis is disabled, DI registers distributed memory, but Storefront cache services use `RedisConnectionFactory` or `IConnectionMultiplexer` directly; they do not use this fallback (`RedisServiceCollectionExtensions.cs:37-88`). |

### Browser Inventory

| Cache or retained boundary | Owner | Lifetime / scope | Populate / consume | Refresh / invalidation | Failure behavior |
|---|---|---|---|---|---|
| StoreConfig Zustand snapshot | `useStoreConfigStore` + bootstrap | Module lifetime; one resolved tenant | Bootstrap fetches SQL-backed no-store API once; nearly all runtime/theme/builder presentation reads it | `setConfig` by bootstrap or controlled preview; `reset` exists. No timer/focus/event refresh | Retryable bootstrap failure applies default theme and records error (`bootstrap.ts:164-243`, `storeConfigStore.ts:30-68`). |
| Bootstrap singleton | `bootstrap.ts` | Module lifetime | Retains booted/in-progress/error; suppresses later bootstrap loads | Explicit `resetBootstrap`; retry only | Concurrent callers poll the singleton every 50 ms; success prevents future StoreConfig refresh (`bootstrap.ts:63-118`, `:236-277`). |
| Category module Map | `useCategories` | Module lifetime; tenant key | API success, including empty array; all category consumers | Manual hook `refetch` or exported clear; clear is documented for tests and has no production caller | Fetch error is not cached and can retry; successful stale value is unbounded (`useCategories.ts:23-28`, `:42-110`, `:121-127`). |
| Homepage section product Map | `useHomepageSectionProducts` | Module lifetime; tenant/source/page-size/seed or curated ids | Curated by-id API or canonical `/discover` | Manual `refetch` deletes one key; exported global clear has no production caller | Empty success is cached. Errors are not cached. In-flight requests deduplicate per key (`useHomepageSectionProducts.ts:17-18`, `:81-130`, `:158-235`). |
| Navigation Zustand snapshot | `useNavigationStore` | Module lifetime; not tenant/language keyed internally | `LayoutRuntime` invokes `loadNavigation` | No TTL, reset, force, mutation event, focus, or visibility refresh | Failure stores empty navigation plus error; the error allows a later load attempt. Success is retained forever (`navigationStore.ts:31-65`, `LayoutRuntime.tsx:116-123`). |
| Footer Zustand snapshot | `useFooterStore` | Module lifetime; not tenant/language keyed | `LayoutRuntime` invokes `loadFooter`; request localization comes from `Accept-Language` | No TTL, reset, force, mutation event, focus, visibility, or language-change refresh | Failure stores empty footer plus error and can retry; successful localized footer survives language changes (`footerStore.ts:20-40`, `httpClient.ts:222-226`, `LayoutRuntime.tsx:173-181`). |
| Search autocomplete Map | `useSearchStore` | 30 seconds by trimmed query only | Header suggestion success | Read-time TTL check; store `reset`; old entries pruned only during later writes | Request abort protects latest request. Cache key omits language even though the loader chooses `ar`/`en`; it also omits tenant/affiliate context (`searchStore.ts:145-212`, `searchApi.ts:202`). |
| Full search Zustand result | `useSearchStore` | Until next search/reset/module reload | Unified search response | Query/page/type action or explicit reset; no TTL/focus refresh | Latest request wins through abort; on error the prior result object is not explicitly cleared (`searchStore.ts:241-316`). |
| Catalog discovery component state | `useProductDiscovery` | Mounted hook lifetime | Canonical `/discover` | Query dependency change or exposed manual `refetch` | Latest request id wins; error clears items. No polling/focus/visibility refresh (`useProductDiscovery.ts:116-185`). |
| Product detail and published CMS component state | Page component | Mounted route/slug lifetime | Product/CMS GET | Slug/remount; CMS preview dependencies | Errors display empty/not-found. No background refresh. Published CMS API lacks the preview route's explicit `no-store` (`CmsPage.tsx:13-49`, `StorefrontPagesController.cs:28-97`). |
| Cart Zustand snapshot | `useCartStore` | Module lifetime | Server Redis cart responses plus optimistic local mutations | Cart/checkout/auth entry fetches; mutation reconciliation; 500 ms recovery fetch on stock/item conflicts | Selected fetch failures deliberately preserve the last valid cart; it can remain stale until another fetch. Store is not persisted (`cartStore.ts:326-389`, `:468-543`, `:561-670`). |
| Wishlist Zustand snapshot | `useWishlistStore` | Module lifetime; authenticated customer | Server wishlist/count; optimistic add/remove/clear | Wishlist page/badge fetches, local mutation reconcile/rollback, auth reset | No TTL/focus refresh. Server Redis is event-invalidated, but no server event reaches the browser (`wishlistStore.ts:49-78`, `:113-229`, `:255-264`). |
| Smart-offer memory + session cache | `smartOfferEngine` | Module memory plus tenant-scoped `sessionStorage`; tab session | First provider load from memory, then session storage, then SQL-backed API | `forceRefresh` parameter or `resetSmartOfferSessionCache`; provider calls neither after initial load | API error rejects load. Successful offer list can outlive authoring changes for the entire tab; in-memory slot itself is not tenant-keyed (`smartOfferEngine.ts:10-15`, `:34-65`, `:143-171`, `:241-251`). |
| Theme layout-resolution Map | `ThemeLayoutResolver` | Module lifetime; theme/slot | Dynamic import or eager default | None; hard reload only | Missing/import failure stores default permanently for the module (`ThemeLayoutResolver.ts:10-49`). Presentation-only; frozen Theme ownership is unchanged. |
| Theme section-resolution Map | `ThemeSectionResolver` | Module lifetime; theme/section type | Dynamic import promise or default/null | None; hard reload only | A failed resolution resolves to default/null and that promise is retained (`ThemeSectionResolver.ts:8-16`, `:30-78`). |
| Product-template resolution Map | `ProductTemplateResolver` | Module lifetime; `updatedAtUtc:templateId` | StoreConfig template or built-in fallback after Product Hero injection | Natural new key only when `updatedAtUtc` or template id changes; no clear | Same timestamp plus changed preview object returns the old normalized clone (`ProductTemplateResolver.ts:4-12`, `:44-82`). Builder remains supplemental. |
| Stripe SDK promise Map | `StripePaymentForm` | Module lifetime; publishable key | `loadStripe` promise | No invalidation; key change creates another entry | Rejected/null resolution remains associated with that key until reload (`StripePaymentForm.tsx:56-65`). |
| Browser HTTP cache boundary | Browser/proxy, policy supplied by endpoint | Undefined for endpoints without directives | Storefront GET responses | Endpoint headers, reload, normal HTTP validation | No service worker or Cache Storage code found. Caddy proxies `/api/*` without cache directives. Because policy is inconsistent, maximum reuse is not a repository-defined contract. |

### Retained State That Is Not a Replaceable Cache

These owners persist state across requests or renders and were checked because they can be mistaken for caches:

| State | Single owner | Lifetime / source of truth | Mutation and recovery |
|---|---|---|---|
| Redis cart | `CartService` (with direct reserved-field writes by `CheckoutService`) | 24-hour authoritative ephemeral Storefront cart | Cart API mutations; owner locks; no bypass. This split mutation surface is an observed violation. |
| Checkout workflow persistence | `useCheckoutStore` | Module state; tenant-scoped `localStorage` for non-sensitive selections and `sessionStorage` for sensitive draft state; backend checkout session is authoritative when saved | Store actions/autosave/reset/auth transition; not a catalog cache (`checkoutStore.ts:977-1000`, `:1207-1260`, `:1960-1975`, `:2123-2135`). |
| Storefront session id | `sessionManager` | Tenant-scoped memory Map plus `localStorage`; identity, not business data | Initialize/recover/clear (`sessionManager.ts:33-35`, `:63-123`, `:173-190`). |
| Affiliate referral | `affiliateRefTracker` | Module value plus tenant-scoped `sessionStorage`; tab attribution state | URL/session initialization and explicit clear (`affiliateRefTracker.ts:24-36`, `:67-99`, `:117-132`). |
| Language preference | `languageStore` | Zustand persisted `localStorage`; user preference | User/route update. It does not invalidate language-dependent footer or search entries (`languageStore.ts:119-168`). |
| Auth state and refresh mutex | `authStore` | Module state; HttpOnly cookie/backend session is authority | Bootstrap/check/401 refresh/logout; a single refresh promise and recovery timer serialize refresh. |
| Analytics queue | `AnalyticsQueue` | Memory plus tenant-scoped `localStorage`; pending telemetry delivery | Timer/visibility flush and accepted-id deletion; this is an outbox-like client queue, not read cache. |
| Chat history/session | `useCommerceChat` / `ChatWidget` | Tenant-scoped local/session storage | User/chat actions and explicit clear; not Storefront content authority. |
| Editor parent-sequence dedupe | `ChildRuntimeBridge` instance | In-memory Set bounded to the latest 1,000 sequence ids | Bridge receive path trims the Set (`ChildRuntimeBridge.ts:81`, `:151-158`). |
| Funnel fraud signal registry | `StorefrontFunnelController` static field | Process lifetime; IP × affiliate key; observability only | `GetOrAdd` and atomic increment; no expiry or removal exists, so cardinality is unbounded (`StorefrontFunnelController.cs:85`, `:358-380`, `:546-563`). |
| Static theme/card/commerce registries | Their defining modules | Immutable code manifest for module lifetime | Read-only lookup; no business mutation or invalidation is appropriate. They are registries, not caches. |

### Technology Presence / Absence

- **React Query / TanStack Query:** absent from the customer Storefront dependency set and runtime source. It exists in `PointOfSale.Web`, not `PointOfSale.Storefront` (`PointOfSale.Storefront/package.json`, `PointOfSale.Web/src/queryClient.js:1-15`).
- **SWR:** no Storefront dependency or use found.
- **Service Worker / Cache Storage / Workbox:** no Storefront registration or cache implementation found.
- **CDN cache configuration:** no CDN rule found. The checked Caddy deployment performs reverse proxy/static serving only (`deploy/staging/Caddyfile:14-19`, `:53-71`).
- **ASP.NET response/output cache:** no `Add/UseResponseCaching` or `Add/UseOutputCache` registration found.
- **In-process Storefront response cache:** only `StorefrontUrlResolver` uses `IMemoryCache`, and its entry is not read.
- **Distributed memory fallback:** registered only as `IDistributedCache`; Storefront caches do not consume that abstraction.

## Cache Ownership Matrix

Every cache family in the Storefront graph has one code owner below. “None” under invalidation means no source-proven invalidator, not an authorization recommendation.

| Cache | Owner | Source of Truth | Refresh | Invalidation |
|---|---|---|---|---|
| Mongo product projection | Product projection consumer set through `StorefrontProductProjectionMutationBoundary` | PostgreSQL product aggregate | Product lifecycle/media/variant/stock/price/promotion events; repair/reprojection | Freshness/tombstone rules replace or remove documents; no TTL |
| Elasticsearch product/unified index | `IUnifiedSearchIndexService` | PostgreSQL product/search authority | Event consumers; manual reindex/repair | Index/delete through the unified service; no TTL |
| Redis PDP candidate keys | `StorefrontProductService` | PostgreSQL, with Mongo candidate | Read-through miss, 10-minute expiry | `ProductCacheInvalidationConsumer`; variant consumer partially; manual pattern deletion |
| Redis legacy product-list keys | `StorefrontProductService` | PostgreSQL, with Mongo candidate membership | Read-through miss, 5-minute expiry | Product invalidation consumer products pattern |
| Redis legacy home product keys | `StorefrontHomeService` | PostgreSQL, with Mongo/Elasticsearch candidates | Read-through miss, 15–30-minute expiry | Product invalidation consumer home pattern |
| Redis legacy home category key | `StorefrontHomeService` | PostgreSQL category lineage, with Mongo candidates | Read-through miss, 60-minute expiry | Manual category invalidation or broad product home invalidation |
| Redis shipping methods | `ShippingMethodService` | PostgreSQL via Mongo shipping projection | Read-through miss, 30-minute expiry | `ShippingMethodProjectionConsumer` after applied projection |
| Redis SEO slug | `SeoService` | PostgreSQL product/category/CMS | Read-through miss, 60-minute expiry | None; TTL only |
| Redis SEO robots | `SeoService` | PostgreSQL tenant URL | Read-through miss, 12-hour expiry | None; TTL only |
| Redis SEO metadata/breadcrumb/sitemap artifacts | `SeoService` | PostgreSQL | Rewritten after each authoritative request | None; TTL only; hits are not return authority |
| Redis category artifacts | `StorefrontCategoryService` | PostgreSQL | Fire-and-forget write on every category read | Manual admin sync for tree/home; slug TTL only |
| Redis direct `ProductDocument` artifact | `ProductCacheService` | Mongo projection / upstream PostgreSQL | No active production warmer | Product invalidation consumer |
| Redis StoreConfig artifact | `StoreConfigCacheService` | PostgreSQL | No active production warmer | StoreConfig projection, tenant currency, admin/manual paths |
| Redis promotion key artifacts | `PromotionCacheService` | PostgreSQL promotions | No active production warmer | Promotion projection and product invalidation consumers |
| Redis promotion-ranking artifact | `PromotionRankingCacheService` | PostgreSQL StoreConfig | `PromotionRankingService` rewrites after SQL reads; 5-minute expiry | No active caller of `InvalidateAsync` found; TTL |
| Redis CMS scope artifact | `CmsPageCacheService` | PostgreSQL CMS | No active production return warmer | CMS publish/delete and CMS service mutations |
| Redis footer artifact | `StorefrontFooterCacheService` | PostgreSQL StoreConfig/CMS | No active production return warmer | Footer update and CMS publish/delete events |
| Redis homepage published-section scope | `HomepageSectionCacheService` | PostgreSQL homepage-section records | Read-through only if `HomepageSectionReadService` is invoked | Super Admin mutation and published event |
| URL memory artifact | `StorefrontUrlResolver` | PostgreSQL Tenant | Rewritten after every resolve; 5-minute expiry | `EvictCache`, but no caller found |
| Redis customer-profile artifact | `StorefrontOnlineCustomerService` | PostgreSQL online customer | Rewritten after every profile read; 15-minute expiry | Online-customer projection consumer |
| Redis wishlist list/count | `StorefrontWishlistService` | PostgreSQL via Mongo projection | Read-through miss; 10-minute expiry | Wishlist mutation service and wishlist projection consumers |
| Browser StoreConfig | `useStoreConfigStore` | StoreConfig API / PostgreSQL | Bootstrap or explicit `setConfig` | Explicit `reset`; no authoring event invalidation |
| Browser categories | `useCategories` module | Categories API / PostgreSQL | First read or manual `refetch` | Manual/exported clear; no authoring event invalidation |
| Browser homepage products | `useHomepageSectionProducts` module | Discover/by-id APIs / PostgreSQL-authorized result | First key read or manual `refetch` | One-key delete or exported clear; no authoring event invalidation |
| Browser navigation | `useNavigationStore` | Navigation API / PostgreSQL | First successful layout load | None after success |
| Browser footer | `useFooterStore` | Footer API / PostgreSQL | First successful layout load | None after success, including language change |
| Browser autocomplete | `useSearchStore` | Search API / Elasticsearch candidates + SQL | Miss after 30-second TTL | Store reset/read-time expiry |
| Browser full search | `useSearchStore` | Search API / Elasticsearch candidates + SQL | Search action | Next action/reset |
| Browser smart offers | `smartOfferEngine` | Smart-offer API / PostgreSQL | First module/tab load or `forceRefresh` | Explicit reset; no authoring event invalidation |
| Browser theme layout modules | `ThemeLayoutResolver` | Compiled module manifest | First theme/slot resolution | None; module reload |
| Browser theme section modules | `ThemeSectionResolver` | Compiled module manifest | First theme/section resolution | None; module reload |
| Browser product templates | `ProductTemplateResolver` | StoreConfig plus built-in registry | First timestamp/template resolution | Natural key change only; no clear |
| Browser HTTP response reuse | Browser/proxy under server endpoint policy | API response | Normal HTTP rules | Explicit `no-store` only on selected routes; otherwise undefined by repo |

## Canonical Cache Contract

This is the source-derived contract baseline. It records who currently may operate each boundary; it does not propose a recovery design.

### Authority Rules

1. PostgreSQL alone may mutate Storefront business truth.
2. Product, StoreConfig, shipping, wishlist, and other projection consumers may mutate only their owned Mongo projection after proving event/SQL freshness.
3. Only `IUnifiedSearchIndexService` and the registered projection/reindex callers may mutate the Storefront Elasticsearch index.
4. A cache owner may write only its own key family. An invalidation consumer may delete a key family only in response to its declared event or an explicit admin repair action.
5. Browser presentation code may retain API results, but those copies are derivative and must never be treated as authoring or business authority.
6. Redis cart is excluded from derivative-cache rules. It is authoritative ephemeral state and cannot fail open to a stale projection.

### Permission Matrix

| Cache | Owner | May mutate | May invalidate | May warm | May read | May bypass |
|---|---|---|---|---|---|---|
| Mongo product projection | Product projection mutation boundary | Product projection consumers after SQL/event freshness proof | Tombstone/delete-capable projection consumers and repair paths | Projection/reconciliation paths | Storefront product/home/SEO/wishlist/search hydration services | Public response layers only by using direct SQL authority paths; they may not write around the boundary |
| Elasticsearch product/unified index | `IUnifiedSearchIndexService` | Registered product/media/variant/stock/price/promotion consumers and explicit reindex tools through the service | Same service delete operations | Index initializer/reindex controllers | Discovery/search/best-seller readers | PDP, curated by-id, and SQL category paths |
| Redis product/list/home families | Their Storefront read service (`StorefrontProductService` or `StorefrontHomeService`) | Owning read-through service | Product invalidation consumer; reviewed variant invalidator; manual admin repair | First request only; no startup tenant warm-up | Owning Storefront read services | `/discover`; cache failure; SQL recomposition always bypasses cached field authority |
| Redis shipping | `ShippingMethodService` | `ShippingMethodService` | `ShippingMethodProjectionConsumer` | First unscoped request | `ShippingMethodService` | Warehouse-scoped request and Redis failure; no SQL response bypass |
| Redis SEO families | `SeoService` | `SeoService` | No active invalidator | First request / every request for ignored-hit artifacts | `SeoService` | Metadata, breadcrumbs, and sitemap always bypass hit authority; slug/robots do not |
| Redis category artifacts | `StorefrontCategoryService` | `StorefrontCategoryService` | Manual `AdminDataSyncController`; promotion invalidator can delete tree | Every SQL category request writes | No active production reader | All public category paths always bypass |
| Redis direct product / StoreConfig / promotion / promotion-ranking artifacts | Their named cache service | Only the named service | Named consumers/controllers shown in the ownership matrix | No active warmer, except promotion-ranking writes after SQL reads | No active return reader | All live Storefront returns bypass |
| Redis CMS/footer artifacts | Their named cache service | Named cache service only | CMS/footer event consumer and authoring services | No effective warm-up; startup code only performs `Get` calls | No active Storefront return reader | Public CMS/footer paths always bypass |
| Redis wishlist | `StorefrontWishlistService` | `StorefrontWishlistService` | Same service after mutation and projection consumers | First cache miss | Wishlist service | Redis failure to Mongo; Mongo absence to SQL |
| Browser StoreConfig | `useStoreConfigStore` | Bootstrap and controlled preview/editor bridge via `setConfig` | Store reset | Bootstrap | Storefront runtime, Theme presentation, Builder supplemental render, checkout selectors | Hard reload/bootstrap retry; no live event bypass |
| Browser categories | `useCategories` | Hook loader | Hook force refresh/exported clear | First hook mount | Category/product/navigation presentation | Manual refetch or module reload |
| Browser homepage products | `useHomepageSectionProducts` | Module loader | Hook force refresh/exported clear | First key use | Runtime section containers | Manual refetch or module reload |
| Browser navigation/footer | Respective Zustand store | Respective `load*` action | No success invalidator | `LayoutRuntime` first load | Layout presentation/selectors | Module reload; error permits retry |
| Browser search | `useSearchStore` | Search/autocomplete actions | `reset`, TTL check for autocomplete | User search/suggest action | Header modal/search page | Force next request by expiry/reset/query change |
| Browser smart offers | `smartOfferEngine` | Loader and display-frequency functions | Explicit session reset | `OfferProvider` initial load | Offer provider/eligibility helpers | `forceRefresh` or tab/module reset |
| Browser theme/layout/template resolution | Respective resolver module | Resolver only | None | First resolution | Presentation layer | Hard reload or natural new template key only |
| Browser HTTP cache | Browser/proxy, constrained by API response headers | Browser/proxy under HTTP rules | Endpoint `no-store`/validators and browser controls | Response receipt | Browser/proxy | `no-store`, reload, cache-busting request; policy missing on many routes |
| Redis cart authoritative store | `CartService` | Cart service; `CheckoutService` currently directly mutates reserved idempotency fields | Cart clear/expiry | Cart creation/mutation | Cart service and Checkout snapshot path | Nobody; unavailable Redis fails closed |

### Contract Violations Against This Baseline

- Browser caches read authoritative responses but receive no invalidation signal from their mutation/projection owners.
- Checkout directly mutates a Redis document nominally owned by `CartService`.
- Several dormant cache services have mutation/invalidation APIs despite no active read owner.
- HTTP cache ownership is undefined on numerous Storefront routes.
- The legacy home cache owner does not include all loader authority inputs in its key.

## Cache Flow

### Product Authoring to Canonical Discovery

```text
POS product authoring
  -> PostgreSQL transaction (business truth)
  -> EF transactional outbox
  -> RabbitMQ
      -> Mongo product projection consumer(s)
      -> Elasticsearch consumer(s) -> IUnifiedSearchIndexService
      -> ProductCacheInvalidationConsumer -> Redis product/list/home keys

GET /api/storefront/products/discover
  -> Elasticsearch candidate ids/order/filter set
  -> current PostgreSQL ProductSearchSqlAuthorityOverlay
  -> public response
  -> useProductDiscovery component state
     OR useHomepageSectionProducts module Map
  -> presentation
```

Cache boundaries: outbox delivery lag, Mongo projection, Elasticsearch projection, Redis legacy/PDP caches, browser retained data, and optionally HTTP reuse. Only the server response recomposition reaches SQL. No event continues to the browser.

### Product Detail

```text
GET product by slug/id
  -> Redis DTO candidate (10 min)
       miss -> Mongo ProductDocument candidate
  -> discard candidate fields and reload current SQL product/media/variant authority by id
  -> public DTO
  -> ProductDetailsPage component state
  -> presentation
```

The Redis hit cannot override current product fields or visibility. It can still provide only the id of a previously known candidate, and Mongo/index absence can hide a newly eligible candidate until projection convergence.

### StoreConfig, Theme, Builder Sections, Navigation, and Footer

```text
Admin StoreConfig / Theme / Builder / Navigation / Footer mutation
  -> PostgreSQL + outbox event
  -> Mongo StorefrontConfig/Navigation projection (derivative)
  -> dormant Redis StoreConfig/footer/navigation-era deletions

Public StoreConfig/navigation/footer request
  -> current PostgreSQL authority
  -> browser bootstrap/Zustand snapshot
  -> LayoutRuntime / Theme presentation / Builder supplemental sections
  -> presentation
```

The backend intentionally bypasses Redis for these current public APIs. The browser copy then becomes the only effective cache and has no live invalidation.

### Shipping

```text
Admin shipping mutation
  -> PostgreSQL
  -> outbox/RabbitMQ
  -> ShippingMethodProjectionConsumer
       -> fresh Mongo upsert/tombstone
       -> Redis tenant shipping-key delete
  -> next Storefront request
       -> Redis (30 min) or Mongo
       -> no-store HTTP response
       -> checkout/browser state
```

### Wishlist

```text
Storefront wishlist mutation
  -> PostgreSQL + outbox event
  -> immediate Redis list/count delete
  -> OnlineCustomerWishlistProjectionConsumer
       -> Mongo wishlist projection
       -> Redis list/count delete again
  -> next read: Redis -> Mongo -> SQL only when Mongo document is absent
  -> wishlist Zustand state
```

### Cart

```text
Browser cart Zustand
  <-> Cart API
  <-> Redis cart document (authoritative, 24 h)
         ^ CartService mutations under owner lock
         ^ CheckoutService direct idempotency-field rewrite
```

There is no SQL/Mongo cart source capable of reconstructing an expired or unavailable Redis cart.

## Invalidation Graph

| Mutation | SQL/event owner | Projection refresh | Server cache invalidation | Browser invalidation | Certification result |
|---|---|---|---|---|---|
| Product create/update/delete | `ProductAuthoringOrchestrator` and remaining product mutation endpoints emit lifecycle events | Mongo product and Elasticsearch consumers | Product detail, lists, home, product-promotion key through `ProductCacheInvalidationConsumer` | None | **Gap:** browser categories/home products/search/full-page snapshots stay stale; projection absence remains possible. |
| Product stock/price/publish | Product stock/price/publication events | Mongo/Elasticsearch consumers | Same broad product/home invalidator | None; cart may refresh 500 ms only after selected mutation conflicts | **Gap:** current open pages retain snapshots. |
| Product variant | Variant authoring events; some paths also emit product/stock events | `ProductVariantProjectionConsumer` updates Mongo/Elasticsearch | Its direct invalidator deletes normal id/slug, `products:all`, featured, new-arrival keys; broader lifecycle/stock event coverage supplies other deletions | None | **Observation:** direct variant invalidator omits affiliate detail keys, list patterns, and best sellers (`ProductVariantProjectionConsumer.cs:1683-1725`). SQL recomposition limits field leakage. |
| Product media | `ProductMediaService` emits `ProductMediaChangedEvent` | Product media consumer updates Mongo and unified search index | No product Redis invalidator consumes the media event | None | SQL overlay returns current media server-side, but already-retained browser product cards/details stay stale. |
| Promotion | Promotion authoring events | Promotion projection updates Mongo product promotion fields and Elasticsearch | Promotion cache service deletes promotion and product patterns; product home membership keys are not all directly owned here | None | Server SQL overlay corrects returned pricing; browser cards remain stale. |
| Category create/update/delete | `CategoriesController` writes SQL | No general category lifecycle event/cache consumer found | Only manual `AdminDataSyncController`; promotion invalidator can incidentally delete tree | None | **Gap:** browser category Map and legacy home-category Redis have no mutation-coupled invalidation. |
| StoreConfig publish/update/rollback/create | Admin StoreConfig/theme services | `StorefrontConfigProjectionConsumer` refreshes Mongo | Deletes dormant Redis StoreConfig key | None | **Gap:** bootstrap/Zustand, template resolver, Theme and Builder presentation do not refresh. |
| Navigation publish | `AdminCmsService` emits `NavigationPublishedEvent` | `NavigationProjectionConsumer` refreshes Mongo | Deletes legacy `storefront:navigation/footer/menu/header:{tenantSlug}` keys | None | Current API reads SQL, so server deletion is irrelevant; navigation Zustand remains stale. |
| Footer update | Admin/Super Admin footer services emit `StorefrontFooterUpdatedEvent` | Event path persists authority/projection as applicable | `RedisInvalidationConsumer` deletes dormant footer Redis | None | Footer Zustand remains stale; localized value also survives language change. |
| CMS publish/delete | `CmsPageService` emits CMS events | CMS projection paths | Deletes dormant CMS scope and footer Redis | No global invalidation; mounted CMS component refreshes only on slug/preview dependency change | Navigation/footer browser snapshots can retain links/content derived before publication. |
| Homepage section publish (separate section subsystem) | Super Admin section service | SQL records are already authority | Homepage-section and marketplace-homepage Redis invalidated | None | This subsystem is not the StoreConfig-driven SPA homepage. |
| Smart offer create/update/delete | `AdminSmartOffersController` emits smart-offer events | `SmartOfferProjectionConsumer` refreshes Mongo | No Storefront browser cache invalidator | None | **Gap:** module/session offer list remains stale for tab lifetime. Redeem rechecks SQL, limiting unauthorized redemption. |
| Shipping method update/delete | Shipping admin event | Freshness-guarded Mongo update/tombstone | Shipping projection consumer deletes Redis key | None | Server path converges on next miss; browser checkout state refresh is route/action driven. |
| Tenant currency | Tenant currency event | StoreConfig/shipping projection consumers | StoreConfig cache invalidated; shipping projection event path determines shipping cache refresh | None | StoreConfig/browser currency can remain stale until reload. |
| Wishlist add/remove/clear | `StorefrontWishlistService` SQL/outbox | Wishlist projection consumer | Immediate service delete and post-projection delete | Optimistic local mutation/reconcile only in initiating tab | Other tabs/sessions stay stale until fetch. |
| Cart add/update/remove/clear | `CartService` | None; Redis is authority | Mutation overwrites/deletes authoritative document | Initiating store reconciles; no cross-tab invalidation | No derivative invalidation is required, but HTTP and multi-tab refresh contracts are absent. |
| Reviews | No tenant Storefront dynamic review runtime/cache consumer found; Marketplace reviews are a separate runtime | Marketplace review summary projection | Marketplace review/rating Redis keys only | None in customer Storefront | No tenant Storefront review cache to invalidate. |
| Testimonials | Builder-authored StoreConfig content | StoreConfig projection | Dormant StoreConfig Redis only | None | Cached in browser StoreConfig/template resolution until reload. |
| Recommendations | No implemented Storefront recommendation cache or mutation owner found | None | None | None | Not present; no cache contract can be certified for a nonexistent surface. |

## Refresh Graph

```text
Automatic source refresh
  SQL commit -> outbox -> RabbitMQ -> projection consumers -> Mongo/Elasticsearch

Automatic cache refresh
  Redis miss/expiry -> request-thread read-through
  Autocomplete TTL expiry -> next access fetch
  Cart/wishlist mutation -> initiating response/store reconciliation

Manual refresh
  category hook refetch
  homepage-products hook refetch
  discovery/search action
  Storefront hard reload / bootstrap retry
  admin category sync / search reindex / projection repair

Absent for Storefront business caches
  window-focus refresh
  visibility refresh
  catalog/config/navigation/footer polling
  background stale-while-revalidate
  server push / websocket invalidation
  cross-tab Storefront content invalidation
```

Detailed findings:

- `useProductDiscovery` refreshes only on query dependency changes or its explicit `refetch` (`useProductDiscovery.ts:133-185`).
- Categories and homepage products implement in-flight deduplication, not background refresh (`useCategories.ts:56-93`, `useHomepageSectionProducts.ts:88-130`).
- Navigation/footer load once after `LayoutRuntime` mounts and suppress later successful loads (`navigationStore.ts:36-56`, `footerStore.ts:25-39`).
- StoreConfig bootstrap is idempotent and returns immediately once bootstrapped (`bootstrap.ts:91-118`).
- Smart offers support `forceRefresh`, but `OfferProvider` calls `loadSmartOffers()` without it (`OfferProvider.tsx:67`, `smartOfferEngine.ts:143-171`).
- Timers found in analytics, auth recovery, payment status, checkout pricing, offer display, and UI feedback do not refresh catalog/config/navigation/footer caches.
- `CacheWarmupService` runs once after five seconds, but it warms only a Redis connection and calls `Get` on Marketplace footer/CMS cache services. It does not populate a miss and explicitly avoids tenant caches (`CacheWarmupService.cs:15-22`, `:50-100`, `:112-143`).
- Hard reload clears module/Zustand/React state, but tenant-scoped session/local storage survives according to browser storage lifetime.

## Hidden Cache Owners

### Proven Hidden Owners

1. Module Maps in `useCategories`, `useHomepageSectionProducts`, `ThemeLayoutResolver`, `ThemeSectionResolver`, `ProductTemplateResolver`, `sessionManager`, and Stripe SDK loading.
2. Zustand singleton stores for StoreConfig, navigation, footer, search, cart, wishlist, auth, and checkout.
3. Module singleton flags/promise state in bootstrap, auth refresh, smart offers, analytics queue, and checkout autosave.
4. Session/local storage for smart offers, checkout, language, tenant session, affiliate attribution, analytics, and chat.
5. The static process-wide `_fraudSignalStore` `ConcurrentDictionary`, which has no eviction.
6. Dynamic import resolution itself plus explicit resolver Maps that retain defaults/failures.
7. Event-fence rows in SQL, which are idempotency state rather than response caches but determine whether a later invalidation may run.
8. Redis refresh locks, cart mutation locks, rate-limiters, OTP state, JWT blacklist state, and idempotency keys. These are operational coordination/security state, not derivative caches.

### Cache Override / Direct Mutation Findings

- `CheckoutService` directly reads and writes `CacheKeyBuilder.CartKey`, bypassing the nominal Cart owner for two fields (`CheckoutService.cs:3552-3598`).
- `ProductVariantProjectionConsumer` directly deletes product Redis keys rather than using the full `IProductCacheService` invalidation family (`ProductVariantProjectionConsumer.cs:1683-1725`).
- `StorefrontConfigProjectionConsumer` directly deletes the StoreConfig Redis key rather than invoking `IStoreConfigCacheService` (`StorefrontConfigProjectionConsumer.cs:373-390`).
- `ShippingMethodProjectionConsumer` directly constructs/deletes the shipping key (`ShippingMethodProjectionConsumer.cs:300-315`) while the reader uses the centralized builder (`ShippingMethodService.cs:88-92`). The current literal matches the builder, but ownership is duplicated.
- `NavigationProjectionConsumer` deletes four legacy literal keys that the current SQL-backed navigation API does not read (`NavigationProjectionConsumer.cs:459-480`).
- Category and SEO services launch cache writes without awaiting the caller; shipping uses `Task.Run`. An invalidation can occur before an older request's late write, resurrecting stale content until TTL.
- No global cache-replacement hook, service-worker cache, LRU in customer Storefront, or runtime cache provider replacement was found.

## Concurrency Behavior

| Boundary | Proven behavior | Consequence |
|---|---|---|
| Common Redis JSON read-through | Distributed refresh lock with 15-second default TTL. If lock is unavailable or not acquired, caller waits 120 ms and then may execute the factory anyway (`RedisSettings.cs:64-78`, `RedisJsonCacheService.cs:214-243`). | Best-effort stampede reduction, not single-writer serialization. |
| Redis invalidation | Failures are logged/swallowed; consumers often finish their SQL event fence anyway. | No guaranteed retry of a failed deletion; TTL/manual action recovers. |
| Product projection/index | Event fences and freshness/tombstone guards; current SQL overlay on public return. | Old events do not overwrite newer projection data; missing candidates remain the main stale mode. |
| Browser category/home Maps | One in-flight promise per cache key. | Same-key duplicate fetches collapse. Manual forced category refresh can run alongside an old promise; last completion can determine the module entry. |
| Search store | Aborts the prior request; applies response only if not aborted. | Latest requested search generally wins; cache key scope remains incomplete. |
| Cart | Owner-scoped distributed mutation lock with renewal. | Normal Cart mutations serialize per owner; direct Checkout document mutation expands the write surface. |
| StoreConfig bootstrap | Boolean singleton plus 50 ms polling for a concurrent bootstrap. | Concurrent bootstrap calls collapse, but there is no later freshness check. |
| Dynamic module resolvers | Promise/value memoization per resolution key. | Duplicate imports collapse; failures/defaults become sticky. |
| Funnel fraud registry | `ConcurrentDictionary` + `Interlocked`. | Thread-safe counts but unbounded process memory/cardinality. |

## Stale Data Risks

Maximum stale lifetime is reported as **unbounded** when the repository provides no time guarantee. Eventual delivery/retry is a recovery mechanism, not a maximum.

| Data / location | Maximum stale lifetime | Why stale data can exist | Recovery path | Owner | Risk |
|---|---:|---|---|---|---|
| Mongo product/config/shipping/wishlist projections | Unbounded | Async outbox/consumer lag or terminal poison/failure has no time SLA in source | Consumer retry, replay, repair/reconciliation, later mutation | Respective projection consumer | High for candidate absence; current SQL overlay suppresses many stale fields |
| Elasticsearch discovery/search | Unbounded | Async indexing; index can omit a new/changed entity indefinitely under unresolved failure | Consumer retry, manual reindex/repair | `IUnifiedSearchIndexService` mutation boundary | High; SQL cannot add absent candidates |
| Redis PDP | 10 minutes per entry, but upstream candidate absence unbounded | Miss may see stale/missing Mongo; failed invalidation; late repopulation | TTL, event deletion, request miss; SQL overlay on every hit | `StorefrontProductService` | Medium; field/visibility staleness is strongly limited by SQL overlay |
| Redis legacy product list | 5 minutes per entry, upstream projection unbounded | Cached membership cannot gain newly eligible ids | Product event deletion, TTL, Mongo recovery | `StorefrontProductService` | Medium; deprecated route |
| Redis legacy home products | 15–30 minutes per entry, upstream projection unbounded | Cached candidate membership; failed deletion; late write; affiliate key collision | Product event deletion, TTL, source convergence | `StorefrontHomeService` | High on exposed route; current SPA does not call it |
| Redis home categories | 60 minutes per entry, upstream projection unbounded | No category event invalidation | Manual admin invalidation, TTL, projection recovery | `StorefrontHomeService` | High on exposed route |
| Redis shipping | 30 minutes per entry, projection unbounded | Best-effort delete, late fire-and-forget write, Mongo lag | Projection event delete, TTL, next miss | `ShippingMethodService` | High in checkout availability |
| Redis SEO slug | 60 minutes for non-authority fields | SQL checks id/slug/publication but returns cached names | TTL | `SeoService` | Medium |
| Redis robots | 12 hours | No invalidation for tenant URL changes | TTL | `SeoService` | Medium |
| Dormant Redis/memory artifacts | Their TTLs; no public response effect | Written but not used or hits ignored | TTL/process restart/manual deletion | Named artifact owner | Low runtime risk; high architecture ambiguity |
| Browser StoreConfig/theme/builder sections/testimonials | Unbounded until module reload | Bootstrap success is never refreshed | Hard reload, explicit bootstrap/store reset, controlled preview update | `useStoreConfigStore` | Critical because it governs broad presentation/config behavior |
| Browser categories | Unbounded until refetch/reload | Module Map has no TTL/event invalidation | Hook refetch, exported clear, hard reload | `useCategories` | High |
| Browser homepage products/media/prices/promotions | Unbounded until refetch/reload | Module Map has no TTL/event invalidation | Hook refetch, clear, hard reload | `useHomepageSectionProducts` | Critical on active homepage shelves |
| Browser navigation | Unbounded until reload | Successful Zustand load suppresses all later loads | Hard reload | `useNavigationStore` | High |
| Browser localized footer | Unbounded until reload | Successful Zustand load suppresses later loads; language absent from key/state | Hard reload; failed loads alone can retry | `useFooterStore` | High and user-visible on language switch |
| Browser autocomplete | 30 seconds | Query-only key reuses data across language | TTL on next access or reset | `useSearchStore` | Medium |
| Browser full search/discovery/PDP/CMS component state | Mounted-page lifetime | No focus/timer refresh | Query/slug change, manual action where exposed, remount/reload | Owning hook/page | Medium |
| Browser smart offers | Tab/session lifetime | Memory/session cache has no TTL or event link | Explicit force/reset, tab close, reload only if session cache cleared | `smartOfferEngine` | High for offer availability; redeem endpoint rechecks SQL |
| Browser HTTP reuse on routes without policy | Undefined / not contractually bounded | Endpoint and proxy do not state a uniform policy | Browser reload/validation; endpoint-specific headers | Endpoint/browser boundary | High because behavior is outside the canonical contract |
| Browser cart | Until next fetch/mutation/remount | Selected errors preserve last valid cart | Explicit/route fetch, conflict recovery, mutation response | `useCartStore` | Medium; checkout revalidates server authority |
| Browser wishlist | Until next fetch/mutation/reload | No cross-tab/server-event refresh | Page/badge fetch or local mutation | `useWishlistStore` | Medium |
| Resolver import fallback | Module lifetime | Failure/default/null is memoized | Hard reload/new deployment | Theme resolver | Low; presentation-only |
| Product template memo | Module lifetime for same timestamp/id | Cache key assumes `updatedAtUtc` changes with content | New timestamp/id or hard reload | `ProductTemplateResolver` | Medium in preview/editor scenarios |

## Duplicate Ownership

Duplicate copies are not automatically duplicate authority. The following cases do create ambiguous or duplicated operational ownership:

1. **Product data:** SQL business truth, Mongo candidates, Elasticsearch candidates, Redis candidates, browser Maps/state, and possible HTTP reuse each retain a copy. SQL is canonical, but invalidation stops before browser copies. No single end-to-end cache owner exists.
2. **StoreConfig:** SQL is canonical; Mongo and Redis are derivative; Redis is dormant; browser Zustand and product-template memo are live. StoreConfig events refresh Mongo/delete Redis but not the browser copy.
3. **Category:** current public API owns an SQL read, while the same service still writes Redis that it never reads; Mongo still exists for legacy/home candidate paths; the browser owns an unbounded Map.
4. **Footer/CMS:** SQL-backed read services coexist with cache services and invalidation consumers that no live return path uses, while the browser maintains the effective cache.
5. **Cart:** `CartService` declares Redis the only cart store, while `CheckoutService` directly rewrites the same serialized document.
6. **Shipping invalidation:** the reader owns key construction through `CacheKeyBuilder`, while the projection consumer owns an equivalent literal delete.
7. **Product invalidation:** both `ProductCacheInvalidationConsumer` through `IProductCacheService` and `ProductVariantProjectionConsumer` direct Redis deletion own overlapping key sets with different coverage.
8. **Navigation:** the projection consumer invalidates legacy Redis keys while the live server path is SQL and the browser Zustand cache has no invalidator.

The canonical source of truth is usually unambiguous; the cache owner and invalidation owner are not.

## Architectural Violations

### C-01 — No end-to-end Storefront invalidation

Server events terminate at projections/Redis. They do not invalidate browser StoreConfig, category, homepage-product, navigation, footer, search, smart-offer, or mounted component snapshots.

### C-02 — Unbounded successful browser caches

Categories, homepage products, navigation, footer, StoreConfig, and smart offers retain successful data without a TTL or automatic refresh.

### C-03 — Best-effort invalidation can be permanently acknowledged

Deletion failures are swallowed inside cache services/consumers. Event fences can be completed after the best-effort call returns, preventing duplicate event delivery from reattempting that specific invalidation.

### C-04 — Projection-lag guard is structurally present but operationally inactive

The common cache envelope requires caller-supplied lag limit and two timestamps. Active Storefront product/home calls do not provide the complete inputs, so the configured 45-second maximum is not a certified maximum.

### C-05 — Legacy home cache key omits affiliate authority input

The cache keys are tenant-only while the loaders use `affiliateModeOnly`. This permits incomplete ordinary results if the affiliate-filtered request populates first.

### C-06 — Dormant/write-only cache families

Multiple cache services are registered, populated, or invalidated with no active return reader. They create false ownership signals, needless side effects, and audit ambiguity.

### C-07 — HTTP cache policy is incomplete

There is no uniform Storefront GET policy. Sensitive/volatile cart, wishlist, product, navigation, footer, published CMS, smart-offer, and search responses do not establish a repository-wide explicit browser/proxy contract.

### C-08 — Cart mutation authority is split

`CartService` declares Redis the sole authoritative cart store, while Checkout directly rewrites the same document.

### C-09 — Invalidation/repopulation race

Fire-and-forget category/SEO/shipping writes and request-driven Redis population have no generation token tied to invalidation. An older request can write after a newer mutation's delete.

### C-10 — Language-dependent cache keys are incomplete

Footer Zustand is not reloaded/keyed on language. Search autocomplete is keyed only by query although language participates in the request.

### C-11 — Hidden unbounded process registry

The Storefront funnel fraud-signal `ConcurrentDictionary` has no TTL, maximum, or removal.

## Risk Assessment

| Risk | Severity | Evidence-based impact |
|---|---|---|
| Browser StoreConfig/home/category/navigation/footer/smart-offer staleness | Critical | Active presentation can remain stale indefinitely without reload. |
| Elasticsearch/Mongo candidate absence | High | Current SQL filtering prevents stale values from leaking but cannot add missing candidates; no finite convergence SLA. |
| Best-effort deletion acknowledged by event fences | High | A transient Redis failure can persist until TTL and will not necessarily retry from duplicate delivery. |
| Legacy affiliate cache-key collision | High | Request authority input is missing from key; exposed API can serve incomplete normal results. |
| Missing HTTP cache contract | High | Browser/proxy behavior and maximum stale life are not certified for many volatile or customer-specific GETs. |
| Cart split mutation authority | High | Two services serialize and rewrite the same authoritative Redis document. |
| Shipping cache race/lag | High | Checkout choices can remain stale for TTL or longer if the projection is not converged. |
| Language-key omissions | High | Footer can remain in the previous language indefinitely; search suggestions for 30 seconds. |
| Dormant caches and direct invalidators | Medium | Misleading ownership, duplicate code paths, avoidable Redis work, and inconsistent invalidation sets. |
| SEO slug/robots TTL-only freshness | Medium | Names or sitemap base URLs can lag 60 minutes / 12 hours. |
| Dynamic-import negative/fallback memoization | Low | A transient import failure remains for module life; frozen Theme presentation ownership is unaffected. |
| Unbounded funnel registry | Medium operational | Process memory grows with unique IP × affiliate pairs until restart. |

## Certification

### Result: FAIL

The Cache Runtime does not satisfy the success criteria:

- **Who owns every cache?** Individual code owners can be named, but overlapping direct mutation/invalidation paths and dormant cache services prevent one canonical end-to-end owner.
- **Who invalidates every cache?** Server Redis/projection invalidators are identifiable; multiple live browser caches and SEO caches have no mutation-coupled invalidator.
- **Who refreshes every cache?** Redis read-through, event projections, and manual browser actions are identifiable; several successful browser caches have no automatic refresh.
- **Who may mutate every cache?** Most families have a named owner; Redis cart and overlapping direct Redis invalidators violate exclusive mutation boundaries.
- **Does duplicate cache ownership exist?** Yes, especially around product, StoreConfig, category, footer/CMS, cart, and invalidation paths.
- **Do stale-data risks exist?** Yes. Several are unbounded by source-defined time.
- **Is the cache architecture canonical?** No.

This document is the canonical Cache Contract baseline for a later recovery phase. It does not implement fixes, reopen completed recoveries, begin Cache Recovery, or begin Final Storefront Certification.

## Primary Source Index

### Core cache infrastructure

- `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs:33-93`
- `PointOfSale.Infrastructure/Redis/Configuration/RedisSettings.cs:24-88`
- `PointOfSale.Infrastructure/Redis/CacheKeyBuilder.cs:17-24`, `:90-172`, `:190-307`, `:345-454`
- `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs:76-193`, `:199-359`, `:365-393`
- `PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs:3-35`

### Projection and invalidation

- `PointOfSale.Infrastructure/Consumers/ProductCacheInvalidationConsumer.cs:36-55`, `:163-228`, `:244-311`
- `PointOfSale.API/Consumers/RedisInvalidationConsumer.cs:19-45`, `:86-169`, `:236-345`
- `PointOfSale.Infrastructure/Consumers/ShippingMethodProjectionConsumer.cs:17-33`, `:91-179`, `:263-324`
- `PointOfSale.Infrastructure/Consumers/StorefrontConfigProjectionConsumer.cs:21-35`, `:126-174`, `:237-415`
- `PointOfSale.Infrastructure/Consumers/NavigationProjectionConsumer.cs:459-480`
- `PointOfSale.Infrastructure/Consumers/ProductVariantProjectionConsumer.cs:1683-1725`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductDiscoveryService.cs:14-25`, `:93-175`, `:547-617`
- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs:108-318`

### Server Storefront paths

- `PointOfSale.API/Services/Storefront/StorefrontProductService.cs:55-121`, `:132-196`, `:561-639`, `:642-791`
- `PointOfSale.API/Services/Storefront/StorefrontHomeService.cs:85-103`, `:126-180`, `:188-242`, `:250-315`, `:421-460`
- `PointOfSale.API/Services/Storefront/StorefrontCategoryService.cs:69-92`, `:151-180`, `:188-215`, `:430-509`
- `PointOfSale.API/Services/Storefront/ShippingMethodService.cs:24-45`, `:71-188`
- `PointOfSale.API/Services/Storefront/SeoService.cs:117-193`, `:272-343`, `:500-634`, `:641-835`
- `PointOfSale.API/Services/Storefront/StorefrontWishlistService.cs:163-200`, `:320-340`, `:484-623`, `:956-980`
- `PointOfSale.API/Services/Cart/CartService.cs:32-58`, `:151-249`, `:2534-2566`, `:2871-3057`
- `PointOfSale.API/Services/Checkout/CheckoutService.cs:3423-3470`, `:3552-3598`
- `PointOfSale.API/Controllers/StorefrontStoreConfigController.cs:21-26`, `:49-144`
- `PointOfSale.API/Controllers/StorefrontNavigationController.cs:29-91`
- `PointOfSale.API/Services/Storefront/StorefrontFooterReadService.cs:35-139`
- `PointOfSale.API/Controllers/StorefrontSmartOffersController.cs:42-97`, `:100-176`

### Browser Storefront paths

- `PointOfSale.Storefront/src/bootstrap/bootstrap.ts:63-118`, `:164-277`
- `PointOfSale.Storefront/src/store/storeConfigStore.ts:30-68`
- `PointOfSale.Storefront/src/hooks/useCategories.ts:23-127`
- `PointOfSale.Storefront/src/hooks/useHomepageSectionProducts.ts:17-18`, `:81-130`, `:134-249`
- `PointOfSale.Storefront/src/hooks/useProductDiscovery.ts:116-185`
- `PointOfSale.Storefront/src/stores/navigationStore.ts:31-65`
- `PointOfSale.Storefront/src/stores/footerStore.ts:20-40`
- `PointOfSale.Storefront/src/stores/searchStore.ts:100-212`, `:241-316`
- `PointOfSale.Storefront/src/storefront/services/smartOfferEngine.ts:10-15`, `:34-65`, `:143-171`, `:225-251`
- `PointOfSale.Storefront/src/storefront/themes/ThemeLayoutResolver.ts:10-49`
- `PointOfSale.Storefront/src/storefront/themes/ThemeSectionResolver.ts:8-78`
- `PointOfSale.Storefront/src/storefront/products/ProductTemplateResolver.ts:4-82`
- `PointOfSale.Storefront/src/storefront/runtime/layout/LayoutRuntime.tsx:116-123`, `:173-181`
- `PointOfSale.Storefront/src/services/api/httpClient.ts:222-226`

## Adjacent Non-Storefront Caches

The following were discovered and deliberately excluded from the customer Storefront ownership matrix because they belong to separate applications/runtimes:

- POS Admin `queryClient`, Marketplace QueryClient, and Super Admin QueryClient: each uses 30-second `staleTime`, one retry, and `refetchOnWindowFocus: false` (`PointOfSale.Web/src/queryClient.js:1-15`).
- POS Admin custom `posRuntimeCache`: tenant/user-scoped module Map, 30-second TTL, 200-entry/5 MiB bounds, sequence/generation protection, timer eviction, and cross-tab local-storage invalidation (`PointOfSale.Web/src/services/posRuntimeCache.js:1-25`, `:193-305`, `:326-426`). Its invalidation does not propagate into `PointOfSale.Storefront`.
- Marketplace CMS/footer/translation/homepage/product/order/review Redis caches and their Marketplace event invalidators (`RedisInvalidationConsumer.cs:123-234`, `:317-368`).
- Marketplace translation cache entries use the configured 24-hour TTL (`RedisSettings.cs:54-62`).
- CommerceHub in-process query, suggestion, autocomplete, browse-count, category-metadata, suppressed-vendor, shipping, synonym, and intent caches. They are not read by customer Storefront routes.
- WhatsApp kill-switch/circuit-breaker distributed state, analytics memory caches, dashboard/report caches, and Super Admin commission caches.

Their presence does not change this Storefront result, and this audit makes no certification claim about those separate runtimes.
