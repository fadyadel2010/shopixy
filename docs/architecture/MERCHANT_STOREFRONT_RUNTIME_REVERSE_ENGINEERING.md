# Merchant Storefront Runtime — Implementation-Backed Reverse Engineering

**Scope:** Read authority per concept, search, checkout, cart, customer session, critical reads,
repair/repair-on-read, fallback, availability gates, caching. Multi-tenant via `X-Tenant-Id`.
PostgreSQL = single authoritative write model; Mongo/Redis/Elasticsearch = derivative projections.

**Finding, in one sentence:** The Storefront is **projection-candidate + SQL-reauthorization** architecture.
Derivative stores (Mongo/ES/Redis) are *only* used to *find candidates and accelerate reads*; every public
visibility decision and every financial/stock decision is **recomputed from SQL** before being served or
committed. There is a deliberate, repeated "overlay" pattern (`ProductSearchSqlAuthorityOverlay.ApplyAsync`)
that re-imposes SQL truth on Mongo/ES candidates.

---

## 0. The Central Mechanism: `ProductSearchSqlAuthorityOverlay` (the SQL reauthorization step)

Everything below funnels through this class. It is the authoritative "repair-on-read" primitive.

- `PointOfSale.Infrastructure/Elasticsearch/Services/ProductSearchSqlAuthorityOverlay.cs`
  - `TryApplyAsync` / `ApplyAsync` (lines 23–83): entrypoint. Enforces `SqlTruthRebuildGuard.RequireSqlAuthority(...)` (line 36) demanding `Variant | Pricing | Stock | Media | Category | Promotion | Tombstone` authorities.
  - `ApplyCoreAsync` (lines 85–344): the actual re-build of a `ProductDocument` from **SQL**:
    - Tenant gate: returns `null` if tenant not active (lines 92–95).
    - Loads `dbContext.Products` with `AsNoTracking().IgnoreQueryFilters()`, including `Media`, `Variants` (with `Attributes`, `Stocks`→`Warehouse`) (lines 114–127).
    - Returns `null` if SQL product missing or deleted (lines 129–132).
    - Resolves live category name from SQL `Categories`, walking the parent chain; returns `null` if any ancestor deleted (lines 134–143, `ResolveLiveCategoryNameAsync` 431–490).
    - **Re-writes all authority fields from SQL**: `IsActive`, `IsOnlineVisible = IsOnlineVisible && PublishStatus == Published` (lines 170–172), category, media, units, variants, price, promotion.
    - **Stock authority**: `SumCurrentVariantStock` sums `variant.Stocks` where warehouse active & not deleted, `Quantity - ReservedQuantity` (lines 231–267, 346–359). This is the SQL source of stock truth used in display.
    - **Price authority**: default variant price drives `Price`/`OnlinePrice`/`SKU` (lines 309–325); variant-centric.
    - **Promotion authority**: loads `dbContext.Promotions` Scope=Product and applies winning promotion from SQL (lines 327–341, `PromotionVisibilityProjectionSupport`).
  - `preserveProjectedStock` (lines 101–112, 269–307): optional flag to keep Mongo-derived stock when caller wants projected stock; normally SQL stock wins.

- `PointOfSale.Infrastructure/Reliability/SqlTruthRebuildGuard.cs` (lines 27–73): guardrail forcing explicit SQL authority kind before any rebuild/reconciliation call. Emits convergence telemetry.

This overlay is invoked from: `StorefrontProductService`, `StorefrontHomeService`, `ProductDiscoveryService`,
`StorefrontSearchController`, `SeoService`, `StorefrontProductsController` list path. It is the universal SQL
repair-on-read.

---

## 1. STOREFRONT READ AUTHORITY PER CONCEPT

