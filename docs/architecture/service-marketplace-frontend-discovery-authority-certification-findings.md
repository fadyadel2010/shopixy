# PASS F-S1-A Service Marketplace Frontend Discovery Authority

Service Marketplace Frontend Discovery Authority is production-certifiable for the inspected runtime after current-authority pagination, filter/sort recomposition, replayable URL state, fail-closed cache presentation, and search failure hardening.

## Search Findings

- Elasticsearch supplies candidate IDs and text relevance evidence only.
- Every candidate is reloaded through current PostgreSQL service, active tenant, and same-tenant active provider-profile authority before presentation.
- Search outages now return HTTP 503. The API and frontend no longer manufacture an authoritative empty success.

## Filter Findings

- Category filters are reapplied to current SQL listing fields after candidate retrieval.
- Rating and trust-badge filters use current SQL-composed review reputation.
- Completed-request filters use current SQL request lifecycle rows.
- Backend validation bounds query, category, rating, completed-request, badge, page, page-size, and sort input.

## Sorting Findings

- Rating, newest, low-price, and high-price sorting run after current SQL/trust recomposition.
- Mutable Elasticsearch trust and request-stat fields no longer influence relevance scoring.
- Sorting changes presentation only and cannot restore or publish a listing rejected by current SQL visibility authority.

## Pagination Findings

- The prior runtime counted only SQL-valid cards on the current Elasticsearch page, making `TotalCount` at most one page and preventing valid next-page navigation.
- Candidate results are now SQL-revalidated and currently filtered/sorted before `TotalCount`, `Skip`, and `Take`.
- Stale, hidden, deleted, inactive, suspended-tenant, or inactive-profile candidates cannot occupy returned pages or inflate the visible count.

## Discovery Result Findings

- Cards render only backend-composed service IDs, listing fields, provider identity, trust, request statistics, pricing, category, and media.
- No client-side filter, sort, visibility, provider, rating, or service lifecycle authority was found.
- Direct card navigation re-enters current service-detail visibility authority.

## Cache Findings

- React Query keys remain scoped by authenticated tenant and user.
- Discovery uses zero stale time and refetches on mount, focus, reconnect, and active-route revalidation.
- Cached cards are hidden while current authority is validating and after initial or background refresh failure.
- Actor or tenant transitions remove prior marketplace cache scopes before replacement-route presentation.

## Realtime Findings

- Discovery does not accept a realtime payload as listing authority.
- Marketplace route revalidation and provider mutation invalidation trigger backend search recomposition; projection events only trigger current-SQL index convergence.
- Replayed or stale projection events reload SQL and delete or suppress documents rejected by current visibility authority.

## Tenant Isolation Findings

- Cache identity includes authenticated tenant and user identity.
- Browser parameters do not supply tenant scope; API requests use the canonical authenticated POS session and trusted backend tenant context.
- Public service discovery is intentionally global, but each returned listing requires current SQL tenant and provider-profile lineage.

## History Findings

- Query, category, sort, and page are normalized URL presentation state.
- Tab changes and tab normalization preserve discovery parameters.
- Back, forward, refresh, bookmarks, and manual parameter edits replay a backend search; they cannot restore cached result DTOs.
- Invalid or oversized parameters normalize or fail backend validation and never become listing authority.

## Failure-Handling Findings

- Elasticsearch failure and candidate-scroll failure fail closed.
- The controller returns 503 instead of HTTP 200 with synthetic empty results.
- The frontend clears cached presentation on authority validation and failure and offers an explicit retry.
- 401/403 behavior remains owned by the canonical session-aware API client.

## Adversarial Findings

- Search replay, cache replay, query tampering, page replay, filter manipulation, history replay, actor switching, tenant switching, stale projection replay, and refresh failure were reviewed.
- None can publish a service or bypass current SQL listing visibility.
- Stale search projection data may affect candidate availability until convergence, but cannot create returned listing identity, visibility, filters, counts, current sort fields, trust, statistics, or tenant lineage.

## Runtime Hardening Performed

- Reworked marketplace search to scan candidates, reload current SQL listing authority, compose current trust/request statistics, apply current filters/sorts, and only then count and paginate.
- Removed mutable projected trust/request statistics from relevance scoring.
- Added bounded backend search parameter validation.
- Changed search failures from synthetic empty success to HTTP 503.
- Made discovery URL state preserve and normalize query, category, sort, page, and tab parameters.
- Hid cached discovery cards during authority validation and after background refresh failures.

## Exact Runtime Files Modified

- `PointOfSale.Infrastructure/Services/MarketplaceSearchService.cs`
- `PointOfSale.Core/DTOs/Marketplace/MarketplaceSearchRequest.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarketplaceSearch.ts`
- `PointOfSale.Web/src/marketplace/pages/MarketplaceShell.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendDiscoveryAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns discovery authority? The frontend owns derivative discovery presentation; current PostgreSQL listing, tenant, and provider-profile authority owns eligibility and returned listing truth.
- Can search become authority? No. Elasticsearch and frontend search state are candidate/presentation evidence only.
- Can filters bypass visibility authority? No. Current SQL visibility is required before current-authority filters are applied.
- Can pagination resurrect hidden services? No. Counting and pagination occur after current visibility recomposition.
- Can cache expose removed listings? Not on inspected validation paths; cached cards are withheld during revalidation and after refresh failure, and actor scopes are purged.
- Can realtime events corrupt discovery truth? No. Events trigger current-SQL convergence or refetch and do not directly write discovery cards.
- Can tenant leakage occur? No inspected cache, URL, API, or projection path can reuse foreign tenant/user authority.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Discovery Authority production-certifiable? Yes, for the inspected runtime after the hardening above.

## Residual Operational Risk

- Elasticsearch remains candidate availability infrastructure. Index lag can temporarily omit a currently visible service until projection or drift convergence, but it cannot expose a service rejected by current SQL authority.
- Production should monitor search 503 rate, candidate-to-visible drift, scroll duration, result volume, and projection convergence latency.
