# PASS F-S6-A Service Marketplace Frontend Analytics Identity Authority

Service Marketplace Frontend Analytics Identity Authority is production-certifiable for inspected runtime paths after provider analytics identity binding, visibility-loss failure hardening, refresh withholding, dedicated query-key scoping, and KPI arithmetic validation.

## Analytics Identity Findings
- Analytics presentation is derivative from current backend request, completion, review, reputation, provider, tenant, actor, and visibility authority.
- Provider analytics DTOs now carry tenant, provider-user, and provider-profile identity. The frontend requires all three identities to match the current authenticated scope and current backend profile before rendering.
- Analytics DTOs, cards, caches, route state, and local state cannot create or replace provider or service identity.

## Provider Findings
- `GET /api/marketplace/me/stats` binds metrics to trusted tenant, authenticated provider user, and current provider profile.
- Missing, foreign-tenant, inactive-profile, inactive-tenant, suspended-provider, or banned-provider authority fails with 404/403 instead of returning success-shaped zero analytics.
- Profile refresh and profile refresh failure withhold analytics, preventing stale provider metrics from surviving provider or visibility changes.

## Metric Findings
- Services offered and completed requests must be non-negative safe integers from the backend.
- Provider completion counts remain bound to final `CompletedByBuyer` SQL completion truth.
- Reputation metrics reuse the certified fail-closed reputation summary boundary.
- Frontend code validates and suppresses metrics; it does not calculate replacement business metrics.

## KPI Findings
- Listing KPI identity remains nested under accepted backend service and provider-profile identity.
- Completion rate must equal completed requests divided by total requests, including exact zero-total behavior within numeric serialization tolerance.
- Contradictory cards are suppressed rather than repaired, recomputed for display, or accepted as independent truth.

## Visibility Findings
- Missing, inactive, foreign-tenant, deleted, hidden, or suspended authority cannot preserve provider analytics presentation.
- Provider stats require a current active profile response; listing analytics remain nested under current discovery visibility.

## Cache Findings
- Analytics keys are authenticated tenant/user scoped, with a dedicated `my-profile-stats` identity.
- Cached profile and stats payloads are withheld during current refresh and after failure.
- Actor and tenant transitions remove prior marketplace query scopes before replacement presentation.

## Realtime Findings
- SignalR and BroadcastChannel remain invalidate/refetch triggers only.
- Reconnect, focus, visibility, periodic refresh, and mutation settlement cannot write analytics DTOs or KPI truth into cache.

## Actor-Transition Findings
- Provider analytics identity must match the current `tenantId:userId` authority scope and current provider profile ID.
- Logout/login, actor replacement, tenant switching, and provider replacement cannot reuse prior analytics presentation.

## History Findings
- Browser history, route state, bookmarks, and deep links carry navigation only.
- Analytics surfaces re-enter current profile, actor, tenant, visibility, and backend metric authority.

## Failure Findings
- 401, 403, 404, 500, timeout, refresh failure, and malformed analytics DTOs withhold analytics.
- Retry is disabled at the query authority layer, so failed or obsolete analytics are not replayed as success.
- Missing-profile analytics no longer fabricate an all-zero success state.

## Adversarial Findings
- Analytics replay, KPI replay, cache replay, history replay, realtime replay, actor switching, tenant switching, provider switching, malformed DTO injection, foreign identity injection, and contradictory completion-rate injection were reviewed.
- None can make analytics authoritative, preserve obsolete provider metrics, override source truth, or cross tenant/user scope on inspected paths.

## Runtime Hardening Performed
- Added tenant, provider-user, and provider-profile identity to provider analytics responses.
- Changed missing, foreign-tenant, and inactive provider analytics to fail closed.
- Added a dedicated frontend analytics identity authority boundary.
- Added exact service KPI count/rate consistency validation.
- Added a dedicated scoped provider-stats query key.
- Withheld cached profile and analytics presentation during refresh and after refresh failure.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.ts`
- `PointOfSale.Web/src/marketplace/listingCardAuthority.ts`
- `PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx`
- `PointOfSale.Web/src/marketplace/queryKeys.ts`
- `PointOfSale.Web/src/marketplace/types/index.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/analyticsIdentityAuthority.test.ts`
- Updated `PointOfSale.Web/src/marketplace/listingCardAuthority.test.ts`.
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendAnalyticsIdentityAuthorityCertificationTests.cs`
- Updated listing-card and reputation architecture freezes for shared authority boundaries.

## Architecture Updates
- Added this F-S6-A findings document.
- Updated all five `.ai-context` authority files with the F-S6-A runtime rule and freeze.

## Explicit Answers
- Who owns analytics identity authority? Current backend request, completion, review, reputation, provider, tenant, actor, and visibility authorities own source truth; F-S6-A owns derivative frontend analytics presentation only.
- Can frontend fabricate analytics identity? No.
- Can KPIs become authority? No.
- Can analytics override source truth? No.
- Can stale analytics survive refresh? No on inspected paths; profile and stats are withheld during refresh and after failure.
- Can realtime fabricate analytics identity? No.
- Can history resurrect analytics truth? No.
- Can visibility loss preserve analytics? No.
- Can tenant leakage occur? No inspected cache, DTO, route, event, or presentation path crosses the authenticated tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Analytics Identity Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser/API chaos coverage for provider suspension during refresh, actor and tenant replacement, delayed cross-tab invalidation, malformed gateway payloads, and decimal serialization differences beyond the accepted KPI tolerance.
