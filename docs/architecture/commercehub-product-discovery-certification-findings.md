# CommerceHub Product Discovery Certification Findings

Date: 2026-06-01

Scope: PASS 16.2, Product Discovery Certification.

## Discovery Inventory

- Homepage: CommerceHub homepage sections, product shelves, category sections, hero/banner links, and Redis-backed homepage read cache.
- Featured and promoted products: featured product queries, homepage product grids, fallback related products, and curated product-id sections.
- Search: CommerceHub product search API, Elasticsearch read service, Mongo fallback, SQL authority overlay, search result cards, pagination, sorting, zero-result suggestions, search telemetry, impressions, and clicks.
- Autocomplete and suggestions: product completions, category completions, suggested terms, popular terms, recent searches, and cached autocomplete responses.
- Category navigation: category tree API, category cards, category filters, category links from autocomplete, category lineage, and category attribute definitions.
- Filters: SQL-derived category facets, price facets, availability, category filter, attribute term/range filters, filter chips, desktop filter sidebar, mobile filter drawer, and URL filter state.
- Product cards: search cards, category cards, homepage cards, related/fallback cards, and product-grid cards.
- Product details: direct product URL, variant selector, gallery, price, stock state, category, media, attributes, reviews, and add-to-cart controls.
- Recommendations: no independent recommendation engine was found. Recommendation-like surfaces are homepage shelves, related-by-category products, featured fallback products, popular searches, autocomplete suggestions, and zero-result suggestions.
- Inventory and pricing: product card price/stock, detail variant price/stock, cart pricing snapshots, SQL variant inventory lineage, and `CommerceHubPriceAuthority`.
- SEO and deep links: direct product URLs and category/search URLs. No dedicated CommerceHub server-side SEO sitemap was found in this pass.
- Audit and monitoring: search query logs, click logs, impression tracking, search-sync health check, reindex result counts, stale-delete counts, auto-healing queue, projection readiness, and governance visibility version cache keys.

## Homepage Findings

- CommerceHub homepage reads load the projected homepage, then call `FilterPublicReadAsync` and `FilterHomepageSectionsByVendorVisibilityAsync` before mapping sections to the frontend.
- Homepage product sections are derivative. SQL listing, product, variant, category, tenant, vendor governance, price, and inventory authority decide whether product IDs survive the public filter.
- `FilterVisibleListingIdsAsync` enforces `!IsDeleted && IsPublished` on listings before checking vendor governance, preventing ghost listing IDs from surviving in homepage product sections.
- Redis homepage cache entries are keyed by tenant and language and remain bounded by projection lag controls.

## Search Findings

- Search results are candidates from Elasticsearch or Mongo fallback. `CommerceHubQueryService` recomposes returned items through `RecomposeVisibleListingItemsAsync` and `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync`.
- Search totals are returned as `visibleItems.Count`, not raw Elasticsearch total counts.
- Search facets are rebuilt from returned SQL-visible items through `MapSqlVisibleFacetsAsync`.
- Reindex deletes Elasticsearch documents absent from current SQL-visible authority through `DeleteStaleSearchDocumentsAsync`.

## Category Findings

- Public category tree candidates are filtered through `CommerceHubCategoryLineageAuthority.LoadLiveIdsAsync`.
- Category attribute reads reject invalid or deleted lineage through `CommerceHubCategoryLineageAuthority.IsLiveAsync`.
- Search, overlay, reindex, and autocomplete category mapping all depend on live category lineage.

## Filter Findings

- Attribute filter definitions are loaded from current SQL category attributes.
- Unsupported attribute filters fail closed with empty results and empty facets.
- Indexed attribute JSON is normalized against current SQL category schema before search projection.
- Price, category, availability, and attribute facets returned to customers are SQL-visible result facets, not raw search aggregations.

## Product Card Findings

- Product cards display title, image, store, category, stock badge, price, and currency from backend product DTOs.
- Backend list DTOs are built from SQL-overlaid `CommerceHubProductDocument` instances, so stale Mongo or Elasticsearch cards cannot be returned without passing current SQL authority.
- Cards expose out-of-stock state instead of hiding the condition from customers.

## Product Details Findings

- Direct detail reads call `GetProductByIdAsync`, reject suppressed vendors, and then rebuild detail from `CommerceHubSearchSqlAuthorityOverlay.ApplyAsync`.
- Detail pages cannot resurrect deleted, unpublished, suppressed, invalid-category, inactive-variant, or tenant-disabled listings because the SQL overlay returns null.
- Variant selection, gallery, price, stock, reviews, and category data are hydrated from the SQL-overlaid document plus current product read model data.

## Recommendation Findings

- No standalone personalized recommendation engine was found.
- Related products use category product queries and featured fallback queries, both routed through the same product/search backend authority.
- Homepage recommendation-like sections are filtered before response and cannot independently resurrect invisible listings.

## Frontend UX Findings

- Products page supports category navigation, search, sorting, filters, filter chips, mobile filters, empty states, loading states, error states, infinite scrolling, and URL-backed state.
- Autocomplete separates product, category, suggestion, popular, and recent sections and records impressions/clicks without treating telemetry as visibility authority.
- Product detail handles loading, not-found/error states, variant selection, quantity, sticky add-to-cart, reviews, features, FAQs, and related products.

## Inventory/Pricing Findings

- CommerceHub search overlay resolves price through `CommerceHubPriceAuthority`.
- Inventory is recomposed from SQL `ProductVariantStocks` through current inventory owner lineage and returns both `InStock` and `AvailableStock`.
- Product details and list cards show the SQL-overlaid price, currency, and stock state.

