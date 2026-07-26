# PASS F-S6-B Service Marketplace Frontend KPI & Metrics Presentation Authority

Service Marketplace Frontend KPI & Metrics Presentation Authority is production-certifiable for inspected runtime paths after backend-owned rating-percentage presentation and review-history failure-count hardening.

## Metric Findings
- Current backend analytics, request, completion, review, reputation, provider, tenant, actor, and visibility authorities own metric truth.
- Frontend DTO boundaries validate identity, numeric bounds, and relationships, then render accepted backend values without creating replacement metrics.
- Metric DTOs, view models, cards, badges, route state, local state, and caches remain derivative.

## Count Findings
- Service, request, completion, and review counts require non-negative safe integers.
- Completed requests cannot exceed total requests.
- Provider completed counts remain final `CompletedByBuyer` SQL truth.
- Review-history failure no longer substitutes a displayed zero count.

## Rate Findings
- Service completion rate remains a backend value and must match backend total/completed counts exactly within serialization tolerance.
- Rating-distribution percentages are now composed by the backend and returned with the distribution DTO.
- Frontend rendering consumes backend percentages directly and suppresses missing, malformed, out-of-range, or count-inconsistent percentages.

## Consistency Findings
- Completion counts and completion rate must agree.
- Reputation average, review total, badge, distribution counts, weighted average, and distribution percentages must agree.
- Contradictory KPI sets are suppressed as a unit; the frontend does not repair or choose a preferred conflicting value.

## Badge Findings
- Reputation badges remain backend-issued and render only after average/count coherence validation.
- Unknown or contradictory badges fail closed and cannot become independent performance authority.

## Visibility Findings
- Provider analytics require current active tenant, provider profile, actor, and governance visibility.
- Service KPIs remain nested under current listing/detail visibility.
- Suspension, ban, deletion, hiding, tenant loss, profile loss, or actor loss withholds KPI presentation.

## Cache Findings
- Marketplace KPI queries are authenticated tenant/user scoped.
- Cached KPI presentation is withheld during current refresh and after refresh failure.
- Actor and tenant transitions remove prior marketplace scopes; cached metrics cannot become authority.

## Realtime Findings
- SignalR, BroadcastChannel, reconnect, focus, visibility, periodic refresh, and mutation settlement invalidate/refetch only.
- No inspected realtime path writes counts, rates, percentages, summaries, badges, or KPI composites into cache.

## History Findings
- Back, forward, refresh, bookmarks, and deep links carry navigation identity only.
- KPI surfaces re-enter current backend actor, tenant, visibility, listing, provider, review, reputation, request, completion, and analytics authority.
- Route state and browser storage cannot restore KPI snapshots.

## Failure Findings
- 401, 403, 404, 500, timeout, malformed DTO, and refresh failure paths withhold affected KPI presentation.
- Queries do not retry obsolete analytics as success.
- Review-history failure renders an unavailable state without fabricating a zero total.

## Adversarial Findings
- Metric replay, KPI replay, cache replay, history replay, realtime replay, actor switching, tenant switching, provider switching, malformed DTO injection, contradictory counts/rates, contradictory reputation aggregates, and forged distribution percentages were reviewed.
- None can inflate, suppress, repair, preserve, resurrect, or cross-scope leak KPI truth on inspected paths.

## Runtime Hardening Performed
- Added backend-owned two-decimal rating-distribution percentages.
- Extended the service detail DTO and frontend authority boundary with percentage validation.
- Removed frontend distribution-bar percentage calculation.
- Removed review-history failure fallback to a displayed zero count.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

## Regression Freezes Added
- Updated `PointOfSale.Web/src/marketplace/reputationPresentationAuthority.test.ts`.
- Updated `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`.
- Added `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendKpiMetricsPresentationAuthorityCertificationTests.cs`.

## Architecture Updates
- Added this F-S6-B findings document.
- Updated all five `.ai-context` authority files with the F-S6-B runtime rule and freeze.

## Explicit Answers
- Who owns KPI authority? Current backend analytics, request, completion, review, reputation, provider, tenant, actor, and visibility authorities own source truth; frontend F-S6-B owns derivative presentation only.
- Can frontend fabricate KPIs? No.
- Can counts drift? Malformed or contradictory counts fail closed and cannot render as current authority.
- Can percentages contradict counts? They may arrive malformed, but they cannot render unless they match backend counts and totals.
- Can KPIs contradict each other? Contradictory KPI sets are suppressed rather than repaired or partially presented.
- Can summaries become authority? No.
- Can visibility loss preserve KPIs? No.
- Can realtime fabricate KPIs? No.
- Can history resurrect KPI truth? No.
- Can tenant leakage occur? No inspected DTO, cache, query key, event, route, or callback path crosses authenticated scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend KPI & Metrics Presentation Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser/API chaos coverage for suspension during refresh, actor and tenant replacement, delayed cross-tab invalidation, malformed gateway payloads, decimal serialization boundaries, and review creation visibility lag.
