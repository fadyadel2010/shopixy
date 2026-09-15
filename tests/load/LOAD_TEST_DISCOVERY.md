# Shopixy Load-Test Discovery & Design Report (DISCOVERY + DESIGN ONLY)

> Scope guard: this phase is **discovery + design only**. No load generator was implemented, no k6/Locust/NBomber/JMeter tests were created, no production business logic was modified, no schemas/migrations were changed, no destructive operations were executed, and no real payments, provider actions, WhatsApp sends, or emails were triggered. The only filesystem change in this phase is this document: `tests/load/LOAD_TEST_DISCOVERY.md`.

- Date (UTC): 2026-09-15
- Repo: Shopixy / PointOfSale (`PointOfSale.sln`)
- Runtime inspected: `PointOfSale.API` (ASP.NET Core), `PointOfSale.Core`, `PointOfSale.Infrastructure`, `PointOfSale.Web` (POS/Admin SPA), `PointOfSale.Storefront` (Vite/React storefront), `PointOfSale.CommerceHub` (name present as folder-level concept; marketplace runtime lives in `PointOfSale.API/Controllers/CommerceHub*` + `Marketplace*`), `PointOfSale.WhatsApp.Baileys`, `PointOfSale.Tests` (xUnit), `deploy/`, `DB/`, `docs/`.
- Method: static inspection only (controllers, `Program.cs`, middleware, services, DbContext, entities, MassTransit wiring, Mongo/Redis/ES services, appsettings, frontend env). No traffic was generated.

---

## 1. Executive summary

1. **Topology:** monolithic ASP.NET Core API (`PointOfSale.API`) backed by PostgreSQL (Npgsql, EF Core system of record), Redis (StackExchange.Redis: carts, locks, rate limits, cache), MongoDB (`ecommerce_read_db` read projections), Elasticsearch (NEST 7.x product/discovery indexes), RabbitMQ via MassTransit (event-driven projections, inventory, fulfillment, affiliate, analytics) + Hangfire (PostgreSQL-backed recurring jobs incl. inventory-reservation cleanup). Frontends: `PointOfSale.Web` (POS/Admin) and `PointOfSale.Storefront` (Vite, per-tenant storefronts via `http://{slug}.localhost:5174` template); CommerceHub/marketplace is served by the same API, not a separate deployable.
2. **Auth is multi-scheme JWT, not single JWT:** five bearer schemes — POS/Admin default (`PointOfSaleAPI` issuer, 60m/7d), `SuperAdminJwt`, `StorefrontJwt` (`StorefrontAPI`, 30m/7d), `CustomerJwt` (`CommerceHubCustomerAPI`, 60m), `AffiliateJwt` (`AffiliateAPI`, 60m/7d) — cookie-first then `Authorization: Bearer`, `ClockSkew=Zero`, per-scheme `OnTokenValidated` rules. Fallback policy is `RequireAuthenticatedUser`; anonymous access is explicit per action (`[AllowAnonymous]` / `[AllowAnonymousTenant]`).
3. **Tenant model is `Tenant == Store`:** there is no separate `Store` table; `Tenant.Slug` / `Tenant.StorefrontBaseUrl` identify the storefront. `StoreId == TenantId` by convention (`AffiliatePromotionLink`, `CommerceHubSubOrder.StoreId`, marketplace `AffectedStoreIds → GetAffectedTenantIds`). All tenant-owned entities implement `ITenantOwned` and are filtered by EF global query filters (`!IsDeleted && TenantId == CurrentTenantId`, no null fallback). A scoped `TenantResolutionMiddleware` is the authority; raw inbound `X-Tenant-Id` is stripped and re-added server-side.
4. **Load-testable surface is large but layered:** safe read-heavy paths are storefront catalog/search/resolve-slug/home/navigation/store-config/payment-methods(read)/shipping-methods(read)/`api/read/*`/review-list; stateful-but-testable paths are storefront cart (Redis-only, 24h TTL) + `checkout/pricing`; strictly-throttled paths are `POST checkout` (10-min inventory reservation, Redis distributed lock, row locks on `ProductVariantStocks`), POS carts (Postgres + mandatory `Idempotency-Key`), sales creation, admin order creation, CommerceHub checkout (30-min TTL). Webhooks, reindex, maintenance reset, debug/outbox mutation, payment lifecycle money movement, bulk catalog ops, and telemetry-write endpoints are **DO-NOT-LOAD-TEST**.
5. **First bottleneck prediction (to be proven by tests, not asserted):** `POST /api/storefront/checkout` write path — Redis `lock:checkout:{tenant}:{cart}` + Postgres `ProductVariantStocks` row locks (`ReserveAsync` in deterministic `VariantId` order) + single-transaction `Order + Shipments + InventoryReservation` insert + MassTransit publish fan-out (`OrderCreated`, `InventoryReserved`, checkout events) + downstream projection consumers (Mongo `orders`, ES index, Redis invalidation). Secondaries: ES `_search` under suggest/autocomplete concurrency, Mongo `products` hydration (`by-ids`), Redis cart hot keys. Reads should scale much further than checkout.
6. **Isolation posture is strong by design but has explicit probe obligations:** global query filters + `TenantSaveChangesInterceptor` + claim-vs-host cross-check (`403 TENANT_FORBIDDEN`) + fail-closed token validation. Residual risks are all in intentional global surfaces (login/register/refresh keyed by username/email/hash; affiliate discovery `storeTenantId` query; super-admin `tenantId` query; `marketplace_services` ES index intentionally tenant-unscoped; DTOs echoing `TenantId` that must never be trusted as authority).
7. **Traceability is partial — the exact leak-trace question cannot be answered end-to-end today:** HTTP has `X-Request-Id`/`X-Correlation-Id` → `TraceIdentifier` → response headers + scoped logs (`CorrelationId/TenantId/UserId`), and Postgres outbox/audit tables carry `CorrelationId/TenantId/IdempotencyKey`, but (a) no Serilog/`FromLogContext`, (b) no W3C `traceparent` handling, (c) MassTransit `CorrelationId/ConversationId/MessageId` are not propagated from HTTP (publisher synthesizes `Activity.Current.Id ?? Guid`), (d) Mongo `OrderDocument`/`ProductDocument` and `AuditLog` persist **no** correlation ID, (e) OTel metrics exist but OTLP export is off by default and there is no Prometheus/Grafana/compose in repo. So `VU → request → CorrelationId → Postgres → RabbitMQ → consumer → Mongo/Redis/ES` breaks after the HTTP + outbox hop.
8. **Tool recommendation: k6 (stay with current preference).** No existing load tooling was found (`tests/load/**`, `*.k6.js`, Locust/NBomber/JMeter/docker-compose globs return zero hits). k6 best fits Windows-local execution, low overhead (Go binary, VU-efficient vs JMeter GUI threads / Locust Python processes), git-friendly JS/TS definitions, deterministic VU + staged ramp + thresholds + tags, CI suitability, and decoupled execution (run k6 on a separate machine/container against the API so the generator is not competing with the backend). Python remains for seeding/analysis/orchestration/validation only.
9. **Next milestone (no code in this phase):** M1 = harness skeleton + deterministic seed design + correlation-header contract + test-DB-only guardrails + read-only smoke (see §16).

---

## 2. Runtime topology relevant to load testing

### 2.1 Process topology (local dev → K8s later)

| Component | Role in load test | Evidence |
|---|---|---|
| `PointOfSale.API` (ASP.NET Core) | System under test (SUT). All storefront/POS/marketplace HTTP surfaces. | `PointOfSale.API/Program.cs` (~3700 lines: 5× `AddJwtBearer`, middleware order, MassTransit, OTel, health, Prometheus); `Controllers/` (~176 controllers recursive) |
| PostgreSQL (Npgsql EF Core) | System of record. `ApplicationDbContext` (7571 lines), `TenantSaveChangesInterceptor`, `InventoryReservations`, `ProductVariantStocks`, `Orders`, outbox/inbox tables, Hangfire schema. Conn key `ConnectionStrings:DefaultConnection` (empty in committed `appsettings.json`; injected via env/user-secrets). | `PointOfSale.API/appsettings.json:3-5`; `PointOfSale.Infrastructure/Data/ApplicationDbContext.cs`; `ProductVariantStockRepository.cs`; Hangfire `Program.cs:780`, `Jobs/HangfireInitializationHostedService.cs:214` |
| Redis (`pos` instance) | Storefront carts (ONLY storage, 24h TTL reset on write), `lock:checkout:*` / `lock:payment:*` / `lock:commercehub:checkout:*`, idempotency records, JWT blacklist, rate limits (`checkout` limiter, funnel limiter), product/home/SEO/marketplace cache. Packages `StackExchange.Redis 2.8.24`, `Microsoft.Extensions.Caching.StackExchangeRedis 9.0.0`, SignalR Redis backplane. | `Infrastructure/Redis/CacheKeyBuilder.cs`; `Services/Cart/CartService.cs:35-57,81`; `CheckoutController` rate-limit; `RedisPerformanceTelemetry` |
| MongoDB (`ecommerce_read_db`) | Read projections: `products`, `categories`, `orders` (via direct `GetCollection<OrderDocument>("orders")`), coupons/promotions, storefront configs/pages/navigation, affiliate/commercehub products, reviews, `ProcessedEvents` inbox, `WhatsAppMessageLogDocument`. Singleton `MongoClient`. | `Infrastructure/MongoDB/Services/MongoDbContext.cs`; `Documents/OrderDocument.cs`, `ProductDocument.cs`, `WhatsAppMessageLogDocument.cs` |
| RabbitMQ + MassTransit | Event bus. `UsingRabbitMq` + kebab-case endpoints + `UseMessageRetry(1s,5s,15s,30s)` + `MarketplacePublishFilter` + `ConfigureEndpoints` auto-wire; ~70 consumers; EF outbox (`InboxState/OutboxMessage/OutboxState` + domain outboxes); `MassTransitEventPublisher` (transactional publish) vs `PublishSensitiveAsync` (`IBus.Publish`, bypasses outbox). | `Program.cs:1880-2253`; `Infrastructure/EventBus/MassTransitEventPublisher.cs` (768 lines); `Infrastructure/Consumers/*`; `API/Consumers/*` (3 files) |
| Elasticsearch (NEST 7.17.5) | Search/discovery indexes: `products`, `categories`, `cmspages`, `public_affiliate_products`, `commercehub_products` (versioned alias Active/Desired/WriteIndex), hardcoded `marketplace_services` (intentionally tenant-unscoped). Settings `Elasticsearch:{Url,Enabled,IndexNames,Shards:1,Replicas:0}`. | `Infrastructure/Elasticsearch/Configuration/ElasticsearchSettings.cs`; `Services/UnifiedSearchIndexService.cs`, `ProductSearchIndexService.cs`, `CommerceHubSearchIndexService.cs`, `SearchTelemetry.cs` |
| Hangfire (Postgres-backed) | Recurring jobs: `InventoryReservationCleanupJob`, `CommerceHubInventoryReconciliationJob`, outbox processors (`StockSync`, `MarketplaceEvent/Projection`, `Accounting`, `ProviderDelivery`), audit queue. | `Jobs/HangfireInitializationHostedService.cs`; `Program.cs:780` |
| `PointOfSale.Web` | POS/Admin SPA calling `api/*` with staff JWT. | `PointOfSale.Web/src/services/api.js`, `App.jsx`, `Sidebar.jsx` |
| `PointOfSale.Storefront` | Vite/React per-slug storefront (`UrlTemplate: http://{slug}.localhost:5174`, `BaseUrl: http://localhost:5173`). | `appsettings.json:47-49`; `PointOfSale.Storefront/package.json`, `src/stores/*`, `src/types/*` |
| `PointOfSale.WhatsApp.Baileys` | WhatsApp side-effect surface — excluded from load. | `BaileysService: BaseUrl http://localhost:3000` (`appsettings.json:6-12`) |
| Env split | Local Windows = API + frontends; separate Ubuntu = infra engines (PG/Mongo/Redis/RMQ/ES). No Prometheus/Grafana/exporters on Windows; K8s later for deep observability. Cross-host latency between generator/API/infra must be recorded as a load variable. | Task brief (environment statement); `deploy/staging/*`; no `docker-compose*` / `prometheus.yml` in repo (verified by glob) |

