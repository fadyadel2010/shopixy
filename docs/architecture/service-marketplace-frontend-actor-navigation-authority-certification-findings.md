# PASS F-S0 Service Marketplace Frontend Actor & Navigation Authority

Service Marketplace Frontend Actor & Navigation Authority is production-certifiable for the inspected runtime after session-scoped cache, runtime revalidation, profile/tab, failure-state, chat-history, realtime replay, and actor-switch hardening.

## Actor Findings

- Backend-authenticated POS session identity owns the current user and tenant scope. Marketplace provider identity derives from current `GET /api/marketplace/me` profile authority; buyer/provider request roles derive from backend request ownership and capability responses.
- Provider and buyer are contextual Service Marketplace actors. Super Admin is not a Service Marketplace workflow actor and receives no request, chat, completion, rating, or reputation authority from these routes.
- Runtime actor eligibility now fails closed unless the backend session supplies both tenant and user identity, and explicitly excludes `SuperAdmin`. The sidebar, notification bell, and direct marketplace routes share this gate.
- Persisted Zustand state is bootstrap evidence only. Protected routes remain blocked until `/api/auth/session` revalidates the session.

## Navigation Findings

- The Services Marketplace sidebar entry is navigation only and grants no provider or buyer capability.
- The sidebar entry and marketplace request notification bell are absent for ineligible or Super Admin sessions.
- Marketplace route content is wrapped by `MarketplaceAuthorityBoundary`, keyed to authenticated tenant and user identity.
- Actor changes remove the prior actor's marketplace cache scope and remount marketplace screens under the new scope.
- Route teardown and route-to-route history transitions remove the active scope in a layout effect before the next marketplace screen can paint cached authority.

## Route Findings

- `/marketplace`, `/marketplace/service/:serviceId`, and `/marketplace/chat/:requestId` remain inside the POS `ProtectedRoute`.
- Direct URLs and refreshes re-run backend session bootstrap and marketplace authority queries.
- Chat deep links require backend request participant authority. Service detail deep links require current backend listing visibility.

## Menu Findings

- Request, completion, review, accept, reject, chat, and service-management actions render only from backend response capabilities or current backend profile/listing authority.
- A provider's own service detail no longer renders the buyer request action.
- The buyer request action also remains hidden while current profile ownership authority is unresolved or refreshing.
- Inactive or unresolved marketplace profiles do not expose provider service or incoming-request actions.

## Tab Findings

- Discover and buyer request history remain general authenticated marketplace surfaces.
- My Services and Incoming Requests require a current active backend marketplace profile.
- Invalid, stale, or profile-ineligible tab query parameters normalize to Discover with history replacement.

## Screen Visibility Findings

- Request and service list screens discard cached rows when current authority refresh fails.
- Service details fail closed on current visibility failure; profile failures cannot preserve provider actions.
- Chat history visibility is now distinct from send authority. Authorized participants may view closed history while final completion still removes the composer and SignalR send path.

## Cache Findings

- React Query marketplace keys include authenticated tenant and user scope.
- Marketplace authority queries use zero stale time, no retry resurrection, always-refetch mount/focus/reconnect behavior, plus active-route periodic revalidation.
- Prior actor cache entries are removed on actor-scope changes. Browser storage does not provide marketplace profile, request, service, chat, or action authority.
- Marketplace mutations capture the initiating tenant/user scope; a response completing after an actor switch cannot invalidate or refresh the replacement actor's marketplace scope.

## Realtime Findings

- SignalR remains transport only. The client connects for current backend send authority, does not optimistically create messages, and accepts events only for the current request ID.
- Automatic reconnect rejoins the authoritative conversation and invalidates request capabilities and chat history.
- Replayed, duplicate, foreign-request, or stale events cannot change navigation, tabs, request capabilities, or actor identity.
- SignalR handlers are synchronously fenced before disconnect, preventing teardown-race events from appending to chat cache after send authority is removed.

## Tenant Isolation Findings

- Marketplace query cache identity includes tenant and user identity, preventing in-memory cache reuse across tenant or actor transitions.
- API requests use the canonical cookie/session client and trusted backend tenant context; route values and local storage do not supply tenant authority.
- Notification seen-state and refresh behavior are tenant/user scoped and reset immediately on actor changes.

