# PASS F-S5-D Service Marketplace Frontend Reputation Presentation Authority

Service Marketplace Frontend Reputation Presentation Authority is production-certifiable for inspected runtime paths after shared reputation-summary validation, aggregate-consistency hardening, provider-profile reputation binding, refresh withholding, and complete badge rendering.

## Reputation Findings
- Current backend S1-E SQL review/reputation authority plus current provider, visibility, lifecycle, actor, and tenant authority owns reputation truth.
- Cards, details, provider-profile statistics, review rows, distributions, and badges consume bounded backend DTOs through shared fail-closed presentation boundaries.
- Frontend presentation cannot calculate, repair, estimate, persist, replay, or promote reputation into authority.

## Average Findings
- Average ratings must be finite and within zero through five.
- Zero reviews requires an exact zero average; a positive review count requires an average from one through five.
- The frontend never recomputes a displayed average from review rows or distribution buckets.

## Count Findings
- Review counts must be non-negative safe integers supplied by backend authority.
- Service detail review headings and trust summaries use backend `totalReviews`, never rendered-row length.
- Provider profile statistics now receive backend `totalReviews`; missing, malformed, refreshing, or failed statistics are withheld instead of replaced with frontend zeroes.

## Distribution Findings
- Every one-through-five bucket must be a non-negative integer.
- Bucket totals must equal backend `totalReviews`, and the bucket-weighted two-decimal average must equal backend `averageRating`.
- Contradictory distributions are suppressed; the frontend does not normalize, redistribute, or repair buckets.

## Badge Findings
- Only `New`, `Rising`, `Trusted`, `TopRated`, and `Standard` can render.
- Badge identity must be coherent with the backend average/count pair under the current S1-E badge contract.
- Cards and details now render every accepted backend badge, including `New` and `Standard`; unknown, replayed, or contradictory badges fail closed.

## Visibility Findings
- Hidden/deleted services, inactive profiles, suspended providers, and tenant/provider lifecycle loss fail current backend discovery/detail/profile authority.
- Reputation is nested under accepted current service/profile presentation and cannot survive visibility loss.

## Cache Findings
- Reputation-bearing query keys remain authenticated tenant/user scoped.
- Current refresh withholds discovery, detail, review, and provider-profile statistics; failures cannot preserve cached reputation as current truth.
- Actor/tenant transitions remove prior marketplace scopes before replacement presentation.

## Realtime Findings
- SignalR, BroadcastChannel, reconnect, focus, visibility, periodic refresh, and rating settlement invalidate/refetch only.
- No inspected event path writes averages, counts, distributions, badges, reviews, or provider reputation into cache.

## History Findings
- Back, forward, refresh, bookmarks, and deep links carry navigation identity only.
- Re-entry reloads current backend listing, provider, review, reputation, visibility, actor, and tenant authority.
- Route state and browser storage contain no reputation snapshot capable of resurrection.

## Failure Findings
- 401, 403, and 404 authority loss remove protected presentation.
- 500, timeout, refresh failure, and malformed DTOs withhold affected reputation surfaces without substituting success-shaped zeroes.
- Review-list failure no longer relabels an authoritative nonzero review total as zero; the backend summary count remains visible while rows report unavailable.

## Adversarial Findings
- Aggregate replay, badge replay, summary replay, cache replay, history replay, realtime replay, actor switching, tenant switching, malformed DTO injection, impossible average/count pairs, and contradictory distributions were exercised.
- None can inflate ratings/counts, fabricate badges, repair aggregates, preserve obsolete reputation, or cross tenant/user scope on inspected paths.

## Runtime Hardening Performed
- Added a shared reputation presentation boundary for summary, distribution, badge, and provider-stat validation.
- Extended provider-profile stats with backend `totalReviews` and computed `trustBadge`.
- Removed profile-stat fallback zero fabrication and withheld statistics during refresh/failure.
- Bound cards and details to the shared reputation boundary.
- Required distribution totals and weighted averages to match backend summary authority.
- Rendered accepted `New` and `Standard` badges and preserved authoritative review counts during review-row failure.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/reputationPresentationAuthority.ts`
- `PointOfSale.Web/src/marketplace/listingCardAuthority.ts`
- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/components/ServiceCard.tsx`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx`
- `PointOfSale.Web/src/i18n/locales/en.json`
- `PointOfSale.Web/src/i18n/locales/ar.json`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/reputationPresentationAuthority.test.ts`
- Updated `PointOfSale.Web/src/marketplace/listingCardAuthority.test.ts`.
- Updated `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`.
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendReputationPresentationAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendTrustReputationPresentationAuthorityCertificationTests.cs`.

## Architecture Updates
- Added this F-S5-D findings document.
- Updated all five `.ai-context` authority files with the F-S5-D runtime rule and freeze.

## Explicit Answers
- Who owns reputation authority? Current backend S1-E review/reputation plus provider, visibility, lifecycle, actor, and tenant authorities own truth; frontend F-S5-D owns derivative presentation only.
- Can frontend fabricate reputation? No.
- Can averages drift? Contradictory or malformed averages fail closed; displayed averages remain backend values.
- Can counts drift? Contradictory or malformed counts fail closed; rendered-row counts never replace backend totals.
- Can distributions contradict totals? They may arrive malformed, but they cannot render unless total and weighted average agree with backend summary authority.
- Can badges be fabricated? No; unknown or summary-inconsistent badges are suppressed.
- Can reputation survive visibility loss? No.
- Can realtime fabricate reputation? No.
- Can history resurrect reputation? No.
- Can tenant leakage occur? No inspected query, cache, event, route, or callback path crosses authenticated scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Reputation Presentation Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser/PostgreSQL chaos coverage for review creation visibility lag, moderation/deletion policy changes, provider suspension during refresh, actor/tenant replacement, timeout/reconnect races, cross-tab invalidation latency, and malformed gateway payloads.