### 2.2 Request pipeline order (load-relevant)

`UseAuthentication → ThemePreviewAuthority → HostResolution → TenantResolution → JwtBlacklist → UseAuthorization → AdminTenantAuthority → PublicationGuard` (`Program.cs:3568-3637`; `test-auth/health` bypass). Implication: every load request must present a resolvable tenant (claim, domain mapping, verified host, or `{slug}` template) **and** pass blacklist/authority checks, or it will `400 TENANT_REQUIRED` / `403 TENANT_FORBIDDEN` before reaching business logic — harness auth/tenant setup is therefore on the critical path, not an afterthought.

### 2.3 Health / metrics endpoints (safe probes)

`MapHealthChecks(/health, /health/marketplace, /health/live, /health/ready)` + ~25 checks (postgres/redis/mongo/elasticsearch/rabbitmq/R2/DNS/certs/paymob/stripe/fawry/manual/COD/marketplace/platform/search-sync/projection-drift/outbox/integration/accounting/bus/outbox/canonical-identity/payout-truth/recovery/redis-latency/stock-reservation/marketplace-ES/provider-trust) and `MapPrometheusScrapingEndpoint()`; ~22 OTel meters (marketplace/affiliate/analytics/abandoned-cart/publish/commerce-reliability/inventory-reservation/checkout-lock/redis/search/paymob/stripe/fawry/payout/commercehub-payment/manual-payment + AspNetCore). Only `/health/*` + Prometheus scrape endpoint are safe at load; all else needs exporters/dashboards that do not yet exist locally (see §14).

---

## 3. Endpoint/flow inventory

Conventions: `api/[controller]` expands (`api/orders`, `api/sales`, `api/products`). Storefront reads generally require `X-Tenant-Id` (server-resolved; see §5) and guest flows additionally use `X-Session-Id`. POS writes generally require `Idempotency-Key`. Verified from `Route`/`Http*` attributes and per-action auth attributes.

### 3.1 Storefront (primary load surface)

| Area | Controller → route prefix | Key endpoints (verb + path) | Auth | Load note |
|---|---|---|---|---|
| Product detail/list/search/discover | `StorefrontProductsController` → `api/storefront/products` (`[AllowAnonymous]`) | `GET /api/storefront/products/{slug}`; `GET /by-id/{id:guid}`; `POST /by-id/{id:guid}/variant-resolution`; `GET /api/storefront/products`; `GET /search`; `GET /discover` | Anonymous | Core read-heavy profile; `search`/`discover` hit ES/Mongo |
| Categories | `StorefrontCategoriesController` → `api/storefront/categories` (`[AllowAnonymous]`) | `GET /api/storefront/categories`; `GET /{slug}` | Anonymous | Read-heavy |
| Full-text search | `StorefrontSearchController` → `api/storefront/search` (`[AllowAnonymous]`) | `GET /api/storefront/search`; `GET /search/suggest` | Anonymous | ES hot path; suggest/autocomplete is bursty |
| Slug resolution | `SlugResolverController` → `api/storefront/resolve-slug` (`[AllowAnonymous]`) | `GET /api/storefront/resolve-slug/{slug}` | Anonymous + `X-Tenant-Id` | Redis cache-first, read-only; ideal warm-up probe |
| Home/rails | `StorefrontHomeController` → `api/storefront/home` | `GET /featured-products`; `GET /new-arrivals`; `GET /best-sellers`; `GET /categories`; `GET /products/by-ids` | Anonymous | `by-ids` hydration stresses Mongo |
| Navigation/config/CMS | `StorefrontNavigationController` → `api/storefront/navigation`; `StorefrontStoreConfigController` → `api/storefront/store-config`; `StorefrontPagesController` → `api/storefront/pages`; `StorefrontFooterController` → `api/storefront/footer`; `StorefrontCheckoutFieldsController` → `api/storefront/checkout-fields` | `GET` each (pages: `GET /{slug}`, `GET /`, `GET /contact`) | Anonymous | Cheap reads; good baseline + tenant-resolution coverage |
| Payment/shipping reads | `StorefrontPaymentMethodsController` → `api/storefront/payment-methods`; `StorefrontShippingMethodsController` → `api/storefront/shipping-methods`; `StorefrontShippingQuotesController` → `api/storefront/shipping` | `GET /api/storefront/payment-methods` (SQL authority); `GET /shipping-methods`; `POST /shipping-methods/preview`; `POST /shipping-methods/select`; `POST /shipping/quotes` | Anonymous | Reads safe; `preview/select/quotes` are compute per cart — include at low weight |
| Cart | `StorefrontCartController` → `api/storefront/cart` (per-action `[AllowAnonymous]`) | `GET /api/storefront/cart`; `POST /items`; `PUT /items/{productId:guid}`; `DELETE /items/{productId:guid}`; `PUT|DELETE /items/variant/{variantId:guid}`; `DELETE /api/storefront/cart`; `POST /apply-coupon`; `POST /apply-draft`; `POST /remove-coupon`; `POST /merge` | Anonymous + tenant/session headers | Redis-only, 24h TTL, mutation lock 3min w/ 30s renewal, every op emits MassTransit cart events; test with unique `X-Session-Id` per VU |
| Checkout pricing (no order) | `CheckoutController` → `api/storefront/checkout` (`[AllowAnonymous]`) | `POST /api/storefront/checkout/pricing` | Anonymous | Authoritative SQL pricing preview; safe transactional rehearsal |
| Checkout (creates order) | same | `POST /api/storefront/checkout` + `[EnableRateLimiting("checkout")]`, idempotency compat headers | Anonymous + tenant/session/customer headers | **Transactional profile**: Redis lock → idempotency check → cart snapshot → SQL repricing → reserve (10-min TTL) → single-tx Order+Shipments+Reservation → payment-intent-only; expect 409s under contention |
| Checkout sessions | `CheckoutSessionController` → `api/storefront/checkout-session` | `POST /`; `PATCH /{id:guid}`; `GET /recover/{token}` | Anonymous | Abandoned-checkout tracking; low weight |
| Order reads | `StorefrontOrdersReadController` → `api/storefront` | `GET /orders/{orderId:guid}` (StorefrontJwt); `GET /{id}/track` (auth); `GET /{id}/track-guest` (anon); `GET /orders/by-number/{orderNumber}` (auth); `GET /orders/track-guest` (anon query) | Mixed | Auth reads need customer JWT; guest reads need id+token/number |
| Storefront auth | `StorefrontAuthController` → `api/storefront/auth` | `POST /register`; `POST /login`; `POST /refresh(+/refresh-token)`; `GET /me [Authorize(StorefrontJwt)]`; `GET /providers`; `POST /google`; `POST /facebook`; `POST /logout`; `POST /forgot-password`; `POST /reset-password` | All anon except `me` | Login/refresh flow loadable; avoid OTP/social spam |
| Wishlist (auth) | `StorefrontWishlistController` → `api/storefront/wishlist` (`[Authorize(StorefrontJwt)]`) | `POST /`; `DELETE /{productId:guid}`; `GET /`; `DELETE /`; `GET /count`; `GET /check/{productId:guid}` | StorefrontJwt | Authenticated read/write mix; low weight |
| Stripe/Paymob intent (no charge) | `StorefrontStripePaymentsController` → `api/storefront/payments/stripe`; `StorefrontPaymobPaymentsController` → `api/storefront/payments/paymob` | Stripe `POST /create-intent`, `GET /status/{orderId}`, `GET /configuration`; Paymob `GET /status/{orderId}`, `GET /status`, `POST /intent/{orderId}` | Anonymous | Intent-only; include only against test DB + mocked providers (see §10) |
| Payments status | `PaymentsController` → `api/payments` | `GET /status/{orderId}` (+ alias `GET /api/storefront/payments/status/{orderId}`); `GET /orders/{orderId}/status`; `POST /intents/{orderId}`; `POST /cod/confirm/{orderId}` | Status reads anon; intents anon | Status polls safe; intent/COD-confirm are writes — throttle/mock |

### 3.2 POS / Admin (authenticated staff flows)

