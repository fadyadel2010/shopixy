# PASS F-S2-E Service Marketplace Frontend Request Timeline & Audit Authority

Service Marketplace Frontend Request Timeline & Audit Authority is production-certifiable for inspected runtime paths after backend audit-timeline DTO exposure, frontend timeline presentation boundary hardening, and realtime append removal.

## Timeline Findings
- Timeline presentation derives from backend-built `auditTimeline` on current request detail authority.
- Frontend route state, local state, browser history, and chat realtime payloads cannot create timeline events.
- Malformed, duplicate, foreign-request, unknown-type, or out-of-order event rows suppress timeline presentation.

## Lifecycle-History Findings
- Lifecycle history is backend-composed from SQL `ServiceRequests` lifecycle timestamps: creation, acceptance, rejection, cancellation, provider completion, and buyer confirmation.
- The frontend validates required events for the current lifecycle and rejects impossible future lifecycle events.
- Lifecycle history is not reconstructed from status labels, capabilities, chat messages, or list rows.

## Actor-Attribution Findings
- Actor attribution is accepted only from backend audit event fields.
- Buyer/provider attribution is backend-derived where SQL lifecycle semantics prove the actor role; cancellation carries no inferred actor when SQL does not store one.
- Unknown actor roles fail closed.

## Timestamp Findings
- Event timestamps come from backend SQL lifecycle timestamp fields.
- The frontend validates timestamps and backend order but does not reorder events.
- Browser time, render time, and route history cannot create chronology.

## Consistency Findings
- Timeline current-state consistency is checked against the same lifecycle presentation boundary used by request detail and chat surfaces.
- Accepted, rejected, cancelled, provider-completed, and buyer-completed states require matching backend audit evidence before timeline presentation renders.
- Timeline cannot contradict current lifecycle authority on inspected surfaces.

## Cache Findings
- React Query request and chat keys remain authenticated tenant/user scoped.
- Timeline presentation is derived at render time from current request detail data and is never written with `setQueryData`.
- Actor or tenant transitions purge prior marketplace scopes through the existing authority boundary.

## Realtime Findings
- SignalR is now an invalidation trigger for chat/request authority rather than a chat timeline cache writer.
- Replayed, duplicate, stale, or out-of-order realtime messages cannot append, reorder, or overwrite timeline/audit presentation.
- Reconnect and message events refetch backend request/chat authority.

## Actor-Transition Findings
- Timeline query identity remains scoped by authenticated tenant/user authority.
- Logout/login, actor switching, tenant switching, and route teardown remove prior scopes before replacement presentation.
- Stale mutation or realtime completions cannot render replacement actor audit truth.

## History Findings
- Browser history, bookmarks, refresh, back, forward, and direct chat deep links carry route IDs only.
- Deep links re-enter protected route, current actor/tenant scope, current backend request authority, and timeline boundary validation.
- Route state and browser storage do not carry audit events or timeline truth.

## Failure Findings
- 401, 403, 404, 500, timeout, refresh failure, missing audit timeline, malformed DTOs, invalid timestamps, duplicate events, and lifecycle contradictions fail closed for timeline presentation.
- Failures preserve no optimistic timeline state because no optimistic timeline write path exists.
- Retries and reconnects refetch current backend authority only.

## Concurrency Findings
- Cross-tab, refresh, realtime, and transition races converge by scoped invalidation/refetch and backend SQL request lifecycle authority.
- Stale timeline DTOs cannot overwrite current audit truth because the frontend does not patch timeline caches.
- Duplicate or reordered events are rejected by the presentation boundary.

## Adversarial Findings
- Timeline replay, audit replay, history replay, cache replay, realtime replay, actor switching, tenant switching, DTO replay, refresh races, and deep-link replay were reviewed.
- No inspected frontend path can fabricate events, suppress required lifecycle history into a valid timeline, reorder chronology, infer missing actor attribution, or leak audit truth across tenants.

## Runtime Hardening Performed
- Added backend-built request `auditTimeline` DTOs from SQL lifecycle timestamp evidence.
- Added `deriveRequestAuditTimelinePresentation` frontend boundary for event identity, ownership, type, timestamp, actor role, ordering, duplicate, and lifecycle consistency validation.
- Rendered request history on the chat/request detail surface only through the boundary.
- Changed SignalR chat message handling from cache append to scoped request/chat invalidation and refetch.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/timelineAuthority.ts`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/timelineAuthority.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendRequestTimelineAuditAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns timeline and audit authority? Backend SQL request, lifecycle, audit, actor, and visibility authority owns audit truth; frontend owns derivative presentation only.
- Can frontend fabricate audit history? No.
- Can lifecycle history be reordered? No; backend order is validated and out-of-order DTOs fail closed.
- Can actor attribution drift? No on inspected surfaces; unknown or missing backend actor authority is not inferred.
- Can stale timeline survive refresh? No on inspected request detail/chat paths; current refresh revalidates or suppresses timeline presentation.
- Can realtime reorder events? No; realtime invalidates/refetches and does not append timeline truth.
- Can history resurrect obsolete audit truth? No; history carries route IDs only and re-enters backend authority.
- Can timeline contradict current lifecycle authority? No; contradictions fail closed.
- Can tenant leakage occur? No inspected cache, route, realtime, or actor-transition path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Request Timeline & Audit Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain PostgreSQL-backed browser chaos coverage for lifecycle timestamp parity, SignalR reconnect, actor/tenant switching while chat is open, concurrent transitions, multi-tab invalidation latency, and timeout-after-commit ambiguity.
