# Recovery 5 — Cache Core and Cleanup Implementation

Date: 2026-08-03  
Scope: Phase 4.2 Cache Core Recovery + Phase 4.3 Dormant Cache Cleanup  
Result: **PASS WITH OBSERVATIONS**

## Executive Summary

Recovery 5 completes the cache architecture work identified by `CACHE_RUNTIME_CERTIFICATION.md` without reopening Product Authoring, Projection, Catalog, Runtime Ownership, Theme Runtime, Builder Runtime, or Browser Runtime Cache Recovery.

The recovered architecture now has explicit semantic owners for the live Storefront product, shipping-method, and wishlist Redis families; required invalidation failures are retryable rather than acknowledged best-effort; owned cache population is fenced against invalidation races; Checkout mutates cart state only through `ICartService`; and all `/api/storefront` responses receive one canonical private/no-store HTTP contract.

All cache families proven to have no runtime reader or return dependency were removed. Live marketplace caches, SEO slug/robots caches, the authoritative Redis cart, and immutable presentation module registries were retained.

## Recovered Violations

| Certification finding | Recovery |
|---|---|
| C-03 best-effort invalidation acknowledgement | Required Redis invalidation now throws `RedisCacheInvalidationException` when Redis is enabled and unavailable or a mutation fails. Consumer fences complete only after invalidation succeeds. |
| C-04 inactive projection-lag guard | Product, homepage-product, and shipping populations supply both projection and source-event timestamps plus the configured maximum lag. Entries missing the required event tuple skip cache population. |
| C-05 affiliate home-key collision | Featured, new-arrival, and best-seller entries are split by affiliate authority input. |
| C-06 dormant/write-only cache families | Deleted the certified product-document, StoreConfig, promotion, promotion-ranking, CMS scope, Storefront footer, homepage-section, category, URL memory, online-customer profile, and ignored SEO artifact paths. |
| C-07 incomplete HTTP cache policy | Added one middleware contract for every `/api/storefront` response and removed endpoint-local policy fragments. |
| C-08 split cart mutation authority | Checkout now reads and updates active checkout idempotency through `ICartService`; `CartService` owns the lock and Redis serialization. |
| C-09 invalidation/repopulation race | Added per-family invalidation generations and conditional Redis transactions for owned populations. |
| Duplicate shipping/product/wishlist invalidators | Replaced raw key deletion with the corresponding semantic owner. |
| Unbounded fraud signal dictionary | Replaced controller-static mutable state with a bounded, expiring singleton owner. |
| Additional write-only reservation documents | Removed `reservation:{tenant}:{reservation}` writes/deletes after repository search proved there was no reader. SQL inventory reservations and their events remain authoritative. |

## Architecture Before

- Event consumers, projection consumers, repair tools, and request services could mutate overlapping Redis key families directly.
- Invalidation failures could be logged and swallowed before event-fence completion.
- A late read-through population could restore an entry after its invalidation completed.
- Product/home cache lag checks lacked the complete timestamp tuple required to operate.
- Checkout rewrote serialized cart documents outside `CartService`.
- Storefront HTTP caching depended on inconsistent controller-level behavior or unspecified browser/proxy defaults.
- Multiple registered caches had writers or invalidators but no runtime reader.
- Funnel fraud counters were held in an unbounded process-static dictionary.

## Architecture After

| Cache/state family | Mutation owner | Invalidation owner | Source of truth |
|---|---|---|---|
| Product detail/list candidates | `StorefrontProductService` | `IStorefrontProductCacheInvalidator` | PostgreSQL authority over Mongo candidates |
| Legacy homepage product candidates | `StorefrontHomeService` | `IStorefrontProductCacheInvalidator` | PostgreSQL authority over Mongo/Elasticsearch candidates |
| Shipping methods | `IShippingMethodCacheService` | `IShippingMethodCacheService` | PostgreSQL through the Mongo projection |
| Wishlist list/count | `StorefrontWishlistService` | `IStorefrontWishlistCacheInvalidator` | PostgreSQL through the Mongo projection |
| SEO slug/robots | `SeoService` | `SeoService` TTL policy | Current SQL authority checks |
| Redis cart | `CartService` | `CartService` | Redis authoritative ephemeral state |
| Marketplace CMS/footer/homepage | Existing marketplace owners | Existing marketplace owners | Existing marketplace authorities; unchanged |
| Browser runtime caches | Recovery 4 owners | Recovery 4 owners | Frozen Recovery 4 contract; unchanged |
| Funnel fraud observations | `IFunnelFraudSignalStore` | Expiry/cap eviction in the same owner | Observability-only process state |

## Redis Ownership Recovery