| Area | Controller → route | Key endpoints | Auth | Load note |
|---|---|---|---|---|
| POS carts | `PosCartsController` → `api/pos-carts` | `POST /`; `GET /{cartId}`; `GET /open`; `POST /{id}/items`; `PUT /{id}/items/{itemId}`; `DELETE /{id}/items/{itemId}`; `POST /{id}/{hold,resume,cancel,expire,recover,abandon}` — **all writes require `Idempotency-Key`** | `Owner,Admin,Manager,Cashier` | Postgres-backed, `ExpectedVersion` optimistic concurrency, transactional outbox; single open cart per cashier enforced |
| Sales | `SalesController` → `api/sales` | `POST /` (decrements `ProductVariantStocks`, writes `StockMovements`); `PUT /{id}`; `DELETE /{id}`; `GET /{id}`, `GET /`; `POST /{id}/return`; `GET /receipt/{id}`; `GET /{id}/can-delete` | `[Authorize]` | Sale creation is inventory-mutating; returns/voids excluded from load |
| Admin catalog | `ProductsController` → `api/products` | `GET /`; `GET /lookup` (POS fast lookup); `GET /{id}`; `GET /barcode/{barcode}` (scan hot path); `POST /`; `PUT /{id}`; `DELETE /`; `PUT /{id}/deactivate`; `POST /bulk/{activate,deactivate,publish,unpublish,delete}`; `GET /search`; `GET /quick-access`; `POST /generate-barcode`; `GET /{id}/stock`; `POST /{id}/adjust-stock`; units CRUD; `POST /migrate-to-multi-unit`; `POST /sync-stock-to-mongo` | `[Authorize]` | Load only `lookup`, `barcode/{barcode}`, `quick-access`, `GET` reads; **exclude** bulk/migrate/sync/adjust |
| Orders admin | `OrdersController` → `api/orders`; `AdminOrdersController` → `api/admin/orders`; `OrderFulfillmentController` → `api/orders/{orderId}/fulfillment` | `GET /api/orders`, `GET /{id}`, `GET /by-number/{n}`; `POST /api/orders` (manual create, 10-min reservation TTL); `PATCH /{id}/status`, `/{id}/payment-status`; `GET /statistics`; admin `GET /`, `GET /{id}`, attachment download, `PUT /{id}/status`, `POST /{id}/cancel` (releases reservation); fulfillment `{confirm,pack,out-for-delivery,delivered,complete,returned,failed-delivery,retry-delivery,cancel}` | Admin roles | Reads + cancel path testable; fulfillment transitions excluded |
| Inventory admin | `AdminInventoryController` → `api/admin/inventory` | `GET /` (list); `POST /adjust`; `GET /movements` | `Owner,Admin,Manager` | List/movements reads safe; `adjust` excluded |
| Payment lifecycle | `PaymentLifecycleController` → `api/payments/lifecycle` | `GET /{id}/actions`; `POST /{id}/cod-delivered`; `POST /{id}/refund`; `POST /settlements/cod`; `POST /settlements/gateway` | `Owner,Admin,Manager` | **Money movement — excluded** |

### 3.3 CommerceHub / Marketplace

| Area | Controller → route | Key endpoints | Auth | Load note |
|---|---|---|---|---|
| Marketplace services/requests | `MarketplaceController` → `api/marketplace` (`[Authorize]`) | `GET /me`, `/me/stats`; `GET /services/{id}/reviews` (anon); `POST /search`; `GET /services/{id}`; `POST /requests`; `GET /my-requests`, `/incoming-requests`, `/requests/{id}`; lifecycle `{accept,reject,complete,confirm,cancel}`; chat; reviews CRUD; vendor service CRUD; profile; `POST /admin/reindex(+/global)`; `GET /admin/diagnostics`; attachments | Mixed (reads partly anon) | Load only anon reads + `POST /search` at low weight; exclude lifecycle writes, review writes, reindex, uploads |
| Marketplace reads | `MarketplaceReadController` → `api/read` | `GET /products`; `GET /products/{id}`; `GET /orders` (auth); `GET /homepage` (anon) | Mixed | Public list/detail/homepage safe |
| Product reviews | `MarketplaceProductReviewsController` → `api/marketplace` | `POST /reviews`, `PUT /{id}`, `DELETE /{id}` (auth writes, excluded); `GET /products/{id}/reviews`, `/rating-summary`, `/review-context` (anon, safe) | Mixed | Only the three `GET`s loadable |
| CommerceHub storefront | `CommerceHubController` → `api/commercehub/products` (`[AllowAnonymous,AllowAnonymousTenant]`); `CommerceHubCartController` → `api/commercehub/cart`; `CommerceHubCheckoutController` → `api/commercehub/checkout`; `CommerceHubOrdersController`; `CommerceHubPaymentsController` → `api/commercehub/payments` | `GET /api/commercehub/products`; `GET /search`, `/search/suggestions`, `/autocomplete`, `/popular`; `POST /search/{click,impression,filter-event}` (telemetry writes); `GET /products/{id}` (+ alias `/api/marketplace/products/{id}`); `POST /{id}/variant-resolution`; cart `GET/POST/DELETE`, `POST /price`; `POST /api/commercehub/checkout` (30-min TTL reservations); order `GET {marketplaceOrderId}` (anon method), suborder actions/transitions, earnings; payments `POST /initiate`, `POST /webhook`, Paymob webhook/callback | Mostly anon reads; checkout/payments writes | Discovery + cart reads loadable; **exclude** telemetry writes, checkout (or single-threaded test-DB-only), `initiate`/webhooks, suborder status writes |
| Feeds/sitemap/robots | `Public{Google,Meta,TikTok,Snapchat}ProductFeedController`, `PublicAffiliate*`, `SitemapController`, `RobotsController` | `GET` feed/sitemap/robots | Anonymous | Cheap cacheable reads; include one feed + sitemap as cache-hit probes |

### 3.4 Service chain (cart → checkout → projections)

1. **Storefront cart:** Redis ONLY (`CartTtlHours=24`, reset on write; `CartMutationLockTtl=3min` + 30s renewal); Mongo read-only for product validation; no reservation. Every mutation emits MassTransit (`CartCreated/ItemAdded/ItemUpdated/ItemRemoved/Cleared/Merged` + `CartActivityEvent`). Source: `Services/Cart/CartService.cs`, `StorefrontCartController.cs:49-54`.
2. **Checkout (`Services/Checkout/CheckoutService.cs`, ~10k lines):** acquire `lock:checkout:{tenant}:{cartId}` (409 on contention) → Postgres idempotency check (`tenant:{tid}:checkout:{requestId}`) → snapshot Redis cart (read-only) → **reprice from SQL authority** (`ProductVariant.OnlinePrice ?? Price`, shipping/payment/store-config from SQL, never Mongo) → `ValidateAndReserveInventoryAsync`: `ProductVariantStocks` grouped `Available=Σ(Qty−Reserved)` scoped by `WhereCurrentInventoryStockOwner(tenant)+WarehouseId`, skip if `!TrackStock||AllowBackorder`, else 409; reserve in `OrderBy VariantId` via `ReserveAsync` (row-lock, no-negative guard), `expiresAt=+10min`, release on partial failure → single-tx `Order+Shipments+InventoryReservation` → payment-intent-only record (`CheckoutPaymentSession`) → store idempotency result → publish (`CheckoutStarted`, `CartActivity`, `CheckoutFailed?`, core `OrderCreated` for RabbitMQ routing to `OrderProjectionConsumer`, `CheckoutOrderCreated`, conditional `AffiliateOrderPlaced`, `PriceRecalculation`, `InventoryReserved(+items)`, `InventoryReservationReleased`, WhatsApp events; `OrderCancelled` on compensation). Contracts: `API/Events/CheckoutEvents.cs` (base carries `TenantId/OrderId/CartId/CorrelationId/IdempotencyKey`).
3. **POS cart:** Postgres (`PosCarts+PosCartItems`), `ExpectedVersion`, `StageOutbox(PosCartCreated/ItemAdded/Expired)`, mandatory `Idempotency-Key` (`Services/Pos/PosCartRuntimeService.cs`; `PosCartsController.cs`).
4. **Inventory authority:** `ProductVariantStocks` is the only mutable stock source (`Services/InventoryService.cs`); deterministic lock ordering; `InventoryReservation` row (`TenantId/OrderId/SubOrderId?/OrderItemId/ProductId/VariantId/WarehouseId/Reserved/Committed/Released/.../Status=Active/ReservationEventId/LastMutationEventId?/ExpiresAtUtc`) with `CK_*_NonNegative`, `CK_ReservedFlow_Valid`, `CK_Returns_DoNotExceedCommitted`; guards (`CheckoutInventoryAuthorityGuard` `FOR UPDATE`), cleanup jobs (Hangfire `InventoryReservationCleanupJob`, `CommerceHubInventoryReconciliationJob`); CommerceHub TTL 30min vs storefront/admin 10min.
5. **Consumers in order path:** `OrderProjectionConsumer` (+Definition), `CommerceHubProjectionConsumer`, `CommerceHubOrderProjectionConsumer`, `OrderStatusSyncConsumer`, `OrderShipmentOrchestrationConsumer` (`OrderConfirmed/Paid/CollectionAccountingPosted/Cancelled`), shipment notifications, `FulfillmentDeliveredEventConsumer`, `ShipmentCodCollectedConsumer`, affiliate accounting/attribution chain, analytics (`AnalyticsEvent`, `AuthoritativePurchaseAnalytics`), `RedisInvalidationConsumer` / `ProductCacheInvalidationConsumer`.

---

## 4. Authentication strategy

### 4.1 Schemes (all JWT, `ClockSkew=Zero`, `ValidateIssuer/Audience/Lifetime/SigningKey=true`)

