# POS-ADMIN-CERTIFICATION-01 PASS 10.4 Elasticsearch Freshness & Search Convergence Certification

Date: 2026-05-29

## Scope

This pass audited Elasticsearch as a derivative search/discovery read model for storefront search, tenant category/CMS search, CommerceHub marketplace search, public affiliate discovery search, legacy service-marketplace search, autocomplete/suggestions, facets/counts, reindex, repair, drift detection, startup initialization, and chaos/recovery behavior.

## Search Inventory Findings

- Product search uses the tenant-scoped `products` index, produced by product search consumers, tenant-admin product search sync, and product reindex repair. Consumers include storefront product search, unified storefront search, header autocomplete, product discovery, homepage best-seller-style discovery, affiliate/public overlays, and repair tooling.
- Category search uses the tenant-scoped `categories` index. It is produced by tenant-admin search sync from SQL category lineage and consumed by storefront unified search after SQL category lineage filtering.
- CMS search uses the tenant-scoped `cmspages` index. It is produced by CMS publish/delete projection and tenant-admin CMS search sync, and consumed by storefront unified search after SQL CMS publication filtering.
- CommerceHub product search uses `commercehub_products`, with versioned concrete indices such as `commercehub_products_v2` behind an alias. It is produced by CommerceHub projection consumers, search analytics recovery, auto-healing, and `CommerceHubReindexService`; public search, autocomplete, suggestions, facets, and listing reads consume it only as candidate/ranking evidence.
- Public affiliate discovery uses `public_affiliate_products`, produced by affiliate public/intelligence projection and AF16E repair/reindex tooling. Public affiliate reads recompose returned candidates through current SQL product, tenant, affiliate-policy, storefront URL, pricing, stock, and category authority.
- Legacy service marketplace search uses `marketplace_services`, produced by `ServiceDiscoveryProjectionConsumer` and repaired by `ElasticsearchDriftReconciliationJob`. It requires current SQL service, tenant, and same-tenant active provider-profile authority before indexing.
- Autocomplete and suggestions are not separate authoritative indexes in the inspected backend. Product autocomplete is analyzer-backed fields in `products`; CommerceHub suggestions/autocomplete are response/query-service surfaces over CommerceHub search/Mongo candidates and cached terms, filtered through current SQL visibility before return.
- No separate analytics Elasticsearch index was found for CommerceHub search analytics. Search query, click, impression, and filter-event analytics are SQL entities, while Elasticsearch stores discovery documents only.

## Search Consumer Findings

- Product create/update/stock/price/promotion/lifecycle consumers collapse duplicate active replay with `ProjectionEventFence` or `ProjectionConsumerEventFence`, recompose through `ProductSearchSqlAuthorityOverlay`, and delete search documents when SQL authority rejects visibility.
- Category and CMS repair/sync paths are SQL-first and stale-delete capable through `CategoryLineageAuthority`, current CMS SQL publication filters, `DeleteStaleCategoriesAsync`, and `DeleteStaleCmsPagesAsync`.
- CommerceHub indexing paths converge through `CommerceHubSearchSqlAuthorityOverlay`, tenant-scoped document ids, alias-aware index service APIs, auto-healing, and SQL-visible count verification before alias promotion.
- Legacy service-marketplace drift repair reloads SQL service/tenant/provider-profile authority before bulk indexing missing documents and deletes extras absent from SQL authority.
- Remaining risk is operational breadth, not a found active consumer gap: future feed/export/support/global-script search writers still require path-specific certification before they mutate public search visibility.

## Search Freshness Findings

