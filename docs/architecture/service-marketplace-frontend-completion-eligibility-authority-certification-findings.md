# PASS F-S4-A Service Marketplace Frontend Completion Eligibility Authority

Service Marketplace Frontend Completion Eligibility Authority is production-certifiable for inspected runtime paths after current-actor identity binding, dedicated completion eligibility validation, actor-scope mutation fencing, and realtime capability revalidation hardening.

## Eligibility Findings
- Completion eligibility is derivative presentation only. Backend S1-B request authority, S1-D completion authority, current lifecycle authority, authenticated actor authority, and visibility authority own the facts from which it is displayed.
- The frontend completion boundary requires valid backend request, service, buyer, and provider identities; distinct participants; current-user membership; explicit backend capability; and the matching lifecycle state.
- Missing, malformed, foreign-actor, role-mismatched, or contradictory capability DTOs fail closed and cannot paint completion controls.

## Provider Findings
- Provider completion presentation requires the current authenticated user to equal the backend `providerUserId`, the request to be `Accepted`, and explicit backend `canMarkCompleted`.
- `Pending`, `CompletedByProvider`, `CompletedByBuyer`, `Rejected`, `Cancelled`, expired, malformed, or foreign-actor rows cannot expose provider completion.
- Provider list DTOs now carry both participant identities so frontend presentation does not infer provider ownership from the selected tab.

## Buyer Findings
- Buyer confirmation presentation requires the current authenticated user to equal the backend `buyerUserId`, prior `CompletedByProvider` lifecycle state, and explicit backend `canConfirmCompletion`.
- Accepted, pending, rejected, cancelled, final-completed, malformed, or foreign-actor rows cannot expose buyer confirmation.
- Buyer list DTOs now carry both participant identities so frontend presentation does not infer buyer ownership from the selected tab.

## Lifecycle Findings
- Valid completion eligibility ordering is `Accepted -> provider completion action`, then `CompletedByProvider -> buyer confirmation action`.
- `CompletedByBuyer`, `Rejected`, `Cancelled`, `Pending`, and invalid states expose neither completion action.
- The existing lifecycle boundary and the dedicated completion boundary must both accept the DTO; either boundary may revoke presentation.

## Visibility Findings
- `MyRequestsTab`, `IncomingTab`, `RequestCard`, and request-detail view models consume the dedicated completion eligibility boundary.
- Provider and buyer completion controls are mutually exclusive and actor-bound. Contradictory DTOs claiming both actions are rejected.
- Completion visibility remains presentation only and cannot authorize the backend mutation.

## Cache Findings
- React Query completion-bearing list and detail keys remain authenticated tenant/user scoped.
- List actions are withheld while current authority is fetching, detail authority refetches periodically, and actor/tenant replacement removes prior scopes.
- Completion mutations remain optimistic-write-free and invalidate scoped backend authority on settlement, including ambiguous failures.

## Realtime Findings
- SignalR remains invalidation transport only. Accepted current-request events now invalidate the full current marketplace scope as well as reset chat history.
- Realtime payloads do not write lifecycle status, participant identity, capability flags, or completion eligibility into caches.
- Reconnect, focus, visibility, periodic refresh, BroadcastChannel, and mutation settlement re-enter backend authority and cannot grant completion.

## Actor-Transition Findings
- Completion mutation execution verifies that the captured initiating authority scope still equals the current authenticated tenant/user scope.
- Logout/login, actor switching, tenant switching, route teardown, and replacement sessions purge or remount marketplace authority state.
- A stale completion callback cannot invalidate or present completion controls for the replacement actor.

## History Findings
- Browser history, bookmarks, back/forward, refresh, and deep links carry request or tab navigation only.
- Re-entry requires protected session authority, current tenant/user-scoped queries, participant identity validation, lifecycle validation, and completion capability validation.
- Route state and browser storage cannot preserve or resurrect completion eligibility.

## Failure Findings
- 401, 403, 404, 409, 500, timeout, refresh failure, malformed capability DTO, missing participant identity, actor mismatch, and wrong-target mutation response fail closed.
- No completion mutation performs optimistic lifecycle or capability writes.
- Failure settlement invalidates current scoped authority so stale eligibility is not accepted as mutation success.

## Adversarial Findings
- Capability replay, cache replay, lifecycle replay, DTO replay, actor switching, tenant switching, history replay, deep-link replay, realtime replay, and stale mutation completion were reviewed.
- A replayed capability must still match current actor identity, exact backend participants, current lifecycle, and current scoped query authority.
- No inspected frontend path can grant backend completion authority, bypass S1-D completion enforcement, preserve completion actions across actor/tenant replacement, or leak completion eligibility across tenants.

## Runtime Hardening Performed
- Added `deriveServiceRequestCompletionEligibility` as a dedicated actor/lifecycle/capability presentation boundary.
- Added buyer and provider participant IDs to both request list DTOs.
- Routed completion action surfaces and request-detail view models through the dedicated boundary.
- Fenced provider-complete and buyer-confirm mutation execution to the initiating authenticated scope.
- Expanded accepted realtime invalidation from chat-only refresh to current marketplace capability revalidation.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/completionEligibility.ts`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/components/RequestCard.tsx`
- `PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts`
- `PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/completionEligibility.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendCompletionEligibilityAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns completion eligibility authority? Current backend request, lifecycle, S1-D completion, authenticated actor, and visibility authorities own the facts; frontend F-S4-A owns derivative presentation only.
- Can frontend grant completion eligibility? No. It may validate and suppress backend DTO presentation only.
- Can providers complete outside valid lifecycle states? No. The frontend exposes provider completion only for current-provider `Accepted` authority, and backend S1-D revalidates every mutation.
- Can buyers confirm completion prematurely? No. Buyer confirmation requires current-buyer identity and `CompletedByProvider`.
- Can stale eligibility survive refresh? No on inspected surfaces; current refresh withholds actions and settlement/realtime/periodic signals revalidate scoped backend authority.
- Can lifecycle loss preserve completion actions? No. Both lifecycle and completion eligibility boundaries must continue to accept the current DTO.
- Can realtime expose invalid completion controls? No. Realtime invalidates/refetches only and cannot write capability or lifecycle truth.
- Can history resurrect obsolete completion eligibility? No. History re-enters current actor, request, lifecycle, visibility, and capability authority.
- Can tenant leakage occur? No inspected query, actor transition, route, mutation, cache, or realtime path crosses authenticated tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Completion Eligibility Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for simultaneous provider/buyer tabs, timeout-after-commit, lifecycle revocation during confirmation dialogs, reconnect during actor replacement, cross-device invalidation latency, and tenant switching during completion mutations.