| Scheme | Issuer/Audience | Token lifetimes | Cookie → header fallback | `OnTokenValidated` (fail-closed unless noted) |
|---|---|---|---|---|
| POS/Admin default (`AddJwtBearer`) | `PointOfSaleAPI` / `PointOfSaleClient` | 60m / 7d | `access_token` → legacy `auth_token` → `Bearer` | Requires `tenant_id` GUID claim + `IPosSessionAuthority.ValidateAccessTokenAsync` |
| `SuperAdminJwt` | separate secret/issuer | — | `super_admin_access_token` | Requires claims + `Role=SuperAdmin` + auth type |
| `StorefrontJwt` | `StorefrontAPI` / `StorefrontClient` | 30m / 7d | `storefront_access_token` + issuer-gated bearer read | Logs only; enforcement via `StorefrontJwtBlacklistMiddleware` + `TokensValidAfterUtc` |
| `CustomerJwt` | `CommerceHubCustomerAPI` / `CommerceHubCustomerClient` | 60m | `commercehub_customer_access_token` + issuer gate | Requires `jti+CustomerId`, not-revoked, session-valid, login-allowed |
| `AffiliateJwt` | `AffiliateAPI` / `AffiliatePortal` | 60m / 7d | `affiliate_access_token` → header | Requires `Role=Affiliate + affiliate_account_id + sub/NameIdentifier + affiliate_session + iat>TokensValidAfterUtc` vs SQL (`IgnoreQueryFilters`) |
| `ManualWalletUploadToken` | — | — | — | Upload-scoped |

Policies: `Fallback=RequireAuthenticatedUser`; `RequireSuperAdmin` (scheme+role+authtype); `AffiliateOnly` (scheme+`Role=Affiliate`). Cookies `HttpOnly+Secure+SameSite=Strict` + `csrf_token` (`AuthCookieConfiguration`, `CookieCsrfProtectionMiddleware`).

### 4.2 Auth routes (verified)

- `api/auth` (`AuthController`, class `[Authorize]`): `POST /register [AllowAnonymous]`; `POST /login [AllowAnonymous,AllowAnonymousTenant]`; `POST /logout [Authorize]`; `GET /session [Authorize,AllowAnonymousTenant]`; `POST /refresh(+/refresh-token) [AllowAnonymous,AllowAnonymousTenant]`; `GET /verify-email [AllowAnonymous,AllowAnonymousTenant]` (+`resend-verification [Authorize]`). POS minting claims: `NameIdentifier/Name/Email/Role/tenant_id/SessionBinding`; refresh hashed (`User.RefreshToken+Expiry`), concurrency → `SESSION_ROTATED/REFRESH_REPLAY_REJECTED`.
- `api/storefront/auth` (`StorefrontAuthController`): `POST /register|/login|/refresh(+/refresh-token)|/logout|/forgot-password|/reset-password`, `POST /google|/facebook`, `GET /providers` (all anon); `GET /me [Authorize(StorefrontJwt)]`. Claims: `CustomerId/TenantId/Email/Provider/EmailVerified/sub/jti/iat/SessionIssuedAtTicks`; refresh reuse → `TOKEN_REUSE_DETECTED`.
- `api/customers/auth` (`CustomersAuthController`, `[AllowAnonymousTenant]`): `POST /register|/login|/logout|/forgot-password|/reset-password|/verify-email`, `GET /me [Authorize(CustomerJwt,Roles=Customer)]`, `POST /revoke-all-sessions [Authorize(CustomerJwt)]`, `PUT /profile`, `POST /change-password|/change-email` (CustomerJwt). This is the marketplace/customer identity — no separate marketplace auth controller.
- `api/affiliate/auth` (`AffiliateAuthController`, `[AllowAnonymous]`, explicitly **no** `X-Tenant-Id`, `tenant_id` NOT in JWT, `TenantId=Guid.Empty`): `POST /register|/login|/refresh`. Global email/token scope with replay entries.
- `api/super-admin/auth`: `POST /login|/refresh [AllowAnonymous]` (super-admin service).
- `api/test-auth` (`TestAuthController`, `[TestingOnly,AllowAnonymous,AllowAnonymousTenant,IgnoreApi]`, stripped in prod via `TestingOnlyControllerFeatureProvider`): `POST /token` (accepts `TenantId` in body — test-only), `POST /ensure-schema`, `POST /si-con-*/run` concurrency runners. **Load harness must never depend on `test-auth` outside test/dev; production discovery must confirm absence.**

### 4.3 Harness auth implications

- Maintain **five token pools** (staff POS, storefront customer, marketplace customer, affiliate, super-admin only if needed — prefer avoiding super-admin in load).
- Per-VU identity: unique user/session per VU (no shared refresh-token racing; refresh endpoints themselves are not load targets except a dedicated low-rate auth-flow scenario).
- Cookies vs headers: harness should use `Authorization: Bearer` (deterministic, avoids cookie-jar cross-VU bleed) and explicitly set `X-Tenant-Id` where the API expects it, plus `X-Session-Id` for guest cart/checkout, `Idempotency-Key (+X-Request-Id` matching where enforced) for POS/checkout writes.
- `ClockSkew=Zero` means token expiry boundaries are sharp — long soak runs must refresh proactively, and refresh races must be measured separately, not intermixed with steady-state latency.

---

## 5. Tenant/store isolation model

### 5.1 Resolution authority

Pipeline: `ThemePreviewAuthority → HostResolution (ForwardedHeaders → Normalize → DomainResolver → Items[DomainResolutionResult]) → TenantResolution → JwtBlacklist → Authorization → AdminTenantAuthority → PublicationGuard`.

`TenantResolutionMiddleware` (trusted-only, in order): (1) `tenant_id/TenantId/tenantId` claim → active-`Tenants` lookup; (2) `DomainResolutionResult.Found.TenantId`; (3) verified `Host → StorefrontBaseUrl`; (4) `{slug}` from `Storefront:UrlTemplate`. Behavior: strips inbound `X-Tenant-Id`, rejects spoof with `403 TENANT_FORBIDDEN`, rejects auth-vs-host mismatch with `403`, returns `400 TENANT_REQUIRED` unless `[AllowAnonymousTenant]`, then sets `TenantContext.SetTenantId` and re-adds header for compat. Excludes swagger/hangfire/health/super-admin/tenants/admin-finance/platform-register/subdomain-check/hubs-chat/uploads/affiliate/public/test-auth. `ITenantContext` (`TenantId/RequiredTenantId`, scoped) is the downstream authority; `StorefrontTenantContextResolver.ValidateTenantHeader` reads context, not raw header; `AdminTenantAuthorityMiddleware` revalidates `Owner/Admin(/Manager)` vs live SQL post-auth.

No `X-Store`/subdomain/slug headers exist; `subdomain` appears only in `PlatformController subdomain/check` + `RegistrationDtos.SubdomainValidator` + `Tenant.Slug` + `Tenant.StorefrontBaseUrl`.

### 5.2 Ownership & enforcement

- `Tenant == Store/Company` (`Core/Entities/Tenant.cs`); `StoreId==TenantId` convention (`AffiliatePromotionLink.TenantId=storeId`; `CommerceHubSubOrder.StoreId`; `MarketplaceOrder.GetAffectedStoreIds`; `CommerceHubEvents.AffectedStoreIds→GetAffectedTenantIds`).
- `ITenantOwned` on `User`, `Product`, `OnlineCustomer` (unique `(TenantId,Email)`), `Customer/Address/Category/Media/Warehouse/Supplier/FinancialAccount/Bank/Cashbox/Expense/Unit/SalesRep/Sales/PurchaseInvoice/Idempotency/PosCart/Order/Return/StockTransfer/Coupon/Promo/Promotion/SmartOffer/Cms/Shipment/Ledgers/BankTx/FinancialTx/WarehouseStock/StoreConfig/Domain/AffiliateAccount`, etc.
- `ApplicationDbContext.OnModelCreating` applies `HasQueryFilter(e => !e.IsDeleted && e.TenantId == CurrentTenantId)` with **no null fallback** (invisible without tenant); `TenantSaveChangesInterceptor` blocks `ITenantOwned` saves without `TenantId`.
- Roles: `Owner/Admin/Manager/Cashier/Warehouse` (+`Customer`, `Affiliate`, `SuperAdmin`). Admin surfaces use `[Authorize(Roles=Owner,Admin[,Manager])]`; POS uses generic `[Authorize]`; storefront `me` uses `[Authorize(StorefrontJwt)]`; customer paths use `[Authorize(CustomerJwt,Roles=Customer)]`; affiliate uses `[Authorize(AffiliateOnly)]`; platform uses `[Authorize(Policy=RequireSuperAdmin)]`.

### 5.3 Client-supplied identity — where it is accepted and why

| Surface | Client knob | Safeguard | Load-test treatment |
|---|---|---|---|
| Raw `X-Tenant-Id` | Stripped + re-added server-side; spoof/mismatch → `403` | Middleware authority | Harness sets it but asserts server resolution wins |
| `Auth login/register/refresh/logout`, affiliate register/login/refresh | Global username/email/refresh-hash lookup (`IgnoreQueryFilters`) then binds `tenant_id` from DB | Intentional global boundary (username/email uniqueness) | Seed unique usernames/emails per tenant; assert login binds correct tenant |
| `AffiliatePortal DiscoverProducts ?storeTenantId` | Filter over SQL-admitted docs (`LoadSqlAdmitted…+FilterAffiliateDocuments`, `StoreId==CurrentTenant` per `AffiliatePromotionLink`) | Must verify per-doc enforcement under concurrency | Dedicated isolation probe (see §8) |
| `SuperAdminOrders ?tenantId` | Route excluded from tenant resolution, `RequireSuperAdmin` only | Intentional platform surface | Exclude from tenant-VU load; cover once with super-admin read if needed |
| `TestAuth /token {TenantId}` | DB-active check, Testing/Development-only, prod-stripped | Test-only | Never use in prod-like load |
| DTO `TenantId` echoes (`CheckoutDtos`, `AdminStoreConfig/Shipment/Order/Inventory/Cms`, `StorefrontDtos`) | Response/error shape only | Must never be used as authority | Harness must source tenant from context/claim, never echo DTO back as authority |
| `marketplace_services` ES index | Intentionally tenant-UNSCOPED for discovery | Platform-level visibility boundary | Assert vendor scoping is applied at read/filter layer, not index |

---

## 6. Correlation/traceability map

### 6.1 What exists