## History Findings

- Browser back, forward, refresh, bookmarks, and shared deep links re-enter protected routes and current backend queries.
- Tab query parameters are normalized against current profile authority.
- Previous actor caches and invalid tabs cannot resurrect a removed provider surface.

## Failure-Handling Findings

- Marketplace requests now use the canonical API client with session refresh and forced logout behavior.
- Current 401, 403, and 404 authority failures cannot preserve protected list, service detail, profile action, or chat route content.
- Network/search failures remain non-authoritative presentation failures and cannot grant actions.

## Adversarial Findings

- Route tampering, deep links, local/session storage tampering, cached actor replay, history replay, refresh races, SignalR replay, multi-tab actor switching, profile drift, and tenant switching were reviewed.
- Navigation, menus, tabs, cache entries, query parameters, and SignalR events remain derivative and cannot create backend actor or workflow authority.

## Runtime Hardening Performed

- Routed marketplace HTTP calls through the canonical session-aware API client.
- Added one backend-session-derived workflow actor gate that requires tenant/user identity and excludes Super Admin across routes, sidebar, and notifications.
- Added tenant/user-scoped marketplace query keys and an active-route authority revalidation boundary.
- Purged foreign and unmounted marketplace cache scopes before route paint and bound mutation invalidation to the initiating actor scope.
- Made provider tabs require current active backend profile authority and fail closed on profile errors.
- Suppressed the request action on a provider's own service and while profile ownership authority is unresolved or refreshing.
- Split chat history visibility from chat send authority and hardened SignalR replay/reconnect handling.
- Added a synchronous realtime teardown fence for capability-loss and actor-transition races.
- Cleared stale list/detail/profile presentation on current authority failures.
- Reset marketplace notifications on actor changes.
- Restored the missing debounced discovery query runtime variable.

## Exact Runtime Files Modified

- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/App.jsx`
- `PointOfSale.Web/src/core/apiClient.ts`
- `PointOfSale.Web/src/components/MarketplaceNotificationBell.jsx`
- `PointOfSale.Web/src/components/Sidebar.jsx`
- `PointOfSale.Web/src/marketplace/authority.ts`
- `PointOfSale.Web/src/marketplace/queryKeys.ts`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx`
- `PointOfSale.Web/src/marketplace/components/RequestCard.tsx`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`
- `PointOfSale.Web/src/marketplace/hooks/useAcceptRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useActivateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCancelRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCreateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useDeactivateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts`
- `PointOfSale.Web/src/marketplace/hooks/useRequestService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useIncomingRequests.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarketplaceProfile.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarketplaceSearch.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMyRequests.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMyServices.ts`
- `PointOfSale.Web/src/marketplace/hooks/useRequestDetails.ts`
- `PointOfSale.Web/src/marketplace/hooks/useServiceDetails.ts`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/MarketplaceShell.tsx`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/DiscoverTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyServicesTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/ProfileTab.tsx`
- `PointOfSale.Web/src/marketplace/viewmodels/useChatVM.ts`
- `PointOfSale.Web/src/marketplace/viewmodels/useRequestDetailsVM.ts`

## Regression Freezes Added

- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendActorNavigationAuthorityCertificationTests.cs`

## Architecture Updates

- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers

- Who owns Service Marketplace actor authority? The authenticated backend POS session, current backend marketplace profile, and SQL request participant/capability authority.
- Can navigation become authority? No. Navigation is derivative presentation only.
- Can routes bypass actor validation? No on inspected routes; deep links require a complete eligible POS session, reject Super Admin workflow entry, and revalidate listing, profile, and request participant authority.
- Can stale cache expose hidden screens? No after session-scoped keys, actor-scope removal, fail-closed errors, and active revalidation.
- Can realtime events corrupt navigation? No. SignalR events are request-bound message transport and cannot mutate navigation or capabilities.
- Can browser history resurrect invalid screens? No. Re-entry runs current protected-route and backend authority checks.
- Can tenant leakage occur? No inspected frontend cache, storage, route, or notification path reuses marketplace authority across tenant/user scopes.
- Were runtime code changes required? Yes.
- Is Service Marketplace Frontend Actor & Navigation Authority production-certifiable? Yes, for the inspected runtime after this hardening.
