# PASS F-S3-E Service Marketplace Frontend Chat History Authority

Service Marketplace Frontend Chat History Authority is production-certifiable for inspected runtime paths after authoritative cursor pagination, complete-window validation, load-older hardening, and reset-only realtime convergence.

## History Identity Findings
- Backend S1-C PostgreSQL request/message authority owns every history row and conversation association.
- The frontend accepts only backend page envelopes bound to the current validated request, actor, participants, lifecycle, and history capability.
- Missing, malformed, duplicate, foreign-request, sender-invalid, attachment-invalid, or fabricated rows invalidate the complete visible history window.

## Pagination Findings
- The prior frontend fetched one default backend page, silently truncating conversations beyond 100 rows.
- The prior timestamp-only backend cursor could omit remaining rows sharing the boundary timestamp.
- History now uses a backend-resolved message-ID cursor. SQL derives its timestamp and applies the deterministic `(CreatedAt, Id)` boundary.
- The endpoint returns 50-row page envelopes with request identity, echoed cursor, `hasMore`, and the next authoritative message cursor.
- The frontend validates every page and cursor link before presenting a merged window.

## Ordering Findings
- SQL selects newest windows by `CreatedAt` then `Id` and returns each page in ascending presentation order.
- The frontend reverses page order only to compose older pages before newer pages, then revalidates the complete chronology.
- It does not sort, deduplicate, repair, append, or infer missing rows.

## Visibility Findings
- History requires current backend participant visibility and explicit `canViewChatHistory`.
- Request, actor, tenant, lifecycle, or history-authority loss removes the visible window.
- Closed authorized history remains viewable while send/upload authority remains independently closed.

## Realtime Findings
- SignalR and BroadcastChannel remain invalidation triggers only.
- Accepted events reset the current chat query to its initial backend window; they never append, merge, or retain older cached pages as truth.
- Reconnect retires recovered transport and re-enters current request and history authority.

## Cache Findings
- Infinite-query cache identity remains tenant/user/request scoped.
- Current refresh, initial load, malformed pages, and pagination failure fail closed.
- Actor/tenant transitions and marketplace route teardown remove prior scopes.
- Loaded history windows are derivative cache only and cannot become message or pagination authority.

## Actor-Transition Findings
- Logout/login, actor switching, tenant switching, and request switching change or remove the authority scope.
- Stale page responses cannot merge into a replacement request because page request identity, cursor chain, participants, and query scope must all match.
- Own-message presentation is recomputed from the current validated actor.

## History-Restoration Findings
- Refresh, back, forward, bookmarks, and deep links carry request identity only.
- Route re-entry reloads session, request, lifecycle, participant, visibility, and history authority.
- Browser history contains no rows, cursors, windows, or pagination truth and cannot resurrect obsolete history.

## Failure Findings
- 401, 403, 404, 500, timeout, malformed envelope, malformed row, invalid cursor, contradictory `hasMore`, duplicate row, overlap, gap, and ordering failure fail closed.
- Pagination failure removes the visible window rather than preserving a partially trusted history.
- Focus, reconnect, or current backend revalidation may recover presentation but cannot promote a failed cached window.

## Concurrency Findings
- React Query fences concurrent load-more requests and the UI also disables overlapping load-more intent.
- Page-chain and complete-window validation reject duplicate, overlapping, reordered, stale, or foreign responses.
- Load-older preserves scroll position as presentation state only; it does not affect history identity or order.
- Realtime, refresh, reconnect, actor-switch, request-switch, and load-more races converge through scoped backend reads.

## Adversarial Findings
- History replay, pagination replay, cache replay, realtime replay, reconnect replay, actor switching, tenant switching, request switching, history-window replay, and deep-link replay were reviewed.
- No inspected frontend path can fabricate rows, duplicate accepted rows, repair chronology, merge noncontiguous windows, retain authority after failure, or leak history across request, actor, or tenant scope.

## Runtime Hardening Performed
- Replaced timestamp-only history pagination with backend-resolved message-ID cursor pagination.
- Added a backend history page envelope with explicit request identity, cursor echo, page size, `hasMore`, and next cursor.
- Replaced the single-page chat query with an infinite query that validates every page and the complete merged history window.
- Added explicit load-older presentation with concurrent-load fencing and scroll preservation.
- Changed realtime and send convergence to reset/refetch the initial authoritative history window instead of retaining loaded cache windows.

## Exact Runtime Files Modified
- `PointOfSale.Core/Interfaces/IChatService.cs`
- `PointOfSale.Infrastructure/Services/ChatService.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.API/Hubs/ChatHub.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`
- `PointOfSale.Web/src/marketplace/chatAuthority.ts`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/historyAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendChatHistoryAuthorityCertificationTests.cs`
- Updated `ServiceMarketplaceFrontendMessageAuthorityCertificationTests`.
- Updated `ServiceMarketplaceFrontendRealtimeAuthorityCertificationTests`.

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns chat history authority? Backend S1-C PostgreSQL request/message authority plus current backend request, actor, lifecycle, and visibility authority.
- Can frontend fabricate history? No.
- Can pagination duplicate or omit messages? Not on inspected hardened paths; authoritative composite cursor boundaries and full-window validation reject duplication, omission-shaped gaps, overlap, and replay.
- Can history reorder? No; backend order is deterministic and the frontend rejects chronology violations.
- Can stale history survive refresh? No on inspected presentation paths.
- Can reconnect resurrect obsolete history? No.
- Can browser history restore invalid history? No.
- Can cross-request history leakage occur? No.
- Can tenant leakage occur? No on inspected paths.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Chat History Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for simultaneous tabs, high-volume equal-timestamp rows, pagination during new-message arrival, lifecycle loss during load-more, reconnect during actor/tenant switching, long-history performance, and cross-tab invalidation latency.
