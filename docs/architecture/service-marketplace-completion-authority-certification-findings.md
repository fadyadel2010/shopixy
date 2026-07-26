# PASS S1-D Service Marketplace Completion Authority

Service Marketplace Completion Authority is production-certifiable for inspected runtime paths after atomic completion-evidence, final-completion statistics, tenant-scoped review eligibility, and lifecycle timestamp hardening.

## Completion findings
- PostgreSQL `ServiceRequests` owns completion truth. Provider completion, buyer confirmation, final closure, completion timestamps, and review eligibility derive from SQL request status and timestamps.
- DTOs, route IDs, frontend state, chat state, Redis, MongoDB, Elasticsearch, projections, caches, and replayed events cannot mint completion identity on inspected paths.

## Provider findings
- Provider completion requires trusted tenant scope, SQL row-lock loading, SQL `ProviderUserId` ownership, and an `Accepted` request state.
- Providers cannot complete pending, cancelled, rejected, buyer-completed, deleted, foreign-tenant, or already provider-completed requests on inspected paths.

## Buyer findings
- Buyer confirmation requires trusted tenant scope, SQL row-lock loading, SQL `BuyerUserId` ownership, and prior `CompletedByProvider` state.
- Buyers cannot confirm without provider completion, confirm foreign requests, or confirm twice because terminal `CompletedByBuyer` has no outbound transition.

## Mutual completion findings
- Provider completion alone moves the SQL request only to `CompletedByProvider`; final closure is `CompletedByBuyer` and requires buyer confirmation.
- One side cannot close alone. Closure bypass and replay are blocked by the entity state machine plus `MarketplaceInvariantInterceptor` transition validation.

## Lifecycle findings
- Valid completion transitions are `Accepted -> CompletedByProvider -> CompletedByBuyer` only.
- Pending, rejected, cancelled, buyer-completed, deleted, and foreign-tenant rows fail closed for completion mutation. Terminal states do not reopen through inspected runtime paths.

## Evidence findings
- Completion evidence is SQL status plus `CompletedByProviderAt` and `CompletedByBuyerAt` timestamps, with lifecycle system messages written inside the same SQL transaction before commit.
- Runtime now rejects lifecycle timestamp mutation unless the SQL status transition is changing in the same save operation.

## Chat-boundary findings
- Chat remains S1-C authority. Provider-completed requests may still allow final coordination messages, but buyer-confirmed completion closes user-message and attachment-upload authority.
- Completion does not reopen chat after final closure, and SignalR/frontend/chat state cannot complete or close a request.

## Rating-eligibility findings
- Service marketplace review eligibility derives from SQL `CompletedByBuyer` only.
- Provider-only completion no longer counts as completed in provider profile stats, and legacy service review creation now tenant-scopes the completed SQL request lookup before allowing review creation.

## Projection findings
- Projections, provider trust, search statistics, frontend capability flags, and event payloads are derivative only.
- `ServiceRequestCompletedEvent` is emitted only after SQL buyer confirmation has moved the tracked request to final completion; replay cannot recreate completion when SQL rejects the lifecycle.

## Isolation findings
- Completion APIs use trusted tenant context and explicit SQL `TenantId` predicates under row-locked mutation loading.
- Foreign requests and foreign engagements cannot be completed or buyer-confirmed on inspected paths.

## Concurrency findings
- Completion mutations run in serializable transactions and use PostgreSQL `FOR UPDATE` where supported.
- Concurrent provider/buyer/replay attempts converge through SQL locks, status transition validation, and terminal-state rejection.

## Historical findings
- Historical completion timestamps are SQL lifecycle evidence and cannot drift independently after hardening.
- Historical request service snapshots remain S1-B authority; completion consumes that request authority but does not rewrite listing, request, chat, rating, or analytics history.

## Adversarial findings
- Completion forgery, provider-only closure, buyer-only closure, completion replay, lifecycle bypass, rating unlock bypass, tenant guessing, DTO tampering, route tampering, stale frontend capability replay, and projection/cache resurrection are blocked or fail closed on inspected runtime paths.

## Runtime Hardening Performed
- Moved provider and buyer completion system-message evidence into the SQL completion transaction before commit.
- Hardened lifecycle timestamp mutation so completion timestamps cannot drift outside their SQL status transition.
- Changed provider profile completed-request statistics to count only final `CompletedByBuyer` closure.
- Tenant-scoped legacy service review eligibility to the current SQL tenant request before allowing reviews.

## Exact Runtime Files Modified
- `PointOfSale.Infrastructure/Services/ServiceRequestService.cs`
- `PointOfSale.Infrastructure/Data/MarketplaceInvariantInterceptor.cs`
- `PointOfSale.Infrastructure/Services/ServiceReviewService.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`

## Regression Freezes Added
- `PointOfSale.Tests/Architecture/ServiceMarketplaceCompletionAuthorityCertificationTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns completion authority? PostgreSQL `ServiceRequests` owns completion authority through SQL request lifecycle state and completion timestamps, deriving from accepted ServiceRequest authority.
- Can completion be forged? No on inspected paths; completion requires SQL tenant scope, participant ownership, row locks, and valid lifecycle transitions.
- Can one side close alone? No; provider completion alone is not final closure, and final closure requires buyer confirmation after provider completion.
- Can ratings unlock before completion? No; service marketplace review eligibility requires SQL `CompletedByBuyer` final closure.
- Can completion be replayed? No on inspected paths; replayed completion encounters terminal or invalid SQL state and fails closed.
- Can tenant leakage occur? No inspected completion or review-eligibility path can complete or unlock foreign-tenant requests after hardening.
- Were runtime code changes required? Yes.
- Is Service Marketplace Completion Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

## Residual Operational Risk
- Production still needs completion-specific SQL audit dashboards, WORM/SIEM lifecycle evidence retention, query-plan review, backup/restore rehearsal, and PostgreSQL-backed high-concurrency chaos runs for provider-complete, buyer-confirm, review-unlock, chat-closure, and replay races.
