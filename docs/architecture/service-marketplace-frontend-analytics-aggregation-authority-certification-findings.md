# PASS F-S6-C Service Marketplace Frontend Analytics Aggregation Authority

Service Marketplace Frontend Analytics Aggregation Authority is production-certifiable for inspected runtime paths after provider-rollup and service-card cross-aggregate consistency hardening.

## Aggregate Findings

- Current backend analytics, request, completion, review, reputation, provider, actor, tenant, and visibility authorities own every aggregate input.
- Frontend aggregate boundaries validate complete backend DTO envelopes and suppress invalid envelopes. They do not calculate replacement business totals.

## Rollup Findings

- Provider profile rollups bind tenant, provider user, and provider profile identity before presentation.
- Provider services, final completions, review totals, average rating, and badge are accepted as one rollup. Reviews cannot exceed final `CompletedByBuyer` requests.
- Service-card analytics and reputation remain backend values and are accepted together only when their relationship is possible.

## Summary Findings

- Profile summary cards render only an accepted provider analytics envelope.
- Listing summaries render accepted backend values without calculating totals, averages, rates, badges, or fallback summaries.
- Missing or contradictory summaries are withheld rather than repaired or replaced with zeroes.

## Consistency Findings

- Completion rates must match backend completed and total request counts.
- Reputation averages, counts, distributions, percentages, and badges must remain internally coherent under the prior F-S5-D and F-S6-B boundaries.
- Review totals must not exceed final completion totals for the same provider or service. Contradictory service aggregate bundles are suppressed as a unit.

## Visibility Findings

- Provider summaries require current active tenant, profile, actor, and governance visibility.
- Service summaries remain nested under current listing visibility.
- Suspension, ban, deletion, hiding, tenant loss, profile loss, or actor loss withholds aggregate presentation.

## Cache Findings

- Marketplace aggregate queries remain authenticated tenant/user scoped.
- Cached provider summaries are withheld during profile or stats refresh and after failure.
- Actor and tenant transitions remove prior marketplace query scopes; cache cannot preserve aggregate truth.

## Realtime Findings

- SignalR, BroadcastChannel, reconnect, focus, visibility, periodic refresh, and mutation settlement invalidate/refetch only.
- No inspected realtime path writes aggregate, rollup, summary, count, rate, reputation, or badge truth into cache.

## Actor-Transition Findings

- Provider aggregate identity must match the current tenant/user scope and current provider profile ID.
- Logout/login, actor replacement, tenant switching, and provider replacement cannot reuse prior summaries.

## History Findings

- Back, forward, refresh, bookmarks, and deep links carry navigation only.
- Aggregate surfaces re-enter current backend profile, listing, actor, tenant, visibility, analytics, completion, and reputation authority.

## Failure Findings

- 401, 403, 404, 500, timeout, malformed DTO, refresh failure, and contradictory aggregate paths withhold affected presentation.
- Queries do not retry obsolete aggregates as success, and no success-shaped fallback summary is created.

## Adversarial Findings

- Aggregate replay, rollup replay, summary replay, cache replay, history replay, realtime replay, actor switching, tenant switching, provider switching, malformed DTO injection, and contradictory aggregate injection were reviewed.
- None can inflate, suppress as replacement truth, repair, preserve, resurrect, or cross-scope leak analytics aggregation truth on inspected paths.

## Runtime Hardening Performed

- Routed provider analytics composition through the shared provider reputation/stat rollup boundary.
- Rejected provider rollups whose review total exceeds final completed requests.
- Rejected service-card aggregate bundles whose review total exceeds final completed requests.
- Suppressed contradictory service reputation and analytics together rather than selecting or repairing one side.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.ts`
- `PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts`
- `PointOfSale.Web/src/marketplace/listingCardAuthority.ts`

## Regression Freezes Added

- Updated `PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.test.ts`.
- Updated `PointOfSale.Web/src/marketplace/reputationPresentationAuthority.test.ts`.
- Updated `PointOfSale.Web/src/marketplace/listingCardAuthority.test.ts`.
- Added `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendAnalyticsAggregationAuthorityCertificationTests.cs`.

## Architecture Updates

- Added this F-S6-C findings document.
- Updated all five `.ai-context` authority files with the F-S6-C runtime rule and freeze.

## Explicit Answers

- Who owns analytics aggregation authority? Current backend analytics, request, completion, review, reputation, provider, actor, tenant, and visibility authorities own source truth; F-S6-C owns derivative frontend aggregate presentation and consistency validation only.
- Can frontend fabricate aggregates? No.
- Can rollups override source metrics? No.
- Can summaries contradict metrics? Contradictory summaries are withheld and cannot render as current authority.
- Can aggregates contradict each other? Malformed DTOs can arrive contradictory, but accepted presentation enforces cross-aggregate consistency.
- Can visibility loss preserve summaries? No.
- Can realtime fabricate aggregates? No.
- Can history resurrect aggregate truth? No.
- Can tenant leakage occur? No inspected DTO, query key, cache, event, route, or callback path crosses authenticated scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Analytics Aggregation Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk

- Production should retain browser/API chaos coverage for review creation during refresh, completion/review read skew, provider suspension, actor and tenant replacement, delayed cross-tab invalidation, malformed gateway payloads, and decimal serialization boundaries.