| Hop | Mechanism | Fields |
|---|---|---|
| Edge → API | `CorrelationIdMiddleware`: prefer `X-Request-Id`, fallback `X-Correlation-Id`, else `Guid.NewGuid()`; sets `TraceIdentifier` + both response headers (immediate + `OnStarting`) + `Activity.Current` tags `correlation.id/request.id` + `BeginScope{CorrelationId,TraceId,RequestPath(redacted)}` | `CorrelationId, TraceId, RequestPath` |
| Request logging | `RequestContextLoggingMiddleware`: `BeginScope{CorrelationId,TenantId,UserId,RequestPath,RequestMethod}` + `Activity` tags + `HTTP request completed \| CorrelationId/TenantId/UserId/Operation/Result/StatusCode/LatencyMs` | Adds `TenantId, UserId, Method, LatencyMs` |
| Context access | `RequestContextMetadata` (`GetCorrelationId()=TraceIdentifier`; tenant from `ITenantContext`/claim/header; user from `NameIdentifier/sub/UserId/CustomerId`); `ICorrelationIdAccessor/CorrelationIdAccessor` singleton (`TraceIdentifier ?? Activity.Current.Id ?? Guid`); per-controller `GetTraceId()=>Activity.Current?.Id ?? TraceIdentifier`; checkout idempotency-mismatch log with `TenantId/TraceId` | Uniform lookup |
| Domain events | `IntegrationEvent{EventId,OccurredAtUtc,EventType,CorrelationId?,IdempotencyKey?,Version,UserId,TenantId?}` + markers (`IAuthoritative/IGlobalPlatform/IAggregateScoped/ISensitive/INonAuthoritative`); `MassTransitEventPublisher` enriches `TenantId` from context, `CorrelationId=Activity.Current.Id ?? Guid`, `IdempotencyKey=EventId`, enforces outbox-inside-transaction for authoritative events | Tenant/event/user linkage at publish |
| Postgres audit/outbox | `StockMovement.CorrelationId?`; `CommerceHubFinancialTransaction/Ledger/MerchantLifecycle/RecoveryLedger.CorrelationId`; `StockSync/MarketplaceEvent/MarketplaceProjection/Accounting/ProviderDelivery/PaymentCancellation/CommerceHubCompensationOutboxMessage{CorrelationId,TenantId,IdempotencyKey,OccurredAt/CreatedAt}`; MassTransit EF `InboxState/OutboxMessage/OutboxState{MessageId,CorrelationId?,ConversationId?}`; `AuditLog{TenantId?,UserId,ActionType,EntityType,EntityId,Before/AfterJson,Ip,UserAgent,ReferenceNumber,Source,SuccessFlag}`; idempotency tables (`Sales/Purchase/Return/Product/PosCartRequestIdempotency`, `InventoryEventInbox/ProcessedEvent`, `BulkIdempotencyService`) | Durable hop for PG-side trace |
| Consumers/outbox processors | `BeginScope{CorrelationId,TenantId,...}` in `StockSync/MarketplaceProjection/MarketplaceEvent/Accounting/ProviderDelivery` processors + `Affiliate*Consumer` + `HostResolution` (3 scopes) + `CheckoutController.BeginScope` | Consumer-side log linkage |
| OTel/metrics/health | OTel Hosting 1.15.3 + AspNetCore instrumentation + OTLP + Prometheus exporter (beta); ~22 meters; `ActivitySource(PointOfSale.ProviderDelivery, PointOfSale.CommerceHub.PaymentOrchestration, PointOfSale.Payments.Cod.ReferenceWorkflow)`; `/health*` + ~25 checks; Prometheus scrape endpoint | Infra signals (when exported) |
| WhatsApp log | `WhatsAppMessageLogDocument{CorrelationId?,IdempotencyKey(unique sparse),TTL 90d}` | Only Mongo doc persisting correlation |

### 6.2 Gaps (report only — no fixes in this phase)

1. **No end-to-end ID continuity:** HTTP correlation is **not** propagated into MassTransit `ConsumeContext.CorrelationId/ConversationId/MessageId`; publisher synthesizes a new ID. RabbitMQ hops therefore start a new chain.
2. **Mongo projections drop correlation:** `OrderDocument{TenantId,OrderId,LastEventId/Version/OccurredAt per facet}` and `ProductDocument{TenantId,ProductId,...}` carry **no** `CorrelationId`; `AuditLog` has no `CorrelationId`/`CreatedAt`. A cross-tenant leak found in Mongo cannot be walked back to the originating request without outbox joins.
3. **No W3C trace context:** no `traceparent/tracestate` handling; `TraceId` is an app GUID, not a W3C trace ID; no trace-to-log join with external systems.
4. **No Serilog enrichment:** no `UseSerilogRequestLogging`, `Enrich.FromLogContext`, or `LogContext.PushProperty` (repo uses `Microsoft.Extensions.Logging` + custom `FeedTokenRedactingJsonConsoleFormatter`). Scoped fields depend on each middleware/processor remembering `BeginScope`.
5. **Frontend does not guarantee IDs:** known backlog item — storefront UI does not always generate `Idempotency-Key/X-Request-Id`. Harness must generate them; product behavior under missing IDs is a separate functional gap.
6. **Consumers/outbox processors do not propagate `Activity`:** log scopes exist but distributed `Activity` context is not forwarded, so traces fragment at the consumer boundary.
7. **Observability export is off by default:** OTLP ships only if `OpenTelemetry:OtlpEndpoint` is set (empty by default); no `docker-compose`, `prometheus.yml`, Grafana dashboards, or Jaeger/Tempo config in repo; Windows-local has no exporters. K8s phase must add them (see §14).
8. **ES docs lack correlation:** indexed docs carry `TenantId` filter (+`StorefrontBaseUrl`) but no `CorrelationId`; search-path leaks are not traceable to a request.

**Traceability verdict:** `VU → HTTP request → CorrelationId → app operation → Postgres mutation → outbox` is traceable; `→ RabbitMQ → consumer → Mongo/Redis/ES projection` is **not** continuously traceable today. The leak-trace goal in the brief (`VU→user→tenant→store→request→correlation→entity→event→persisted data`) requires the M1 correlation contract (harness-generated `X-Request-Id`/`X-Correlation-Id` + `Idempotency-Key` pairing, §16) plus later server-side propagation work (out of scope for this phase).

---

## 7. Synthetic data model

Deterministic, stable-identity universe. Seed implementation is **out of scope** for this phase; this section defines entities, relationships, identity strategy, and counts so a later seeder can be built without redesign.

### 7.1 Identity strategy

- **Stable natural keys + stable GUIDs:** every entity gets a human-stable slug/code (`tenant-a`, `store-a` (= tenant), `user-a-001`, `product-a-001`, `TEST-PROBE-TENANT-A`) **and** a deterministic GUID (e.g., name-based UUIDv5 from a fixed namespace + natural key) so re-seeds are idempotent and load scripts can hardcode IDs.
- **Credentials:** per-user deterministic test passwords/secrets stored only in test vault (never committed); JWTs minted at scenario setup via real login/refresh (POS/storefront/customer/affiliate), never via `test-auth` in prod-like envs.
- **Headers per VU:** `X-Tenant-Id` = VU's tenant GUID, `X-Session-Id` = per-VU (or per-iteration for guest cart isolation), `X-Request-Id`/`X-Correlation-Id` = per-request UUIDv7 (time-ordered), `Idempotency-Key` = per-write UUIDv7 (POS/checkout), `X-Customer-Id` where the checkout contract allows.
- **Sentinels:** `TEST-PROBE-*` entities with globally-unique, grep-able names/SKUs/slugs per tenant (see §7.3) so any cross-tenant hit is unambiguous.

### 7.2 Required entities & relationships

| Entity | Per tenant | Key fields | Relationships |
|---|---|---|---|
| Tenant (= store) | ≥3 (`tenant-a/b/c`) + 1 control | `TenantId (GUID, deterministic)`, `Slug`, `StorefrontBaseUrl`, status active | Owns everything below |
| Storefront domain mapping | 1 per tenant | host → `TenantId` | Drives `HostResolution` path coverage |
| Staff users | ≥3 per tenant (`user-{a,b,c}-001/002/003`; roles Owner, Admin/Cashier, Manager) | `UserId`, `Role`, `TenantId`, login | `User.TenantId`; used for POS/admin pools |
| Customer users (storefront + CommerceHub) | ≥5 per tenant | `CustomerId`, `TenantId`, email unique per tenant | `OnlineCustomer(TenantId,Email)` unique; storefront + customer JWT pools |
| Affiliate accounts | ≥1 per tenant + 1 global probe | `AffiliateAccountId`, `StoreId==TenantId` link | `AffiliatePromotionLink(TenantId=storeId)` |
| Categories | ≥4 per tenant (incl. 1 sentinel category) | `CategoryId`, `TenantId`, `Slug` | Product → category |
| Products (with variants) | ≥20 per tenant (≥1 sentinel, ≥1 low-stock/contended, ≥1 backorder-allowed, ≥1 untracked) | `ProductId`, `TenantId`, `Slug`, SKU/barcode unique per tenant | `Product.TenantId`; variants → `ProductVariantStocks` |
| Product media/units | cover variant + multi-unit product per tenant | — | Exercises units/variant-resolution paths |
| Warehouses + stock | ≥2 per tenant; deterministic quantities (e.g., plentiful=10k, contended=20, zero=0) | `WarehouseId`, `TenantId` | `ProductVariantStocks(TenantId,VariantId,WarehouseId,Qty,Reserved)` |
| Coupons/promos | 1 valid + 1 expired per tenant | codes namespaced per tenant (`A-LOAD-10`) | Cart coupon probes |
| Carts | ephemeral per VU (seeded empty; created at runtime) | Redis keys per session | Validates cart isolation |
| Checkout sessions | ephemeral per VU | — | Abandoned-checkout path |
| Orders (safe synthetic) | created only in `SAFE-IN-TEST-DB-ONLY` scenarios, bounded count | `OrderId`, `TenantId`, `OrderNumber` | `Order+Shipments+InventoryReservation` tx; cancel path releases reservation |
| Payments intents | intent-only, mocked provider | `PaymentIntentId/Reference` | Never charges (see §10) |
| Reviews (read) | ≥2 per sentinel product | — | Rating-summary probes |
| CMS/navigation/footer/store-config | minimal per tenant | — | Baseline read coverage |