## SEO/Deep-Link Findings

- Direct product deep links are safe because detail reads revalidate SQL authority before returning a product.
- Category deep links are safe because category tree and category attributes reject invalid lineage and product search filters are SQL-revalidated.
- Dedicated CommerceHub sitemap/structured-data certification was not found; this remains a production SEO maturity item.

## Auditability Findings

- Search queries, impressions, and clicks provide customer discovery telemetry.
- Reindex, search sync status, stale deletion, auto-healing, projection readiness, and governance cache invalidation provide operational evidence.
- Suppression and lifecycle evidence from prior PASS 15.5B/16.1B work supports incident reconstruction, but discovery-specific incident dashboards and alert packs remain incomplete.

## Chaos Findings

- Ghost listings: search/detail/homepage paths fail closed through SQL overlay or public filters.
- Stale search: reindex deletes stale documents and read-time search recomposes returned hits.
- Stale categories: category tree, search overlay, and filter metadata require live lineage.
- Stale facets and attribute drift: unsupported attributes fail closed and facets are derived from visible items.
- Inventory and pricing drift: list/detail documents are recomposed from SQL stock and `CommerceHubPriceAuthority`.
- Suppressed/deleted resurrection: vendor governance, tenant activity, listing publication, product publication, category lineage, and variant lifecycle are checked before response.
- Frontend/backend mismatch: frontend only renders backend DTOs and does not calculate visibility authority.

## Monitoring Findings

- Existing monitoring covers search sync health, projection readiness, reindex counts, stale deletion, auto-healing queue activity, query/click/impression telemetry, and governance visibility versions.
- Missing launch-grade monitoring remains: discovery incident dashboard, alert rules for facet drift, autocomplete suppression misses, detail deep-link rejection spikes, and homepage stale-reference deletion spikes.

## Runtime Hardening Performed

- `CommerceHubPublicVendorVisibilityService.FilterVisibleListingIdsAsync` — Added `!listing.IsDeleted && listing.IsPublished` to the listing query filter. Previously, this method only checked vendor governance suppression, allowing deleted or unpublished listing IDs to survive in homepage product sections when the vendor was active.
- `CommerceHubQueryService.GetPopularQueriesAsync` — Added `RevalidatePopularQueriesResponseAsync` to revalidate cached popular query terms through `FilterLiveSuggestionTermsAsync` on cache hit. Previously, cached popular terms were returned without SQL liveness verification, unlike suggestions and autocomplete which both had revalidation paths.
- `ProductDiscoveryService.MapSearchResponseAsync` — Replaced empty `new ProductDiscoveryFacets()` with `BuildFacetsFromVisibleItems(items)` that rebuilds brand, category, stock, promotion, and price range facets from the SQL-authority-recomposed result set. Added `BuildFacetsFromVisibleItems` method to compute facets exclusively from items that passed `RecomposeDiscoveryItemsFromSqlAuthorityAsync`.
- Added regression freeze coverage in `CommerceHubProductDiscoveryCertificationTests`: `HomepageListingVisibility_MustRejectDeletedAndUnpublishedListings`, `PopularQueries_MustRevalidateCachedTermsBeforeReturning`, `StorefrontDiscoveryFacets_MustBeRebuiltFromSqlVisibleItems`.

## Architecture Updates

- PASS 16.2 is recorded in `.ai-context/PROJECT_CONTEXT.md`, `.ai-context/ARCHITECTURE_TRUTH.md`, `.ai-context/KNOWN_RISKS.md`, `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`, and `.ai-context/CODEX_RULES.md`.

## Remaining Debt

- CommerceHub public launch remains blocked by PASS 15.6/16.1B deployment proof, monitoring, alerting, SMTP, evidence retention, and chaos validation requirements.
- Add discovery-specific dashboards and alerts for search/detail/homepage/category/filter drift.
- Add dedicated CommerceHub SEO sitemap and structured-data certification.
- Add PostgreSQL-backed runtime chaos tests for search stale deletion, suppressed listing direct links, category deletion, facet drift, and recommendation fallback behavior.
- If a personalized recommendation engine is introduced, it must call the same SQL authority overlay before response and rebuild mutation.

## Required Answers

- Can customers reliably find products? Yes for the inspected repository runtime surfaces, subject to remaining launch monitoring/deployment debt.
- Can search surface invisible products? No on certified read paths; returned hits are SQL-overlaid and stale documents are deleted during reindex.
- Can recommendations surface invisible products? No independent recommendation engine exists; recommendation-like surfaces route through homepage/search product authority.
- Are categories trustworthy? Yes on inspected category tree, attributes, search, and filter paths.
- Are filters trustworthy? Yes on inspected paths; unsupported attributes fail closed and visible facets are SQL-derived.
- Are product details trustworthy? Yes; direct detail reads revalidate current SQL authority before response.
- Are inventory and prices trustworthy? Yes on inspected discovery/detail paths; they use SQL inventory lineage and `CommerceHubPriceAuthority`.
- Are discovery incidents auditable? Partially. Telemetry and operational evidence exist, but launch-grade discovery dashboards and alerts remain debt.
- Are critical discovery risks present? No new critical runtime authority inversion was found; critical launch readiness risks remain operational/deployment risks already tracked.
- Is CommerceHub product discovery production-ready? Runtime discovery is production-certifiable, but public launch remains not ready until the broader PASS 15.6/16.1B blockers and discovery monitoring debt are closed with environment evidence.
