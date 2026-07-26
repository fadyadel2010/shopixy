# PASS M7 CommerceHub Search & Discovery Authority Certification Findings

Scope: CommerceHub search, discovery, recommendations, autocomplete, featured content, trending/popular content, ranking, indexing, projections, Elasticsearch documents, Mongo projections, feeds, and discovery visibility.

Status: CommerceHub Search & Discovery Authority is production-certifiable for inspected runtime paths after analytics/ranking ingress hardening.

## Search Findings

Search identity is SQL listing identity. Elasticsearch and MongoDB provide candidate documents only. `CommerceHubQueryService` recomposes search/list results through `CommerceHubSearchSqlAuthorityOverlay`, returns totals from SQL-visible items, and rebuilds public facets from visible items instead of raw Elasticsearch totals or aggregations.

## Visibility Findings

Hidden, deleted, unpublished, invalid-category, inactive-variant, inactive-tenant, non-EGP, unpriced, inventory-lineage-invalid, governance-hidden, or suspended-vendor listings fail closed through the SQL overlay, public vendor suppression filters, category lineage checks, and stale search deletion. Search cannot bypass M6 catalog authority.

## Recommendation Findings

No independent personalized CommerceHub recommendation engine was found. Recommendation-like surfaces are homepage shelves, related/category fallback, suggestions, autocomplete, popular terms, and zero-result suggestions. These surfaces route through the same SQL-visible product/listing/category authority before response.

## Trending Findings

Trending/popular evidence is derivative analytics. Popular query responses are revalidated through live SQL-visible CommerceHub terms. Runtime hardening now requires click and impression telemetry to resolve a current SQL-visible CommerceHub listing before analytics/ranking evidence is accepted.

## Autocomplete Findings

Autocomplete products, categories, suggestions, cached autocomplete responses, popular terms, and zero-result suggestions are filtered through current SQL listing/product/variant/category/vendor visibility. Cached responses are revalidated before return.

## Indexing Findings

Elasticsearch indexing is derivative. Projection consumers, search analytics popularity updates, auto-healing, catch-up reindex, tenant reindex, and full reindex call `CommerceHubSearchSqlAuthorityOverlay` before indexing and delete documents when SQL authority rejects visibility. Reindex deletes stale documents absent from current SQL-visible listing identities.

## Ranking Findings

Ranking uses Elasticsearch score, stock, recency, price intent, and popularity as ordering signals only. Those signals cannot create visibility because response items are recomposed through SQL authority and analytics-triggered reindex deletes hidden candidates.

## Governance Findings

Vendor governance propagates into search through `CommerceHubVendorAuthority`, public suppression filters, `CommerceHubSearchSqlAuthorityOverlay`, health checks, reindex, projection replay, and read-time filtering. Suspended, banned, unverified, unapproved, inactive, or COD-suspended vendors cannot remain visible on inspected paths.

## Isolation Findings

Tenant isolation is enforced by trusted tenant resolution, tenant predicates on listing/search/projection identity, tenant-scoped Elasticsearch document identities, `ResolveTenantScope`, and SQL overlay tenant matching. Foreign route/query tenant IDs cannot grant discovery authority.

## Replay Findings

Projection replay is fenced with `ProjectionConsumerEventFence`; stale event versions fail closed; reindex and catch-up recompute from SQL; alias promotion requires SQL-visible count verification; stale documents are deleted. Replay cannot restore listings after SQL visibility is gone.

## Historical Findings

Historical analytics, search logs, clicks, impressions, Mongo projections, Elasticsearch documents, cached suggestions, and old listing events remain evidence only. Current SQL authority decides response, indexing, and deletion at certification boundaries.

## Adversarial Findings

Search poisoning, index replay, projection replay, recommendation poisoning, trending manipulation, vendor visibility bypass, catalog resurrection, autocomplete leakage, cross-tenant discovery, cross-vendor discovery, DTO tampering, and route tampering are blocked or fail closed on inspected paths by SQL recomposition, governance filtering, tenant scoping, analytics ingress validation, projection fences, and stale document deletion.

## Runtime Hardening Performed

- `CommerceHubController.TrackClick` now resolves current SQL-visible listing authority before enqueueing click analytics and stamps analytics with the authoritative product, variant, and tenant.
- `CommerceHubController.TrackImpression` now filters impression product IDs through current SQL-visible listing authority before enqueueing analytics.
- Existing analytics background processing still reapplies `CommerceHubSearchSqlAuthorityOverlay` before popularity-triggered search indexing and deletes hidden documents.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/CommerceHubController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubSearchDiscoveryAuthorityCertificationTests.cs`

## Architecture Updates

- `.ai-context/PROJECT_CONTEXT.md`
- `.ai-context/ARCHITECTURE_TRUTH.md`
- `.ai-context/KNOWN_RISKS.md`
- `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`
- `.ai-context/CODEX_RULES.md`
- `docs/architecture/commercehub-search-discovery-authority-certification-findings.md`

## Explicit Answers

- Who owns search authority? Current PostgreSQL CommerceHub catalog authority owns visibility; search owns derivative indexing, ranking, and discovery candidate ordering only.
- Can search expose hidden listings? No on inspected runtime paths.
- Can recommendations bypass catalog authority? No inspected recommendation-like path bypasses catalog authority.
- Can trending override visibility? No. Analytics can influence ordering only after SQL-visible authority is proven.
- Can autocomplete leak hidden content? No on inspected autocomplete/suggestion/popular-term paths.
- Can indexing resurrect listings? No; indexing and reindex delete/no-op when SQL rejects visibility.
- Can ranking become authority? No. Ranking is derivative ordering over SQL-visible candidates.
- Can vendor governance drift into search? No inspected path permits governance-hidden vendors to remain visible.
- Can tenant leakage occur? No inspected search/discovery path permits cross-tenant authority.
- Were runtime code changes required? Yes, analytics/ranking ingress hardening was required.
- Is CommerceHub Search & Discovery Authority production-certifiable? Yes, for inspected runtime paths.

## Remaining Debt

Production still needs discovery-specific dashboards, alert packs, SQL/Mongo/Elasticsearch parity monitoring, SEO sitemap/structured-data certification, and PostgreSQL-backed chaos runs for high-volume search, autocomplete, analytics, projection replay, reindex, and governance-change races.