### 7.3 Sentinel/probe entities (isolation tripwires)

Per tenant `X ∈ {A,B,C}` create:

- `TEST-PROBE-TENANT-{X}` product: name/SKU/slug/barcode globally unique (e.g., `SKU-PROBE-A-9f3k`, slug `test-probe-tenant-a-9f3k`), assigned to sentinel category `TEST-PROBE-CAT-{X}`, price fingerprint (e.g., A=111.11, B=222.22, C=333.33).
- `TEST-PROBE-CUSTOMER-{X}` + `TEST-PROBE-ORDER-{X}` (order number fingerprint, e.g., `ORD-PROBE-A-…`) created once in test DB.
- `TEST-PROBE-DOMAIN-{X}` host mapping.

Any response, projection (Mongo/ES), event, or log containing another tenant's probe constant is a **P0 isolation failure** with full `VU→user→tenant→store→request→correlation→entity→event→data` capture (see §8).

### 7.4 Scale guidance for seeding

Start small and deterministic: 3 tenants × (3 staff + 5 customers + 20 products × 2 variants + 2 warehouses + 4 categories). Contended SKUs get their own warehouse + fixed qty so reservation-conflict rates are reproducible. Large-catalog (10k+ products) and large-order-history fixtures are a **later** scale stage, not M1.

---

## 8. Isolation invariants

Each invariant lists the assertion, the load-visible probe, and the trace capture required on violation. All probes run **concurrently across tenants** (same ramp) so time-overlap is guaranteed.

| # | Invariant | Probe (concurrent) | Pass criterion | Violation capture |
|---|---|---|---|---|
| I-1 | Tenant A never receives Tenant B products | VU-A lists/searches/`by-ids`/suggests while VU-B does the same; assert every returned `TenantId`/probe constant ∈ A (and vice versa) | Zero cross-tenant IDs; zero foreign `TEST-PROBE-*` strings in body | VU id, user, tenant, store, method+URL, `X-Request-Id`/`X-Correlation-Id`, response IDs/hashes, `TenantId` claims |
| I-2 | Store A never resolves to Store B | Resolve `slug-A`/`slug-B`, `resolve-slug`, `store-config`, navigation, home concurrently; assert config/slug/host maps to own tenant | `SlugResolver.type/id/canonicalSlug` + store-config tenant match | Request host/slug/headers, resolved `TenantId`, response payload |
| I-3 | User A cannot access User B data | Authenticated `me`, wishlist, order-detail/track, `my-requests`, cart reads with swapped tokens (negative) + own-token positives | Cross-token reads → 401/403/404 (never 200 + foreign data) | Both users' IDs/tenants, token `jti` (never secret), status, body ID set |
| I-4 | Tenant A order reads never return Tenant B orders | `GET /api/storefront/orders/{id}`, `/by-number/{n}`, guest-track, admin `GET /api/orders`, CommerceHub order/suborder reads with own + foreign IDs | Foreign IDs → 404/403; own reads return own `TenantId` only | Order IDs/numbers, tenant claims, correlation IDs |
| I-5 | Marketplace vendor entities remain vendor-scoped | `DiscoverProducts?storeTenantId=X` per vendor + `api/read/*` + reviews/rating-summary concurrently | Every doc's `StoreId/TenantId` ∈ admitted set; no foreign probes | Query params, admitted-set snapshot, returned doc IDs |
| I-6 | Derivative projections never expose foreign tenant data | After checkout/order writes in A, read Mongo-backed (`by-ids`, homepage, `api/read`), ES (`search/suggest`), Redis-cached (`resolve-slug`, feeds) paths in B | No `TEST-PROBE-A` constants in B's projections within *and* beyond consumer lag window | Event `EventId/CorrelationId`, consumer lag timestamps, projection doc versions |
| I-7 | Writes in A never mutate B stock/ledger | Contended-SKU checkout/POS-sale in A while B reads own stock/movements/stats | B's `Available/Reserved` unchanged except by B's own writes; no foreign `OrderId` in B reservations | `VariantId/WarehouseId`, before/after quantities, `InventoryReservation` rows, `StockMovement.CorrelationId` |
| I-8 | Global surfaces bind correctly | Concurrent logins/registers/refreshes with colliding-vs-unique usernames/emails per tenant matrix | Each session's `tenant_id` claim == seeded tenant; no cross-binding | Username/email hash (never password), issued `tenant_id`, session `jti` |
| I-9 | Idempotency keys are tenant-scoped | Replay same `Idempotency-Key` across tenants (negative) and within tenant (positive) | Cross-tenant replay → independent (no foreign stored response); same-tenant replay → identical stored response | `Idempotency-Key`, `X-Request-Id`, tenant, stored-response hash |

**Leak-trace bundle (required on any I-1…I-9 failure):** `vuId, userId, tenantId, storeId/slug, method, url, requestHeaders(X-Request-Id/X-Correlation-Id/Idempotency-Key, never tokens), requestBodyHash, statusCode, responseBodyHash + foreign-ID list, dbEntityIds, eventIds (EventId/CorrelationId/IdempotencyKey), consumer + projection doc/version/index`. If the bundle cannot be completed with current server fields, record which hop is missing (feeds §6.2 back into observability requirements).

---

## 9. Workload profiles

Weights below are **starting proposals** for M1 validation (read-dominant), not capacity claims. All profiles carry `tenant, store, profile, endpoint` tags + VU identity for slicing.

### 9.1 P-READ — Read-heavy storefront (≈70% of M1 traffic)

- `GET resolve-slug/{slug}` (warm-up/cache-hit probe) → `GET store-config` → `GET navigation` → `GET categories` → `GET products` (paged) → `GET products/{slug}` → `GET products/by-id/{id}` → `GET search?q=` → `GET search/suggest` → `GET home/{featured,new-arrivals,best-sellers}` → `GET pages/footer/checkout-fields/payment-methods/shipping-methods` → `GET api/read/{products,homepage}` → `GET marketplace/products/{id}/reviews|rating-summary` → `GET public feed/sitemap` (1×).
- Expected backend mix: Redis (resolve/cache) → Mongo projections → ES (search/suggest) → SQL authority (payment-methods). First bottleneck screen for read path.

### 9.2 P-CART — Cart profile (≈20%)

- `GET cart` → `POST items` → `PUT items/{product}` (qty) → `POST apply-coupon` → `POST pricing` (no order) → `DELETE items/{product}` / `DELETE cart` → `POST merge` (auth transition, low rate).
- Unique `X-Session-Id` per VU; Redis-backed; asserts cart contents never contain foreign-tenant products (I-1/I-3 live check).

### 9.3 P-TXN — Transactional checkout (low rate, bounded; test-DB-only)

- `POST pricing` → `POST checkout` (unique `Idempotency-Key` + `X-Request-Id` pair per attempt) → poll `GET payments/status/{orderId}` → `GET orders/{id}` (auth) / `track-guest` → `POST admin/orders/{id}/cancel` (release path, staff token).
- Contended-SKU sub-scenario (separate schedule): N VUs × same variant/warehouse → measure 200 vs 409 distribution, reservation TTL behavior, cleanup-job lag. Never exceed seeded stock-loss budget.

### 9.4 P-POS — POS staff profile (low concurrency, authenticated)

- Login (setup) → `GET products/lookup` → `GET products/barcode/{barcode}` (scan hot path) → `GET products/quick-access` → `POST pos-carts` → `POST .../items` → `GET .../open` → `GET sales/{id}` / `GET sales` reads. Sale creation (`POST /api/sales`) only in isolated test-DB scenario with expendable stock; returns/voids/adjusts excluded.

### 9.5 P-MKT — Marketplace discovery (read-dominant + filtered writes excluded)

- `GET api/read/products(+{id})` → `GET api/commercehub/products(+{id})` → `GET commercehub/search(+suggestions/autocomplete/popular)` → `POST api/marketplace/search` (low rate) → `GET marketplace/products/{id}/reviews|rating-summary|review-context` → `GET api/commercehub/cart` → `POST commercehub/cart/price` (no checkout) → vendor-filtered discovery (`storeTenantId`, I-5 probe).
- CommerceHub checkout (`POST /api/commercehub/checkout`, 30-min TTL) treated like P-TXN: isolated, single-threaded-or-low-rate, test-DB-only, or excluded from steady-state load.

---

## 10. Unsafe side effects

Four classes. Harness must enforce by **route denylist + separate credentials/scopes + test-DB gate + provider mocks**.

### 10.1 DO-NOT-LOAD-TEST (never generate load; single manual call at most)

- Payment webhooks: `POST /api/webhooks/stripe`, `/webhooks/paymob`, `/webhooks/fawry`, `/api/payments/webhooks/{provider}`, `/api/commercehub/payments/webhook`, Paymob webhook/callback — signature-verified money events; replay risks double-settle.
- Carrier webhooks: `POST /api/webhooks/shipping/{carrierCode}|/bosta|/aramex|/mylerz|/dhl` — mutate shipments. (`GET .../health` is the only benign read.)
- Destructive/platform: `POST /api/maintenance/reset-database` (SuperAdmin, **destroys DB**); `POST /api/admin/search/{index|recreate-index|reindex}` + `DELETE .../index`; `POST /api/marketplace/admin/reindex(+/global)`; `POST /api/admin/af16e-reindex/trigger`; `POST /api/upload` (storage cost); `api/debug/outbox/{process|reset|requeue}` (mutates outbox); `api/dev/commercehub/{seed|.../execute-cod-debt-reduction|.../execute-online-before-payout|.../simulate-success|.../repair-cod-reservation}` (seed + simulated money + reservation repair).
- Catalog-danger: `POST /api/products/bulk/*`, `/migrate-to-multi-unit`, `/sync-stock-to-mongo`, `/generate-barcode` (at volume).

### 10.2 REQUIRES-MOCKING (may be exercised only with providers stubbed + test DB)

- Stripe `POST /create-intent`, Paymob `POST /intent/{orderId}`, `POST /api/payments/intents/{orderId}`, CommerceHub `POST /payments/initiate`, Paymob/Fawry/Stripe gateway calls, Baileys/WhatsApp sends (`whatsapp:otp:*`, checkout WhatsApp events), emails/OTPs (`request-phone-otp`, password-reset, verification), shipping-carrier calls. Committed config already disables Paymob execution (`Payments.Paymob.FeatureFlags.*Enabled=false`, `SandboxMode=true`); harness must assert mock mode before any intent scenario.