| Concept | Storefront read source | Authority overlay / critical revalidation | Evidence |
|---|---|---|---|
| Product | Mongo candidate → Redis cache → **SQL reauthorization** | `ProductSearchSqlAuthorityOverlay.ApplyAsync` | `StorefrontProductService.cs:73–128`, `:655–810` |
| Variant | Mongo candidate → **SQL-variant reauthorization** | Same overlay; `ProductVariants` loaded with stocks | `ProductSearchSqlAuthorityOverlay.cs:231–267` |
| Category | Mongo (tree) → **SQL lineage reauthorization** | `CategoryLineageAuthority` | `StorefrontCategoriesController.cs`, `CategoryLineageAuthority.cs` |
| Brand | SQL-derived inside product overlay | `product.Brand` from SQL `Products.Brand` | `ProductSearchSqlAuthorityOverlay.cs:160` |
| Price | Mongo price → **overwritten by SQL** | overlay sets `Price`/`OnlinePrice` from variant | `ProductSearchSqlAuthorityOverlay.cs:309–325` |
| Promotion | Mongo badge → **recomputed from SQL Promotions** | `PromotionVisibilityProjectionSupport` | `ProductSearchSqlAuthorityOverlay.cs:327–341` |
| Coupon | SQL (`StorefrontCartCouponStates`) authoritative | revalidated per response | `StorefrontCartController.cs:1357–1469` |
| Inventory/Stock | Mongo stock → **overwritten by SQL sum** | `SumCurrentVariantStock` | `ProductSearchSqlAuthorityOverlay.cs:346–359` |
| Order | Mongo read model (`IOrderReadRepository`) → **SQL repair gate** | `TryRepairOrderProjectionAsync` | `StorefrontOrdersReadController.cs:91–125` |
| Customer | SQL `OnlineCustomers` | `StorefrontOnlineCustomerService.GetProfileAsync` | `StorefrontAuthController.cs:417–418` |
| CMS Page | **SQL `CmsPages`** (read service) | publication filter `IsPublished && !IsDraft && PublishedAt!=null` | `StorefrontPagesController.cs:94–130` |
| Navigation | **SQL `StoreConfigs.CustomizationJson`** + lineage recomposition | `RecomposeNavigationFromSqlAuthorityAsync` | `StorefrontNavigationController.cs:46–203` |
| Theme/StoreConfig | **SQL `StoreConfigs`** authoritative | "tenant-owned PostgreSQL config is authoritative" | `StorefrontStoreConfigController.cs:21–56` |
| Media | Mongo candidate → **SQL Media reauthorization** | overlay rebuilds media list from SQL | `ProductSearchSqlAuthorityOverlay.cs:217–229` |
| SEO Metadata | Redis→Mongo→**SQL overlay** | `SeoService.LoadCurrentVisibleProductBySlugAsync` | `SeoService.cs:1183–1211` |
| Search | **Elasticsearch** candidate → SQL visibility re-filter | `FilterSearchItemsBySqlAuthorityAsync` | `StorefrontSearchController.cs:142–500` |
| Review | Elasticsearch candidate; SQL marketplace outbox gate | projection readiness check | `CommerceHubProjectionReadinessService.cs:83–131` |

**Critical vs derivative reads:**
- **CRITICAL (must be SQL-fresh):** price & stock at checkout, order existence/ownership, coupon admission,
  customer profile, published-page/tenant-active gates. See §3, §4, §8.
- **DERIVATIVE (Mongo/ES/Redis browsing):** product list cards, homepage sections, search candidate ranking,
  category tree, SEO copy. These are always re-authorized by SQL before being shown.

---

## 2. SEARCH

- Controller: `PointOfSale.API/Controllers/StorefrontSearchController.cs` (`/api/storefront/search`), `[AllowAnonymous]`.
  - Unified `Search` (lines 70–215): **Elasticsearch ONLY** via `IUnifiedSearchIndexService.SearchAsync` (line 143).
    Then `FilterSearchItemsBySqlAuthorityAsync` (line 160) re-filters by SQL.
  - `Suggest` header search (lines 236–369): ES `HeaderSuggestAsync`, then `RecomposeHeaderSuggestionsBySqlAuthorityAsync` (line 316).
  - Min query length 2 (unified) / 1 (suggest) (lines 102, 273).
