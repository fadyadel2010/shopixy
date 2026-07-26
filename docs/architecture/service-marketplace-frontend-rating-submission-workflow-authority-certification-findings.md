# PASS F-S5-B Service Marketplace Frontend Rating Submission Workflow Authority

Service Marketplace Frontend Rating Submission Workflow Authority is production-certifiable for inspected runtime paths after fresh-request payload binding, exact mutation-payload ownership, failure propagation, modal identity reset, and synchronous dialog submission fencing.

## Workflow Findings
- Rating Submission Workflow Authority owns derivative review form, dialog, submission, success, and failure presentation only.
- Current backend S1-E rating/review, S1-D completion, request, actor, tenant, and visibility authorities own all business truth.
- Workflow identity is the authenticated tenant/user scope plus the backend request identity; local form, route, history, cache, and mutation state never become review authority.

## Submission Findings
- Star values are restricted to integer ratings from 1 through 5.
- Comment intent is bounded to the backend 5,000-character contract.
- Marketing evidence is required or forbidden from the freshly revalidated backend request category, not the list row or modal snapshot.
- Campaign name, platform, timestamps, and chronology are structurally validated before the exact accepted payload is sent.
- Backend review authority still independently revalidates buyer ownership, final completion, category evidence, uniqueness, and tenant scope.

## Mutation Findings
- Every inspected review mutation routes through `executeServiceRequestRatingWorkflow`.
- Mutation execution requires fresh request details and accepted F-S4-D completion plus F-S5-A eligibility before the API call.
- The API receives the exact payload returned by the fresh-request submission boundary.
- Mutations use no automatic retry, offline replay, optimistic write, or cache patch.
- Same-runtime duplicate execution is synchronously fenced by actor scope and request identity.

## Success Findings
- HTTP completion alone is not success authority.
- The creation response must contain valid backend-issued review, request, service, reviewer, provider, rating, and timestamp identity.
- Success additionally requires a fresh backend request proving the same review ID now exists and rating eligibility is revoked.
- Dialog closure occurs only after the workflow promise fulfills; stale or failed mutation callbacks cannot manufacture success.

## Failure Findings
- Submission errors now reject back into the modal instead of being swallowed by `MyRequestsTab`.
- Failed authoritative submission keeps review intent open and displays failure without closing as though the review succeeded.
- Timeout, network failure, 409 conflict, malformed response, and failed post-submit proof trigger backend revalidation and remain failure or ambiguity, never inferred success.
- Timeout-after-commit can withdraw the stale form after backend refresh proves an existing review, but the frontend does not claim which payload committed without a valid creation response.

## Cache Findings
- Rating-bearing queries remain authenticated tenant/user/request scoped.
- Current refresh withholds request and rating presentation; failed authority reads cannot preserve review controls.
- Settlement invalidates the complete initiating marketplace scope without writing review or eligibility truth into cache.
- Browser storage contains no review workflow, payload, success, or failure authority.

## Realtime Findings
- BroadcastChannel and SignalR paths remain invalidate/refetch triggers only.
- Cross-tab events must match the current authenticated authority scope.
- Realtime payloads cannot create reviews, close dialogs as success, grant eligibility, or publish review identity.
- Reconnect, focus, visibility, and periodic refresh converge only through backend authority.

## Actor-Transition Findings
- Scope is checked before preflight, before mutation, after mutation, and after settled authority refresh.
- Actor or tenant replacement rejects stale workflow callbacks and removes the prior marketplace query scope.
- The marketplace boundary remounts children by scope, so local dialog and form state cannot cross actors or tenants.
- Request identity changes remount the review modal and cannot preserve prior form intent as current workflow truth.

## History Findings
- Back, forward, refresh, bookmarks, and deep links carry navigation identity only.
- No rating form, mutation result, review ID, eligibility, success, or failure state is persisted in route state or browser storage.
- Re-entry reloads current request, completion, actor, visibility, and rating authority.
- History cannot resurrect an obsolete review action or successful workflow.

## Concurrency Findings
- Dialog submission uses a synchronous ref fence before React pending state updates.
- Workflow execution uses a synchronous actor-scope/request fence across all same-runtime callers.
- Cross-tab and cross-device attempts may both reach the backend, but S1-E SQL uniqueness permits only one review per service request.
- Refresh, request switching, actor switching, and timeout races cannot overwrite current review truth because no frontend review cache write exists.

## Adversarial Findings
- Mutation replay, eligibility replay, completion replay, cache replay, history replay, realtime replay, malformed response injection, actor switching, tenant switching, request switching, timeout-after-commit, double click, and concurrent-tab submission were reviewed.
- Replayed eligibility still requires fresh current request, completion, buyer, participant, and duplicate-state validation.
- Malformed creation responses cannot become success, and stale callbacks cannot paint a replacement actor or tenant.
- No inspected frontend path can authorize a provider, foreign actor, incomplete request, duplicate review, or cross-tenant review.

## Runtime Hardening Performed
- Added `deriveServiceReviewSubmissionAuthority` and bound submission intent to fresh backend request identity and category.
- Changed mutation orchestration to send only the exact validated payload.
- Strengthened creation-response timestamp validation.
- Stopped `MyRequestsTab` from swallowing rating failures and removed premature success-driven dialog closure.
- Keyed the dialog by request identity and added synchronous modal submission fencing.
- Added timeout-after-commit, payload, malformed response, and concurrent submission regression coverage.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/ratingWorkflow.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/components/ReviewModal.tsx`

## Regression Freezes Added
- Updated `PointOfSale.Web/src/marketplace/ratingWorkflow.test.ts`.
- Added `PointOfSale.Web/src/marketplace/components/ReviewModal.test.tsx`.
- Added `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRatingSubmissionWorkflowAuthorityCertificationTests.cs`.

## Architecture Updates
- Added this F-S5-B certification findings document.
- Updated all five `.ai-context` authority files with the F-S5-B runtime rule and freeze.

## Explicit Answers
- Who owns rating submission workflow authority? The frontend owns derivative workflow presentation; current backend rating, completion, request, actor, tenant, and visibility authorities own truth.
- Can frontend fabricate review success? No. Valid creation identity plus fresh backend duplicate-state proof is required.
- Can duplicate submissions occur? Same-runtime duplicate execution is fenced; cross-tab/device attempts converge to one SQL review through S1-E uniqueness.
- Can timeout ambiguity create false success? No. It remains failure/ambiguity and triggers backend convergence without inferred success.
- Can stale workflow survive actor transitions? No.
- Can realtime fabricate review outcomes? No.
- Can history resurrect workflow truth? No.
- Can unauthorized reviews occur? No on inspected paths; backend authority independently enforces buyer, completion, request, category, and tenant rules.
- Can tenant leakage occur? No inspected workflow, cache, callback, realtime, or route path crosses authenticated scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Rating Submission Workflow Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for simultaneous tabs/devices, timeout-after-commit, actor replacement during mutation, cross-tab invalidation latency, and tenant switching during review submission.
