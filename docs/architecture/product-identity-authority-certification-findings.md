# PASS SF-PA3.1 Product Identity Authority Certification

Date: 2026-06-28

## Certification Result

Product Identity Authority is production-certifiable after stale public-affiliate slug admission hardening.

Backend SQL product authority, tenant lineage, category lineage, publication/visibility state, current canonical slug, current product id, current variant ownership, and backend read services own Product Identity, Canonical Identity, Slug Resolution, ProductId Resolution, Variant Entry, Redirect Policy, Publication Identity, Visibility Identity, Store Lineage, Tenant Lineage, Persistence, and Recovery.

Frontend routes, URL slugs, browser history, React state, loading/skeleton UI, SEO rendering, and navigation own presentation only. Redis, MongoDB, Elasticsearch, and DTOs are derivative candidates and must re-enter current backend SQL authority before product identity reaches presentation.

## Runtime Findings

- Storefront product detail slug reads call `StorefrontProductService.GetProductBySlugAsync`, use Redis/Mongo only as candidates, recompose cached and projected candidates through `ProductSearchSqlAuthorityOverlay.ApplyAsync`, and suppress cached detail when the backend SQL slug does not match the requested slug.
- Product id reads resolve from backend `ProductId` plus current tenant SQL authority and cannot be fabricated by the frontend beyond submitting a lookup candidate.
- `ProductSearchSqlAuthorityOverlay` overwrites identity fields from SQL: `ProductId`, `Slug`, category, media, variants, visibility, publication state, and tenant-scoped product lineage.
- Public affiliate search/list reads already treated Elasticsearch as a candidate and returned recomposed SQL identity. The detail endpoint had one gap: an old Elasticsearch slug could find a candidate by stale `ProductSlug`, recompose by backend `ProductId`, then return the product under the old URL. Runtime now rejects that candidate unless the requested slug matches the current backend canonical slug.
- Variant entry remains backend-owned: variant DTOs are rebuilt from current SQL variants filtered to non-deleted current semantic owners, with default variant selected from SQL ordering.

## Boundary Review

- Identity Boundary: backend SQL `Products` plus `ProductSearchSqlAuthorityOverlay`; recovery source is SQL; stale/missing/deleted/malformed candidates fail closed.
- Canonical Boundary: backend SQL `Product.Slug`; recovery source is SQL; stale slug candidates return 404 unless a future explicit backend redirect policy is added.
- Variant Boundary: backend SQL `ProductVariants` and merge/split semantic ownership; recovery source is SQL variant rows; deleted/non-current variants are suppressed.
- Presentation Boundary: admitted backend DTOs only; recovery source is backend reread; malformed or stale DTOs must not render identity.
- Recovery Boundary: service/read-overlay re-entry after Redis, Mongo, Elasticsearch, browser refresh, rebuild, or restart; recovery source is SQL; derivative drift converges or fails closed.
- SEO Boundary: canonical slug/meta are backend DTO evidence; recovery source is backend SQL; stale frontend/browser/search slug cannot survive admission.

## Required Answers

- Product Identity owner: backend SQL product authority and backend services.
- Canonical Identity owner: backend SQL current product slug and backend read admission.
- Slug Resolution owner: backend services after SQL authority re-entry.
- Can frontend resolve Product Identity? No.
- Can URL become authority? No; it is only a lookup candidate.
- Can stale slug survive recovery? No; storefront Redis/Mongo slug reads and public affiliate Elasticsearch slug detail now suppress stale slug candidates after SQL recomposition.
- Can Search expose old identity? Search may return a stale candidate internally, but public responses are recomposed through SQL and detail reads reject stale slug mismatch.
- Can Redis become identity authority? No; cached detail is recomposed through SQL and stale slug/product identity is suppressed.
- Where does Product Identity begin? At backend SQL product/tenant/category/variant authority.
- Where does Canonical Identity begin? At backend SQL `Product.Slug` during read admission.
- Where does Presentation begin? After backend DTO admission returns current identity evidence.
- Were runtime changes required? Yes, in `PublicAffiliateProductsController`.
- Is Product Identity production-certifiable? Yes.

## Runtime Hardening Performed

- `PublicAffiliateProductsController.GetProductBySlug` now compares requested slug with the SQL-recomposed current product slug via `IsCurrentCanonicalProductSlug`.
- Stale public affiliate slug hits are logged and return 404 instead of presenting the current product under an old URL.

## Modified Runtime Files

- `PointOfSale.API/Controllers/PublicAffiliateProductsController.cs`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/StorefrontCatalogPresentationAuthorityCertificationTests.cs`

## Architecture And AI Context Updates

- Added this findings document.
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.