- **SQL visibility re-filter** `FilterSearchItemsBySqlAuthorityAsync` (lines 387–500):
  - Tenant active gate via SQL `Tenants` (lines 392–404).
  - Product items: `ProductSearchSqlAuthorityOverlay.ApplyAsync` and keep only `{ IsActive: true, IsOnlineVisible: true }` (lines 450–474).
  - Category items: `CategoryLineageAuthority.RequireLiveCategoryAsync` (lines 477–485).
  - CMS items: SQL `CmsPages` where `!IsDeleted && IsPublished && !IsDraft && PublishedAt != null` (lines 426–445).
- **Discovery (browse/listing)** `StorefrontProductsController.cs` `discover` (lines 720–938): `IProductDiscoveryService.DiscoverAsync` → ES.
  - `ProductDiscoveryService.cs`: ES query (lines 86–117), filters `IsActive`/`IsOnlineVisible` terms (lines 288–290),
    then `RecomposeDiscoveryItemsFromSqlAuthorityAsync` re-applies `ProductSearchSqlAuthorityOverlay` per candidate (lines 533–556).
  - ES disabled → returns empty result (lines 94–104) (degraded mode, no SQL fallback for discovery listing).
- **Indexing pipeline / publication filtering:** products are indexed into ES from Mongo/SQL projections; search
  terms embed `IsActive`/`IsOnlineVisible` but final gate is SQL. Out-of-stock is NOT filtered at search layer
  (stock is re-derived from SQL; availability shown but not excluded). Unpublished drafts are excluded by
  `PublishStatus == Published` in the overlay (`:171–172`).
- **ES service:** `ProductDiscoveryService` (`PointOfSale.Infrastructure/Elasticsearch/Services/`), `IUnifiedSearchIndexService`.

---

## 3. CHECKOUT (critical price/stock revalidation)

- Service: `PointOfSale.API/Services/Checkout/CheckoutService.cs` (explicit authority guarantee, lines 64–132).
  - "MongoDB projections may populate storefront selectors, but final checkout validation uses SQL authority"
    (lines 76–79). "A Product is NEVER sold. A ProductVariant (SKU) is ALWAYS sold." (lines 85–91).
  - Step model (lines 113–123): Redis cart snapshot → **SQL price validation** → SQL shipping/payment →
    **SQL inventory reservation** → PostgreSQL order → payment intent (no gateway call).
- **SQL price revalidation** `LoadAuthoritativeCheckoutVariantLookupAsync` (lines 4047–4096):
  - Queries `dbContext.ProductVariants` (SQL, `AsNoTracking`) (lines 4071–4095).
  - `EffectivePrice = OnlinePrice ?? Price` (lines 4084–4086) — SQL variant price is the truth.
  - Per item, throws `CheckoutCartInvalidException` if product inactive / not online-visible / not published /
    category not live in lineage / variant inactive (lines 3992–4005).
  - `item.CurrentPrice = authoritativeVariant.EffectivePrice` (line 4008) — cart price overwritten by SQL.
  - Optional `FOR UPDATE` row locks (`LockCheckoutCatalogAuthorityRowsAsync`, lines 4098–4158) when committing.
- **Inventory reservation** (lines 5114–5294): "validating inventory from ProductVariantStocks" (line 5135);
  stock from `ProductVariantStocks` (line 5203), reserve in `ProductVariantStocks` (line 5203); SQL is the
  reservation write-side authority. On failure → abort 409; on order fail → rollback + release.
- **Coupon/promotion admission:** coupons revalidated at cart response time (§7); checkout re-derives promotion
  from SQL. Price-change detection returns to client if SQL price differs from cart price (lines 4016–4034).
- **CheckoutSession** (abandoned/lost-order recovery): entity is SQL — `CheckoutSessionService.cs` reads/writes
  `_db.CheckoutSessions` (lines 158, 301, 426, 438, 666, 693, 771, 835, 1006, 1060). `CheckoutSession.cs` entity
  in `PointOfSale.Core/Entities/`. Controller `CheckoutSessionController.cs` (`/api/storefront/checkout-session`,
  `[AllowAnonymous]`, lines 65–99) loads **authoritative cart from Redis** then validates ownership (lines 147–160).
  Identity from claims only; `X-Customer-Id` header ignored to prevent spoofing (lines 55–63).
