# Homepage, Discovery & Recommendation Certification Findings

Date: 2026-05-29

Scope: POS-ADMIN-CERTIFICATION-01 PASS 9.4, Homepage, Discovery & Recommendation Certification.

## Authority Model

- PostgreSQL product, category, media, variant, stock, price, promotion, affiliate-policy, tenant, vendor-governance, and CommerceHub listing state owns homepage, discovery, featured, trending, popular, new-arrival, curated, recommendation, and affiliate discovery eligibility.
- Redis homepage caches, MongoDB product/homepage documents, Elasticsearch search/ranking hits, analytics popularity scores, curated section JSON, and replayed rebuild payloads are candidate and ordering evidence only.
- If SQL and any homepage, discovery, recommendation, cache, projection, or analytics surface disagree, SQL wins and public visibility fails closed.

## Findings

- Storefront homepage shelves were already hardened in the current worktree: featured products, new arrivals, best sellers, curated product IDs, and homepage categories now recompose cached Mongo/Elasticsearch candidates through current SQL tenant, product, category, affiliate-policy, promotion, media, and stock authority before response.
- CommerceHub homepage projection and read paths were already guarded by replay fencing, active tenant checks, SQL listing/product/category validation, vendor suppression, and read-time public filtering.
- Homepage cache invalidation was already derivative and replay-collapsed on reviewed product/homepage Redis invalidation boundaries.
- Confirmed affiliate discovery residual gap: `AffiliateProductDiscoveryService` still described and implemented Elasticsearch as final visibility authority. A stale affiliate search document could survive SQL unpublish/delete, affiliate-policy disablement, tenant deactivation, price/stock invalidation, or storefront URL authority loss until reindex.
- Confirmed public affiliate products residual gap: `PublicAffiliateProductsController` returned public affiliate ES documents directly for list and slug reads. Projection writes were SQL-authoritative, but read-time stale-index resurrection was still possible.

## Runtime Hardening

- `AffiliateProductDiscoveryService` now treats Elasticsearch as candidate/ranking input only. Returned items are recomposed through `ProductSearchSqlAuthorityOverlay`, current `AffiliateProductPolicies`, current active tenant authority, current storefront URL authority, and storefront affiliate pricing/stock rules.
- Public affiliate product list and slug reads now apply the same current SQL recomposition before returning response items.
- Affiliate discovery totals now report the SQL-visible returned item count for the inspected page instead of preserving stale ES total-count visibility.
- `IAffiliateProductDiscoveryService` is now scoped because it consumes scoped SQL authority.

## Regression Freeze

- `HomepageDiscoveryRecommendationCertificationTests` freezes storefront homepage SQL recomposition, CommerceHub homepage projection/read filtering, affiliate discovery SQL recomposition, public affiliate product read-time SQL recomposition, and homepage cache replay-collapse markers.
- Existing PASS 9.1, PASS 9.2, PASS 9.3, PASS 5, and PASS 7 tests continue to cover CMS/category/product/search/CommerceHub projection and replay authority boundaries used by homepage and discovery surfaces.

## Certification Results

- Homepage visibility: reviewed storefront homepage shelves and CommerceHub homepage reads require current SQL visibility before returning product/category/listing references.
- Discovery visibility: reviewed storefront discovery/search hydration and CommerceHub discovery already recompose from SQL; affiliate discovery now also recomposes stale ES candidates from SQL before response.
- Recommendation authority: no independent recommendation engine was found on the inspected runtime boundary. Existing recommendation-like surfaces are homepage shelves, best sellers, new arrivals, curated product IDs, CommerceHub homepage sections, suggestions/autocomplete, and affiliate discovery, all now treated as derivative candidate/ranking input on reviewed reads.
- Trending/popular products: reviewed best-seller/popular-query surfaces may rank from Elasticsearch or analytics, but returned product/term visibility is SQL-filtered on the inspected paths.
- Featured products: `Products.IsFeatured` may influence ordering/selection, but current SQL product/category/publication/stock/price authority decides return eligibility.
- Cache authority: Redis homepage and search caches remain acceleration only; reviewed cache invalidation collapses duplicate replay before fanout.
- Projection authority: CommerceHub homepage projections are mirrors; rebuild and read filtering scrub invalid listing/category/vendor references from current SQL authority.
- Overlay authority: storefront homepage cards, storefront discovery, CommerceHub suggestions/autocomplete/search, and affiliate discovery now apply current SQL overlays before response.
- Replay/rebuild: homepage projection replay is fenced, rebuilds converge from current SQL, and invalid curated references are deleted, scrubbed, or omitted.
- Recovery tools: no additive-only homepage or recommendation repair tool was promoted to authority in this pass.
- Tenant isolation: reviewed storefront homepage paths bind tenant id and active tenant lineage; CommerceHub listing/vendor paths revalidate listing tenant ownership; affiliate discovery is intentionally global but every returned item is recomposed against its own current tenant/product/policy authority.
- Resurrection chaos: product unpublish/delete, affiliate-policy disablement, tenant deactivation, category invalidation, stale ES/Mongo/Redis candidates, worker restart, cache restart, homepage rebuild, and discovery replay now converge to absence on the reviewed surfaces.

## Remaining Debt

- No personalized recommendation engine exists in the inspected backend. If one is added later, it must call the same SQL product/listing/category/media/stock/price/publication authority before response and rebuild mutation.
- Search/discovery facet and all-page total recomposition remain broader search-read debt. This pass forces returned affiliate discovery items and inspected page totals to fail closed, but does not implement full SQL-recomputed global counts.
- Future feed/export/cache-warmer/CDN purge/support/global-script/analytics-reporting recommendation paths remain path-specific certification debt.
