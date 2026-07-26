# PASS F-S2-C Service Marketplace Frontend Contract Presentation Authority

Service Marketplace Frontend Contract Presentation Authority is production-certifiable for inspected runtime paths after contract snapshot presentation boundary hardening.

## Contract Identity Findings
- Contract presentation derives from current backend request DTOs carrying immutable SQL request snapshots: request id, service id, service title, category, price, pricing type, currency, packages, request message, status, and actor capabilities.
- The frontend now requires valid backend request identity, service identity, service title, and lifecycle presentation before rendering contract summary surfaces.
- Frontend route IDs, list rows, chat headers, browser history, and local state cannot fabricate contract identity.

## Pricing Findings
- Pricing presentation uses backend request snapshot `startingPrice`, `pricingType`, and `currency` only after bounded validation.
- Negative, non-finite, missing-currency, malformed-currency, or unknown-pricing DTOs suppress pricing presentation instead of repairing or relabeling commercial truth.
- Pricing display is formatting only; it cannot become pricing authority or alter service/request economics.

## Scope Findings
- Scope presentation derives from backend `requestMessage` only after current lifecycle and identity validation.
- Request-list endpoints now include `requestMessage`, eliminating frontend reliance on a wrong or absent field for scope-shaped display.
- Frontend does not compose accepted scope from service description, route state, form state, chat messages, package JSON, or cache state.

## Deliverable Findings
- Package entries are optional backend snapshot presentation only. The frontend parses package JSON through a bounded adapter and accepts only named package entries.
- Malformed, unnamed, oversized, or non-array package payloads are suppressed. Package presentation cannot create deliverable authority.
- No separate deliverable mutation, cache, or optimistic path exists in the inspected frontend runtime.

## Timeline Findings
- Timeline-shaped package fields such as delivery days are accepted only as non-negative bounded integers from backend snapshot package JSON.
- The frontend does not derive deadlines from created dates, lifecycle status, browser time, chat state, or service listing state.
- Missing or malformed timeline values are omitted rather than inferred.

## Revision Findings
- Revision-shaped package fields are accepted only as non-negative bounded integers from backend snapshot package JSON.
- The frontend does not calculate revision limits from pricing type, package names, service descriptions, local state, or chat activity.
- Missing or malformed revision values are omitted rather than promoted into contract truth.

## Lifecycle Findings
- Contract presentation is gated to current lifecycle presentation for `Accepted`, `CompletedByProvider`, and `CompletedByBuyer` only.
- Pending, rejected, cancelled, expired, unknown, malformed, or lifecycle-invalid DTOs cannot render contract presentation.
- Lifecycle loss removes contract presentation because list/detail/chat surfaces withhold during current refresh and fail closed on invalid lifecycle authority.

## Cache Findings
- React Query keys remain authenticated tenant/user scoped through `marketplaceKeys.scope(...)`.
- Contract presentation is derived at render time from current request/list/detail query data and is not written with `setQueryData`.
- Actor/tenant transitions remove prior marketplace scopes, and current refresh withholds list/detail/chat presentation.

## Realtime Findings
- SignalR remains chat transport only and may append SQL-persisted chat messages; it does not write request, contract, pricing, scope, package, timeline, or revision truth.
- Reconnect invalidates request and chat queries. BroadcastChannel, focus, visibility, reconnect, and periodic events invalidate/refetch only.
- Realtime cannot overwrite backend contract authority because no realtime contract cache write path exists.

## Actor-Transition Findings
- Marketplace query keys and authority boundary are tenant/user scoped.
- Logout/login, actor switching, tenant switching, and route teardown purge prior marketplace cache scopes before replacement presentation.
- Stale mutation completions invalidate only the initiating actor scope and cannot render replacement actor contract presentation.

## History Findings
- Browser history, back/forward, bookmarks, refresh, and direct chat/list routes carry route identifiers only.
- Contract presentation re-enters current backend request/list/detail authority and the contract presentation boundary.
- Route state and browser storage do not carry contract identity, pricing, scope, package, timeline, revision, or success truth.

## Failure Findings
- 401, 403, 404, 409, 500, timeout, refresh failure, malformed lifecycle DTO, malformed contract identity, malformed pricing, and malformed package JSON fail closed or omit malformed fields.
- Failure states do not preserve contract presentation as current truth because current-authority list/detail/chat refreshes are withheld and cache scopes are purged on actor replacement.
- Retry/reconnect behavior refetches current backend authority and cannot resurrect obsolete contract facts.

## Adversarial Findings
- Contract replay, pricing replay, scope replay, deliverable replay, timeline replay, revision replay, cache replay, history replay, realtime replay, actor switching, tenant switching, lifecycle transitions, and deep-link replay were reviewed.
- The hardened runtime does not accept frontend-composed contract truth from route state, local state, browser storage, optimistic mutation state, realtime payloads, stale query scopes, or package JSON repair.
- No inspected path can corrupt commercial truth, preserve stale contracts after lifecycle loss, cross tenant boundaries, or bypass backend request/contract authority.

## Runtime Hardening Performed
- Added `deriveServiceRequestContractPresentation` as the frontend contract presentation boundary.
- Extended frontend request DTO types to include backend snapshot contract fields.
- Wired request cards, My Requests, Incoming Requests, and Chat request header through the contract presentation boundary.
- Added backend `requestMessage` to buyer/provider list request DTOs for current-authority scope presentation.
- Added focused frontend contract presentation regression coverage and architecture freeze coverage.

## Exact Runtime Files Modified
- `PointOfSale.API/Controllers/MarketplaceController.cs`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/contractPresentation.ts`
- `PointOfSale.Web/src/marketplace/components/RequestCard.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/MyRequestsTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/tabs/IncomingTab.tsx`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`

## Regression Freezes Added
- `PointOfSale.Web/src/marketplace/contractPresentation.test.ts`
- `PointOfSale.Tests/Architecture/ServiceMarketplaceFrontendContractPresentationAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns contract authority? Backend S1-B SQL `ServiceRequests` request/engagement authority plus current backend request, lifecycle, actor, and visibility authority owns contract truth; frontend owns derivative presentation only.
- Can frontend fabricate contract truth? No. It can validate, format, and suppress backend DTO fields only.
- Can pricing drift from backend authority? No on inspected surfaces; malformed pricing is suppressed and valid pricing derives from backend request snapshots.
- Can scope diverge from backend authority? No on inspected surfaces; scope derives from backend `requestMessage` and is lifecycle/identity gated.
- Can stale contracts survive refresh? No on inspected list/detail/chat paths; current refresh withholds presentation and actor scopes are purged.
- Can lifecycle loss preserve contract presentation? No. Contract presentation is allowed only for accepted/completed lifecycle states and fails closed otherwise.
- Can history resurrect obsolete contracts? No. History carries routes only and re-enters current backend authority.
- Can realtime overwrite contract authority? No. Realtime does not write contract fields and invalidates/refetches request authority on reconnect.
- Can tenant leakage occur? No inspected contract cache, route, realtime, or actor-transition path crosses tenant/user scope.
- Were runtime changes required? Yes.
- Is Service Marketplace Frontend Contract Presentation Authority production-certifiable? Yes, for inspected runtime paths after this hardening.

## Residual Operational Risk
- Production should retain browser chaos coverage for accepted-to-cancelled/rejected/completed transitions, actor/tenant switching while chat/detail is open, package DTO drift, SignalR reconnect, multi-tab cache invalidation latency, and backend projection lag.