### 10.3 SAFE-IN-TEST-DB-ONLY (bounded, budgeted, reversible-or-expired)

- `POST /api/storefront/checkout` (10-min reservations; needs stock-loss budget + cleanup-job awareness), `POST /api/orders` (admin manual create), `POST /api/commercehub/checkout` (30-min TTL), `POST /api/sales` (decrements real stock), `POST /pos-carts*` lifecycle, `POST /admin/orders/{id}/cancel` (release), `POST /payments/cod/confirm/{orderId}`, fulfillment/payment-lifecycle/settlement/refund/reward/payout writes, review/request-lifecycle writes, telemetry writes (`POST /search/{click,impression,filter-event}` — write amplification), attachment upload/confirm leases.

### 10.4 SAFE (default load surface)

All `GET` reads in §3.1/§3.3 (`resolve-slug`, store-config/navigation/categories/products/search/suggest/home/pages/footer/checkout-fields/payment-methods/shipping-methods-reads, `api/read/*`, review-list/rating-summary/context, feeds/sitemap/robots, `payments/status/*`, `SearchReindex GET /status`, webhook `GET /health` probes), `POST checkout/pricing`, cart CRUD with unique sessions, `POST shipping/quotes|preview` at low weight, auth login/refresh at low rate (no OTP/social spam).

---

## 11. Recommended load-testing framework

**Recommendation: k6.** (Current preference confirmed; repo evidence gives no reason to reject it.)

| Criterion | k6 | Locust | NBomber | JMeter | Verdict |
|---|---|---|---|---|---|
| Windows-local execution | Single Go binary, no JVM/Python env per VU | Needs Python + processes | .NET (fits shop, but load agents on Windows compete with API) | JVM + GUI overhead | k6 simplest on Windows |
| CPU/mem overhead | Low (Go, VU-efficient, headless) | Higher (Python procs/gevent) | Low (but runs on same .NET host class) | Highest (threads + GUI) | k6 wins |
| Git-friendly definitions | JS/TS modules, reviewable diffs | Python, also git-friendly | C#/F# (good) but load code mixes with app lang | XML/JMX (poor diffs) | k6 wins |
| Deterministic VU behavior | `scenarios` (ramping-vus, constant-vus, per-VU), seeded RNG, `setup()/teardown()`, groups/checks | Possible but less precise staging | Strong .NET scenarios | Thread groups, less expressive | k6 ≈ NBomber > rest |
| Staged ramp + thresholds | Native `stages`, `thresholds` (p95/p99, error %), abort-on-breach | Manual/extension | Native | Via plugins | k6 native |
| Tags/metadata | Native `tags` (tenant/store/profile/endpoint/VU), `X-Request-Id` per request, custom metrics | Possible | Good | Labels, clunky | k6 native — critical for §8 slicing |
| CI suitability | `k6 run` exit codes, JUnit/JSON, containers | Good | Good (dotnet test) | CI-capable but heavy | k6 wins |
| Decoupled scale-out | Run k6 on separate host/container vs API; k6 Cloud/operator later on K8s | Also decouples | Typically co-located .NET | Distributed mode is ops-heavy | k6 decoupling story is simplest |
| Team fit | JS/TS familiar via storefront; load DSL isolated from C# business logic | Python familiar for data work | C# familiar but risks mixing load + app code | Lowest fit | k6 keeps concerns separated |

**Role split (firm):** k6 = primary traffic generator (the only thing that produces HTTP load). Python = test-data generation, result analysis, orchestration, validation — never ad-hoc `requests`-loop load. NBomber remains the credible fallback **iff** the team later demands in-process .NET load steps reusing C# DTOs; that scenario must still run off-host and is not M1.

---

## 12. Proposed project structure

All new, all under `tests/load/` (does not exist yet — this doc is the first file). No production code touched.

```text
tests/load/
  LOAD_TEST_DISCOVERY.md        # this file (design only)
  README.md                     # (later) how to run safely; test-DB gate checklist
  k6/
    smoke.js                    # (later) 1-VU read Carlton + tenant checks
    read-storefront.js          # (later) P-READ
    cart.js                     # (later) P-CART
    checkout-txn.js             # (later) P-TXN, test-DB-only + budget guard
    pos.js                      # (later) P-POS
    marketplace.js              # (later) P-MKT
    soak.js / stress.js / spike.js / breakpoint.js  # (later) staged progressions
    lib/
      auth.js                   # scheme-aware login/refresh pools (5 schemes)
      tenant.js                 # tenant/store resolution + header builder
      identity.js               # deterministic ids, probe constants, idempotency keys
      http.js                   # X-Request-Id/X-Correlation-Id injection + leak-trace capture
      checks.js                 # I-1..I-9 assertions + violation bundle writer
      thresholds.js             # shared SLOs (p95/p99, error %, 409-rate for contended SKU)
  data/
    universe.json               # (later, generated) deterministic tenants/stores/users/products/warehouses
    probes.json                 # (later) TEST-PROBE-* constants
    manifests/                  # per-run seed manifest (ids, hashes, stock budgets)
  python/
    seed_plan.py                # (later) plan/validate universe (no writes in M1)
    analyze.py                  # (later) k6 JSON → latency/RPS/bottleneck + isolation verdict
    orchestrate.py              # (later) stage runner + guardrails (test-DB check, mock check)
    validate.py                 # (later) I-1..I-9 offline verification from captured bundles
  results/
    .gitkeep                    # (later) run artifacts (gitignored payloads, committed summaries)
  grafana/
    dashboard.json              # (later, K8s phase) Prometheus/Grafana views
```

Conventions: every k6 request sets `tags {tenant, store, profile, endpoint, vu_kind}` + `X-Request-Id`/`X-Correlation-Id` + scenario VU identity; every run writes a manifest (seed hashes, stock budgets, provider-mock proof, DB target proof) before sending traffic; `results/` payloads stay gitignored, summaries committed.

---

## 13. Proposed staged test progression

Derived from architecture + environments (Windows-local validation first, K8s characterization later), **not** blind 10→1000. Local Windows runs API + frontends while infra sits on a separate Ubuntu host — generator/API/infra contention and cross-host latency dominate early, so early stages prove harness correctness + isolation, not capacity.

### Stage 0 — Harness validation (local, no SLO pressure)

- 1 VU × 5 min: `resolve-slug → store-config → categories → products → product-detail → search → suggest` per tenant; assert I-1/I-2 + header echo + correlation response headers.
- 3 VUs (1 per tenant) × 10 min concurrent: same reads overlapped; assert zero foreign probes (first real I-1…I-6 signal).

### Stage 1 — Read baseline (local, find read knee)

- Ramping-VUs `1 → 10 → 25 → 50` (5 min each + 2 min hold), P-READ only. Gate: p95/p99 + error % + ES `search/suggest` latency split + Redis-hit vs Mongo/ES-miss split. Stop at first SLO breach; record RPS ceiling. **Do not jump to 100+ locally** — Windows host noise invalidates capacity claims.

### Stage 2 — Cart state (local, Redis behavior)

- Constant-VUs `10 → 25` (10 min each), P-CART with unique sessions. Watch Redis lock contention (`CartMutationLock`), MassTransit cart-event backlog, Mongo validation latency. Assert I-1/I-3 on every cart read.

### Stage 3 — Transactional trickle (test DB only, reservation mechanics)

- `1 → 5` VUs P-TXN (bounded order budget, unique idempotency keys), then contended-SKU `5 → 10` VUs on one variant/warehouse. Measure 200/409 split, `lock:checkout` 409s, `ReserveAsync` row-lock waits, outbox lag, cleanup-job expiry. Assert I-4/I-6/I-7. Abort on stock-budget exhaustion.

### Stage 4 — POS + marketplace reads (local, authenticated mix)

- `5 → 10` VUs P-POS reads (`lookup`, `barcode`, `quick-access`, `pos-carts` lifecycle at 1 VU) + `10 → 25` VUs P-MKT reads. Assert I-5 + vendor scoping.

### Stage 5 — Stress / spike / breakpoint (K8s or isolated perf env only)

- Stress: past Stage-1 read knee until p99/error SLO breaks; spike: 2× burst × 2 min; breakpoint: ramp to first 500-class/errors or queue blow-up. These stages are **meaningless on shared Windows-local** — require K8s + exporters (§14).

### Stage 6 — Soak (K8s/staging, test DB, 2–8 h)

- Sustained Stage-1 knee × 0.7 + P-CART background + P-TXN trickle. Watch reservation-cleanup drift, outbox dead-letters, Redis memory, Mongo/ES lag, token-refresh health (`ClockSkew=Zero` sharp expiry), Hangfire recurring stability.

### SLO starting points (to be calibrated, not enforced as prod SLOs yet)

- Reads: p95 < 300 ms local (< 150 ms K8s later), p99 < 800 ms, error < 0.5%.
- Pricing/cart-write: p95 < 600 ms, error < 1% (excluding expected contended-SKU 409s, tracked separately).
- Checkout: p95 < 1500 ms at ≤5 VUs, 409-rate on contended SKU reported (not failed), zero 500s, zero isolation violations at any stage (any I-failure aborts the run as P0 regardless of latency).

---

## 14. Observability requirements

### Needed for credible load testing (none fully present locally today)

