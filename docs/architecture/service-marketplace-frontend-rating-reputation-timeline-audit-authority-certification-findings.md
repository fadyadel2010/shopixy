# PASS F-S5-E Service Marketplace Frontend Rating & Reputation Timeline / Audit Authority

Service Marketplace Frontend Rating & Reputation Timeline / Audit Authority is production-certifiable for inspected runtime paths after deterministic backend review ordering, backend review-history envelope composition, collection-level frontend audit validation, attribution fail-closed handling, and service-history/provider-reputation count separation.

## Audit Findings
- PostgreSQL S1-E `ServiceReviews` joined to final `CompletedByBuyer` request lineage owns review identity, ownership, attribution, timestamps, and history.
- The backend review-history envelope now carries current service, provider-profile, provider-user, review, request, and reviewer identities.
- The frontend validates and presents the complete envelope or withholds it; it does not create audit IDs or promote DTO/cache state into authority.

## Chronology Findings
- Backend history is ordered by `CreatedAt DESC, Id DESC`, giving equal-time rows deterministic order across refreshes.
- Frontend chronology accepts strict UTC timestamps and exact backend order only.
- It does not sort, repair, merge, infer, or reconstruct chronology.
- Service reviews are immutable; `updatedAt` is backend-composed and must equal `createdAt`.

## Attribution Findings
- Reviewer attribution derives from the review's SQL `ReviewerUserId`, which S1-E binds to the request buyer.
- Provider attribution must match the current visible service profile's provider user.
- Missing reviewer records or names make history unavailable; the backend no longer fabricates an `Anonymous` attribution fallback.

## Presentation Findings
- Duplicate review IDs, duplicate request IDs, malformed identities, foreign service/provider rows, invalid ratings, malformed timestamps, count omissions, and reordered histories suppress the complete history.
- The frontend derives buyer initials only as formatting from accepted backend buyer names.
- Marketing evidence remains optional derivative metadata and cannot create review or financial authority.

## Consistency Findings
- Service review history exposes its own backend `totalReviews`, which must exactly equal the returned service-history rows.
- Provider reputation totals/distributions remain separate S1-E provider-wide authority.
- The service review heading now uses the service-history total; provider trust cards and distributions continue using provider reputation totals, removing the prior apparent contradiction.
- Deleted, hidden, invalid-lineage, or non-final reviews are excluded by backend S1-E reads and cannot remain represented in current history or reputation.

## Realtime Findings
- SignalR and BroadcastChannel carry scoped invalidation only.
- Replayed, duplicated, malformed, reconnect, and late events cannot append, reorder, or create review rows.
- Reconnect, focus, visibility, periodic refresh, and rating settlement converge through current backend review/reputation reads.

## Cache Findings
- Review and reputation queries remain authenticated tenant/user/service scoped with zero stale time and current refresh revalidation.
- Actor/tenant transitions remove prior marketplace scopes.
- Malformed or failed history refreshes withhold rows; no cache sorting, deduplication, patching, or optimistic review insertion exists.

## Actor-Transition Findings
- Marketplace authority scope binds caches and invalidations to the current tenant and user.
- Logout/login, actor switching, tenant switching, provider switching through route identity, and boundary teardown remove or withhold prior review history.
- Late callbacks cannot publish history into a replacement scope because review reads are query-derived and no local history write exists.

## History Findings
- Refresh, bookmarks, back, forward, and deep links carry service identity only.
- Route entry revalidates current service visibility, provider identity, review history, reputation, actor, and tenant authority.
- Route state and browser storage contain no review-history snapshot or chronology authority.

## Failure Findings
- 401, 403, and 404 visibility failures remove the detail authority surface.
- 500, timeout, attribution loss, malformed envelopes, malformed rows, count mismatch, timestamp mutation, and ordering mismatch withhold review history.
- Retries and later refreshes may recover only by returning a new complete backend-authoritative history; stale rows are not preserved as current truth.

## Adversarial Findings
- Review replay, chronology replay, cache replay, history replay, realtime replay, actor switching, tenant switching, provider switching, malformed DTO injection, duplicate IDs, omitted rows, attribution drift, timestamp drift, and equal-time reordering were exercised.
- Review deletion/moderation effects remain backend-owned. The current S1-E runtime makes reviews immutable and excludes deleted/invalid-lineage rows; any future moderation model must re-enter this boundary.
- No inspected frontend path can fabricate, append, reorder, deduplicate, repair, resurrect, or cross-scope leak review history.

## Runtime Hardening Performed
- Added deterministic backend `CreatedAt DESC, Id DESC` review ordering.
- Replaced the raw review array with a backend service/provider-bound review-history envelope.
- Added request, service, reviewer, provider, created, and updated audit metadata to review rows.
- Removed fabricated anonymous reviewer attribution and fail closed when attribution authority is unavailable.
- Added `deriveServiceReviewHistoryPresentation` for envelope identity, count, duplicate, lineage, immutable timestamp, and chronology validation.
- Changed service review rendering to accept the complete history or none.
- Separated service-history totals from provider-wide reputation totals in presentation.

## Exact Runtime Files Modified
- `PointOfSale.Infrastructure/Services/ServiceReviewService.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts`
- `PointOfSale.Web/src/marketplace/ratingTimelineAuthority.ts`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/ratingTimelineAuthority.test.ts`
- Updated `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`.
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRatingReputationTimelineAuditAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendServiceDetailAuthorityCertificationTests.cs`.

## Architecture Updates
- Added this F-S5-E findings document.
- Updated all five `.ai-context` authority files with the F-S5-E runtime truth and freeze.

## Explicit Answers
- Who owns rating/reputation timeline authority? Current backend S1-E review/reputation plus provider, lifecycle, actor, tenant, and visibility authorities own truth; frontend F-S5-E owns derivative validation and presentation only.
- Can frontend fabricate review history? No.
- Can chronology reorder? Not on inspected paths; backend tie ordering is deterministic and frontend rejects reordered DTOs.
- Can attribution drift? Not into presentation; foreign or missing attribution fails closed.
- Can review history contradict reputation summaries? Service-history and provider-reputation totals are now explicitly separate; each is backend-derived and internally validated.
- Can stale review history survive refresh? No.
- Can realtime fabricate review history? No.
- Can history resurrect obsolete review truth? No.
- Can tenant leakage occur? No inspected cache, route, invalidation, DTO, or callback path crosses current tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Rating & Reputation Timeline / Audit Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL/browser chaos coverage for simultaneous review creation, timeout-after-commit, provider visibility loss, future moderation/deletion policy changes, equal-timestamp rows, actor/tenant replacement, reconnect latency, and cross-tab invalidation delay.