- **Payment selection:** SQL `StoreConfig`/payment-method config re-validated (authority comment lines 77–79).
  Payment intent generated but gateway not called (line 122). Payment method controllers: `StorefrontPaymentMethodsController.cs`,
  `StorefrontStripePaymentsController.cs`, `StorefrontPaymobPaymentsController.cs`.

---

## 4. CART

- Controller `StorefrontCartController.cs` (`/api/storefront/cart`, `[AllowAnonymous]`).
  - Architecture header (lines 49–54): **"Redis is the ONLY storage for cart state"**; Mongo read-only for
    product validation; TTL 24h reset on every write.
- Service `CartService.cs` (`PointOfSale.API/Services/Cart/`):
  - "Redis is the ONLY storage for cart state" (lines 35, 50); `RedisConnectionFactory` dependency (line 61).
  - `GetCartAsync` reads only Redis; legacy key fallback + migrate (lines 150–210). `GetCartFromRedisAsync` /
    `SaveCartToRedisAsync` / `DeleteCartFromRedisAsync` throughout.
  - **Redis unavailable → 503 SERVICE_UNAVAILABLE** (catches `RedisUnavailableException`, lines 144–151, 332–339).
  - Cart is **not** in SQL (except coupon state, see below). Guest cart = `X-Session-Id`; customer cart = claims `CustomerId`.
- **Ownership model** `ValidateHeadersAsync` (lines 1817–1884):
  - Authenticated customer → owner `customer` + `CustomerId`; tenant-claim mismatch → 401 (lines 1835–1862).
  - Guest → requires `X-Session-Id`, owner `session` (lines 1865–1883).
  - `ResolveAuthenticatedCustomerAsync` authenticates `Customer` or `StorefrontJwt` scheme (lines 1886–1901).
- **Coupon state persistence (SQL):** `StorefrontCartCouponStates` is SQL — `GetOrCreateCouponStateAsync` reads
  `_dbContext.StorefrontCartCouponStates` (line 1600), persisted in a transaction with outbox event
  (lines 1478–1533, 1546–1593). So coupon admission/removal is SQL-authoritative, not Redis.
- **Merge:** guest→customer cart merge moves Redis keys (lines 1298–1444).
- **AddItem validation reads SQL** for affiliate price presence / variant liveness: `_dbContext.ProductVariants`
  `AsNoTracking().IgnoreQueryFilters()` (lines 2164–2178) and `_dbContext.ProductUnits` (line 2188) — SQL, not Mongo.

---

## 5. CUSTOMER SESSION / AUTH

- Controller `StorefrontAuthController.cs` (`/api/storefront/auth`):
  - **Completely separate** from POS auth; uses `OnlineCustomer` entity, separate JWT secret/issuer/audience;
    Storefront JWTs cannot call POS APIs (lines 15–36). Scheme `StorefrontJwt` (line 69).
  - `Register`/`Login`/`Refresh`/`google`/`providers`/`me`. Rate-limited (lines 83, 129, 272).
  - Refresh token in **HttpOnly cookie**; JWT blacklist for revoked tokens (lines 25–29).
  - `GetCurrentCustomer` (`me`, line 396): `[Authorize(AuthenticationSchemes="StorefrontJwt")]`; reads profile via
    `IStorefrontOnlineCustomerService.GetProfileAsync` (SQL) and **blocks if `!profile.IsActive`** (lines 417–441).
- **Customer entity:** `OnlineCustomer` (SQL table). Profile read is SQL (`StorefrontAuthController.cs:417–418`).
- **Authorization model:** Storefront endpoints are `[AllowAnonymous]` for browsing/cart/checkout (guest allowed);
  customer-scoped endpoints (`me`, order history, checkout-session resume) authenticate via `StorefrontJwt`/
  `Customer` scheme and validate tenant claim. `X-Customer-Id` header is ignored for checkout identity
  (`CheckoutSessionController.cs:55–63`).
- Orders read: `StorefrontOrdersReadController.cs` uses `[Authorize(AuthenticationSchemes="StorefrontJwt")]` and
  enforces `authority.OnlineCustomerId == customerId` (lines 92–99).

---

## 6. PRICING DISPLAY (Storefront)