- Added `IStorefrontProductCacheInvalidator` as the only product/list/home invalidation boundary.
- Added `IShippingMethodCacheService` as the only shipping entry mutation and invalidation boundary.
- Added `IStorefrontWishlistCacheInvalidator` as the only wishlist list/count invalidation boundary.
- Routed product lifecycle, variant, promotion, search-repair, and admin rebuild invalidation through the product owner.
- Routed shipping projection invalidation through the shipping owner.
- Routed wishlist projection and customer-deactivation invalidation through the wishlist owner.
- Removed raw Redis invalidation from StoreConfig, navigation, and online-customer profile projection paths after their dormant server caches were retired.
- Converted Redis-enabled invalidation endpoint absence and mutation failure into retryable exceptions.
- Preserved Redis-disabled behavior: invalidation is a legitimate no-op only when Redis is configured off.

## Cache Ownership Recovery

- Added `CacheEnvelope<T>.InvalidationGeneration`.
- Added `IRedisJsonCacheService.GetOrCreateOwnedAsync` and `AdvanceInvalidationGenerationAsync`.
- Owned reads verify a stable generation before and after the Redis read.
- Owned writes use a conditional Redis transaction and cannot commit after their observed generation advances.
- Product/home/shipping readers use owned population and operational projection-lag inputs.
- Wishlist request mutations no longer duplicate projection-triggered invalidation.
- Checkout delegates the active checkout idempotency mutation to `CartService` under the cart mutation lock.
- The former global fraud map is capped at 10,000 entries and expires inactive entries after one hour.

## HTTP Cache Contract

`StorefrontHttpCachePolicyMiddleware` is registered exactly once and applies to every `/api/storefront` response, including errors:

```text
Cache-Control: private, no-store, no-cache, max-age=0, must-revalidate
Pragma: no-cache
Expires: 0
Surrogate-Control: no-store
```

The middleware removes `ETag` and `Last-Modified`. The contract deliberately emits no validator and does not implement `If-None-Match`; Storefront compatibility remains normal non-conditional responses while browser runtime caches remain governed by frozen Recovery 4.

Endpoint-local `Cache-Control` fragments were removed from Checkout, Products, Shipping Quotes, Shipping Methods, Payment Methods, Landing, and CMS preview/published paths.

## Dormant Cache Cleanup

Removed only after source search established no runtime reader and no return dependency:

- Direct Redis `ProductDocument` cache and its interface/registration.
- Redis StoreConfig cache, invalidators, manual clear behavior, and key helper.
- Redis promotion key families and legacy invalidators.
- Write-only promotion-ranking cache and registration.
- Storefront CMS published-scope cache, cache options, invalidators, and private write-only path.
- Storefront footer cache and invalidators.
- Unreachable homepage-section cache, options, and mutation invalidators.
- Category tree/by-slug/home-category Redis artifacts and manual invalidation API behavior. Compatibility routes remain successful no-ops where external callers may still invoke them.
- `StorefrontUrlResolver` write-only `IMemoryCache` entry and unused eviction method.
- Online-customer profile Redis write-through artifact and duplicate profile invalidations.
- SEO metadata, breadcrumb, and sitemap entries whose hits were ignored or never read. Live SEO slug resolution and robots caching remain.
- Write/delete-only Checkout reservation tracking documents.
- Dead cache key helpers, DTO/test doubles, options, and DI registrations belonging to the removed families.

## Files Modified

### New ownership and contract files

- `PointOfSale.Infrastructure/Redis/Services/StorefrontProductCacheInvalidator.cs`
- `PointOfSale.Infrastructure/Redis/Services/ShippingMethodCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/StorefrontWishlistCacheInvalidator.cs`
- `PointOfSale.Infrastructure/Services/FunnelFraudSignalStore.cs`
- `PointOfSale.API/Middleware/StorefrontHttpCachePolicyMiddleware.cs`
- `PointOfSale.Tests/Architecture/CacheCoreAndCleanupArchitectureTests.cs`

### Core Redis and registration files

- `PointOfSale.Infrastructure/Redis/CacheKeyBuilder.cs`
- `PointOfSale.Infrastructure/Redis/Configuration/RedisSettings.cs`
- `PointOfSale.Infrastructure/Redis/Models/CacheEnvelope.cs`
- `PointOfSale.Infrastructure/Redis/RedisServiceCollectionExtensions.cs`
- `PointOfSale.Infrastructure/Redis/Services/RedisJsonCacheService.cs`
- `PointOfSale.API/Program.cs`

### Storefront/cache consumers and services

- Product lifecycle/search/variant/promotion consumers under `PointOfSale.Infrastructure/Consumers/`
- `ShippingMethodProjectionConsumer.cs`
- `OnlineCustomerProjectionConsumer.cs`
- `OnlineCustomerWishlistProjectionConsumer.cs`
- `StorefrontConfigProjectionConsumer.cs`
- `TenantCurrencyProjectionConsumer.cs`
- `NavigationProjectionConsumer.cs`
- `RedisInvalidationConsumer.cs`
- Storefront product, home, category, shipping, wishlist, online-customer, SEO, footer, URL resolver, Cart, Checkout, CMS, homepage-section, promotion-ranking, repair, admin CMS, settings, sync, and reindex services/controllers affected by the retired boundaries
- Storefront controllers from which endpoint-local HTTP cache headers were removed

