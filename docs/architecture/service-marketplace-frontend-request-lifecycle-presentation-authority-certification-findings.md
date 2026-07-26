# PASS F-S2-B Service Marketplace Frontend Request Lifecycle Presentation Authority

Service Marketplace Frontend Request Lifecycle Presentation Authority is production-certifiable for inspected runtime paths after lifecycle presentation boundary and provider rejection endpoint hardening.

## Status Findings
- Request lifecycle status presentation is derivative from current backend `ServiceRequests` DTO status.
- The frontend now allowlists lifecycle status values before rendering. Unknown, malformed, or fabricated statuses fail closed instead of rendering raw status text.
- `Pending`, `Accepted`, `Rejected`, `Cancelled`, `CompletedByProvider`, and `CompletedByBuyer` are recognized only as exact current backend enum DTO values; aliases and frontend-only future states fail closed.

## Badge Findings
- Status badge color and label derive from the lifecycle presentation boundary.
- Badge rendering no longer falls back to arbitrary raw status strings. Invalid lifecycle values suppress the request surface.
- Badges cannot contradict the accepted lifecycle key because class and label are selected from the same validated status key.

## Transition Findings
- Lifecycle transition responses are not patched optimistically into cache.
- Accept, reject, cancel, provider-complete, buyer-confirm, and review mutations invalidate current backend authority instead of writing lifecycle truth.
- Provider rejection now calls `/marketplace/requests/{requestId}/reject`, preserving `Rejected` lifecycle presentation rather than routing rejection intent through cancellation.

## List Findings
- My Requests and Incoming Requests derive rows through `deriveServiceRequestLifecyclePresentation` before rendering.
- Invalid lifecycle rows are suppressed, and list surfaces are withheld while current backend authority is fetching.
- Request counts derive from validated lifecycle rows, not raw cached DTO arrays.

## Detail Findings
- Request detail view models and chat request headers derive action/status presentation through `deriveServiceRequestDetailsLifecyclePresentation`.
- Invalid detail lifecycle DTOs fail closed as unavailable request presentation.
- Detail and chat request headers are withheld during current request authority refresh.

## Action Visibility Findings
- Action visibility still requires backend capability flags, but those flags are now status-consistency checked before rendering.
- `canAccept` and `canReject` render only for `Pending`; provider completion only for `Accepted`; buyer confirmation only for `CompletedByProvider`; cancellation only for `Pending` or `Accepted`; review only for `CompletedByBuyer`; send-chat only for `Accepted` or `CompletedByProvider`.
- Contradictory backend-shaped DTOs suppress actions rather than presenting lifecycle-invalid authority.

## Cache Findings
- React Query request/list/detail/chat keys remain authenticated tenant/user scoped.
- Request lifecycle mutations invalidate the current marketplace authority scope and do not use `setQueryData` for lifecycle truth.
- Cached request lifecycle rows are not displayed during current-authority list/detail/chat refresh, and actor/tenant scope changes remove prior marketplace query scopes.

## Realtime Findings
- SignalR remains chat transport only. Message events are request-id fenced and cannot write request lifecycle status or capabilities.
- Reconnect invalidates request detail and chat queries; BroadcastChannel/focus/visibility/periodic refresh invalidate only.
- Realtime cannot overwrite backend lifecycle authority because no lifecycle cache write path accepts realtime status payloads.

## Actor-Transition Findings
- Marketplace query keys include current tenant/user authority scope.
- Actor, tenant, logout/login, and route teardown remove previous scope caches before replacement presentation.
- Stale lifecycle mutation completions invalidate only the initiating scoped marketplace authority and cannot render replacement actor actions.

## History Findings
- Browser history, back/forward, bookmarks, and direct URLs carry request IDs/routes only.
- History re-enters backend request/list/detail authority and lifecycle presentation validation.
- No route state or browser storage path persists lifecycle status, badge, action, or success truth.

## Failure Findings
- 401, 403, 404, refresh failure, malformed lifecycle DTOs, invalid statuses, and contradictory capabilities fail closed.
- Mutation failures do not preserve optimistic lifecycle transitions because none are written.
- Retry or timeout outcomes converge through scoped invalidation/refetch of current backend authority.

## Concurrency Findings
- Same-tab pending action fences remain in Incoming Requests.
- Multi-tab and refresh races converge through backend S1-B row-locked SQL lifecycle transitions plus scoped invalidation.
- Stale transitions cannot overwrite current lifecycle presentation because transition responses are not patched into request caches.

## Adversarial Findings
- Status replay, badge replay, transition replay, cache replay, history replay, realtime replay, actor switching, tenant switching, refresh races, deep links, and contradictory DTO composition were reviewed.
- Invalid lifecycle values suppress presentation; status/action contradictions fail closed; realtime and cache cannot become lifecycle authority.
- No inspected path can fabricate lifecycle truth, preserve stale lifecycle status across current refresh, or leak lifecycle state across tenant/user scopes.

## Runtime Hardening Performed
- Added status lifecycle presentation boundary in `status.ts`.
- Replaced raw status rendering in request cards, My Requests, Incoming Requests, request detail view model, and chat header.
- Suppressed lifecycle-invalid or contradictory request actions.
- Withheld list/detail/chat lifecycle surfaces during current-authority refresh.
- Routed provider rejection to the backend rejection endpoint instead of cancellation.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/status.ts`
- `PointOfSale.Web/src/marketplace/components/RequestCard.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/status.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRequestLifecyclePresentationAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendActorNavigationAuthorityCertificationTests.cs`.

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns request lifecycle authority? Backend S1-B PostgreSQL `ServiceRequests` plus current SQL actor, lifecycle, and visibility authority owns lifecycle truth.
- Can frontend derive lifecycle state? No. The frontend may validate and present backend lifecycle DTOs only; invalid or contradictory presentation fails closed.
- Can stale statuses survive refresh? No on inspected list/detail/chat lifecycle surfaces after refresh withholding and scoped invalidation.
- Can actions remain visible after lifecycle loss? No. Actions require backend flags and status-consistent lifecycle presentation.
- Can history resurrect obsolete lifecycle truth? No. History carries routes only and re-enters backend authority.
- Can realtime overwrite backend authority? No. Realtime invalidates/refetches or appends SQL-persisted chat messages only; it does not write lifecycle status.
- Can contradictory states appear? No on hardened inspected surfaces; contradictory status/action DTOs suppress actions or the row.
- Can tenant leakage occur? No inspected lifecycle cache, route, realtime, or actor-transition path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Request Lifecycle Presentation Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser chaos coverage for concurrent accept/reject/cancel/complete/confirm transitions, SignalR reconnect, multi-tab cache invalidation latency, actor/tenant switching during transitions, and backend projection lag.