- Pricing displayed in **product/list/home/search DTOs is built from the Mongo `ProductDocument` after the SQL
  overlay rewrites price** (`MapToDto`/`MapToListItemDto` in `StorefrontProductService.cs:270–375`, `:867–952`).
- Effective price = default variant's `EffectivePrice` (variant-centric), else product-level
  `StorefrontEffectivePrice` (`:309–326`, `:898–915`).
- **Promotion display** (`HasActivePromotion`, `PromotionBadge`, `PromotionDiscountType/Value`) is sourced from
  SQL via `PromotionVisibilityProjectionSupport.ApplyProductPromotionOverlay` inside the overlay
  (`ProductSearchSqlAuthorityOverlay.cs:327–341`), not trusted from Mongo. Mapped through
  `StorefrontPricingPresentationMapper.Build` (`:341–346`).
- At **checkout** price is recomputed fresh from `ProductVariants.OnlinePrice ?? Price` (§3, lines 4071–4095, 4008).
- Coupon discount shown in cart is computed server-side and revalidated each response (§7).

---

## 7. INVENTORY DISPLAY

- **Stock shown in browsing is SQL-derived**, not trusted Mongo stock: the overlay recomputes
  `SumCurrentVariantStock = Σ(Quantity − ReservedQuantity)` across active warehouses
  (`ProductSearchSqlAuthorityOverlay.cs:346–359`), overwriting `document.StockQuantity` (`:294–307`).
- `InStock` / `StockQuantity` in DTOs come from variant total stock (`:315–318`, `:904–907`).
- Variant-level stock recomputed per variant (`TotalStock`) from SQL (`:248`).
- **`preserveProjectedStock`** flag (`:101–112`, `:269–307`) exists to keep projected stock in some recomposition
  paths, but normal storefront display uses SQL stock.
- At **checkout**, stock is re-validated and **reserved** in `ProductVariantStocks` (§3, lines 5114–5294) — this is
  the hard gate; browsing display is advisory.

---

## 8. FALLBACK / REPAIR-ON-READ

This is the dominant pattern. Three layers:

1. **Per-read SQL overlay (repair-on-read):** `ProductSearchSqlAuthorityOverlay.ApplyAsync` re-imposes SQL on
   every product/category/search/seo candidate. If SQL says not-active/not-visible/deleted → candidate suppressed
   (returns `null`), even though Mongo/ES had it. Evidence:
   - Product detail: `StorefrontProductService.cs:762–810` (`ApplyCurrentSqlProductDetailAuthorityAsync`,
     `IsPubliclyVisibleProduct`).
   - Product list: `:783–805` (`RecomposeCurrentSqlProductListAuthorityAsync`).
   - Home sections: `StorefrontHomeService.cs:174,236,328,398` (`RecomposeProductCardsFromSqlAuthorityAsync`).
   - Discovery: `ProductDiscoveryService.cs:533–556`.
   - Search: `StorefrontSearchController.cs:387–500`.
   - SEO: `SeoService.cs:1183–1211`.
   - Categories: `CategoryLineageAuthority.cs` + `StorefrontNavigationController.cs:160–203`.

2. **Order projection repair-on-read:** `StorefrontOrdersReadController.cs`:
   - Loads SQL `LoadOrderAuthoritySnapshotAsync` to prove existence/ownership (line 92).
   - `TryRepairOrderProjectionAsync` triggers Mongo projection repair **before** trusting Mongo
     (`IStorefrontOrderProjectionRepairService`); if repair fails → 503 (lines 109–125). Mongo is the served read
     model but only after SQL-authority repair (controller doc lines 27–32).

3. **ES → Mongo fallback (best-sellers):** `StorefrontHomeService.GetBestSellersAsync` falls back to Mongo
   `GetNewArrivalsAsync` if ES fails (lines 296–309); still re-authorized by SQL afterward.

4. **Redis → 503 (no SQL fallback for cart):** cart has no SQL fallback — Redis is sole source; failure = 503
   (`StorefrontCartController.cs:144–151`).

