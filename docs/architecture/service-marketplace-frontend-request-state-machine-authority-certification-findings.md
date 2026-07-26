# PASS F-S2-D Service Marketplace Frontend Request State Machine Authority

Service Marketplace Frontend Request State Machine Authority is production-certifiable for inspected runtime paths after lifecycle mutation-response boundary hardening and buyer confirmation replay fencing.

## Transition Findings
- Lifecycle transition presentation remains derivative from current backend request lifecycle DTOs.
- Allowed frontend transitions are status-bound to backend capability flags: `Pending -> Accepted`, `Pending -> Rejected`, `Pending -> Cancelled`, `Accepted -> CompletedByProvider`, `CompletedByProvider -> CompletedByBuyer`, and `Accepted -> Cancelled`.
- The frontend does not invent transition ordering. Invalid, unknown, terminal, or contradictory status/capability DTOs fail closed before action rendering.

## Capability Findings
- `canAccept`, `canReject`, `canCancel`, `canMarkCompleted`, `canConfirmCompletion`, `canReview`, and chat capabilities are accepted only when the current backend lifecycle status allows the capability.
- Stale capability rows are withheld during current list/detail/chat refresh and prior actor/tenant scopes are removed before replacement presentation.
- Cached capabilities cannot become transition authority because mutations invalidate/refetch scoped backend authority and never patch lifecycle state.

## Action Visibility Findings
- Request cards, My Requests, Incoming Requests, request details, and chat headers derive action visibility through the lifecycle presentation boundary.
- Contradictory actions do not render: accept/reject require pending, provider completion requires accepted, buyer confirmation requires provider-completed, cancellation requires pending or accepted, and chat send requires accepted or provider-completed.
- Same-tab provider actions and buyer confirmation are synchronously fenced while a request action is in flight.

## Mutation Findings
- Accept, reject, cancel, provider-complete, and buyer-confirm mutations submit intent only to backend lifecycle endpoints.
- Runtime hardening added `deriveServiceRequestLifecycleMutationAuthority`, requiring mutation completion responses to match the expected backend state-machine target before frontend workflow success is accepted.
- Malformed, fabricated, missing, wrong-order, or wrong-target success-shaped mutation responses fail closed and trigger scoped invalidation/refetch. No lifecycle mutation uses optimistic updates, `onMutate`, or `setQueryData`.

## Consistency Findings
- List lifecycle rows, detail lifecycle state, chat lifecycle state, and contract-gated lifecycle presentation all use the same status/action boundary.
- Request detail and chat surfaces are withheld while current request authority refreshes, preventing cached detail or chat headers from contradicting current list authority.
- Contract presentation remains lifecycle-gated and cannot preserve obsolete accepted/completed state after lifecycle loss.

## Cache Findings
- React Query request, detail, chat, service, and profile keys are scoped by current authenticated tenant/user authority.
- Actor or tenant changes remove prior marketplace query scopes. Lifecycle mutation settlements invalidate scoped backend authority only.
- Cache state cannot override the state machine because no inspected path writes lifecycle status, capabilities, or transition success into request caches.

## Realtime Findings
- SignalR remains chat transport only. Realtime message events are request-id fenced and may append SQL-persisted chat messages only.
- SignalR reconnect, BroadcastChannel, focus, visibility, and periodic refresh invalidate/refetch backend request authority; they do not write lifecycle status or capabilities.
- Stale, duplicate, out-of-order, or foreign-request realtime events cannot reorder, fabricate, or overwrite lifecycle transitions.

## Actor-Transition Findings
- Marketplace actor scope derives from authenticated backend tenant/user identity and excludes Super Admin workflow actors.
- Logout/login, actor switching, tenant switching, and route teardown purge prior marketplace scopes before replacement presentation.
- Stale lifecycle mutation completions invalidate only the initiating actor scope and cannot render replacement actor actions.

## History Findings
- Browser history, direct URLs, bookmarks, back, forward, and refresh carry route IDs only.
- Deep links re-enter protected route, current actor/tenant scope, current backend request authority, and lifecycle presentation validation.
- Route state and browser storage do not persist lifecycle status, action capability, transition success, or mutation response authority.

## Failure Findings
- 400, 401, 403, 404, 409, 500, timeout, network failure, malformed lifecycle DTOs, and malformed mutation responses cannot fabricate transition success.
- Failures preserve no optimistic lifecycle state because no optimistic lifecycle write path exists.
- Ambiguous outcomes converge through scoped invalidation/refetch and backend state machine truth.

## Concurrency Findings
- Same-tab accept/reject/provider-complete and buyer-confirm actions are synchronously fenced per request.
- Cross-tab, refresh-during-transition, and mutation races converge through backend S1-B row-locked SQL lifecycle transitions plus scoped invalidation.
- Stale mutation completions cannot overwrite current truth because mutation responses are validated and never patched into lifecycle caches.

## Adversarial Findings
- Accept replay, reject replay, cancel replay, complete replay, capability replay, cache replay, history replay, realtime replay, actor switching, tenant switching, refresh races, cross-tab races, malformed DTOs, and contradictory status/action payloads were reviewed.
- No inspected frontend path can corrupt state-machine truth, fabricate transitions, skip lifecycle ordering, resurrect obsolete transition authority, expose contradictory lifecycle actions, or leak transition authority across tenants.
- Backend SQL request lifecycle authority remains the enforcement boundary for invalid stale clicks that race ahead of frontend invalidation.

## Runtime Hardening Performed
- Added `deriveServiceRequestLifecycleMutationAuthority` to validate lifecycle mutation completion responses against expected state-machine targets.
- Routed accept, reject, cancel, provider-complete, and buyer-confirm mutation wrappers through the mutation-response boundary.
- Added settlement invalidation to reusable lifecycle mutation hooks so malformed success-shaped responses converge by refetch.
- Added same-tab pending fencing and disabled-state presentation for buyer confirmation in My Requests.
- Added frontend and architecture regression freezes for lifecycle mutation response validation and replay fencing.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/status.ts`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/hooks/useAcceptRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCancelRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts`
- `PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/status.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRequestStateMachineAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns lifecycle transition authority? Backend S1-B PostgreSQL `ServiceRequests` plus current backend lifecycle, capability, actor, request, and visibility authority owns lifecycle transition authority.
- Can frontend invent transitions? No. The frontend can submit intent, validate backend DTOs, and present current backend-derived state only.
- Can stale capabilities survive refresh? No on inspected paths; list/detail/chat surfaces withhold during refresh and actor/tenant scopes are purged.
- Can invalid actions appear? No on inspected surfaces; action visibility requires backend capability plus status-consistent lifecycle presentation.
- Can mutation replay create transitions? No. Frontend mutations are retry-free/optimistic-write-free, same-tab fenced where actions render, mutation responses are target-validated, and backend SQL state machine rejects replay.
- Can history resurrect obsolete transition authority? No. History carries routes only and re-enters current backend authority.
- Can realtime reorder transitions? No. Realtime invalidates/refetches request authority and does not write lifecycle status or capabilities.
- Can contradictory actions appear? No on inspected surfaces; contradictory status/action DTOs suppress actions.
- Can tenant leakage occur? No inspected cache, route, mutation, realtime, or actor-transition path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Request State Machine Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos runs for concurrent accept/reject/cancel/complete/confirm transitions, multi-tab invalidation latency, SignalR reconnect, actor/tenant switching during mutations, refresh while transitions are in flight, projection lag, and timeout-after-commit ambiguity.
