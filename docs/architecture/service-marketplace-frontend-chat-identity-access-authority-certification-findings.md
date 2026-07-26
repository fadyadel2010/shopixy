# PASS F-S3-A Service Marketplace Frontend Chat Identity & Access Authority

Service Marketplace Frontend Chat Identity & Access Authority is production-certifiable for inspected runtime paths after route/request participant binding, message identity validation, current-refresh withholding, explicit capability enforcement, and SignalR authority-loss fencing.

## Chat Identity Findings
- Chat identity derives from the current backend request detail authority, authenticated user identity, and the route request ID.
- The frontend requires valid request/service/buyer/provider IDs, exact route/request identity, distinct buyer/provider membership, and current-user membership before presenting chat.
- Message sender identity and Buyer/Provider presentation are accepted only when sender ID and role match the current request participant authority.

## Access Findings
- Chat history presentation requires explicit backend `canViewChatHistory`; send/upload/realtime presentation requires explicit backend `canSendChat`.
- Legacy `canChat`, route presence, cached messages, and local connection state cannot grant access.
- Request-detail and history authority are withheld during current refresh and after authority failure.

## Membership Findings
- The current authenticated user must equal exactly one backend buyer/provider participant.
- Foreign, missing, malformed, duplicated, or contradictory participant identity fails closed.
- Message rows from non-participants or with role/participant mismatches invalidate the history payload.

## Lifecycle Findings
- Backend lifecycle plus explicit capabilities gate history and send authority.
- Pending requests expose no history. Accepted and provider-completed requests may send only with explicit backend capability. Closed non-pending requests may show participant history but cannot send.
- Lifecycle refresh or loss synchronously removes frontend send authority and tears down realtime transport.

## Route Findings
- Direct URLs, bookmarks, refresh, browser history, and guessed request IDs re-enter protected session and backend request authority.
- The returned request ID must match the normalized route request ID before chat presentation.
- Route state carries no participant, membership, capability, or message authority.

## SignalR Findings
- SignalR is request-bound transport only and starts only after current send authority is proven.
- Start, join, reconnect, receive, and send paths are synchronously fenced by current request, actor scope, connection identity, and an authority-loss flag.
- Cleanup clears connection authority before stopping transport; late start/reconnect/receive work cannot restore access.

## Cache Findings
- Request and chat keys remain tenant/user/request scoped.
- Cached history is hidden while current history authority is fetching and after request/history failure or malformed payload.
- Realtime invalidates/refetches only; it does not append or patch message, membership, participant, lifecycle, or access truth.

## Actor-Transition Findings
- Actor and tenant scope derive from the authenticated backend POS session.
- Actor/tenant changes purge marketplace scopes, null current chat identity, reject stale sends, and disconnect SignalR.
- Own-message presentation compares only against validated current participant identity.

## Failure Findings
- 401, 403, 404, 500, timeout, malformed request DTOs, malformed message DTOs, mixed-request rows, role drift, duplicate IDs, and out-of-order history fail closed.
- Failed refresh cannot preserve stale visible history or a stale live subscription.
- Retries and reconnects must re-enter current backend authority.

## Concurrency Findings
- Refresh, lifecycle, actor-switch, reconnect, receive, and send races are fenced by current identity refs and connection authority.
- A send begun before authority loss is rejected before SignalR invocation if current authority or connection scope changed.
- Multi-tab correctness remains backend authority plus scoped invalidation; cross-tab payloads cannot carry chat truth.

## Adversarial Findings
- Request-ID guessing, route replay, history replay, cache replay, malformed DTO replay, SignalR replay, reconnect races, actor switching, tenant switching, and lifecycle loss were reviewed.
- No inspected frontend path can fabricate membership, relabel participant identity, preserve stale access, join from route authority alone, or leak chat truth across actor/tenant scopes.

## Runtime Hardening Performed
- Added `deriveChatIdentityAuthority`, binding current request, route, actor, buyer, provider, lifecycle, and explicit chat capabilities.
- Added `deriveChatMessagesAuthority`, validating request ownership, participant sender identity, role, type, chronology, duplicate identity, and attachment coherence.
- Withheld cached history during current validation and after request/history failure.
- Removed legacy `canChat` fallback authority.
- Fenced SignalR start/join/reconnect/receive/send and teardown against authority loss.

## Exact Runtime Files Modified
- `PointOfSale.Web/src/marketplace/chatAuthority.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.Web/src/marketplace/status.ts`
- `PointOfSale.Web/src/marketplace/viewmodels/useChatVM.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/chatAuthority.test.ts`
- Updated `PointOfSale.Web/src/marketplace/status.test.ts`.
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendChatIdentityAccessAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendActorNavigationAuthorityCertificationTests.cs`.

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns chat identity authority? Current backend SQL request, actor, lifecycle, visibility, and S1-C chat authority own it; frontend presentation is derivative.
- Can frontend grant chat access? No.
- Can participant identity drift? No on inspected paths; request/message participant mismatches fail closed.
- Can non-participants access chat? No; backend enforcement remains final and frontend membership presentation requires current participant proof.
- Can deep links bypass chat authority? No.
- Can SignalR subscriptions survive authority loss? No on the hardened frontend path; events are fenced before asynchronous disconnect completes.
- Can stale chat access survive actor transitions? No.
- Can tenant leakage occur? No inspected route, cache, message, actor-transition, or realtime path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Chat Identity & Access Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for actor/tenant switching while connected, lifecycle revocation during upload/send, reconnect during logout, simultaneous tabs, timeout-after-send, malformed gateway payloads, and cross-tab invalidation latency.
