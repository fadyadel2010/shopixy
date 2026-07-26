# POS-ADMIN-CERTIFICATION-01 PASS 10.5 Cross-System Convergence Certification

Scope: whole-system convergence across PostgreSQL, MongoDB, Redis, Elasticsearch, outbox consumers, rebuild/repair tooling, startup recovery, freshness telemetry, drift detection, and chaos behavior.

This pass certifies observable eventual convergence, not strong consistency. PostgreSQL remains authority. MongoDB is a read model. Redis is an acceleration layer. Elasticsearch is a discovery layer. RabbitMQ/MassTransit and specialized SQL outboxes are propagation infrastructure only.

## Convergence Inventory Findings

The reviewed convergence chain is `PostgreSQL -> Outbox -> Consumers -> MongoDB -> Redis -> Elasticsearch`, with direct SQL-first repair/reindex paths allowed only as convergence tools.

| Surface | SQL source | Mongo projection | Redis cache | Elasticsearch document |
|---|---|---|---|---|
| Product | `Products`, `ProductVariants`, `ProductUnits`, `ProductMedia`, stock, promotions, category lineage, tenant lineage | `ProductDocument` through `IProductSqlAuthorityProjectionWriter` | product detail/list/home/promotion keys through SQL-filtered reads and fenced invalidation | `ProductSearchDocument` in `products` |
| Category | `Categories` plus live parent lineage | `CategoryDocument` through admin sync/repair | category/navigation/SEO/sitemap caches as candidate data | `CategorySearchDocument` in `categories` |
| CMS | `CmsPages` publication state plus tenant lineage | `StorefrontPageDocument` through fenced CMS projection | CMS/footer/SEO/sitemap caches filtered through SQL publication | `CmsPageSearchDocument` in `cmspages` |
| Homepage | SQL product/listing/category/media/stock/price/publication eligibility | `MarketplaceHomepageReadModelDocument`, storefront homepage read models, and product documents | homepage caches and section cache entries | product/CommerceHub/affiliate search candidates only |
| Discovery | SQL-visible product/listing/category/media/pricing/stock authority | product, CommerceHub, affiliate intelligence/read models | discovery/search response caches where present | `products`, `commercehub_products`, `public_affiliate_products`, `marketplace_services` |
| Search | SQL product/category/CMS/listing/service authority | Mongo used as candidate input for some rebuilds | search/SEO/autocomplete caches are derivative | product/category/CMS/CommerceHub/affiliate/service indexes |
| CommerceHub | `CommerceHubListings`, products, variants, vendor/tenant/category/review authority | `CommerceHubProductDocument`, marketplace product/order/homepage read models | marketplace product/order/review/CMS/footer caches | `CommerceHubProductSearchDocument` via versioned alias |
| Affiliate | SQL product visibility, affiliate policy, tenant storefront URL/currency/pricing | `AffiliateProductIntelligence` and affiliate public/analytics documents | affiliate/public cache surfaces where present | `PublicAffiliateProductSearchDocument` |

No authoritative Mongo, Redis, or Elasticsearch write path was found on the reviewed public-visibility boundaries. Orphan projections remain possible as eventual lag, but reviewed repair/rebuild paths either delete, tombstone, suppress, or filter them through SQL.

## Product Convergence Findings

Price, stock, media, variant, visibility, promotion, and category changes converge through SQL-authoritative product overlays and shared writers. Product events are triggers only; current SQL product/category/media/variant/unit/stock/promotion/publication and tenant authority decides Mongo upsert/tombstone, Redis invalidation or repopulation, and Elasticsearch indexing/deletion.

Permanent product divergence is bounded by duplicate replay fences, version/freshness checks, SQL overlay recomposition, Redis TTL/invalidation, search reindex/repair, and public read-time SQL filtering. The remaining eventual behavior is normal read-model lag until consumers, repair, or reindex catch up.

## Category/CMS Convergence Findings

Category rename, delete, and lineage changes converge through `CategoryLineageAuthority`, tenant-admin Mongo sync, category search repair, SQL-filtered public category/navigation/SEO reads, and stale-delete repair. CMS publish, unpublish, and delete converge through fenced projection, SQL publication filters, CMS search repair, and cache/public read filtering.

Stale categories, CMS pages, routes, and navigation entries are observable as projection/search/cache lag and are recoverable by SQL-first repair. No reviewed path may preserve public category or CMS visibility from Redis, MongoDB, Elasticsearch, event payloads, or stale navigation JSON after SQL rejects it.

## Homepage/Discovery Convergence Findings

Homepage shelves, curated references, storefront discovery, CommerceHub homepage, affiliate discovery, recommendations-like surfaces, and marketplace/service discovery all use derivative stores as candidate or ranking input only. Current SQL product/listing/category/media/stock/price/promotion/publication, tenant, affiliate policy, and vendor/service authority decides return eligibility.

Stale shelves and recommendations converge by candidate scrubbing, SQL-filtered read shaping, projection rebuild, search repair, and cache invalidation. Exact ranking freshness remains eventual.

## Drift Findings

Reviewed drift surfaces include SQL vs Mongo projection health, SQL vs Redis freshness through TTL/invalidation and SQL-filtered reads, SQL vs Elasticsearch search health/reconciliation, Mongo vs Redis through cache envelopes and projection-lag bypass, and Mongo vs Elasticsearch through CommerceHub search sync metadata and auto-healing.

