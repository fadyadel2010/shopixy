# PASS F-S5-C Service Marketplace Frontend Rating State Authority

Service Marketplace Frontend Rating State Authority is production-certifiable for inspected runtime paths after shared rated/unrated state derivation, review-identity containment, and cross-surface presentation hardening.

## Review-Identity Findings
- PostgreSQL S1-E review authority owns review identity, request association, reviewer ownership, provider ownership, and durability.
- Frontend state accepts `existingReviewId` only as a valid backend GUID paired with the same current buyer-owned, final-completed request.
- Review identity remains internal proof. The badge does not emit it into DOM attributes, routes, history, or browser storage.

## Rated/Unrated Findings
- `deriveServiceRequestRatingStatePresentation` defines the only accepted states.
- Unrated requires final `CompletedByBuyer`, current buyer ownership, `canReview=true`, lifecycle agreement, and no review ID.
- Rated requires final `CompletedByBuyer`, current buyer ownership, `canReview=false`, lifecycle agreement, and a valid backend review ID.
- Missing, malformed, or contradictory combinations suppress the whole rating-state presentation.

## Visibility Findings
- Request-linked review identity is visible only to the current buyer because the backend supplies it only on buyer-authorized list/detail reads.
- Provider and foreign-actor calls to the frontend boundary fail closed, including Incoming Requests.
- Public review summaries remain governed separately by current service visibility and S1-E review reads.

## Action-Consistency Findings
- The review action and the rating-state badge derive from the same boundary.
- Unrated may expose the review action; rated never does.
- The modal is withdrawn when current refreshed authority no longer proves an unrated actionable request.

## Cache Findings
- Rating-bearing React Query keys remain authenticated tenant/user/request scoped.
- Lists and details are withheld while current authority refreshes and fail closed after authority errors.
- No rating workflow uses optimistic review writes, cache patches, browser persistence, or mutation retry.

## Realtime Findings
- SignalR and BroadcastChannel remain scoped invalidation/refetch triggers only.
- Duplicate, stale, replayed, malformed, reconnect, and foreign-scope events cannot write rated/unrated state or review identity.
- Reconnect, focus, visibility, periodic refresh, and mutation settlement converge through backend request/review authority.

## Actor-Transition Findings
- Rating state requires current actor identity to equal backend buyer identity.
- Logout/login, actor switching, tenant switching, request switching, and authority-boundary teardown remove prior scoped caches and local presentation.
- Provider and foreign actors cannot inherit buyer-visible review state.

## History Findings
- Back, forward, refresh, bookmarks, and deep links carry request identity only.
- Route re-entry reloads current request, completion, actor, visibility, capability, and review identity authority.
- History and route state cannot preserve or resurrect a rated/unrated result.

## Failure Findings
- 401, 403, 404, 409, 500, timeout, malformed DTO, invalid review ID, missing boolean capability, refresh failure, and failed post-submit proof fail closed.
- Failures cannot preserve a stale review action or manufacture a rated badge.
- Timeout-after-commit converges only when a later backend read proves the existing review state.

## Concurrency Findings
- Same-runtime submission remains synchronously fenced.
- Cross-tab/device creation races converge through S1-E SQL uniqueness.
- Refresh, mutation, reconnect, request-switch, actor-switch, and tenant-switch races cannot overwrite current truth because no frontend rating-state cache write exists.

## Adversarial Findings
- Review replay, mutation replay, cache replay, history replay, realtime replay, actor switching, tenant switching, request switching, malformed DTO injection, and contradictory capability injection were reviewed.
- No inspected frontend path can fabricate review identity, drift ownership, display both rated and unrated, retain an obsolete action, or cross tenant/user scope.

## Runtime Hardening Performed
- Added `deriveServiceRequestRatingStatePresentation` as the shared fail-closed rated/unrated authority boundary.
- Routed F-S5-A eligibility through the shared state boundary.
- Added a reusable rating-state badge that displays state without exposing review identity.
- Wired My Requests, RequestCard, Chat/request details, the request-details view model, and Incoming Requests visibility suppression through the boundary.
- Added localized rated/unrated labels.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/ratingStateAuthority.ts`
- `PointOfSale.Web/src/marketplace/ratingEligibility.ts`
- `PointOfSale.Web/src/marketplace/components/RatingStateBadge.tsx`
- `PointOfSale.Web/src/marketplace/components/RequestCard.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/ratingStateAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRatingStateAuthorityCertificationTests.cs`

## Architecture Updates
- Added this F-S5-C certification findings document.
- Updated all five `.ai-context` authority files with the F-S5-C runtime rule and freeze.

## Explicit Answers
- Who owns rating state authority? Current backend S1-E review, S1-D completion, request, actor, tenant, and visibility authorities own truth; frontend F-S5-C owns derivative presentation only.
- Can frontend fabricate review state? No.
- Can review ownership drift? No on inspected paths; current buyer identity and backend request lineage are required.
- Can contradictory rated/unrated states appear? No; contradictory DTOs suppress the whole state.
- Can stale review state survive refresh? No.
- Can realtime fabricate review state? No.
- Can history resurrect obsolete review state? No.
- Can actions contradict review state? No; action and badge share one boundary.
- Can tenant leakage occur? No inspected cache, route, callback, realtime, or presentation path crosses authenticated scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Rating State Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for simultaneous rating tabs/devices, timeout-after-commit, actor/tenant replacement, reconnect latency, request switching, and cross-tab invalidation delay.
