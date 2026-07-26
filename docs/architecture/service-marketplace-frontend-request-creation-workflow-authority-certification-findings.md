# PASS F-S2-A Service Marketplace Frontend Request Creation Workflow Authority

Service Marketplace Frontend Request Creation Workflow Authority is production-certifiable for inspected runtime paths after created-request response validation hardening.

## Submission Findings
- Submission remains gated by backend-composed `requestEntry.canRequest`, current service detail authority, and local synchronous double-submit fencing.
- Form state is component-local presentation only, reset on actor/service/eligibility changes, trimmed before submission, and bounded to the backend request-message limit.
- Submission cannot occur as frontend authority; backend S1-B request authority rechecks tenant, actor, listing, provider, lifecycle, self-service, and duplicate state before SQL creation.

## Success Findings
- Success presentation now requires a backend-created request response with valid request identity, the submitted service identity, and initial `Pending` status.
- Malformed, cross-service, stale, or non-pending success-shaped responses fail closed and do not navigate as workflow completion.
- Frontend success state is not persisted in route state, browser storage, React Query request caches, or history.

## Failure Findings
- 400, 401, 403, 404, 409, 500, timeout, malformed-success, and network failures remain failure presentation only and cannot become success.
- Failure alerts are fenced to the initiating actor scope, mounted route, and service ID so stale failures cannot affect replacement actors or routes.
- Settlement invalidation refetches current backend authority after failures and ambiguous outcomes.

## Mutation Findings
- The request mutation has no optimistic success, no `onMutate`, no cache insertion, no request identity creation, and no retry.
- `networkMode: 'always'` prevents offline-paused mutation replay after reconnect.
- Mutation completion validates backend-created request identity before success navigation and invalidates only the initiating actor scope.

## Navigation Findings
- Post-create navigation occurs only after validated backend-created request authority and only if the page is still mounted under the initiating actor scope and service route.
- Navigation carries no route-state success payload and therefore cannot fabricate workflow completion through browser history.
- Back, forward, refresh, direct links, and bookmarks re-enter current backend detail/request/list authority.

## Cache Findings
- React Query keys remain authenticated tenant/user scoped; prior marketplace scopes are removed before replacement-route presentation.
- The mutation does not call `setQueryData` and never writes created request truth into cache.
- Detail, request, and profile caches may refetch or invalidate only; cache cannot become request creation authority.

## Realtime Findings
- BroadcastChannel, focus, visibility, reconnect, and periodic route revalidation invalidate/refetch only.
- Cross-tab messages carry no request identity, success, actor, tenant, or eligibility authority.
- Realtime cannot create workflow success or bypass backend request creation authority.

## Actor-Transition Findings
- Actor and tenant scope are captured at submit time and rechecked before success or failure presentation.
- Stale completions after logout/login, actor switching, tenant switching, or route replacement cannot navigate or alert the replacement workflow.
- Prior actor caches are purged by the marketplace authority boundary.

## History Findings
- Request form text, open state, and success state are not stored in route state or browser storage.
- Browser history can replay only the service detail route or marketplace tabs, both of which reload current backend authority.
- History cannot resurrect request creation success.

## Offline Findings
- Offline submission produces a network failure or timeout presentation; it is not queued for later replay by React Query.
- Reconnect triggers current-authority refetch only and cannot replay request creation.
- Ambiguous timeout outcomes converge through backend request-entry/list authority rather than frontend success fabrication.

## Recovery Findings
- Recovery after failure, timeout, refresh, or actor transition requires a fresh backend authority response.
- Local workflow recovery clears request state on scope/service/eligibility loss and cannot restore stale success.
- Duplicate or already-created outcomes converge through backend active-request authority and SQL uniqueness.

## Concurrency Findings
- Same-tab double submit is synchronously fenced before React mutation state updates.
- Cross-tab submit races may issue multiple HTTP intents, but serializable SQL creation and `UX_ServiceRequests_Active_Service_Buyer` prevent duplicate active request authority.
- Stale mutation completions cannot corrupt the current actor, tenant, or service workflow.

## Adversarial Findings
- Mutation replay, success replay, failure replay, history replay, route-state replay, offline replay, cross-tab replay, actor switching, tenant switching, refresh races, and realtime replay were reviewed.
- None can fabricate request creation, preserve stale success, create duplicate workflow authority, leak across tenants, or bypass backend request authority on inspected runtime paths.

## Runtime Hardening Performed
- Added `deriveCreatedServiceRequestAuthority` to validate backend-created request identity before success presentation.
- Changed request creation mutation completion to reject malformed, cross-service, or non-pending success-shaped responses.
- Added an explicit request-message runtime length boundary before mutation submission.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/requestCreationAuthority.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/hooks/useRequestService.ts`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/requestCreationAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRequestCreationWorkflowAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns request creation authority? PostgreSQL `ServiceRequests` plus current SQL service/profile/tenant authority under S1-B request authority owns request creation.
- Can frontend fabricate request creation? No. The frontend can submit intent and present only validated backend-created request responses.
- Can stale success survive actor transitions? No. Success navigation is fenced to the initiating actor scope, mounted route, and service ID.
- Can duplicate workflow creation occur? Multiple intents can race, but duplicate active request authority cannot be created because backend serializable creation and SQL uniqueness own the boundary.
- Can history replay creation success? No. Success state is not persisted in route state, browser storage, or cache.
- Can offline replay create requests? No. Mutations are retry-free and offline-replay-free; reconnect refetches only.
- Can realtime fabricate workflow truth? No. Realtime and BroadcastChannel invalidate/refetch only.
- Can tenant leakage occur? No inspected cache, mutation, route, actor-transition, or cross-tab path carries request creation truth across tenant/user scopes.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Request Creation Workflow Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos runs for simultaneous tabs, timeout-after-commit, refresh during submit, actor/tenant switch during submit, offline/reconnect, and cross-tab invalidation latency.