- Product/category/CMS documents expose `IndexedAtUtc`; product documents also carry `LastEventId`.
- CommerceHub search documents expose `UpdatedAtUtc`, and CommerceHub Mongo projection state tracks `SearchSyncStatus`, `SearchLastAttemptedAtUtc`, `SearchLastSucceededAtUtc`, `SearchLastError`, and `LastSyncedAt`.
- Search operation telemetry is exported through `PointOfSale.Elasticsearch.Search`; convergence telemetry records drift decisions for reviewed search health and service-marketplace drift reconciliation.
- Runtime hardening in this pass changed `CommerceHubSearchSyncHealthCheck` to compare Elasticsearch count against current SQL-visible CommerceHub listings instead of Mongo published documents, and to emit bounded search drift diagnostics.

## Product Search Findings

- Product search documents are derivative. Public reads and hydrators recompose Elasticsearch candidates through `ProductSearchSqlAuthorityOverlay` before response shaping.
- Stale price, stock, media, category, publication, affiliate policy, and simple-product unit state fail closed through SQL overlay deletion/suppression rather than search-document authority.
- Product search counts on reviewed public pages are SQL-visible returned counts, not raw Elasticsearch totals. Rich all-page SQL facet recomputation remains future search-read work.

## Category/CMS Search Findings

- Category search visibility derives from current SQL category lineage; invalid parent chains, missing parents, self-reference, cycles, deleted rows, and wrong tenant lineage are deleted or filtered.
- CMS search visibility derives from current SQL CMS publication authority; stale CMS search hits are filtered at read time and stale documents are deleted by tenant-admin repair.
- Category and CMS search documents remain useful discovery candidates only and cannot preserve public routes, SEO, or navigation visibility.

## Suggestion/Autocomplete Findings

- Storefront header autocomplete recomposes product suggestions through current SQL product/media/pricing/stock/publication authority.
- CommerceHub suggestions, autocomplete products, category suggestions, popular terms, zero-result suggestions, and cached suggestion responses are filtered through current SQL listing/product/category/vendor authority before return.
- Deleted products, hidden vendors, unpublished listings, invalid categories, and terms that no longer resolve to live SQL-visible results are suppressed rather than preserved by cached or indexed suggestions.

## Facet/Count Findings

- Reviewed product discovery and CommerceHub search suppress raw Elasticsearch aggregations where SQL-visible aggregation is not available.
- CommerceHub category and price facets are rebuilt from SQL-visible returned listings only.
- Counts are bounded to SQL-visible returned items on reviewed public pages. Exact all-page facet/count convergence is intentionally remaining debt.

## Reindex Findings

- Tenant-admin product reindex and admin search sync enumerate SQL product ids, apply `ProductSearchSqlAuthorityOverlay`, index only accepted SQL projections, and delete stale product documents when SQL rejects visibility.
- CommerceHub full reindex builds or repairs a versioned target index, reconciles Mongo candidates through `CommerceHubSearchSqlAuthorityOverlay`, fills Mongo-missing SQL listing seeds from SQL authority, verifies target count against SQL-visible listing count, and promotes the alias only after verification.
- Tenant-admin global index delete/recreate remains blocked; tenant-scoped repair is available without turning admin search tools into global search authority.

## Repair Findings

- Product/category/CMS repair paths are stale-delete capable, not additive-only.
- CommerceHub auto-healing and reindex use tenant-scoped listing identity and SQL overlay before index mutation.
- AF16E affiliate repair deletes stale public affiliate visibility when current tenant storefront/currency/product/policy authority is missing.
- Service-marketplace drift repair deletes orphan search documents and only restores services that current SQL authority accepts.

## Drift Detection Findings

- `ElasticsearchDriftReconciliationJob` scans service-marketplace SQL truth and Elasticsearch state, emits convergence telemetry for drift within tolerance, drift detected, and health-check failure, then repairs missing/extra documents from SQL.
- `CommerceHubSearchSyncHealthCheck` now uses SQL-visible listing counts plus Elasticsearch counts, Mongo sync flags, and auto-healing metrics to expose search divergence.
- Search telemetry tracks latency, errors, zero results, and index operations with bounded dimensions.
- There is no universal every-index SQL-vs-Elasticsearch parity scanner. Current drift detection is path-specific on reviewed service-marketplace and CommerceHub search boundaries.

