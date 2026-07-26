# PASS F-S3-D Service Marketplace Frontend Realtime Authority

Service Marketplace Frontend Realtime Authority is production-certifiable for inspected runtime paths after reconnect revalidation, startup-loop removal, history-failure teardown, actor-scoped BroadcastChannel validation, and cross-tab lifecycle convergence hardening.

## Connection Findings
- Connection presentation is derivative from current backend chat send authority and validated REST history state.
- SignalR starts only for a current request participant with explicit backend `canSendChat`.
- Connection state controls transport presentation only. It cannot grant history, send, attachment, lifecycle, request, actor, tenant, or visibility authority.
- Chat-history validation failure removes send presentation and tears down the connection.

## Subscription Findings
- The only room identity is the current validated backend request ID.
- Startup joins only after current request, actor, lifecycle, participant, and send authority are accepted.
- Request, actor, tenant, lifecycle, or history-authority loss clears the connection authority ref before asynchronous stop completes.
- No subscription is persisted in browser storage, React Query, route state, or BroadcastChannel.

## Reconnect Findings
- Automatic transport recovery is not accepted as restored authority.
- `onreconnected` immediately fences events, clears connected presentation, retires the recovered transport, and invalidates current request and chat REST authority.
- A room can be joined again only through a fresh hook lifecycle after backend request/chat authority succeeds.
- Reconnect cannot preserve or replay a stale room subscription.

## Event Findings
- `ReceiveMessage` payloads are checked for valid event identity and exact current request identity.
- Accepted events invalidate the scoped chat query only; payload body, sender, attachment, timestamp, and order never enter cache.
- Duplicate, replayed, late, malformed, foreign-request, and out-of-order events can cause at most redundant backend refetch.
- Lifecycle and request truth are not inferred from message events.

## BroadcastChannel Findings
- BroadcastChannel carries scoped invalidation signals only.
- Events require `type: invalidate` and exact initiating tenant/user authority scope.
- Malformed, foreign-scope, ineligible, and stale-actor events are ignored.
- Construction/post failures are best-effort convergence failures and cannot create fallback authority.

## Cache Findings
- React Query keys remain tenant/user/request scoped.
- Realtime uses invalidation/refetch only and contains no message, attachment, lifecycle, request, participant, or visibility cache writes.
- Cached chat rows remain withheld during validation and after failure.
- Connection startup refreshes chat history without invalidating the already-current request query, avoiding connection teardown/restart loops.

## Actor-Transition Findings
- Logout/login, actor switching, and tenant switching change the marketplace authority scope, purge prior cache scope, null chat identity, and fence sends/events.
- Broadcast publication requires the captured initiating scope to still equal current authenticated scope.
- Stale mutation completions cannot publish invalidation into the replacement actor scope.

## History Findings
- Refresh, back/forward, bookmarks, deep links, and route replay carry request identity only.
- Every route re-entry reloads protected session, request, lifecycle, participant, history, and send authority.
- Browser history cannot restore a connection, room membership, event payload, or cached realtime truth.

## Failure Findings
- Disconnect, timeout, start failure, join failure, reconnect failure, history failure, malformed event, and BroadcastChannel failure fail closed.
- Connection failure leaves backend REST presentation authoritative and connected/send presentation false.
- Ambiguous sends invalidate authoritative history; no local success row is created.

## Concurrency Findings
- Connection instance, request ID, actor scope, current identity ref, and synchronous event-acceptance fence guard late callbacks.
- Request/actor/tenant transitions clear connection authority before transport shutdown completes.
- Duplicate events, reconnect races, send races, focus refresh, periodic refresh, and cross-tab invalidation converge through scoped backend reads.

## Adversarial Findings
- SignalR replay, BroadcastChannel replay, subscription replay, reconnect replay, late callback replay, duplicate delivery, out-of-order delivery, actor switching, tenant switching, request switching, refresh, and network interruption were reviewed.
- No inspected frontend path can use realtime to fabricate, append, reorder, preserve, resurrect, relabel, or cross-scope leak backend business truth.

## Runtime Hardening Performed
- Removed request-query invalidation from initial connection startup and message delivery, closing connection teardown/restart churn.
- Changed reconnect recovery to retire the recovered transport and require fresh request/chat backend validation before rejoining.
- Made chat-history authority failure remove connection/send presentation.
- Added actor-scoped BroadcastChannel payload validation and best-effort failure containment.
- Bound BroadcastChannel publication to the captured initiating actor scope.
- Added cross-tab invalidation after request lifecycle mutation settlement, including ambiguous outcomes.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/authority.ts`
- `PointOfSale.Web/src/marketplace/components/MarketplaceAuthorityBoundary.tsx`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`
- `PointOfSale.Web/src/marketplace/hooks/useAcceptRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useActivateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCancelRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts`
- `PointOfSale.Web/src/marketplace/hooks/useDeactivateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts`
- `PointOfSale.Web/src/marketplace/hooks/useRequestService.ts`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/realtimeAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRealtimeAuthorityCertificationTests.cs`
- Updated prior F-S3-A, F-S3-B, request-creation, and visibility/lifecycle source freezes for the hardened runtime signatures.

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns realtime authority? The frontend owns derivative connection and synchronization presentation; current backend request, actor, chat, lifecycle, and visibility authorities own truth.
- Can SignalR fabricate truth? No.
- Can subscriptions survive authority loss? No on inspected paths.
- Can reconnect restore stale authority? No; recovered transport is retired and backend authority must be revalidated.
- Can duplicate events become truth? No.
- Can out-of-order events become authority? No.
- Can stale callbacks survive actor transitions? No inspected callback can mutate or signal the replacement actor scope.
- Can cross-request leakage occur? No on inspected paths.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Realtime Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser and PostgreSQL-backed chaos coverage for simultaneous tabs, reconnect during logout, cookie/session revocation while connected, lifecycle changes during network loss, Redis backplane interruption, high-volume duplicate delivery, and cross-tab invalidation latency.
