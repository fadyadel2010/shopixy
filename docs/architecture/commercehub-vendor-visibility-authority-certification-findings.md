# PASS M1-E CommerceHub Vendor Visibility Authority Certification Findings

Date: 2026-06-16

## Certified Boundary

Backend SQL authority owns CommerceHub vendor visibility. Public presentation is derivative of current listing, vendor, governance, lifecycle, publication, tenant, category, product, variant, stock, pricing, moderation, and financial-control authority. Frontend state, route state, React Query caches, MongoDB, Elasticsearch, search state, filters, mutation responses, browser storage, and cross-tab events are consumers only.

## Findings

### Visibility

`CommerceHubPublicVendorVisibilityService` suppresses vendors by recomposing current `CommerceHubVendorAuthority` eligibility and current financial-control suspension state. `CommerceHubSearchSqlAuthorityOverlay` rejects missing or unpublished listings, inactive or deleted tenants, non-platform CommerceHub currency, ineligible vendors, hidden products, invalid public variant lineage, dead categories, and invalid price/stock authority. Visibility cannot be fabricated from DTOs or preserved by stale projections.

### Search And Discovery

Product lists, search results, suggestions, autocomplete, popular terms, facets, category discovery, and zero-result suggestions route through `CommerceHubQueryService`, vendor exclusions, live-term filtering, and SQL overlay recomposition. Cached suggestion/autocomplete/popular responses are revalidated before return, and cache keys include the governance visibility version plus suppressed-vendor fingerprint.

### Storefront

Direct product URLs call `GetProductByIdAsync`, reject suppressed vendors, and reapply `CommerceHubSearchSqlAuthorityOverlay` before mapping detail DTOs. Public controller responses are marked `no-store`, including surrogate cache control, so browser or edge cache cannot become visibility authority.

### Listings

Public listing cards and detail DTOs are mapped from SQL-recomposed documents. Hidden, unpublished, deleted, inactive, rejected, suspended, debt-blocked, or category-dead vendors cannot retain public listing visibility through Elasticsearch or MongoDB because stale documents must pass the overlay before presentation.

### Governance And Lifecycle

Suspended and banned vendor governance hides listings through `HideByGovernance()` and enqueues search verification. Reactivation restores only after reloading current vendor eligibility, active product publication, current variant lineage, and live category authority; otherwise it clears restore state.

### Bootstrap And Refresh

Initial load, refresh, search reloads, and product-detail reloads call backend endpoints. No inspected CommerceHub frontend product/search query reads vendor visibility from local storage, session storage, route state, or mutation responses. Refresh can only receive the backend's current visibility answer.

### Cache And Transition

Backend memory caches are short lived, visibility-version keyed, and revalidated for cached search-assist surfaces. Frontend React Query stores derivative API responses only. Actor and tenant transitions remain isolated by backend tenant scope and by query keys; no cross-tenant vendor visibility authority is stored in browser storage.

### Failure And Adversarial Review

Missing SQL authority returns no product/detail result. Malformed or stale search, autocomplete, suggestion, impression, click, Mongo, and Elasticsearch data is filtered through current SQL overlay or accepted only as non-authoritative analytics evidence. URL replay, history replay, cache replay, and search replay fail closed.

## Runtime Hardening Performed

No additional runtime hardening was required for this pass. The existing runtime already routes public vendor presentation through backend authority. Added regression freeze coverage for the M1-E authority boundary.

## Exact Runtime Files Modified

None.

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/CommerceHubVendorVisibilityAuthorityCertificationTests.cs`

## Architecture Updates

- Added this certification record: `docs/architecture/commercehub-vendor-visibility-authority-certification-findings.md`.

## Explicit Answers

- Who owns vendor visibility authority? Backend SQL authority: listing, vendor/governance, lifecycle, publication, tenant, financial-control, product, variant, category, price, stock, and moderation authority.
- Can frontend fabricate visibility? No.
- Can hidden vendors appear in search? No inspected path permits it; search results and search-assist surfaces are SQL-recomposed or live-filtered.
- Can hidden vendors expose storefronts? No; direct product/detail URLs reject suppressed vendors and reapply SQL authority.
- Can hidden vendors retain listings? No; published listing visibility requires current backend eligibility and overlay approval.
- Can stale visibility survive refresh? No; refresh calls backend public endpoints and responses are `no-store`.
- Can cache restore visibility? No inspected cache is authority; cached backend responses are visibility-version keyed and revalidated.
- Can tenant leakage occur? No inspected public visibility path permits tenant leakage; tenant scope is enforced by backend query scope and SQL overlay tenant predicates.
- Were runtime changes required? No.
- Is Vendor Visibility Authority production-certifiable? Yes, with the regression freeze added in this pass.