5. **Projection-readiness gate (fail-closed):** `CommerceHubProjectionReadinessService.EnsurePublicProductReadModelsReadyAsync`
   (`PointOfSale.API/Services/CommerceHub/`) checks `StockSyncOutboxMessage` and `MarketplaceEventOutboxMessage`
   outboxes for blocked/failed/stale deliveries (>15 min, or ≥3 failures) and throws
   `ProjectionReadModelUnavailableException` (lines 17–131). Called at the start of every product read
   (`StorefrontProductsController.cs:110,199,266,402,645,837` via `EnsurePublicProductProjectionReadyAsync`).
   This is a **staleness gate**: if the projection pipeline is broken, public product reads are refused rather
   than serving possibly-stale data.

---

## 9. AVAILABILITY GATES

Applied consistently via SQL overlay + lineage:

- **Tenant active:** `TenantLineageAuthority.IsActiveTenantAsync` (SQL `Tenants` where `IsActive && Status==Active && DeletedAtUtc==null`)
  — `TenantLineageAuthority.cs:27–46`; used by overlay (`:92`), search filter (`:392`), navigation (`:51`),
  SEO (`SeoService.cs:1228–1239`).
- **Product published/visible:** `IsActive && IsOnlineVisible && PublishStatus==Published` — overlay
  `:170–172`; enforced at `IsPubliclyVisibleProduct` (`StorefrontProductService.cs:807–810`) and search
  (`:462`).
- **Category live in lineage:** `CategoryLineageAuthority.IsLiveInLineage` walks parent chain, rejects deleted or
  orphaned categories (`CategoryLineageAuthority.cs:74–108`); used by search (`:479`),
  navigation (`:172`), checkout (`:3995`).
- **CMS page published:** `!IsDeleted && IsPublished && !IsDraft && PublishedAt != null`
  (`StorefrontPagesController.cs:433–437`; search `:433–437`).
- **Storefront live status:** navigation suppressed unless `StoreConfig.StorefrontStatus == "live"`
  (`StorefrontNavigationController.cs:62–66`).
- **Variant active & priced:** checkout rejects inactive variants or products with no active variant priced >0
  (overlay `:285–289`; checkout `:3992–4005`).
- **Customer active:** auth `me` blocks deactivated customers (`StorefrontAuthController.cs:428–441`).
- **Stock > 0:** advisory at browse (SQL-derived); hard at checkout via `ProductVariantStocks` reservation.

---

## 10. FAILURE HANDLING / CACHING

- **Redis caching layer** (`IRedisJsonCacheService`):
  - Product detail/list cached with TTL `ProductCacheTtlMinutes` / `ProductListCacheTtlMinutes`
    (`StorefrontProductService.cs:73–112, 590–646`). Cache is populated **only with SQL-accepted DTOs**
    (service doc lines 24–25) — i.e., after the overlay.
  - Home sections cached (featured 30m, new-arrivals 15m, best-sellers 15m, categories 60m)
    (`StorefrontHomeService.cs:85–103`).
  - SEO cached (metadata 30m, breadcrumbs 60m, sitemap 360m, robots 720m) (`SeoService.cs:69–89`).
  - Cache failures are **non-fatal** — "Cache failures never break the request" (service docs;
    `StorefrontProductService.cs:21–31`). On cache miss, queries Mongo/ES then re-authorizes with SQL.
- **Cache invalidation** `ProductCacheInvalidationConsumer.cs` (MassTransit consumer):
  - Listens to `ProductCreated/Updated/Deleted/StockChanged/PriceChanged/PublishStatusChanged` events (lines 37–42).
  - Invalidates tenant product list + specific product (id+slug) + promotion cache + homepage patterns
    (lines 279–293). Uses `ProjectionConsumerEventFence` for dedup/idempotency (lines 189–229) and resolves
    **current SQL slug** before invalidating (`:208–221`, `ResolveCurrentSqlProductSlugAsync:231–242`).
  - Best-effort: failures logged, swallowed (lines 304–312).
- **Projection freshness gate:** `CommerceHubProjectionReadinessService` (§8.5) refuses reads if outbox delivery
  is blocked/stale — the system prefers unavailability over stale public data.