1. **Metrics export on:** OTLP endpoint configured (currently empty by default) **or** Prometheus scrape wired to a real Prometheus; existing ~22 meters + AspNetCore instrumentation become queryable; add dashboards (RPS, p50/p95/p99 by endpoint×tenant, error %, checkout 200/409 split, lock waits, ES query latency, Mongo op latency, Redis latency, MassTransit publish/consume lag, consumer backlog, outbox depth, reservation active/expired/released, Hangfire job duration/failures, refresh-token reuse detections).
2. **Logs joined:** ship JSON logs (existing `FeedTokenRedactingJsonConsoleFormatter`) to a queryable store with `CorrelationId/TenantId/UserId/RequestPath/Method/LatencyMs` indexed; add missing `Activity` propagation (consumer/outbox) and W3C `traceparent` handling so VU→request→event→projection joins work.
3. **Traces:** enable OTel tracing export (currently only `ProviderDelivery/CommerceHub.Payment/Cod.Reference` sources + AspNetCore); propagate HTTP correlation into MassTransit (`CorrelationId/ConversationId`) and persist it into Mongo projections + `AuditLog` (schema change — later milestone, not load phase).
4. **Infra signals:** Postgres (`pg_stat_activity`, lock waits, `ProductVariantStocks` row contention), Redis (`INFO`, evictions, lock keys), Mongo (op latencies, replication lag), RabbitMQ (queue depth, unacked, redeliveries from the 1s/5s/15s/30s retry chain), ES (indexing/search latency, cache hit), Hangfire (job backlog). Ubuntu infra host needs node-exporters; Windows API host needs process/host metrics; k6 host isolated so generator saturation never masquerades as SUT saturation.
5. **Run manifests:** every run records API build/commit, DB target proof (test DB, never prod), seed manifest hash, stock budgets, provider-mock proof, stage/VU/duration, infra versions, network path (local vs K8s). Without this, cross-run comparisons are invalid.

---

## 15. Known gaps/blockers

| # | Gap/blocker | Impact | Unblocks |
|---|---|---|---|
| G-1 | No load harness exists (`tests/load/` absent before this doc; zero k6/Locust/NBomber/JMeter/docker-compose hits) | Nothing to run | M1 skeleton (§16) |
| G-2 | No deterministic seed universe (tenants/stores/users/products/warehouses/customers/carts) | VUs have no stable identities; isolation asserts impossible | Seed planner + test-DB seeder (later milestone) |
| G-3 | HTTP→MassTransit→Mongo/ES correlation break (§6.2) | Leak cannot be traced to originating VU/request past outbox | Server-side propagation + projection/audit schema additions (later; load phase only asserts + captures) |
| G-4 | No Prometheus/Grafana/OTLP locally; OTLP endpoint empty by default | No capacity numbers beyond k6 client-side latency | Exporters + dashboards (K8s phase); M1 uses k6 JSON + `/health/*` only |
| G-5 | Windows-local shares API + frontends; infra remote on Ubuntu | Capacity numbers not transferable; noisy knees | Treat local as validation-only; capacity in K8s/perf env |
| G-6 | Frontend does not guarantee `Idempotency-Key/X-Request-Id` (backlog-noted) | Real-client behavior differs from harness; replay semantics unclear | Harness generates IDs; product gap tracked separately |
| G-7 | Checkout/POS/marketplace writes contend on `ProductVariantStocks` row locks + Redis locks + 10/30-min TTLs + rate limits | Naive ramp melts reservations; 409 storms misread as failures | Budgeted, low-rate P-TXN + contended-SKU schedule + cleanup-job awareness |
| G-8 | `marketplace_services` ES index intentionally tenant-unscoped; affiliate `storeTenantId` + super-admin `tenantId` are intentional global knobs | False-positive "leak" reports if probed naively | I-5/I-8 scoped probes with admitted-set semantics |
| G-9 | `test-auth` minting exists in dev/test but is prod-stripped | Harness must use real logins; cannot shortcut auth in prod-like envs | Auth-pool design in M1 |
| G-10 | Secrets/connection strings empty in committed config (by design) | Load env needs injected test credentials + test-DB target proof | Env setup checklist with never-commit-secrets rule |

---

## 16. Recommended next implementation milestone (M1 — harness skeleton, no traffic yet)

**Goal:** runnable `k6 smoke.js` (1 VU reads) + auth/tenant/header libraries + isolation-check library + seed plan + guardrails — still zero production writes.

1. **Skeleton:** `tests/load/{README,k6/{smoke,lib/*},data/.gitkeep,python/{seed_plan,analyze,orchestrate,validate},results/.gitkeep}`; k6 installed on generator host (separate from API host); `k6 --version` + `smoke.js --vus 1 --iterations 1` against health + `resolve-slug` proves wiring.
2. **Identity & headers lib:** deterministic UUIDv5 universe draft + per-request UUIDv7 `X-Request-Id`/`X-Correlation-Id` + `Idempotency-Key` pairing + tag taxonomy (`tenant,store,profile,endpoint,vu_kind`) + five-scheme auth pools via real login/refresh.
3. **Isolation-check lib:** implement I-1…I-9 assertions + violation-bundle writer (§8) against recorded fixtures first (no live traffic), then Stage-0 live reads.
4. **Seed plan (no writes):** `seed_plan.py` outputs `universe.json` + `probes.json` + stock budgets + order-budget caps for review; seeder itself is M2.
5. **Guardrails:** `orchestrate.py` preflight — test-DB target proof, provider-mock proof (Paymob/Stripe/Fawry/WhatsApp/email/shipping disabled or stubbed), route denylist (§10.1) enforced, super-admin token refused unless explicit flag, stock/order budgets loaded; any check fails → refuse to run.
6. **Read-smoke:** Stage-0 (1 VU, then 3×1-tenant concurrent) with k6 JSON output → `analyze.py` latency/RPS summary + isolation verdict. Success = green reads + zero foreign probes + complete trace bundles on any failure.
7. **Exit criteria for M2 (seed + staged load):** smoke green on test DB, dashboards/exporters plan agreed for K8s, stock-loss + order-count budgets signed off, mock proofs automated.

---

## Appendix A — Files inspected (representative, not exhaustive)

- `PointOfSale.API/Program.cs` (pipeline, 5× JWT, MassTransit, OTel, health, Prometheus)
- `PointOfSale.API/appsettings.json` (Jwt ×5, Storefront URL template, Paymob disabled flags, Baileys, CommerceHub)
- `PointOfSale.API/Middleware/{TenantResolutionMiddleware,HostResolutionMiddleware,AdminTenantAuthorityMiddleware,CorrelationIdMiddleware,RequestContextLoggingMiddleware,RequestContextMetadata,StorefrontJwtBlacklistMiddleware,StorefrontPublicationGuardMiddleware,ThemePreviewAuthorityMiddleware}.cs`
- `PointOfSale.API/Security/{AuthCookieConfiguration,JwtBearerRoutingHelper,PosSessionAuthority}` + `Configuration/JwtMiddlewareConfiguration.cs`
- `PointOfSale.API/Controllers/AuthController.cs`, `StorefrontAuthController.cs`, `CustomersAuthController.cs`, `AffiliateAuthController.cs`, `SuperAdmin/**/SuperAdminAuthController.cs`, `TestAuthController.cs`, `TenantsController.cs`, `SlugResolverController.cs`
- `PointOfSale.API/Controllers/Storefront{Products,Categories,Search,Cart,Home,Navigation,StoreConfig,Pages,Footer,PaymentMethods,ShippingMethods,ShippingQuotes,Shipment,OrdersRead,CheckoutFields,Auth,Wishlist,StripePayments,PaymobPayments,Checkout Session variants}.cs` + `CheckoutController.cs`
- `PointOfSale.API/Controllers/{PosCarts,Sales,Products,Orders,AdminOrders,OrderFulfillment,Payments,PaymentLifecycle,PaymentMethodsAdmin,AdminInventory}.cs`
- `PointOfSale.API/Controllers/{Marketplace,MarketplaceRead,MarketplaceProductReviews,CommerceHub,CommerceHubCart,CommerceHubCheckout,CommerceHubOrders,CommerceHubPayments,MarketplaceCmsPages,MarketplaceFooter}.cs`
- `PointOfSale.API/Controllers/{StripeWebhooks,PaymobWebhooks,FawryWebhook,ShippingWebhooks,PaymentWebhooks,Upload,SearchReindex,Maintenance,Debug,DevelopmentCommerceHub}.cs`
- `PointOfSale.API/Services/{Cart/CartService,Checkout/CheckoutService,Pos/PosCartRuntimeService,InventoryService,Storefront/*,CommerceHub/CommerceHubCheckoutService,CommerceHub/InventoryConsistencyService,Payments/*}.cs` + `API/Events/CheckoutEvents.cs`
- `PointOfSale.API/Consumers/{OrderShipmentOrchestrationConsumer,RedisInvalidationConsumer,CommerceHub/CommissionRulesCacheInvalidationConsumer}.cs`
- `PointOfSale.Infrastructure/{Data/ApplicationDbContext,Data/TenantSaveChangesInterceptor,Data/Repositories/ProductVariantStockRepository,EventBus/MassTransitEventPublisher,EventBus/InMemoryEventPublisher,Instrumentation/MarketplacePublishFilter,Jobs/OutboxPublisherJob,Consumers/*,MongoDB/{Services/MongoDbContext,Documents/*},Redis/CacheKeyBuilder,Redis/Services/RedisPerformanceTelemetry,Elasticsearch/{Configuration/ElasticsearchSettings,Services/*}}.cs`
- `PointOfSale.Core/{Entities/{Tenant,User,Product,OnlineCustomer,InventoryReservation,CommerceHub*},Interfaces/{ITenantContext,ITenantOwned,ICorrelationIdAccessor},Events/IntegrationEvent,Enums/UserRole}.cs`
- `PointOfSale.API/Infrastructure/CorrelationIdAccessor.cs`, `Instrumentation/FeedTokenRedactingJsonConsoleFormatter.cs`, `HealthChecks/HealthCheckJsonResponseWriter.cs`, `Jobs/HangfireInitializationHostedService.cs`
- `PointOfSale.Storefront/{package.json,.env,src/stores/*,src/types/*}`; `PointOfSale.Web/src/services/api.js`; `deploy/staging/*`; `PointOfSale.Tests/**` (noted concurrency/reliability suites; no load generators found)

## Appendix B — Verification

- `Test-Path tests/load` before: `False`; created `tests/` + `tests/load/` for this document only.
- Glob checks for `tests/load/**`, `**/*.k6.js`, `**/k6*`, `**/locust*`, `**/nbomber*`, `**/jmeter*`, `**/docker-compose*`: zero hits (no prior harness).
- `git status --porcelain` after this phase must show only `?? tests/` (plus pre-existing dirty state noted at phase start — no new modifications to tracked files; verified before finishing).
