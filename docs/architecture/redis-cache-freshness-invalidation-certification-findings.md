# POS-ADMIN-CERTIFICATION-01 PASS 10.2 Redis Cache Freshness & Invalidation Certification

Date: 2026-05-29

## Scope

This pass certifies Redis cache correctness as a derivative convergence layer. It does not introduce a cache migration, redesign, performance tuning program, or new TTL strategy.

PostgreSQL remains the authority. Redis may accelerate reads, distributed coordination, rate limits, idempotency hints, sessions, locks, and SignalR/backplane behavior, but it must not decide public visibility, ownership, publication state, stock, price, category lineage, CMS publication, homepage eligibility, search visibility, or recovery truth.

## Cache Inventory Findings

Reviewed Redis usage includes:

- Product caches: `ProductCacheService`, `StorefrontProductService`, product id/slug/list keys under `CacheKeyBuilder.Product*`, TTL from `RedisSettings.ProductCacheTtlMinutes`.
- Category caches: category tree and slug keys under `CacheKeyBuilder.Categories*`; reviewed public category reads recompose from SQL lineage before return.
- CMS caches: storefront CMS scope key `cms:{tenant}:scope:v1`, marketplace CMS scope key `marketplace:cms:scope:v1`, and SEO/sitemap CMS cache keys.
- Homepage caches: storefront homepage shelf keys under `tenant:{tenant}:home:*`, global/tenant homepage section published scope keys, and marketplace homepage language keys.
- Search/SEO caches: SEO metadata, breadcrumbs, slug resolution, sitemap, robots, CommerceHub suggestions/search-side caches, and Redis-backed search diagnostics where present.
- Navigation/footer caches: storefront footer tenant keys, marketplace footer key, navigation cache invalidation paths, and SQL-filtered navigation/category reads from earlier PASS 9 work.
- Affiliate/CommerceHub caches: marketplace products/orders/reviews/rating-summary, marketplace translations, marketplace homepage, governance visibility tokens, commission rule cache invalidation, and affiliate/public discovery surfaces that are SQL-filtered before return.
- Operational/ephemeral Redis: distributed locks, cache refresh locks, checkout/auth/rate-limit/session/OTP/circuit-breaker/idempotency helpers, analytics hot store counters, SignalR/backplane, and WhatsApp transport/session state.

No new cache authority was introduced. Several older direct Redis helpers remain specialized infrastructure paths and are not public visibility authority.

## Cache Invalidation Findings

Product invalidation is event-driven through `ProductCacheInvalidationConsumer` for create, update, delete, stock, price, and publication events. It uses `ProjectionConsumerEventFence.TryAcquireAsync(...)` to collapse duplicate or active replay before Redis fanout, resolves the current SQL product slug where possible, deletes product id/slug/list keys, promotion-by-product keys, and homepage pattern keys.

Homepage, CMS, footer, marketplace product/order/review, and marketplace CMS/footer invalidation is centralized in `RedisInvalidationConsumer`. It uses `ProjectionEventFence.TryAcquireAsync(...)` for integration events and marketplace domain events before Redis deletion or pattern invalidation.

Mutation-side cache invalidation remains best-effort. Correctness does not rely on Redis deletion succeeding because public reads either recompose from SQL before cache population or filter cached candidates against current SQL authority.

## Product Freshness Findings

Storefront product detail, product id/slug reads, product list reads, search hydration, discovery cards, SEO, breadcrumbs, and sitemap product URLs remain SQL-authoritative through `ProductSearchSqlAuthorityOverlay` and related PASS 9 hardening.

Redis product DTOs can temporarily contain stale price, stock, media, variants, visibility, category, promotion, or publication fields. They cannot survive indefinitely because:

- authoritative product mutations publish invalidation events;
- duplicate invalidation replay is fenced;
- product/list/homepage keys have bounded TTLs;
- public product reads populate Redis only after SQL-accepted recomposition;
- checkout and reservation authority never comes from Redis.

## Homepage & Discovery Cache Findings

Homepage section caches are presentation acceleration only. `HomepageSectionReadService` sanitizes cached or SQL-loaded homepage discovery references through current SQL CommerceHub listing/category/vendor authority before return.

Storefront homepage shelves and CommerceHub homepage reads remain SQL-filtered before return. Redis homepage keys may influence latency and candidate reuse, but stale product/category/listing references are omitted or scrubbed.

## Search Cache Findings

Search caches, suggestions, autocomplete, SEO, sitemap, and slug caches are derivative. PASS 9.5 established SQL recomposition for storefront search/header autocomplete, CommerceHub suggestions/autocomplete, legacy affiliate discovery, SEO metadata, sitemaps, and returned count/facet behavior.

This pass confirms Redis SEO/search caches are not final authority. Cached slug, sitemap, or suggestion evidence must be checked against current SQL product/category/CMS/listing/vendor authority before public visibility is returned.

## Cache Warmer Findings

`CacheWarmupService` runs once after startup, is best-effort, and intentionally does not warm tenant-specific caches. It warms Redis connectivity plus marketplace footer and marketplace CMS global caches only.