- **Coupon cache:** coupon code lives in Redis cart; coupon *admission* is SQL (`StorefrontCartCouponStates`),
  revalidated every cart response (`StorefrontCartController.cs:1357–1469`). Guest carts cannot hold coupons
  (removed, requires authenticated ownership, lines 1385–1407).

---

## 11. SEO / SEO RUNTIME

- Controllers: `SeoController.cs` (`/api/storefront/seo`, `[AllowAnonymous]`) and `SitemapController.cs`
  (`/api/storefront/sitemap` JSON + `/xml`, `[AllowAnonymous]`).
- Service `SeoService.cs` (`PointOfSale.API/Services/Storefront/`):
  - Read flow: **Redis → Mongo → SQL overlay** (doc lines 30–49). Cache-first (metadata 30m, sitemap 6h).
  - Product SEO: `BuildProductSeoMetadataAsync` → `LoadCurrentVisibleProductBySlugAsync` (line 204) →
    `ApplyCurrentSqlProductAuthorityAsync` which calls `ProductSearchSqlAuthorityOverlay.ApplyAsync` and keeps
    only `{ IsActive, IsOnlineVisible }` (lines 1183–1211). `Index = product.IsActive && product.IsOnlineVisible`
    (line 229) — so noindex for unpublished.
  - Category SEO: `LoadCurrentCategoryBySlugAsync` + SQL lineage; `Index = true` (lines 244–265).
  - CMS SEO: `BuildCmsSeoMetadataAsync` reads SQL `CmsPages` (line 136) — publication-filtered.
  - Breadcrumbs/sitemap built from Mongo categories then re-checked against SQL lineage
    (`BuildCategoryTrail`, `LoadCurrentCategoriesAsync`).
  - Sitemap URLs built from SQL-authorized products/categories/cms pages; robots/cache TTLs as above.
- **SEO metadata fields** (`MetaTitle`/`MetaDescription`) are copied from SQL product into the Mongo document by
  the overlay (`ProductSearchSqlAuthorityOverlay.cs:153–154`) and thereby into SEO responses — SQL is the source.
- CMS page SEO/search docs also carry `MetaDescription` (`CmsPageSearchDocument.cs:68,127`).

---

## 12. SUMMARY DIAGRAM (data flow per read)

```
Storefront request
   │
   ├─ X-Tenant-Id validated (StorefrontTenantContextResolver)
   ├─ [Products/Home/Search/SEO] CommerceHubProjectionReadinessService  ──fail-closed if outbox blocked──▶ 503
   │
   ├─ candidate from DERIVATIVE store:
   │     Product/Home/List  ──► MongoDB  (+ Redis cache)
   │     Search/Suggest     ──► Elasticsearch
   │     Discovery          ──► Elasticsearch  (Mongo fallback for best-sellers)
   │     Navigation/Config  ──► SQL StoreConfigs.CustomizationJson
   │     CMS Page           ──► SQL CmsPages
   │     Cart               ──► Redis ONLY (503 if down)
   │
   ├─ RE-AUTHORIZE with SQL (the overlay / lineage / readiness):
   │     ProductSearchSqlAuthorityOverlay.ApplyAsync  (price, stock, variant, promo, media, category, tombstone)
   │     CategoryLineageAuthority.IsLiveInLineage
   │     TenantLineageAuthority.IsActiveTenantAsync
   │     Orders: LoadOrderAuthoritySnapshotAsync + TryRepairOrderProjectionAsync
   │
   └─ serve ONLY if SQL says visible/published/active/owned
        (otherwise suppress → 404 / empty / 503)
```

**Key invariants confirmed by implementation:**
1. SQL is the *only* authority for price, stock, visibility, ownership, coupons, orders.
2. Mongo/ES/Redis are candidates/accelerators only; never trusted for public visibility or financial truth.
3. Every public product/category/search/SEO read re-runs `ProductSearchSqlAuthorityOverlay` (repair-on-read).
4. Checkout re-reads `ProductVariants` + reserves `ProductVariantStocks` under SQL row locks.
5. Projection pipeline breakage → fail-closed (503) via `CommerceHubProjectionReadinessService`.

---

*Generated by implementation inspection. Every claim cites `path:line`. UI/React components intentionally excluded per scope.*
