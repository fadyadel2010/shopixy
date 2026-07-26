# POS-ADMIN-CERTIFICATION-01 PASS 9.5 Search, SEO & Public Index Certification

Date: 2026-05-29

## Scope

This pass audited storefront search, storefront autocomplete, CommerceHub search/suggestions/autocomplete, product discovery facets/counts, SEO metadata, sitemaps, breadcrumb JSON-LD, public affiliate discovery, CommerceHub reindex/rebuild, and public search repair boundaries.

## Findings

- Storefront unified search now treats Elasticsearch product/category/CMS hits as candidates only. `StorefrontSearchController` reapplies current SQL product authority through `ProductSearchSqlAuthorityOverlay`, category authority through `CategoryLineageAuthority`, and CMS publication authority before returning results.
- Storefront header autocomplete now recomposes product suggestions from current SQL product/category/media/unit/stock/publication authority before response mapping. Stale Elasticsearch autocomplete hits cannot return deleted or unpublished products.
- Storefront product search and product discovery now report counts from SQL-visible returned items. Discovery facets are intentionally suppressed until they can be recomputed from SQL-visible result sets instead of Elasticsearch aggregations.
- CommerceHub product list/search now reports totals from SQL-visible listing items after vendor suppression and `CommerceHubSearchSqlAuthorityOverlay` recomposition. Public aggregations are suppressed, and category/price facets are rebuilt only from SQL-visible returned listings.
- CommerceHub suggestions, autocomplete, popular terms, zero-result suggestions, and cached suggestion responses are revalidated against current live SQL listing/product/category/vendor authority before return.
- SEO metadata, canonical URLs, slug resolution, sitemaps, and breadcrumb JSON-LD remain SQL-authoritative through the PASS 9.1-9.3 `SeoService` hardening. Redis SEO/sitemap caches remain performance artifacts only.
- No first-class product JSON-LD generator beyond breadcrumbs was found. Breadcrumb structured data derives from SQL-authoritative product/category/CMS resolution.
- No Google/Facebook/marketplace product feed generator was found in the inspected backend. Existing order/finance/audit CSV exports are operational exports, not public product feeds.
- Public affiliate Elasticsearch discovery was already SQL-revalidated in PASS 9.4; this pass also hardens the legacy affiliate portal public discovery/landing/detail endpoints that still read Mongo `AffiliateProductIntelligence`.
- CommerceHub reindex/rebuild remains SQL-first: Mongo candidates are recomposed through `CommerceHubSearchSqlAuthorityOverlay`, Mongo-missing SQL listings are rebuilt from SQL seeds, stale documents are deleted, and alias promotion requires SQL-visible count verification.
- Tenant isolation remains explicit on reviewed storefront search paths through tenant header resolution and SQL tenant checks, on CommerceHub through requested/current tenant scope resolution, and on repair/reindex through tenant-scoped delete/index identity.

## Runtime Hardening

- Added SQL revalidation for storefront unified search products, categories, and CMS hits.
- Added SQL recomposition for storefront header autocomplete product suggestions.
- Changed storefront product search, product discovery, CommerceHub list/search, and affiliate portal public discovery counts to reflect SQL-visible returned items rather than pre-overlay index/Mongo counts.
- Suppressed product discovery facets and raw CommerceHub public aggregations where counts would otherwise come from stale Elasticsearch state.
- Added SQL-visible CommerceHub category/price facet construction from returned listings only.
- Added SQL recomposition for legacy affiliate portal landing pages, category counts, public product discovery, and product detail.

## Regression Freeze

`SearchSeoPublicIndexCertificationTests` freezes the reviewed boundaries around:

- storefront unified/header search SQL recomposition,
- product search/discovery SQL-visible counts,
- CommerceHub search totals/facets/suggestions/reindex authority,
- legacy affiliate portal public discovery recomposition,
- SEO/sitemap/breadcrumb structured-data SQL authority.

## Remaining Debt

- Full all-page SQL-recomputed search totals and facets remain future search-read work. Current guarantees are fail-closed for returned visibility and no index-derived count inflation on the reviewed page.
- Product JSON-LD, organization JSON-LD, feed/export generators, cache warmers, CDN purge jobs, support/global scripts, and analytics/reporting indexes need path-specific certification if introduced or promoted to public visibility infrastructure.
- Facet richness is intentionally reduced on reviewed product and CommerceHub public search until a SQL-visible aggregation model exists.
