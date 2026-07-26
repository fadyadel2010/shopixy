# PASS F-S3-B Service Marketplace Frontend Message Authority

Service Marketplace Frontend Message Authority is production-certifiable for inspected runtime paths after deterministic message ordering, strict chronology validation, ambiguous-send refresh, and duplicate-submit fencing.

## Message Identity Findings
- PostgreSQL `ServiceRequestMessages` and S1-C chat authority own message identity. The frontend accepts only non-empty GUID message IDs bound to the current authoritative request.
- Duplicate IDs, mixed-request rows, malformed rows, and attachment identity contradictions invalidate the complete history payload rather than being merged or repaired.
- No optimistic, route-generated, local-state-generated, or SignalR-generated message row is rendered.

## Sender Findings
- Sender identity and Buyer/Provider role derive from backend message DTOs and must match the current backend request participants.
- Unknown senders, role mismatches, and participant drift fail closed. Own-message presentation compares the validated sender only with the validated current participant.
- System messages remain backend-created lifecycle evidence and require the backend provider-system convention; the frontend does not infer a system actor.

## Ordering Findings
- Backend history now orders by `CreatedAt` and then `Id`, including the newest-page selection and final ascending presentation order.
- The frontend validates backend order and rejects descending timestamps or reversed equal-timestamp IDs. It does not sort, merge, append, or repair history.
- Refresh, reconnect, DTO replay, and SignalR replay cannot change chronology without a new accepted backend history response.

## Timestamp Findings
- Rendered message time derives only from backend `createdAt`.
- Message timestamps must be explicit ISO timestamps with UTC or numeric offset. Browser-shaped local timestamps and malformed values fail closed.
- The frontend formats accepted backend timestamps only and does not create message chronology from browser time.

## Visibility Findings
- Message history requires current F-S3-A chat identity, participant membership, lifecycle, visibility, and explicit backend history capability.
- Cached rows are withheld while request or message authority refreshes and after authority failure.
- Lifecycle loss, request loss, actor loss, tenant loss, or malformed history removes visible message truth and realtime send authority.

## Realtime Findings
- SignalR is an invalidation trigger only. Receive events are request-ID fenced and cause scoped REST refetch.
- Reconnect rejoins current backend authority and invalidates request and message history; no event payload is appended to cache.
- Duplicate or replayed SignalR events can cause redundant refetch only and cannot duplicate rendered messages.

## Cache Findings
- React Query message keys include authenticated tenant, user, and request scope.
- Actor/tenant changes and marketplace route teardown remove old marketplace scopes.
- Cache data is not displayed during current validation or after failure and cannot become message authority.

## Actor-Transition Findings
- Logout/login, actor switching, tenant switching, and route transitions null chat identity, reject stale sends, disconnect realtime, and purge prior scopes.
- Sender presentation is recomputed from current request participants and cannot retain the prior actor's own-message classification.
- No inspected message cache or local state crosses authenticated tenant/user scope.

## History Findings
- Bookmarks, refresh, back/forward, deep links, and route replay carry request identity only and re-enter current request/chat authority.
- Browser history and route state do not contain message rows, sender roles, chronology, or visibility authority.
- Obsolete cached history is withheld during revalidation and removed on scope teardown.

## Failure Findings
- 401, 403, 404, 500, timeout, malformed DTO, invalid timestamp, invalid attachment, duplicate ID, mixed-request, sender mismatch, and ordering failure all fail closed.
- Send success and failure both invalidate message history, resolving timeout-after-commit and lost-SignalR ambiguity through backend REST authority.
- Failed refresh does not preserve stale visible rows.

## Concurrency Findings
- A synchronous composer ref fences overlapping same-tab sends before React state updates.
- Realtime receive, reconnect, refresh, actor-switch, and send races converge through scoped invalidation and complete backend history validation.
- Equal-timestamp rows have deterministic backend identity tie-breaking, preventing refresh reorder.

## Adversarial Findings
- Message replay, DTO replay, SignalR replay, cache replay, history replay, actor switching, tenant switching, reconnect races, cross-request injection, sender-role tampering, timestamp tampering, attachment-name tampering, and duplicate-submit races were reviewed.
- No inspected frontend path can fabricate a message, relabel a sender, append realtime truth, preserve stale history through failure, duplicate a replayed event, or leak message history across request, actor, or tenant scope.

## Runtime Hardening Performed
- Added deterministic backend message ordering by `CreatedAt` and `Id`.
- Added strict ISO timestamp, equal-timestamp identity ordering, and attachment filename validation to the message authority boundary.
- Added authoritative chat-history invalidation after every SignalR send outcome.
- Added synchronous same-tab send fencing and corrected typed/number formatting on the chat page.

## Exact Runtime Files Modified
- `PointOfSale.Infrastructure/Services/ChatService.cs`
- `PointOfSale.Web/src/marketplace/chatAuthority.ts`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/messageAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendMessageAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns message authority? Backend S1-C PostgreSQL chat authority, current request authority, actor authority, lifecycle authority, and visibility authority.
- Can frontend fabricate messages? No.
- Can sender identity drift? No on inspected paths; participant and role mismatch fails closed.
- Can messages reorder? No on inspected paths; backend ordering is deterministic and frontend validation rejects reordered history.
- Can SignalR replay duplicate messages? No; SignalR invalidates/refetches only.
- Can stale messages survive refresh? No on inspected message surfaces; current refresh and failure withhold cached rows.
- Can history resurrect obsolete messages? No; history re-enters current backend authority.
- Can cross-request message leakage occur? No; every row must match the authoritative request ID.
- Can tenant leakage occur? No inspected cache, route, realtime, or actor-transition path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Message Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for simultaneous tabs, equal-timestamp high-volume sends, timeout-after-commit, reconnect during actor/tenant changes, lifecycle revocation during upload/send, malformed gateway payloads, and cross-tab invalidation latency.