Drift is detectable through health checks, document freshness fields, event versions, processed-event receipts, search sync status, readiness gates, and `OperationalConvergenceTelemetry`. The platform does not claim a universal every-entity parity scanner.

## Repair/Recovery Findings

Repair and rebuild tools are convergence tools, not authority. Product rebuild routes through `IProductSqlAuthorityProjectionWriter`; category/CMS repair deletes stale search/projection documents; CommerceHub reindex uses `CommerceHubSearchSqlAuthorityOverlay` and SQL-visible alias promotion; affiliate repair deletes stale public visibility when SQL tenant/product/policy/currency authority is missing; service-marketplace drift repair reloads SQL service/tenant/profile authority.

Replay converges all reviewed systems because consumers collapse duplicates, reject stale versions, reload SQL authority, and delete/tombstone when SQL authority is gone. Partial recovery remains debt for future feed/export/CDN purge/support/reporting/global-script paths until path-specific certification is added.

## Startup Findings

Startup recovery is convergence-driven. Projection/outbox readiness gates expose backlog and lag; CommerceHub startup/background auto-healing uses tenant-scoped `{ TenantId, ListingId }` recovery identity; Redis loss is treated as cache miss; Elasticsearch loss is repaired by SQL-first reindex; Mongo projection lag is repaired by consumers/rebuilds and read-time SQL filtering.

Startup does not require derivative stores to be perfect before SQL remains authority, but readiness can fail closed when backlog or recovery lag exceeds reviewed thresholds.

## Freshness Findings

Freshness is measurable through outbox timestamps, event versions, mutation versions, `LastEventId`, `LastEventOccurredAtUtc`, `LastEventVersion`, `IndexedAtUtc`, `UpdatedAtUtc`, `SearchSyncStatus`, `ProjectionUpdatedAtUtc`, Redis cache envelopes, health-check data, and bounded telemetry.

Freshness blind spots remain where future or non-reviewed analytics/feed/support/export projections are introduced without their own markers and health diagnostics.

## Drift Detection Findings

The reviewed detection stack includes `ProjectionDriftHealthCheck`, `ProjectionOutboxHealthCheck`, `MassTransitBusOutboxReadinessHealthCheck`, `CommerceHubSearchSyncHealthCheck`, `ElasticsearchDriftReconciliationJob`, Redis performance/projection-lag telemetry, search telemetry, and `OperationalConvergenceTelemetry.RecordDriftDiagnostic`.

Invisible drift is constrained on reviewed public-visibility paths because stale derivative data is filtered before public return and repair/rebuild paths are stale-delete capable. The gap is breadth, not authority: there is no global SQL-vs-Mongo-vs-Redis-vs-Elasticsearch reconciliation daemon for every entity type.

## Convergence SLO Findings

Operational thresholds are defined on reviewed readiness paths: projection and MassTransit bus outbox warning lag at 5 minutes and critical lag at 15 minutes, CommerceHub search sync healthy at 95 percent and degraded above 80 percent, Redis latency thresholds, and repeated-failure readiness gates.

These are operational convergence thresholds, not user-facing strong-consistency guarantees. Future SLO work should make tenant- and surface-specific lag objectives explicit without promoting derivative stores to authority.

## Chaos Findings

Mongo lag, Redis lag, Elasticsearch lag, consumer restart, replay, repair, reindex, and startup recovery converge through independent but aligned controls: outbox leases, processed-event fences, SQL overlays, tombstones/deletes, cache TTL and invalidation, SQL-visible search reindex, tenant-scoped recovery identity, and fail-closed public reads.

Unrecoverable drift was not found on reviewed public-visibility paths. The remaining chaos debt is absence of a universal chaos harness and absence of path-specific certification for future non-reviewed projection/cache/search surfaces.

## Runtime Hardening Performed

This pass added regression-freeze coverage for whole-system convergence rather than changing the authority model. It binds PASS 10.1 through PASS 10.4 into one audited contract and prevents future edits from documenting a subsystem in isolation while omitting cross-system inventory, drift, repair, startup, freshness, SLO, and chaos expectations.

## Monitoring Improvements

The pass codifies the required monitoring surfaces: projection/outbox readiness, search drift health, service-marketplace drift reconciliation, Redis performance and projection-lag telemetry, search telemetry, auto-healing metrics, and bounded `OperationalConvergenceTelemetry`.

No new metric family was required because PASS 10.1 and PASS 10.4 already added convergence telemetry for outbox lag and search drift.

## Governance Updates

`.ai-context` now records PASS 10.5 as a whole-system convergence boundary. Future Mongo, Redis, Elasticsearch, outbox, repair, replay, startup, cache-warmer, recommendation, feed/export, support, reporting, analytics, or global-script changes that touch public visibility must update cross-system convergence tests and findings in the same change.

## Honest Remaining Debt

The platform remains eventually consistent. It does not provide read-your-write guarantees across MongoDB, Redis, or Elasticsearch; it does not run a universal parity scanner for all entity types; it does not provide exact all-page SQL-derived search facets/counts; it does not include a centralized chaos framework; and it does not certify future feed/export/CDN purge/support/reporting/global-script surfaces until they receive path-specific SQL-first review.

The certified guarantee is narrower and explicit: on reviewed public-visibility paths, PostgreSQL authority wins, derivative drift is observable, repair/replay/startup recovery converge derivative systems toward SQL, and stale derivative data cannot remain public authority after SQL rejects it.