### Deleted dormant implementation files

- `PointOfSale.Infrastructure/Redis/Services/ProductCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/IProductCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/StoreConfigCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/IStoreConfigCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/PromotionCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/PromotionRankingCacheService.cs`
- `PointOfSale.Infrastructure/Redis/Services/IPromotionRankingCacheService.cs`
- `PointOfSale.API/Services/Cms/CmsPageCacheService.cs`
- `PointOfSale.API/Services/Homepage/HomepageSectionCacheService.cs`
- `PointOfSale.API/Services/Storefront/StorefrontFooterCacheService.cs`
- `PointOfSale.API/Configuration/CmsPageCacheOptions.cs`
- `PointOfSale.API/Configuration/HomepageSectionCacheOptions.cs`

### Regression test updates

- Cache-dependent CMS, homepage, footer, admin, marketplace, integration, shipping, Storefront isolation, and architecture tests were updated to use the recovered ownership boundaries or direct SQL behavior.
- Frozen runtime/theme/builder implementation files were not changed by Recovery 5.

## Regression Tests

`CacheCoreAndCleanupArchitectureTests` permanently fails CI for:

- Missing or duplicate live Storefront cache-owner registrations.
- Raw Redis mutation/invalidation from consumers or repair tools.
- Missing generation fencing or fail-closed invalidation behavior.
- Checkout cart mutation or dormant reservation writes outside `CartService`.
- Return of any certified dormant service, helper, option, or registration.
- Category/profile/URL/SEO write-only cache paths.
- Theme or Builder imports that mutate the Browser Runtime Cache owner.
- Reintroduction of the unbounded funnel global dictionary.
- Missing/duplicate canonical HTTP middleware registration.
- Endpoint-local Storefront HTTP cache overrides.
- Failure to strip HTTP validators at runtime.

Existing Runtime Ownership, Builder Runtime, and Redis freshness certification tests were retained and aligned to the new semantic product invalidator.

## Search Proof

Repository source search produced:

```text
owner_product_registration=1
owner_shipping_registration=1
owner_wishlist_registration=1
direct_consumer_redis_mutations=0
dormant_symbols=0
storefront_controller_cache_headers=0
write_only_reservation_keys=0
checkout_direct_cart_redis=0
theme_builder_runtime_cache_imports=0
funnel_static_global_dictionary=0
http_contract_registration=1
```

The dormant-symbol search covers the deleted product, StoreConfig, promotion, promotion-ranking, CMS, homepage, footer, category invalidation, and ignored SEO key helpers across production C# source while excluding build output and frozen historical documents.

## Validation Results

| Validation | Result |
|---|---|
| Cache/runtime/builder architecture tests | PASS — 24/24 |
| Focused recovered backend tests | PASS — 39/39 |
| Storefront frontend tests | PASS — 234/234 |
| Relevant Web Builder tests | PASS — 19/19 |
| Storefront production build | PASS |
| Web production build | PASS |
| .NET solution build | PASS — 0 warnings, 0 errors on final build |
| `git diff --check` | PASS — no whitespace errors; existing CRLF conversion warnings only |

Broader diagnostic suites were also executed:

- .NET Storefront suite: 50/63 passed. Thirteen failures are outside Recovery 5: four frozen Theme token expectations, one frozen Builder preview expectation, and eight SQLite fixtures blocked by the PostgreSQL-only `btrim` function.
- Broad relevant backend group: 58/69 passed. Eleven existing CMS/marketplace seed/query-filter fixtures failed; all 39 cache-recovery-focused backend tests pass.
- Full Web suite: 489/495 passed. Six existing failures are in checkout mocks/state and i18n data, outside the cache architecture changes.

## Remaining Observations

- The broad suite failures above predate or belong to frozen/completed subsystems. Recovery 5 did not alter their implementations to manufacture unrelated green results.
- Live SEO slug and robots entries remain TTL-owned because they have real return readers; they were not dormant and therefore were not deleted.
- Live marketplace caches retain their existing owners and warmers; they were outside the certified dormant Storefront list.
- Redis cart remains authoritative ephemeral state, not a reconstructive cache.
- Build output retains existing chunk-size/dynamic-import and Browserslist warnings; both frontend production builds succeed.

## Final Certification

**PASS WITH OBSERVATIONS**

Cache Core Recovery and Dormant Cache Cleanup are complete. The recovered cache architecture has explicit mutation/invalidation boundaries, race fencing, one Storefront HTTP cache contract, permanent CI guardrails, and no remaining certified dormant production cache artifacts.

This certification stops at Cache Recovery. It does not begin Final Storefront Architecture Certification.
