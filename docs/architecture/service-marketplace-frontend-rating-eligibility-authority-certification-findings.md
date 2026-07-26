# PASS F-S5-A Service Marketplace Frontend Rating Eligibility Authority

Service Marketplace Frontend Rating Eligibility Authority is production-certifiable for inspected runtime paths after POS review-route restoration, existing-review capability identity, buyer-bound eligibility validation, and scoped rating workflow revalidation.

## Eligibility Findings
- Rating eligibility is derivative presentation only. Current backend S1-E rating, S1-D completion, request, actor, and visibility authorities own the facts.
- `deriveServiceRequestRatingEligibility` requires valid request, service, buyer, provider, and current actor identities; distinct participants; final `CompletedByBuyer`; explicit backend `canReview`; and coherent existing-review identity.
- Missing, malformed, contradictory, foreign-actor, provider-actor, stale, or replayed capability DTOs fail closed.

## Buyer Findings
- Only the current SQL buyer may see a rating action.
- Buyers cannot rate before final buyer-confirmed completion or rate an incomplete, cancelled, rejected, provider-completed, malformed, or foreign request.
- Already-rated requests carry backend `existingReviewId` and expose no second rating action.

## Provider Findings
- Providers have no service-rating capability in the S1-E model and cannot receive a frontend rating action as the current actor.
- Provider identity equal to buyer identity fails closed, preventing self-rating presentation.
- A malformed DTO granting `canReview` to a provider is rejected by the actor-bound boundary.

## Completion Findings
- Rating requires the F-S4-D completion-state boundary to accept canonical final completion and coherent completion evidence.
- Completion loss or lifecycle contradiction suppresses rating eligibility even if a stale capability remains true.
- Rating does not mutate or infer completion authority.

## Visibility Findings
- `MyRequestsTab` and request-detail view models consume the dedicated rating boundary rather than generic lifecycle `canReview`.
- The review modal remains mounted only while the current accepted row retains rating eligibility.
- Rating buttons, prompts, and modal entry cannot become mutation authority.

## Cache Findings
- Rating-bearing list/detail keys remain authenticated tenant/user scoped with zero stale time, no query retries, mount/reconnect/focus refresh, periodic revalidation, and refresh withholding.
- Rating workflows perform no optimistic writes, cache patches, browser persistence, or mutation retries.
- Existing-review identity comes from current backend DTOs, not frontend submission state.

## Realtime Findings
- SignalR and BroadcastChannel remain scoped invalidation triggers only.
- Rating settlement publishes scoped invalidation and refreshes the complete marketplace authority scope.
- Realtime payloads cannot create, revoke, or overwrite rating capability or review identity.

## Actor-Transition Findings
- Rating workflow scope is checked before preflight, before mutation, after mutation, and after settlement revalidation.
- Current actor must equal the backend buyer throughout the workflow.
- Logout/login, actor switching, tenant switching, request switching, and route teardown remove or remount prior scoped authority.

## History Findings
- Back, forward, refresh, bookmarks, and deep links carry navigation identity only.
- Re-entry reloads current actor, request, completion, rating capability, and existing-review authority.
- Route state and browser storage contain no rating eligibility, review success, or duplicate-eligibility authority.

## Failure Findings
- 401, 403, 404, 409, 429, 500, timeout, malformed DTO, malformed creation response, stale capability, and failed revalidation fail closed.
- A failure triggers backend revalidation/invalidation but cannot preserve a rating action as success.
- Success requires a backend-issued review response bound to request, service, buyer, provider, rating value, and timestamp, followed by backend proof that the same review ID revoked eligibility.

## Adversarial Findings
- Capability replay, cache replay, history replay, realtime replay, actor switching, tenant switching, completion replay, DTO replay, deep-link replay, duplicate submission, and malformed response injection were reviewed.
- Same-runtime duplicate submissions are synchronously fenced; backend SQL uniqueness remains final duplicate-rating authority across tabs/devices.
- No inspected frontend path can rate as provider, rate before completion, rate twice, preserve stale eligibility, bypass S1-E, or cross tenant/user scope.

## Runtime Hardening Performed
- Added a dedicated authenticated POS endpoint at `/api/marketplace/service-reviews`; the CommerceHub customer-review route remains separate.
- Added backend `existingReviewId` capability identity to buyer request-list and request-detail DTOs.
- Added `deriveServiceRequestRatingEligibility` for final-completion, buyer, participant, capability, and duplicate-state validation.
- Added `executeServiceRequestRatingWorkflow` with fresh preflight, scope fencing, response validation, duplicate fencing, and post-submit eligibility revocation proof.
- Routed `MyRequestsTab`, `useRequestDetailsVM`, and reusable review mutation hooks through the rating authority boundaries.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/ratingEligibility.ts`
- `PointOfSale.Web/src/marketplace/ratingWorkflow.ts`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/ratingEligibility.test.ts`
- `PointOfSale.Web/src/marketplace/ratingWorkflow.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRatingEligibilityAuthorityCertificationTests.cs`

## Architecture Updates
- Added this F-S5-A certification findings document.
- Updated all five `.ai-context` authority files.

## Explicit Answers
- Who owns rating eligibility authority? Current backend S1-E rating, S1-D completion, request, actor, and visibility authorities own truth; frontend F-S5-A owns derivative presentation only.
- Can frontend grant rating eligibility? No. It may validate, suppress, submit intent, and refetch only.
- Can buyers rate before completion? No. Final `CompletedByBuyer` authority and coherent completion evidence are required.
- Can providers rate themselves? No. Providers receive no rating capability, and buyer/provider identity equality fails closed.
- Can duplicate ratings occur? No on inspected authority paths. Frontend duplicate execution is fenced and SQL enforces one review per request.
- Can stale eligibility survive refresh? No. Refresh withholding and current backend revalidation revoke stale actions.
- Can completion loss preserve rating actions? No. Rating requires the accepted current completion-state boundary.
- Can realtime expose invalid rating controls? No. Realtime invalidates/refetches only.
- Can history resurrect obsolete rating eligibility? No. Navigation re-enters current scoped backend authority.
- Can tenant leakage occur? No inspected query, route, cache, mutation, callback, or invalidation path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Rating Eligibility Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for simultaneous rating tabs/devices, timeout-after-commit, actor replacement during submission, completion revocation races, reconnect latency, and tenant switching during rating workflows.