Runtime hardening in this pass ensures warmed marketplace CMS/detail and footer cache values are not public visibility authority:

- marketplace CMS detail reads now load current SQL by slug;
- marketplace CMS cached scope reads are filtered against current SQL published/non-draft rows with `PublishedAt != null`;
- marketplace footer cached CMS links are filtered against current SQL marketplace CMS publication;
- storefront footer cached CMS links are filtered against current tenant SQL CMS publication.

Warmers therefore cannot resurrect stale public CMS/footer state.

## Cache Recovery Findings

Redis restart or loss is safe for reviewed public visibility. Cache misses fall back to SQL, Mongo, or Elasticsearch candidate paths, and those paths are SQL-filtered before public return on the certified boundaries.

Cache rebuild remains invalidation-first or SQL-read-populated. No reviewed recovery path restores public product/category/CMS/listing visibility from Redis values, cache snapshots, or warmer payloads.

## Observability Findings

`RedisJsonCacheService` wraps cached payloads in `CacheEnvelope<T>` with `CachedAtUtc`, optional projection/source timestamps, and schema version. It records hits, misses, corruptions, refresh latency, read/write latency, projection lag, and pattern invalidations through `RedisPerformanceTelemetry`.

`PointOfSale.Redis.Performance` remains exported through the OpenTelemetry registration, with bounded `cache_type` and `pattern_family` dimensions. Raw Redis keys, tenant ids, product ids, order ids, invalidation patterns, and payload fragments are not metric labels.

Cache drift remains observable through:

- bounded Redis performance metrics;
- projection/outbox lag health and `OperationalConvergenceTelemetry` from PASS 10.1;
- invalidation logs with event ids, tenants, products, and key counts;
- corruption detection and deletion on malformed cache envelopes.

## Cross-System Convergence Findings

The certified convergence model is:

SQL authority -> events/outboxes -> consumers/projections/search -> Redis invalidation or SQL-filtered cache population.

Redis can lag MongoDB and Elasticsearch and can miss invalidation attempts, but it converges because public read paths are SQL-filtered, invalidation is replay-safe, and TTLs bound stale cache survival. If Redis, MongoDB, Elasticsearch, and SQL disagree, SQL wins.

## Startup & Failover Findings

Startup warmup does not block serving and does not create tenant-scoped public visibility. Redis cold start behaves as cache miss. Redis restart removes cache state rather than restoring business truth.

Failover may cause temporary misses or stale entries until invalidation/TTL/read-time SQL filtering converges. Reviewed public visibility does not promote Redis values to authority during failover.

## Chaos Findings

Reviewed chaos scenarios:

- Redis down: reads miss or skip cache; public visibility remains SQL-filtered.
- Redis restart: cache is repopulated from reviewed read paths; no cache snapshot is authority.
- Consumer delay/projection delay: PASS 10.1 lag telemetry plus SQL-filtered reads preserve observable eventual convergence.
- Duplicate invalidation: event fences collapse active/duplicate replay before Redis fanout.
- Lost invalidation attempt: TTL plus SQL-filtered cache population prevents indefinite stale authority.
- Cache rebuild/warmer execution: warmers use read services whose public outputs are SQL-filtered.

## Runtime Hardening Performed

- `MarketplaceCmsPageReadService.GetPublishedPageAsync(...)` now loads current SQL published/non-draft CMS authority by slug instead of using cached scope existence.
- Marketplace CMS cached scope reads now filter cached pages against current SQL publication before return.
- Marketplace CMS cache population now stores only current SQL-published, non-draft rows with `PublishedAt != null`.
- `MarketplaceFooterReadService` now filters cached CMS footer links against current SQL marketplace CMS publication before return.
- `StorefrontFooterReadService` now builds derived page references from current SQL CMS publication and filters cached CMS footer links against tenant-scoped SQL CMS publication before return.
- Added `RedisCacheFreshnessInvalidationCertificationTests`.

## Monitoring Improvements

No new metrics were added. The pass validates that existing Redis performance telemetry, projection lag telemetry, invalidation logs, and outbox readiness telemetry are sufficient for the reviewed cache freshness boundary.

## Governance Updates

Redis cache freshness rules are now recorded in `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, and `.ai-context/CODEX_RULES.md`.

Future cache, warmer, recovery, search, SEO, feed/export, support, reporting, or global-script paths that touch public visibility must prove current SQL authority before return or cache population, fence duplicate invalidation when available, expose bounded freshness diagnostics, and add replay/rebuild tests plus documentation.

## Honest Remaining Debt

This pass does not certify every analytics/reporting/support/global-script/feed/export/CDN purge/WhatsApp/session/rate-limit/idempotency Redis path as public visibility infrastructure. Many are intentionally operational or ephemeral and require path-specific review if they start shaping public product/category/CMS/listing visibility.

Redis invalidation remains best-effort, not exactly-once. Convergence is observable and bounded by SQL-filtered reads plus TTLs, not by strong cache consistency.

Some cache services still use local SQL-filtering equivalents rather than a single universal cache freshness helper. That is acceptable for this pass but remains future maintainability debt.
