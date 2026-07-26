# PASS F-S1-D Service Marketplace Frontend Trust & Reputation Presentation Authority

Service Marketplace Frontend Trust & Reputation Presentation Authority is production-certifiable for inspected runtime paths after review-count presentation hardening.

## Rating Findings

- Average rating and rating values derive from current backend SQL-composed reputation and review authority.
- The listing card and service detail adapters accept only finite ratings within valid bounds.
- The frontend does not recalculate averages from review rows and cannot inflate ratings through cache, history, or realtime payloads.

## Review Findings

- Review rows derive from the current backend review endpoint and are accepted only with valid review identity, buyer display name, date, and integer rating from 1 through 5.
- Cached reviews are withheld during current-authority refresh and after refresh failure.
- The review section count now uses backend trust summary `totalReviews`, not rendered review-row length.

## Distribution Findings

- Rating distributions derive from the backend trust summary.
- Each star bucket must be a non-negative integer and the bucket sum must equal backend total review authority before rendering.
- The frontend does not build histograms from review rows.

## Trust Badge Findings

- Trust badges are backend reputation values presented through a frontend allowlist.
- Unknown or malformed badges are suppressed and cannot be mapped, promoted, or fabricated by the browser.
- Badges are suppressed when the surrounding rating summary is invalid.

## Campaign Evidence Findings

- Campaign evidence is bounded per backend review DTO and is evidence presentation only.
- Valid campaign evidence requires campaign name, platform, valid period, known evidence type, and POS-verified order count when required.
- Campaign revenue remains intentionally absent because the evidence DTO does not carry authoritative revenue currency.

## Provider Reputation Findings

- Provider reputation rendering on cards and detail pages derives from backend provider/reputation DTOs.
- Provider display fields are presentation only and cannot replace reputation authority.
- Provider lifecycle loss remains handled by current backend visibility authority and removes trust presentation with the service.

## Cache Findings

- Detail, discovery, and review query keys are authenticated tenant/user scoped.
- Cached trust and reviews are withheld during current authority validation and after authority failures.
- Actor or tenant transitions remove prior marketplace cache scopes before replacement-route presentation.

## Realtime Findings

- Realtime, focus, visibility, reconnect, periodic, and BroadcastChannel paths invalidate/refetch only.
- No inspected frontend path writes rating, review, distribution, badge, campaign evidence, or provider reputation values from an event into cache.

## Lifecycle Findings

- Hidden, deleted, inactive, suspended-provider, or suspended-tenant services fail current backend discovery/detail authority.
- Lifecycle loss removes service detail and therefore removes trust, reviews, distributions, badges, campaign evidence, and provider reputation presentation.
- Trust cannot bypass listing visibility or provider lifecycle authority.

## History Findings

- Browser history, refresh, bookmarks, and deep links re-enter current backend detail and review authority.
- Route state carries no trust or review snapshot that can resurrect obsolete reputation.

## Tenant Isolation Findings

- Trust, review, detail, and discovery caches include authenticated tenant/user scope.
- Tenant scope is not accepted from route state, query parameters, DTO media references, local storage, or realtime payloads.
- Actor and tenant switching purge prior marketplace scope before replacement presentation.

## Failure-Handling Findings

- Detail authority failure removes the entire detail presentation.
- Authority-shaped review 401, 403, or 404 removes detail presentation; review refresh failure removes cached reviews.
- Malformed optional trust, review, distribution, badge, campaign evidence, and provider fields are suppressed without fallback authority.

## Adversarial Findings

- Rating replay, review replay, trust replay, badge replay, campaign replay, history replay, cache replay, realtime replay, actor switching, tenant switching, lifecycle loss, and projection lag were reviewed.
- None can create frontend reputation truth, inflate ratings, preserve deleted reviews as current counts, fabricate trust badges, override backend campaign truth, or leak cross-tenant reputation on inspected paths.

## Runtime Hardening Performed

- Changed the service detail review-count display to use backend trust summary authority instead of rendered review-row length.
- Added a frontend unit freeze for distribution/total mismatch suppression.
- Added an architecture regression freeze for trust/reputation presentation, cache, realtime, and documentation posture.

## Exact Runtime Files Modified

- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`
- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`

## Regression Freezes Added

- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendTrustReputationPresentationAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns trust and reputation authority? Current backend SQL reputation, review, rating, campaign evidence, and provider authorities own trust and reputation truth; the frontend owns bounded presentation only.
- Can ratings become authority? No.
- Can reviews survive deletion? No on inspected current-authority paths; refresh/failure withholding and backend authority decide visibility.
- Can trust badges be fabricated? No; only known backend badge values can render.
- Can campaign evidence override backend truth? No; it is bounded evidence presentation only.
- Can provider reputation drift? Not through inspected frontend presentation, cache, or history paths.
- Can stale trust survive refresh? No; cached detail/reviews are withheld while validating and after failure.
- Can lifecycle loss preserve reputation? No; lifecycle loss removes service detail and trust presentation.
- Can tenant leakage occur? No inspected cache, route, realtime, or DTO path crosses tenant/user scopes.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Trust & Reputation Presentation Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk

- Production should keep browser chaos coverage for review deletion/moderation, trust recomposition lag, actor/tenant switching during review refresh, lifecycle removal, refresh failures, direct deep links, and realtime/cross-tab replay.
