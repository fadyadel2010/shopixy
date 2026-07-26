# Product Detail, Media & Public Projection Certification Findings

Date: 2026-05-29

Scope: POS-ADMIN-CERTIFICATION-01 PASS 9.3, Product Detail, Media & Public Projection Certification.

## Authority Model

- PostgreSQL `Products` owns product existence, active state, delete/archive state, publication state, storefront visibility, SEO eligibility, and public detail visibility.
- PostgreSQL `ProductVariants` owns variant public lineage, current semantic ownership, SKU/barcode identity, media linkage, stock projection eligibility, and variant purchasability.
- PostgreSQL `ProductMedia` owns product media lineage, gallery ordering, and primary image selection. `ProductVariants.MediaId` may link a variant to a current same-tenant, same-product media row; legacy variant image strings are derivative display evidence only.
- PostgreSQL `CommerceHubListings` plus current tenant/product/variant/category/media/stock/price/vendor governance owns CommerceHub listing visibility.
- MongoDB `ProductDocument`, CommerceHub Mongo documents, Redis product/SEO caches, Elasticsearch product documents, thumbnails, CDN state, replay events, and repair payloads are derivative evidence only.
- If SQL and any derivative surface disagree, SQL wins and public product/media/detail visibility fails closed.

## Findings

- Confirmed storefront detail gap: public storefront product detail reads loaded Redis/Mongo product DTOs without reapplying current SQL product/media/variant/category/unit authority at response time. A stale `ProductDocument` could therefore preserve detail visibility until projection/cache convergence.
- Confirmed storefront list/category-tree gap: deprecated Mongo-backed list paths and category-tree filtering returned Mongo candidates as public products. Category lineage was already certified, but product visibility and media lineage still needed SQL recomposition before DTO mapping.
- Confirmed product SEO gap: product SEO metadata, product breadcrumbs, cached product slug resolution, and sitemap product URLs used Mongo product read models as return authority. A stale slug/cache/search document could keep deleted or unpublished product SEO visibility alive.
- Confirmed search hydration gap: storefront search and discovery used Elasticsearch results plus Mongo hydration as public response authority. Search mutation paths were already SQL-overlaid, but read-time hydration still needed current SQL recomposition to suppress stale hits and stale media.
- Confirmed media lineage residual: the shared product SQL overlay rebuilt gallery media from SQL `ProductMedia`, but it still copied legacy `ProductVariant.ImageUrl` into variant projection fields. Public DTOs currently resolve variant image URLs through `MediaId` and product media first, but carrying the legacy string inside the refreshed projection was unnecessary resurrection surface.
- Existing media event protections held: `ProductMediaChangedEventConsumer` fences duplicate replay, validates SQL product version, reloads current tenant/product-scoped `ProductMedia`, orders gallery rows by SQL position, and sends search sync through `ProductSearchSqlAuthorityOverlay`.
- Existing CommerceHub listing protections held from PASS 5/PASS 7: CommerceHub listing projection, detail reads, search overlay, homepage/recommendation, auto-healing, and reindex paths recompose SQL listing/product/variant/category/media/review/vendor authority before preserving public visibility.

## Runtime Hardening

- `StorefrontProductService` now treats Redis and Mongo as candidate layers only. Product-by-slug, product-by-id, and Mongo-backed product lists recompose through `ProductSearchSqlAuthorityOverlay` and require `{ IsActive: true, IsOnlineVisible: true }` before returning or caching DTOs.
- `StorefrontProductsController` now applies the same SQL overlay to the direct category-tree Mongo list path before mapping public product cards.
- `ProductSearchService` now SQL-recomposes Elasticsearch/Mongo hydrated hits before public search response mapping.
- `ProductDiscoveryService` now SQL-recomposes Elasticsearch discovery hits before returning product cards, so stale Elasticsearch fields and stale media URLs cannot be final response authority.
- `SeoService` now uses current SQL-overlaid products for product SEO metadata, product breadcrumbs, product slug resolution, cached product slug revalidation, and product sitemap URLs.
- `ProductSearchSqlAuthorityOverlay` now requires active tenant lineage and no longer carries legacy variant image strings into refreshed variant projections. Variant image visibility must resolve through current `MediaId` and SQL `ProductMedia`.

## Regression Freeze

- `ProductDetailMediaPublicProjectionCertificationTests` freezes the reviewed product-detail, product-list, search-hydration, discovery, SEO, slug, sitemap, and media-lineage authority boundaries.
- Existing product/search/rebuild architecture tests continue to freeze product projection writes through `IProductSqlAuthorityProjectionWriter`, search mutation through `ProductSearchSqlAuthorityOverlay`, media replay fencing, and CommerceHub SQL listing overlay behavior.

## Certification Results

- Product visibility: reviewed storefront detail, list, search hydration, discovery, SEO, sitemap, and slug surfaces now require current SQL-visible product authority.
- Variant visibility: reviewed public DTOs now receive variants from the SQL overlay, which filters non-current semantic owners and inactive/unpriced variant sets before public mapping.
- Product media: reviewed public media/gallery response data now comes from current SQL `ProductMedia` ordering; legacy variant image strings are not retained by the shared SQL overlay.
- Product detail projection: Redis/Mongo detail caches are candidate acceleration only; current SQL overlay decides return versus absence.
- Product SEO: product metadata, breadcrumbs, slug resolution, and sitemap product URLs now fail closed when SQL no longer proves product visibility.
- Search hydration: storefront search/discovery hits are recomposed from SQL before public response shaping.
- CommerceHub listing: previously certified CommerceHub listing/detail/search/homepage/rebuild paths remain listing-authoritative and media-authoritative.
- Replay/rebuild: reviewed product/media/search/rebuild paths converge through current SQL overlay or tombstone/delete outcomes.
- Recovery tools: reviewed admin product rebuild/search repair paths remain SQL-first; this pass did not promote any repair payload to authority.
- CDN/assets: CDN URLs remain strings referenced only when current SQL `ProductMedia` accepts them; CDN state itself is not visibility authority.
- Tenant isolation: reviewed product/media/detail/SEO/search decisions bind `TenantId`, and the shared product overlay now also requires active tenant lineage.
- Resurrection chaos: product delete/unpublish/media delete followed by cache restart, worker replay, search reindex, or storefront redis/Mongo reuse now converges to absence on the reviewed surfaces when SQL no longer proves visibility.

## Remaining Debt

- Discovery facets and total counts are still derived from Elasticsearch aggregations before read-time SQL suppression. Public items fail closed, but exact facet/count recomposition remains future search-read certification debt.
- The deprecated Mongo-backed product list endpoint still exists for backward compatibility; it is now SQL-filtered before return, but the preferred public browse path remains discovery/search.
- Product structured data beyond current SEO metadata was not expanded in this pass. Future JSON-LD/product schema work must use the same SQL-overlaid product authority.
- Feed/export/cache-warmer/CDN purge/support/global-script paths remain path-specific certification debt until they add current SQL product/media/variant visibility checks and replay/rebuild tests.
