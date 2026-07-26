# PASS F-S1-E Service Marketplace Frontend Service Request Entry Authority

Service Marketplace Frontend Service Request Entry Authority is production-certifiable for the inspected runtime after backend eligibility composition, active-request presentation, self-service denial, submission replay fencing, failure convergence, and cross-tab revalidation hardening.

## Eligibility Findings

- Request-entry presentation now consumes `requestEntry.canRequest` from the current backend service-detail response.
- Backend eligibility requires current trusted actor/tenant context, same-tenant active service, active provider profile, active tenant, provider ownership, and no active buyer/service request.
- Missing, unknown, or internally inconsistent eligibility DTOs fail closed in the frontend adapter.

## Self-Service Findings

- Backend eligibility returns `OwnService` when the authenticated actor owns the provider profile.
- Request creation independently rejects buyer/provider identity equality before persistence.
- Actor changes cannot preserve self-request presentation because detail authority is actor-scoped, purged, and reloaded.

## Active-Request Findings

- Pending, accepted, and provider-completed requests are active request authority.
- Backend detail composition detects the current buyer's active request and returns `ActiveRequestExists`.
- The frontend removes request creation and presents navigation to current request history.
- SQL serializable creation, duplicate preflight, and `UX_ServiceRequests_Active_Service_Buyer` prevent concurrent duplicate authority.

## Form Findings

- Request form state resets on service, actor, and eligibility loss.
- Request messages are trimmed and bounded to 2000 characters in both presentation and backend mutation validation.
- Form state is component-local presentation only and is never placed in route state, browser storage, or query cache.

## Capability Findings

- Request capability is backend-composed from current listing, provider, tenant, actor, and active-request authority.
- The frontend does not calculate request permission from listing status, profile cache, route state, or request-list cache.
- Malformed or stale capability snapshots suppress request entry.

## Mutation Findings

- Request creation remains owned by S1-B PostgreSQL request authority.
- The mutation has no optimistic success or cache insertion.
- Mutation retry is disabled and `networkMode: 'always'` prevents offline-paused mutation replay after reconnect.
- Success navigation is fenced to the initiating actor scope, mounted route, and service ID.

## Cache Findings

- Detail and request caches remain tenant/user scoped and are withheld during current-authority validation or after authority failure.
- Every mutation outcome invalidates the initiating actor scope.
- Cache state cannot grant request capability or fabricate request success.

## Navigation Findings

- Deep links, refresh, back, forward, and bookmarks re-enter current backend detail and request-entry authority.
- Form state is not stored in history and cannot replay onto another service.
- A stale mutation completion cannot redirect a replacement actor or a different service route.

## Realtime Findings

- No realtime payload grants request capability.
- Focus, visibility, periodic refresh, and cross-tab `BroadcastChannel` messages trigger backend refetch only.
- Cross-tab messages carry no eligibility, actor, tenant, request, or success authority.

## Tenant Isolation Findings

- Backend eligibility requires the service tenant to equal trusted current tenant authority.
- Query keys, mutation invalidation, and route presentation remain tenant/user scoped.
- Actor or tenant transitions purge prior marketplace state and stale completions are ignored.

## Failure-Handling Findings

- Missing or failed eligibility suppresses request entry.
- Duplicate active request races return HTTP 409; self-service attempts return HTTP 403.
- 400, 401, 403, 404, 409, 500, timeout, and offline outcomes cannot create frontend success authority.
- Mutation settlement triggers current-authority refresh, allowing ambiguous timeout outcomes to converge from backend truth.

## Concurrency Findings

- Same-tab double submission is synchronously fenced before React mutation state updates.
- Multi-tab and replay races converge through serializable SQL creation and the active-request unique index.
- PostgreSQL unique-constraint races map to `ACTIVE_REQUEST_EXISTS` instead of surfacing as fabricated success or an unclassified server failure.

## Adversarial Findings

- Capability replay, eligibility replay, cache replay, actor switching, tenant switching, request replay, double submit, history replay, deep-link replay, realtime replay, and offline replay were reviewed.
- None can create duplicate request authority, preserve self-request capability, cross tenant boundaries, or make frontend state authoritative.

## Runtime Hardening Performed

- Added backend-composed request-entry authority to service details.
- Added explicit current actor/tenant/listing/provider/active-request eligibility.
- Added self-service and duplicate conflict semantics plus concurrent unique-conflict handling.
- Replaced frontend profile/listing inference with bounded backend request-entry capability.
- Added message normalization/validation, synchronous double-submit fencing, offline replay prevention, stale completion fencing, settlement invalidation, and cross-tab refetch triggers.

## Exact Runtime Files Modified

- `PointOfSale.Core/DTOs/Marketplace/RequestServiceDto.cs`
- `PointOfSale.Core/Exceptions/MarketplaceDomainException.cs`
- `PointOfSale.Core/Interfaces/IServiceRequestService.cs`
- `PointOfSale.Infrastructure/Services/ServiceRequestService.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.ts`
- `PointOfSale.Web/src/marketplace/authority.ts`
- `PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx`
- `PointOfSale.Web/src/marketplace/hooks/useRequestService.ts`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`

## Regression Freezes Added

- `PointOfSale.Web/src/marketplace/serviceDetailAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRequestEntryAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendActorNavigationAuthorityCertificationTests.cs`.

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns request-entry authority? The frontend owns derivative request-entry presentation; current backend request, capability, listing, provider, actor, and tenant authorities own eligibility.
- Can request eligibility become authority? No. It is a current backend-derived capability snapshot and the frontend fails closed.
- Can providers request their own services? No.
- Can stale capabilities survive refresh? No on inspected paths; cached detail is withheld during refresh and malformed or failed eligibility suppresses entry.
- Can duplicate requests be initiated? Multiple HTTP attempts may race, but duplicate active request authority cannot be created.
- Can browser history resurrect request capability? No.
- Can realtime events expose request actions? No. They may trigger refetch only.
- Can tenant leakage occur? No inspected request-entry state, cache, mutation, route, or cross-tab path crosses tenant/user authority.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Service Request Entry Authority production-certifiable? Yes, for the inspected runtime after this hardening.

## Residual Operational Risk

- Production should retain PostgreSQL-backed browser chaos runs for simultaneous multi-tab submit, timeout-after-commit, actor/tenant switch during submit, offline/reconnect, and visibility/realtime drift.
- Cross-tab `BroadcastChannel` is convergence acceleration only. Backend SQL uniqueness and current-authority refresh remain the correctness boundary.