## Cross-System Convergence Findings

- Normal convergence remains `PostgreSQL -> MongoDB -> Redis -> Elasticsearch`, but reviewed Elasticsearch repair and reindex paths may recompose directly from SQL when Mongo is missing or stale.
- MongoDB, Redis, cached suggestions, Elasticsearch hits, and replay payloads are candidate evidence only. SQL remains the eligibility authority for public search visibility.
- If SQL and Elasticsearch disagree, SQL-filtered reads, stale deletes, reindex, repair, and auto-healing converge Elasticsearch toward SQL.

## Startup/Recovery Findings

- Startup index initializers ensure storefront/category/CMS/service/affiliate and CommerceHub search indices exist without blocking the API when Elasticsearch is unavailable.
- CommerceHub startup/background auto-healing discovers missing search documents, uses tenant-scoped identity where available, and mutates Elasticsearch only after `CommerceHubSearchSqlAuthorityOverlay`.
- Elasticsearch loss is recoverable by SQL-first reindex/repair. Elasticsearch state is never a restore authority.

## Chaos Findings

- Consumer down: outbox/backlog and health telemetry expose lag; SQL-filtered public reads prevent Elasticsearch from becoming authority while catch-up occurs.
- Index lag: CommerceHub health and auto-healing expose and recover lag; product/category/CMS repair can be run tenant-scoped from SQL.
- Duplicate event: reviewed product and promotion search consumers collapse duplicate active replay before Elasticsearch side effects.
- Out-of-order event: product search indexing uses SQL overlay plus stale document timestamp protection, so older events cannot promote stale visibility when SQL has moved on.
- Full reindex and alias swap: CommerceHub reindex verifies SQL-visible document count before alias promotion.
- Partial reindex and repair: tenant-scoped product/category/CMS/affiliate/service repair recomposes from SQL and deletes stale documents.

## Runtime Hardening Performed

- `CommerceHubSearchSyncHealthCheck` now computes expected search freshness from SQL-visible CommerceHub listing authority, including active tenant, published listing, current product, public variant lifecycle, and live CommerceHub category lineage.
- The same health check now emits `OperationalConvergenceTelemetry.RecordDriftDiagnostic(...)` for CommerceHub search drift within tolerance and drift detected.

## Monitoring Improvements

- CommerceHub search health data now includes `sqlVisibleCount`, `mongoPublishedCount`, `elasticsearchCount`, `syncedFlagCount`, auto-healing metrics, and SQL-vs-search ratio.
- Reviewed search drift emits low-cardinality convergence diagnostics under `OperationalConvergenceAuthority.SearchIndex`.
- Existing Elasticsearch search/index telemetry remains exported through OpenTelemetry.

## Governance Updates

- PASS 10.4 is now documented as the Elasticsearch convergence boundary: indexes are derivative, SQL-derived reindex/repair is required, stale-delete behavior is mandatory, and counts/facets/suggestions may not become authority.
- Source-freeze coverage was added in `ElasticsearchFreshnessSearchConvergenceCertificationTests`.
- `.ai-context` authority rules now explicitly carry the PASS 10.4 freeze for future Elasticsearch search, autocomplete, suggestion, facet/count, reindex, repair, drift, startup, and chaos changes.

## Honest Remaining Debt

- Elasticsearch remains eventually consistent. This pass does not add strong consistency, synchronous indexing, or exact read-your-write search.
- There is no universal SQL-vs-Elasticsearch parity scanner for every index, tenant, and document type.
- Full SQL-recomputed all-page facets/counts remain future work; reviewed public pages avoid raw index-derived authority rather than providing perfect global facet freshness.
- Future feed/export/CDN purge/support/global-script/reporting/search-analytics Elasticsearch paths need path-specific SQL-authority certification before they mutate or expose public visibility.
